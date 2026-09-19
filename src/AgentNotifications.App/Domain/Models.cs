namespace AgentNotifications.App.Domain;

public enum ListenerState
{
    Starting,
    AwaitingPermission,
    Baselining,
    Watching,
    Paused,
    PermissionDenied,
    Retrying,
    Faulted,
}

public enum DeliveryState
{
    Observed,
    Debouncing,
    Queued,
    Sending,
    Updating,
    Delivered,
    RetryScheduled,
    DeadLetter,
}

public enum NotificationAccess
{
    Unspecified,
    Allowed,
    Denied,
    Unsupported,
}

public sealed record ObservedNotification(
    string Key,
    string SourceAppId,
    string SourceName,
    uint WindowsNotificationId,
    DateTimeOffset CreatedAt,
    string Title,
    string Body,
    string ContentHash);

public sealed record WindowsToast(
    string SourceAppId,
    string SourceDisplayName,
    uint WindowsNotificationId,
    DateTimeOffset CreatedAt,
    string Title,
    string Body);

public sealed record DeliveryRecord(
    string Key,
    string SourceAppId,
    string SourceName,
    long WindowsNotificationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ObservedAt,
    string? Title,
    string? Body,
    string ContentHash,
    string? FeishuMessageId,
    DeliveryState State,
    int AttemptCount,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset ExpiresAt,
    string? LastError,
    DateTimeOffset UpdatedAt);

public sealed record DeliveryCounts(int Pending, int Failed);

public sealed record FeishuDestination(
    string Domain,
    string AppId,
    string AppSecret,
    string RecipientType,
    string Recipient);

public sealed record FeishuMessage(string MessageId);

public enum FeishuFailureKind
{
    Authentication,
    RateLimited,
    NotFound,
    Transient,
    Permanent,
}

public sealed class FeishuException : Exception
{
    public FeishuException(FeishuFailureKind kind, string message, TimeSpan? retryAfter = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        RetryAfter = retryAfter;
    }

    public FeishuFailureKind Kind { get; }

    public TimeSpan? RetryAfter { get; }
}
