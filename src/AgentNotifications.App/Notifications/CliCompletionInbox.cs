using System.Text.Json;
using AgentNotifications.App.Configuration;
using AgentNotifications.App.Infrastructure;

namespace AgentNotifications.App.Notifications;

public sealed record CliCompletion(Guid EventId, string Source, string SessionId, DateTimeOffset CompletedAt,
    string Workspace, string Message)
{
    public static CliCompletion FromGrokTurn(string sessionId, string? promptId, string workspace, DateTimeOffset completedAt) => new(
        string.IsNullOrWhiteSpace(promptId) ? Guid.NewGuid()
            : Guid.ParseExact(TextTools.Sha256($"{CliIntegrationDefaults.GrokSourceId}\u001f{sessionId}\u001f{promptId}")[..32], "N"),
        CliIntegrationDefaults.GrokSourceId, sessionId, completedAt,
        TextTools.LimitRunes(Path.GetFileName(Path.TrimEndingDirectorySeparator(workspace)), CliIntegrationDefaults.MaximumLabelRunes),
        "Grok has finished its turn.");
}

public sealed class CliCompletionInbox(AppPaths paths)
{
    public static CliCompletion CaptureGrokEnvironment()
    {
        if (Environment.GetEnvironmentVariable("GROK_EVENT") != "turn_complete")
            throw new InvalidDataException("Expected a Grok turn_complete event");
        var sessionId = Environment.GetEnvironmentVariable("GROK_SESSION_ID");
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > CliIntegrationDefaults.MaximumLabelRunes)
            throw new InvalidDataException("Missing or invalid Grok session ID");
        var workspace = Environment.GetEnvironmentVariable("GROK_WORKSPACE_ROOT");
        if (string.IsNullOrWhiteSpace(workspace)) workspace = Environment.CurrentDirectory;
        var message = Environment.GetEnvironmentVariable("GROK_MESSAGE");
        var promptId = Environment.GetEnvironmentVariable("GROK_PROMPT_ID");
        var completion = CliCompletion.FromGrokTurn(sessionId, promptId, workspace,
            DateTimeOffset.TryParse(Environment.GetEnvironmentVariable("GROK_COMPLETED_AT"), out var completedAt)
                ? completedAt : DateTimeOffset.UtcNow);
        return completion with { Message = TextTools.LimitRunes(string.IsNullOrWhiteSpace(message) ? completion.Message : message,
            CliIntegrationDefaults.MaximumMessageRunes) };
    }

    public async Task WriteAsync(CliCompletion completion, CancellationToken cancellationToken = default)
    {
        Validate(completion);
        Directory.CreateDirectory(paths.CliInboxDirectory);
        if (Directory.EnumerateFiles(paths.CliInboxDirectory).Take(CliIntegrationDefaults.MaximumPendingFiles).Count()
            >= CliIntegrationDefaults.MaximumPendingFiles)
            throw new IOException("CLI completion inbox is full");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(completion);
        if (bytes.Length > CliIntegrationDefaults.MaximumFileBytes) throw new InvalidDataException("CLI event is too large");
        var path = Path.Combine(paths.CliInboxDirectory, $"{completion.EventId:N}.json");
        var temporaryPath = Path.Combine(paths.CliInboxDirectory, $"{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
            try { File.Move(temporaryPath, path, overwrite: false); }
            catch (IOException) when (File.Exists(path)) { }
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public async Task<CliCompletion> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4_096, FileOptions.Asynchronous);
        if (stream.Length > CliIntegrationDefaults.MaximumFileBytes) throw new InvalidDataException("CLI event is too large");
        var completion = await JsonSerializer.DeserializeAsync<CliCompletion>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("CLI event is empty");
        Validate(completion);
        return completion;
    }

    private static void Validate(CliCompletion completion)
    {
        if (completion.EventId == Guid.Empty || completion.Source != CliIntegrationDefaults.GrokSourceId
            || string.IsNullOrWhiteSpace(completion.SessionId) || completion.SessionId.Length > CliIntegrationDefaults.MaximumLabelRunes
            || completion.Workspace is null || completion.Workspace.Length > CliIntegrationDefaults.MaximumLabelRunes * 2
            || string.IsNullOrWhiteSpace(completion.Message) || completion.Message.Length > CliIntegrationDefaults.MaximumMessageRunes * 2
            || completion.CompletedAt == default)
            throw new InvalidDataException("Invalid CLI completion event");
    }
}
