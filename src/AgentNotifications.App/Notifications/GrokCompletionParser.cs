using System.Text.Json;

namespace AgentNotifications.App.Notifications;

public static class GrokCompletionParser
{
    public static CliCompletion? Parse(ReadOnlyMemory<byte> line, string sessionId, string workspace, DateTimeOffset cutoff)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var method = Text(root, "method");
            if (method is not ("_x.ai/session/update" or "session/update")
                || !root.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object
                || Text(parameters, "sessionId") != sessionId
                || !parameters.TryGetProperty("update", out var update) || update.ValueKind != JsonValueKind.Object
                || Text(update, "sessionUpdate") != "turn_completed" || Text(update, "stop_reason") != "end_turn") return null;
            var promptId = Text(update, "prompt_id");
            if (string.IsNullOrWhiteSpace(promptId) || promptId.Length > Configuration.CliIntegrationDefaults.MaximumLabelRunes) return null;
            DateTimeOffset completedAt;
            if (parameters.TryGetProperty("_meta", out var metadata) && metadata.ValueKind == JsonValueKind.Object
                && metadata.TryGetProperty("agentTimestampMs", out var milliseconds) && milliseconds.TryGetInt64(out var value))
                completedAt = DateTimeOffset.FromUnixTimeMilliseconds(value);
            else if (root.TryGetProperty("timestamp", out var timestamp) && timestamp.TryGetInt64(out var seconds))
                completedAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
            else return null;
            return completedAt > cutoff ? CliCompletion.FromGrokTurn(sessionId, promptId, workspace, completedAt) : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string property) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
