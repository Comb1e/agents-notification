using System.Text.Json.Serialization;

namespace AgentNotifications.App.Configuration;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    public bool MonitoringEnabled { get; set; } = true;

    public bool StartWithWindows { get; set; }

    public bool GrokNotificationsEnabled { get; set; } = true;

    public List<string> GrokHomeDirectories { get; set; } = [];

    public FeishuSettings Feishu { get; set; } = new();

    public List<SourceRuleSettings> SourceRules { get; set; } = [];

    public List<string> RedactionPatterns { get; set; } = [];

    public RuntimeSettings Runtime { get; set; } = new();

    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Feishu.AppId)
        && !string.IsNullOrWhiteSpace(Feishu.Recipient)
        && (GrokNotificationsEnabled || SourceRules.Any(rule => rule.Enabled));
}

public sealed class FeishuSettings
{
    public string Domain { get; set; } = "feishu";

    public string AppId { get; set; } = string.Empty;

    public string RecipientType { get; set; } = "email";

    public string Recipient { get; set; } = string.Empty;
}

public sealed class SourceRuleSettings
{
    public string Name { get; set; } = string.Empty;

    public string Pattern { get; set; } = string.Empty;

    public string MatchMode { get; set; } = "exact";

    public bool Enabled { get; set; } = true;
}

public sealed class RuntimeSettings
{
    public int NotificationReadTimeoutMilliseconds { get; set; } = 10_000;

    public int DebounceMilliseconds { get; set; } = 1_000;

    public int ReconcileSeconds { get; set; } = 15;

    public int DispatcherPollMilliseconds { get; set; } = 500;

    public int RetryBaseMilliseconds { get; set; } = 1_000;

    public int RetryMaximumMinutes { get; set; } = 15;

    public int DeliveryExpiryHours { get; set; } = 24;

    public int MetadataRetentionDays { get; set; } = 7;

    public int LogRetentionDays { get; set; } = 7;

    public long LogMaximumBytes { get; set; } = 50_000_000;

    public int CardTitleMaximumRunes { get; set; } = 100;

    public int CardBodyMaximumRunes { get; set; } = 3_000;
}
