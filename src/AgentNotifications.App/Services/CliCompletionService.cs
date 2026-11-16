using System.Text.Json;
using AgentNotifications.App.Configuration;
using AgentNotifications.App.Domain;
using AgentNotifications.App.Infrastructure;
using AgentNotifications.App.Notifications;
using AgentNotifications.App.Persistence;

namespace AgentNotifications.App.Services;

public sealed class CliCompletionService(
    AppPaths paths, DeliveryStore store, Func<AppSettings> settings, IClock clock,
    Action<ObservedNotification> showWindowsNotification, Action wakeDelivery, AppLogger logger) : IAsyncDisposable
{
    private readonly CliCompletionInbox inbox = new(paths);
    private readonly CancellationTokenSource cancellation = new();
    private Task? worker;

    public void Start() => worker ??= Task.Run(() => RunAsync(cancellation.Token));

    public async Task ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(paths.CliInboxDirectory);
        var now = clock.UtcNow;
        foreach (var temporaryPath in Directory.EnumerateFiles(paths.CliInboxDirectory, "*.tmp").Take(CliIntegrationDefaults.MaximumPendingFiles))
        {
            if (File.GetLastWriteTimeUtc(temporaryPath) < now.UtcDateTime.AddHours(-settings().Runtime.DeliveryExpiryHours))
                File.Delete(temporaryPath);
        }
        foreach (var path in Directory.EnumerateFiles(paths.CliInboxDirectory, "*.json").Take(CliIntegrationDefaults.BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var completion = await inbox.ReadAsync(path, cancellationToken);
                var configuration = settings();
                if (completion.CompletedAt > now.AddMinutes(5)) throw new InvalidDataException("Future CLI event");
                var expiry = completion.CompletedAt.AddHours(configuration.Runtime.DeliveryExpiryHours);
                if (expiry > now && configuration.MonitoringEnabled && configuration.GrokNotificationsEnabled)
                {
                    var redactor = new Redactor(configuration.RedactionPatterns);
                    var title = redactor.Apply("Grok Build finished");
                    var body = redactor.Apply($"{completion.Workspace}\n{completion.Message}\nSession: {completion.SessionId}");
                    var notification = new ObservedNotification(
                        $"{completion.Source}:{completion.EventId:N}", completion.Source, CliIntegrationDefaults.GrokSourceName,
                        0, completion.CompletedAt, title, body, TextTools.Sha256($"{title}\u001f{body}"));
                    // Commit before removing the file. Re-reading it after a crash reuses the delivery key.
                    var queued = store.Get(notification.Key) is null && store.UpsertObserved(notification, now, TimeSpan.Zero, expiry);
                    wakeDelivery();
                    try
                    {
                        if (queued)
                        {
                            showWindowsNotification(notification);
                            logger.Info("cli.windows_notification_shown", new { notification.Key });
                        }
                    }
                    catch (Exception exception)
                    {
                        logger.Warn("cli.windows_notification_failed", new { exception.HResult });
                    }
                    logger.Info("cli.completion_received", new { notification.Key, notification.SourceName });
                }
                File.Delete(path);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException or System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                logger.Warn("cli.event_rejected", new { reason = "invalid_event" });
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.Warn("cli.inbox_retry", new { exception.HResult });
            }
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

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try { await ProcessPendingAsync(cancellationToken); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.Warn("cli.inbox_retry", new { exception.HResult });
            }
            await Task.Delay(settings().Runtime.DispatcherPollMilliseconds, cancellationToken);
        }
    }
}
