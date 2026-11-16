using System.Security.Cryptography;
using AgentNotifications.App.Configuration;
using AgentNotifications.App.Infrastructure;

namespace AgentNotifications.App.Services;

public enum AgentInstructionState { Disabled, AwaitingSource, Settling, Syncing, Watching, Retrying }

public sealed record AgentInstructionStatus(AgentInstructionState State, IReadOnlyList<string> Files,
    int PendingFiles, DateTimeOffset? LastSyncedAt);

public sealed class AgentInstructionSync(Func<AppSettings> settings, IClock clock, AppLogger logger,
    string profilesDirectory = AgentInstructionDefaults.UserProfilesDirectory) : IAsyncDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly Dictionary<string, string> applied = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> failures = new(StringComparer.OrdinalIgnoreCase);
    private string? sourcePath;
    private string? candidateHash;
    private DateTimeOffset stableSince;
    private DateTimeOffset? lastSyncedAt;
    private Task? worker;

    public AgentInstructionStatus Status { get; private set; } = new(AgentInstructionState.AwaitingSource, [], 0, null);
    public event EventHandler<AgentInstructionStatus>? StatusChanged;

    public void Start() => worker ??= Task.Run(() => RunAsync(cancellation.Token));

    public async Task PollAsync(CancellationToken cancellationToken = default)
    {
        var configuration = settings().AgentInstructions;
        if (!configuration.Enabled || string.IsNullOrWhiteSpace(configuration.SourcePath))
        {
            Reset(null);
            Publish(configuration.Enabled ? AgentInstructionState.AwaitingSource : AgentInstructionState.Disabled, [], 0);
            return;
        }
        if (!string.Equals(sourcePath, configuration.SourcePath, StringComparison.OrdinalIgnoreCase)) Reset(configuration.SourcePath);

        byte[] content;
        try
        {
            // Deny concurrent writes while taking a bounded snapshot; editor rename saves are also
            // picked up by polling the path, without relying on potentially dropped watcher events.
            await using var stream = new FileStream(sourcePath!, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                bufferSize: 4_096, FileOptions.Asynchronous);
            if (stream.Length > AgentInstructionDefaults.MaximumSourceBytes) throw new InvalidDataException("Agent instruction source exceeds the size limit");
            content = new byte[(int)stream.Length];
            await stream.ReadExactlyAsync(content, cancellationToken);
            failures.Remove(sourcePath!);
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            candidateHash = null;
            ReportFailure(sourcePath!, exception);
            Publish(exception is FileNotFoundException or DirectoryNotFoundException ? AgentInstructionState.AwaitingSource
                : AgentInstructionState.Retrying, Status.Files, Status.PendingFiles);
            return;
        }

        var hash = Convert.ToHexString(SHA256.HashData(content));
        if (hash != candidateHash)
        {
            candidateHash = hash;
            stableSince = clock.UtcNow;
        }
        var files = AgentInstructionFiles.Discover(profilesDirectory, sourcePath!);
        foreach (var path in applied.Keys.Where(path => !files.Contains(path, StringComparer.OrdinalIgnoreCase)).ToArray()) applied.Remove(path);
        foreach (var path in failures.Keys.Where(path => path != sourcePath && !files.Contains(path, StringComparer.OrdinalIgnoreCase)).ToArray()) failures.Remove(path);
        var pending = files.Count(path => !applied.TryGetValue(path, out var version) || version != hash);
        if (clock.UtcNow - stableSince < TimeSpan.FromMilliseconds(AgentInstructionDefaults.SettleMilliseconds))
        {
            Publish(AgentInstructionState.Settling, files, pending);
            return;
        }

        if (pending > 0) Publish(AgentInstructionState.Syncing, files, pending);
        var processed = 0;
        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (applied.TryGetValue(path, out var version) && version == hash) continue;
            try
            {
                if (!await HasSameContentAsync(path, content, cancellationToken))
                {
                    await AtomicFile.ReplaceExistingAsync(path, content, cancellationToken);
                    logger.Info("instructions.file_updated", new { path });
                }
                applied[path] = hash;
                failures.Remove(path);
                processed++;
            }
            catch (Exception exception) when (IsFileFailure(exception)) { ReportFailure(path, exception); }
        }
        pending = files.Count(path => !applied.TryGetValue(path, out var version) || version != hash);
        if (pending == 0 && processed > 0)
        {
            lastSyncedAt = clock.UtcNow;
            logger.Info("instructions.sync_completed", new { fileCount = files.Count });
        }
        Publish(pending > 0 ? AgentInstructionState.Retrying : AgentInstructionState.Watching, files, pending);
    }

    private void Reset(string? path)
    {
        sourcePath = path;
        candidateHash = null;
        applied.Clear();
        failures.Clear();
        lastSyncedAt = null;
    }

    private static async Task<bool> HasSameContentAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            bufferSize: 4_096, FileOptions.Asynchronous);
        if (stream.Length != content.Length) return false;
        var existing = new byte[content.Length];
        await stream.ReadExactlyAsync(existing, cancellationToken);
        return content.AsSpan().SequenceEqual(existing);
    }

    private void ReportFailure(string path, Exception exception)
    {
        if (!failures.TryGetValue(path, out var previous) || previous != exception.HResult)
            logger.Warn("instructions.file_retry", new { path, exception.HResult });
        failures[path] = exception.HResult;
    }

    private void Publish(AgentInstructionState state, IReadOnlyList<string> files, int pending)
    {
        if (Status.State == state && Status.PendingFiles == pending && Status.LastSyncedAt == lastSyncedAt
            && Status.Files.SequenceEqual(files, StringComparer.OrdinalIgnoreCase)) return;
        Status = new AgentInstructionStatus(state, files, pending, lastSyncedAt);
        StatusChanged?.Invoke(this, Status);
    }

    private static bool IsFileFailure(Exception exception) => exception is IOException or UnauthorizedAccessException
        or InvalidDataException or ArgumentException or NotSupportedException;

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await PollAsync(cancellationToken);
            await Task.Delay(AgentInstructionDefaults.PollMilliseconds, cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        cancellation.Cancel();
        if (worker is not null)
        {
            try { await worker; }
            catch (OperationCanceledException) { }
        }
        cancellation.Dispose();
    }
}
