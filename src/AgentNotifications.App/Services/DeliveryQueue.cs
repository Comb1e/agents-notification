using AgentNotifications.App.Configuration;
using AgentNotifications.App.Domain;
using AgentNotifications.App.Feishu;
using AgentNotifications.App.Infrastructure;
using AgentNotifications.App.Persistence;

namespace AgentNotifications.App.Services;

public sealed class DeliveryQueue : IAsyncDisposable
{
    private readonly DeliveryStore store;
    private readonly IFeishuClient feishu;
    private readonly Func<FeishuDestination?> destination;
    private readonly Func<AppSettings> settings;
    private readonly IClock clock;
    private readonly IRandomSource random;
    private readonly AppLogger logger;
    private readonly SemaphoreSlim wakeSignal = new(0, 1);
    private CancellationTokenSource? cancellation;
    private Task? worker;

    public DeliveryQueue(
        DeliveryStore store,
        IFeishuClient feishu,
        Func<FeishuDestination?> destination,
        Func<AppSettings> settings,
        IClock clock,
        IRandomSource random,
        AppLogger logger)
    {
        this.store = store;
        this.feishu = feishu;
        this.destination = destination;
        this.settings = settings;
        this.clock = clock;
        this.logger = logger;
        this.random = random;
    }

    public event EventHandler<DeliveryCounts>? CountsChanged;

    public event EventHandler<string>? StatusChanged;

    public void Start()
    {
        if (worker is not null) return;
        store.RecoverInterrupted(clock.UtcNow);
        store.ApplyRetention(clock.UtcNow.AddDays(-settings().Runtime.MetadataRetentionDays));
        cancellation = new CancellationTokenSource();
        worker = RunAsync(cancellation.Token);
        PublishCounts();
    }

    public void Wake()
    {
        if (wakeSignal.CurrentCount == 0) wakeSignal.Release();
    }

    public int RetryFailed()
    {
        var count = store.RetryDeadLetters(clock.UtcNow, TimeSpan.FromHours(settings().Runtime.DeliveryExpiryHours));
        Wake();
        PublishCounts();
        return count;
    }

    public async Task<string> SendTestAsync(CancellationToken cancellationToken)
    {
        var target = destination() ?? throw new InvalidOperationException("Feishu settings are incomplete");
        var now = clock.UtcNow;
        var notification = new DeliveryRecord(
            "test", "AgentNotifications", "Agent Notifications", 0, now, now,
            "Delivery test", "Agent Notifications can deliver Windows notifications to this destination.",
            "test", null, DeliveryState.Sending, 1, now, now.AddMinutes(5), null, now);
        var result = await feishu.SendAsync(target, notification, cancellationToken);
        return result.MessageId;
    }

    public async ValueTask DisposeAsync()
    {
        var source = cancellation;
        var task = worker;
        cancellation = null;
        worker = null;
        source?.Cancel();
        Wake();
        if (task is not null)
        {
            try { await task; }
            catch (OperationCanceledException) { }
        }
        source?.Dispose();
        wakeSignal.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await ProcessDueAsync(cancellationToken);
            try
            {
                await wakeSignal.WaitAsync(TimeSpan.FromMilliseconds(settings().Runtime.DispatcherPollMilliseconds), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ProcessDueAsync(CancellationToken cancellationToken)
    {
        var target = destination();
        if (target is null) return;
        foreach (var record in store.GetDue(clock.UtcNow))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = clock.UtcNow;
            if (record.ExpiresAt <= now)
            {
                store.MarkDead(record.Key, record.ContentHash, "Delivery expired", now);
                logger.Warn("delivery.expired", new { record.Key, record.SourceName, record.ContentHash });
                continue;
            }

            if (!store.TryBegin(record, now)) continue;
            try
            {
                string messageId;
                if (string.IsNullOrWhiteSpace(record.FeishuMessageId))
                {
                    messageId = (await feishu.SendAsync(target, record, cancellationToken)).MessageId;
                }
                else
                {
                    try
                    {
                        await feishu.UpdateAsync(target, record.FeishuMessageId, record, cancellationToken);
                        messageId = record.FeishuMessageId;
                    }
                    catch (FeishuException exception) when (exception.Kind == FeishuFailureKind.NotFound)
                    {
                        messageId = (await feishu.SendAsync(target, record, cancellationToken)).MessageId;
                    }
                }

                store.MarkDelivered(record.Key, record.ContentHash, messageId, clock.UtcNow);
                logger.Info("delivery.succeeded", new { record.Key, record.SourceName, record.ContentHash, messageId });
                StatusChanged?.Invoke(this, $"Delivered {record.SourceName} notification");
            }
            catch (FeishuException exception)
            {
                HandleFailure(record, exception.Kind, exception.Message, exception.RetryAfter);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                HandleFailure(record, FeishuFailureKind.Transient, exception.Message, null);
            }
        }

        PublishCounts();
    }

    private void HandleFailure(DeliveryRecord record, FeishuFailureKind kind, string message, TimeSpan? retryAfter)
    {
        var now = clock.UtcNow;
        var safeMessage = new Redactor(settings().RedactionPatterns).Apply(message);
        if (kind is FeishuFailureKind.Transient or FeishuFailureKind.RateLimited)
        {
            var delay = retryAfter ?? new RetryPolicy(settings().Runtime, random).DelayForAttempt(record.AttemptCount + 1);
            var next = now + delay;
            if (next < record.ExpiresAt)
            {
                store.MarkRetry(record.Key, record.ContentHash, next, safeMessage, now);
                logger.Warn("delivery.retry_scheduled", new { record.Key, record.SourceName, record.ContentHash, retryAt = next, kind = kind.ToString() });
                StatusChanged?.Invoke(this, $"Delivery retry scheduled for {record.SourceName}");
                return;
            }
        }

        store.MarkDead(record.Key, record.ContentHash, safeMessage, now);
        logger.Error("delivery.dead_letter", new { record.Key, record.SourceName, record.ContentHash, kind = kind.ToString() });
        StatusChanged?.Invoke(this, $"Delivery failed for {record.SourceName}");
    }

    private void PublishCounts() => CountsChanged?.Invoke(this, store.GetCounts());
}
