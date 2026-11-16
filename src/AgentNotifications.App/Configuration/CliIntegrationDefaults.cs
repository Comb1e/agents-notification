namespace AgentNotifications.App.Configuration;

public static class CliIntegrationDefaults
{
    public const string GrokCompletionArgument = "--grok-completed";
    public const string GrokSourceId = "grok.build.cli";
    public const string GrokSourceName = "Grok Build";
    public const string InboxFolder = "cli-inbox";
    public const int MaximumFileBytes = 32_768;
    public const int MaximumPendingFiles = 1_000;
    public const int MaximumMessageRunes = 2_000;
    public const int MaximumLabelRunes = 256;
    public const int BatchSize = 20;
}
