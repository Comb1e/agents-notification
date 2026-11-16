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
    public const int GrokPollMilliseconds = 1_000;
    public const int GrokMaximumLineBytes = 1_048_576;
    public const int GrokReadBatchBytes = 4_194_304;
    public const int GrokMaximumMetadataBytes = 1_048_576;
    public const int GrokMaximumRoots = 16;
}
