using AgentNotifications.App.Domain;
using AgentNotifications.App.Infrastructure;
using Microsoft.Data.Sqlite;

namespace AgentNotifications.App.Persistence;

public sealed class DeliveryStore : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly object gate = new();

    public DeliveryStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? throw new ArgumentException("Database path has no directory", nameof(databasePath)));
        connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        }.ToString());
        connection.Open();
        Initialize();
    }

    public bool UpsertObserved(ObservedNotification notification, DateTimeOffset observedAt, TimeSpan debounce, DateTimeOffset expiresAt)
    {
        lock (gate)
        {
            using var transaction = connection.BeginTransaction();
            var existing = GetInternal(notification.Key, transaction);
            if (existing is not null && existing.ContentHash == notification.ContentHash && existing.State != DeliveryState.DeadLetter)
            {
                transaction.Commit();
                return false;
            }

            if (existing is not null) DeliveryStateMachine.Ensure(existing.State, DeliveryState.Debouncing);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO deliveries(
                    key, source_app_id, source_name, notification_id, created_at, observed_at,
                    title, body, content_hash, feishu_message_id, state, attempt_count,
                    next_attempt_at, expires_at, last_error, updated_at)
                VALUES ($key, $sourceAppId, $sourceName, $notificationId, $createdAt, $observedAt,
                    $title, $body, $contentHash, $feishuMessageId, $state, 0,
                    $nextAttemptAt, $expiresAt, NULL, $updatedAt)
                ON CONFLICT(key) DO UPDATE SET
                    source_app_id = excluded.source_app_id,
                    source_name = excluded.source_name,
                    notification_id = excluded.notification_id,
                    created_at = excluded.created_at,
                    observed_at = excluded.observed_at,
                    title = excluded.title,
                    body = excluded.body,
                    content_hash = excluded.content_hash,
                    state = excluded.state,
                    attempt_count = 0,
                    next_attempt_at = excluded.next_attempt_at,
                    expires_at = excluded.expires_at,
                    last_error = NULL,
                    updated_at = excluded.updated_at;
                """;
            Add(command, "$key", notification.Key);
            Add(command, "$sourceAppId", notification.SourceAppId);
            Add(command, "$sourceName", notification.SourceName);
            Add(command, "$notificationId", (long)notification.WindowsNotificationId);
            Add(command, "$createdAt", ToUnix(notification.CreatedAt));
            Add(command, "$observedAt", ToUnix(observedAt));
            Add(command, "$title", notification.Title);
            Add(command, "$body", notification.Body);
            Add(command, "$contentHash", notification.ContentHash);
            Add(command, "$feishuMessageId", existing?.FeishuMessageId);
            Add(command, "$state", DeliveryState.Debouncing.ToString());
            Add(command, "$nextAttemptAt", ToUnix(observedAt + debounce));
            Add(command, "$expiresAt", ToUnix(expiresAt));
            Add(command, "$updatedAt", ToUnix(observedAt));
            command.ExecuteNonQuery();
            transaction.Commit();
            return true;
        }
    }

    public IReadOnlyList<DeliveryRecord> GetDue(DateTimeOffset now, int limit = 20)
    {
        lock (gate)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT * FROM deliveries
                WHERE state IN ('Debouncing', 'RetryScheduled') AND next_attempt_at <= $now
                ORDER BY next_attempt_at, observed_at
                LIMIT $limit;
                """;
            Add(command, "$now", ToUnix(now));
            Add(command, "$limit", limit);
            using var reader = command.ExecuteReader();
            var result = new List<DeliveryRecord>();
            while (reader.Read()) result.Add(Read(reader));
            return result;
        }
    }

    public bool TryBegin(DeliveryRecord record, DateTimeOffset now)
    {
        var next = string.IsNullOrWhiteSpace(record.FeishuMessageId) ? DeliveryState.Sending : DeliveryState.Updating;
        DeliveryStateMachine.Ensure(record.State, DeliveryState.Queued);
        DeliveryStateMachine.Ensure(DeliveryState.Queued, next);
        lock (gate)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE deliveries
                SET state = $state, attempt_count = attempt_count + 1, updated_at = $updatedAt
                WHERE key = $key AND content_hash = $contentHash
                  AND state IN ('Debouncing', 'RetryScheduled');
                """;
            Add(command, "$state", next.ToString());
            Add(command, "$updatedAt", ToUnix(now));
            Add(command, "$key", record.Key);
            Add(command, "$contentHash", record.ContentHash);
            return command.ExecuteNonQuery() == 1;
        }
    }

    public void MarkDelivered(string key, string contentHash, string messageId, DateTimeOffset now)
    {
        lock (gate)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE deliveries
                SET feishu_message_id = $messageId,
                    state = CASE WHEN content_hash = $contentHash THEN 'Delivered' ELSE state END,
                    title = CASE WHEN content_hash = $contentHash THEN NULL ELSE title END,
                    body = CASE WHEN content_hash = $contentHash THEN NULL ELSE body END,
                    last_error = NULL,
                    updated_at = $updatedAt
                WHERE key = $key;
                """;
            Add(command, "$messageId", messageId);
            Add(command, "$contentHash", contentHash);
            Add(command, "$updatedAt", ToUnix(now));
            Add(command, "$key", key);
            command.ExecuteNonQuery();
        }
    }

    public void MarkRetry(string key, string contentHash, DateTimeOffset nextAttemptAt, string error, DateTimeOffset now)
    {
        MarkFailure(key, contentHash, DeliveryState.RetryScheduled, nextAttemptAt, error, now);
    }

    public void MarkDead(string key, string contentHash, string error, DateTimeOffset now)
    {
        MarkFailure(key, contentHash, DeliveryState.DeadLetter, now, error, now);
    }

    public void RecoverInterrupted(DateTimeOffset now)
    {
        lock (gate)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE deliveries
                SET state = 'RetryScheduled', next_attempt_at = $now,
                    last_error = 'Interrupted during delivery', updated_at = $now
                WHERE state IN ('Queued', 'Sending', 'Updating');
                """;
            Add(command, "$now", ToUnix(now));
            command.ExecuteNonQuery();
        }
    }

    public int RetryDeadLetters(DateTimeOffset now, TimeSpan expiry)
    {
        lock (gate)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE deliveries
                SET state = 'Debouncing', attempt_count = 0, next_attempt_at = $now,
                    expires_at = $expiresAt, last_error = NULL, updated_at = $now
                WHERE state = 'DeadLetter' AND title IS NOT NULL;
                """;
            Add(command, "$now", ToUnix(now));
            Add(command, "$expiresAt", ToUnix(now + expiry));
            return command.ExecuteNonQuery();
        }
    }

    public DeliveryCounts GetCounts()
    {
        lock (gate)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    SUM(CASE WHEN state NOT IN ('Delivered', 'DeadLetter') THEN 1 ELSE 0 END),
                    SUM(CASE WHEN state = 'DeadLetter' THEN 1 ELSE 0 END)
                FROM deliveries;
                """;
            using var reader = command.ExecuteReader();
            reader.Read();
            return new DeliveryCounts(reader.IsDBNull(0) ? 0 : reader.GetInt32(0), reader.IsDBNull(1) ? 0 : reader.GetInt32(1));
        }
    }

    public void ApplyRetention(DateTimeOffset cutoff)
    {
        lock (gate)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM deliveries WHERE state IN ('Delivered', 'DeadLetter') AND updated_at < $cutoff;";
            Add(command, "$cutoff", ToUnix(cutoff));
            command.ExecuteNonQuery();
        }
    }

    public DeliveryRecord? Get(string key)
    {
        lock (gate) return GetInternal(key, null);
    }

    public void Dispose() => connection.Dispose();

    private void Initialize()
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            CREATE TABLE IF NOT EXISTS deliveries(
                key TEXT PRIMARY KEY,
                source_app_id TEXT NOT NULL,
                source_name TEXT NOT NULL,
                notification_id INTEGER NOT NULL,
                created_at INTEGER NOT NULL,
                observed_at INTEGER NOT NULL,
                title TEXT,
                body TEXT,
                content_hash TEXT NOT NULL,
                feishu_message_id TEXT,
                state TEXT NOT NULL,
                attempt_count INTEGER NOT NULL,
                next_attempt_at INTEGER NOT NULL,
                expires_at INTEGER NOT NULL,
                last_error TEXT,
                updated_at INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_deliveries_due ON deliveries(state, next_attempt_at);
            CREATE INDEX IF NOT EXISTS ix_deliveries_retention ON deliveries(state, updated_at);
            """;
        command.ExecuteNonQuery();
    }

    private void MarkFailure(string key, string contentHash, DeliveryState state, DateTimeOffset nextAttemptAt, string error, DateTimeOffset now)
    {
        lock (gate)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE deliveries
                SET state = $state, next_attempt_at = $nextAttemptAt,
                    last_error = $lastError, updated_at = $updatedAt
                WHERE key = $key AND content_hash = $contentHash;
                """;
            Add(command, "$state", state.ToString());
            Add(command, "$nextAttemptAt", ToUnix(nextAttemptAt));
            Add(command, "$lastError", error.Length > 500 ? error[..500] : error);
            Add(command, "$updatedAt", ToUnix(now));
            Add(command, "$key", key);
            Add(command, "$contentHash", contentHash);
            command.ExecuteNonQuery();
        }
    }

    private DeliveryRecord? GetInternal(string key, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM deliveries WHERE key = $key;";
        Add(command, "$key", key);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    private static DeliveryRecord Read(SqliteDataReader reader) => new(
        reader.GetString(reader.GetOrdinal("key")),
        reader.GetString(reader.GetOrdinal("source_app_id")),
        reader.GetString(reader.GetOrdinal("source_name")),
        reader.GetInt64(reader.GetOrdinal("notification_id")),
        FromUnix(reader.GetInt64(reader.GetOrdinal("created_at"))),
        FromUnix(reader.GetInt64(reader.GetOrdinal("observed_at"))),
        reader.IsDBNull(reader.GetOrdinal("title")) ? null : reader.GetString(reader.GetOrdinal("title")),
        reader.IsDBNull(reader.GetOrdinal("body")) ? null : reader.GetString(reader.GetOrdinal("body")),
        reader.GetString(reader.GetOrdinal("content_hash")),
        reader.IsDBNull(reader.GetOrdinal("feishu_message_id")) ? null : reader.GetString(reader.GetOrdinal("feishu_message_id")),
        Enum.Parse<DeliveryState>(reader.GetString(reader.GetOrdinal("state"))),
        reader.GetInt32(reader.GetOrdinal("attempt_count")),
        FromUnix(reader.GetInt64(reader.GetOrdinal("next_attempt_at"))),
        FromUnix(reader.GetInt64(reader.GetOrdinal("expires_at"))),
        reader.IsDBNull(reader.GetOrdinal("last_error")) ? null : reader.GetString(reader.GetOrdinal("last_error")),
        FromUnix(reader.GetInt64(reader.GetOrdinal("updated_at"))));

    private static void Add(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static long ToUnix(DateTimeOffset value) => value.ToUnixTimeMilliseconds();

    private static DateTimeOffset FromUnix(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value);
}
