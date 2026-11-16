using System.Diagnostics;
using System.Text.Json;
using AgentNotifications.App.Configuration;
using AgentNotifications.App.Infrastructure;
using AgentNotifications.App.Notifications;

namespace AgentNotifications.App.Services;

public sealed record GrokSessionStatus(int OpenSessions, int Processes, int MonitoredSessions);

public sealed class GrokSessionMonitor(
    AppPaths paths, Func<AppSettings> settings, IClock clock, AppLogger logger,
    Func<IEnumerable<string>>? dataRoots = null, Func<int, bool>? processIsRunning = null) : IAsyncDisposable
{
    private sealed record Session(string Id, string Workspace, JsonLineTailer Reader);
    private readonly Dictionary<string, Session> sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly CliCompletionInbox inbox = new(paths);
    private readonly CancellationTokenSource cancellation = new();
    private readonly DateTimeOffset cutoff = clock.UtcNow;
    private readonly HashSet<string> migratedRoots = new(StringComparer.OrdinalIgnoreCase);
    private Task? worker;
    public GrokSessionStatus Status { get; private set; } = new(0, 0, 0);
    public event EventHandler<GrokSessionStatus>? StatusChanged;

    public void Start() => worker ??= Task.Run(() => RunAsync(cancellation.Token));

    public async Task PollAsync(CancellationToken cancellationToken = default)
    {
        var openSessions = new HashSet<string>(StringComparer.Ordinal);
        var processes = new HashSet<int>();
        var existingLogs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in (dataRoots?.Invoke() ?? ResolveDataRoots()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(root)) continue;
            try
            {
                if (!migratedRoots.Contains(root))
                {
                    try { RetireLegacyHook(root); }
                    catch (Exception exception) when (IsReadFailure(exception))
                    {
                        logger.Warn("grok.legacy_hook_cleanup_failed", new { exception.HResult });
                    }
                    migratedRoots.Add(root);
                }
                ReadActiveSessions(root, openSessions, processes);
                var sessionRoot = Path.Combine(root, "sessions");
                if (!Directory.Exists(sessionRoot)) continue;
                foreach (var logPath in Directory.EnumerateFiles(sessionRoot, "updates.jsonl", new EnumerationOptions
                {
                    RecurseSubdirectories = true, MaxRecursionDepth = 2,
                    IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint,
                }))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var directory = Path.GetDirectoryName(logPath)!;
                    existingLogs.Add(logPath);
                    try
                    {
                        if (!sessions.TryGetValue(logPath, out var session))
                        {
                            using var summary = ReadMetadata(Path.Combine(directory, "summary.json"));
                            var metadata = summary.RootElement;
                            if (metadata.TryGetProperty("session_kind", out var kind)
                                && kind.ValueKind == JsonValueKind.String && kind.GetString()!.StartsWith("subagent", StringComparison.Ordinal)) continue;
                            var info = metadata.GetProperty("info");
                            var id = info.GetProperty("id").GetString();
                            var workspace = info.GetProperty("cwd").GetString();
                            if (string.IsNullOrWhiteSpace(id) || id.Length > CliIntegrationDefaults.MaximumLabelRunes
                                || id != Path.GetFileName(directory) || string.IsNullOrWhiteSpace(workspace)) continue;
                            session = new Session(id, workspace, new JsonLineTailer());
                            sessions.Add(logPath, session);
                        }
                        // Filter history by event time rather than seeking to a moving EOF: a turn can
                        // finish between discovery and opening the file, including on app startup.
                        await session.Reader.ReadAsync(logPath,
                            async line =>
                            {
                                var completion = GrokCompletionParser.Parse(line, session.Id, session.Workspace, cutoff);
                                if (completion is not null && settings().MonitoringEnabled && settings().GrokNotificationsEnabled)
                                {
                                    await inbox.WriteAsync(completion, cancellationToken);
                                    logger.Info("grok.turn_detected", new { completion.EventId, completion.SessionId });
                                }
                            }, cancellationToken);
                    }
                    catch (Exception exception) when (IsReadFailure(exception))
                    {
                        // A half-written summary or temporarily locked log is retried on the next scan.
                    }
                }
            }
            catch (Exception exception) when (IsReadFailure(exception))
            {
                logger.Warn("grok.discovery_retry", new { exception.HResult });
            }
        }
        foreach (var path in sessions.Keys.Where(path => !existingLogs.Contains(path)).ToArray()) sessions.Remove(path);
        var status = new GrokSessionStatus(openSessions.Count, processes.Count, sessions.Count);
        if (status != Status)
        {
            Status = status;
            logger.Info("grok.sessions_discovered", status);
            StatusChanged?.Invoke(this, status);
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

    private IEnumerable<string> ResolveDataRoots()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok");
        var environmentRoot = Environment.GetEnvironmentVariable("GROK_HOME");
        if (!string.IsNullOrWhiteSpace(environmentRoot) && Path.IsPathFullyQualified(environmentRoot)) yield return environmentRoot;
        foreach (var directory in settings().GrokHomeDirectories) yield return directory;
    }

    private void ReadActiveSessions(string root, HashSet<string> active, HashSet<int> processes)
    {
        var path = Path.Combine(root, "active_sessions.json");
        if (!File.Exists(path)) return;
        try
        {
            using var document = ReadMetadata(path);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var id = item.GetProperty("session_id").GetString();
                var pid = item.GetProperty("pid").GetInt32();
                if (!string.IsNullOrWhiteSpace(id) && (processIsRunning?.Invoke(pid) ?? IsGrokProcess(pid)))
                {
                    active.Add(id);
                    processes.Add(pid);
                }
            }
        }
        catch (Exception exception) when (IsReadFailure(exception)) { }
    }

    private static bool IsGrokProcess(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited && process.ProcessName.Equals("grok", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    private static JsonDocument ReadMetadata(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > CliIntegrationDefaults.GrokMaximumMetadataBytes) throw new InvalidDataException("Grok metadata is too large");
        return JsonDocument.Parse(stream);
    }

    private static void RetireLegacyHook(string root)
    {
        var path = Path.Combine(root, "hooks", "agent-notifications.json");
        if (!File.Exists(path)) return;
        using var document = ReadMetadata(path);
        if (!document.RootElement.TryGetProperty("hooks", out var hooks) || hooks.ValueKind != JsonValueKind.Object
            || hooks.EnumerateObject().Count() != 1 || !hooks.TryGetProperty("Stop", out var groups)
            || groups.ValueKind != JsonValueKind.Array || groups.GetArrayLength() != 1
            || !groups[0].TryGetProperty("hooks", out var handlers) || handlers.ValueKind != JsonValueKind.Array
            || handlers.GetArrayLength() != 1) return;
        var hook = handlers[0];
        var expectedCallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentNotifications", "integrations", "grok", "GrokCompletionHook.ps1");
        if (hook.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array
            && args.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String
                && string.Equals(value.GetString(), expectedCallback, StringComparison.OrdinalIgnoreCase))) File.Delete(path);
    }

    private static bool IsReadFailure(Exception exception) => exception is IOException or InvalidDataException or UnauthorizedAccessException
        or JsonException or InvalidOperationException or KeyNotFoundException or FormatException;

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await PollAsync(cancellationToken);
            await Task.Delay(CliIntegrationDefaults.GrokPollMilliseconds, cancellationToken);
        }
    }
}
