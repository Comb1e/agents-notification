using AgentNotifications.App.Configuration;
using AgentNotifications.App.Domain;
using AgentNotifications.App.Infrastructure;
using AgentNotifications.App.Persistence;

namespace AgentNotifications.App.Notifications;

public sealed class NotificationMonitor : IAsyncDisposable
{
    private readonly IWindowsNotificationSource source;
    private readonly DeliveryStore store;
    private readonly Func<AppSettings> settings;
    private readonly IClock clock;
    private readonly SemaphoreSlim reconcileLock = new(1, 1);
    private readonly Dictionary<string, TrackedToast> tracked = new(StringComparer.Ordinal);
    private CancellationTokenSource? loopCancellation;
    private Task? loopTask;
    private ListenerStateMachine stateMachine = new();
    private volatile bool baselining;
    private int reconcileRequested;
    private DateTimeOffset baselineCutoff;

    public NotificationMonitor(IWindowsNotificationSource source, DeliveryStore store, Func<AppSettings> settings, IClock clock)
    {
        this.source = source;
        this.store = store;
        this.settings = settings;
        this.clock = clock;
    }

    public event EventHandler<ListenerState>? StateChanged;

    public event EventHandler? QueueChanged;

    public ListenerState State => stateMachine.State;

    public NotificationAccess AccessStatus => source.GetAccessStatus();

    public async Task<NotificationAccess> RequestAccessAsync()
    {
        if (source.GetAccessStatus() == NotificationAccess.Allowed) return NotificationAccess.Allowed;
        if (State != ListenerState.AwaitingPermission) MoveTo(ListenerState.AwaitingPermission);
        var result = await source.RequestAccessAsync();
        if (result == NotificationAccess.Denied) MoveTo(ListenerState.PermissionDenied);
        return result;
    }

    public async Task StartAsync(bool requestAccess, CancellationToken cancellationToken = default)
    {
        await StopLoopAsync();
        var access = source.GetAccessStatus();
        if (access != NotificationAccess.Allowed && requestAccess) access = await RequestAccessAsync();
        if (access != NotificationAccess.Allowed)
        {
            MoveTo(access == NotificationAccess.Denied ? ListenerState.PermissionDenied : ListenerState.AwaitingPermission);
            return;
        }

        if (!settings().MonitoringEnabled)
        {
            MoveTo(ListenerState.Paused);
            return;
        }

        MoveTo(ListenerState.Baselining);
        baselineCutoff = clock.UtcNow;
        baselining = true;
        source.Changed += OnSourceChanged;
        var baselineSucceeded = false;
        try
        {
            var snapshot = await GetSnapshotAsync(cancellationToken);
            tracked.Clear();
            var normalized = Normalize(snapshot);
            foreach (var notification in normalized)
            {
                var suppressed = notification.CreatedAt <= baselineCutoff;
                tracked[notification.Key] = new TrackedToast(notification.ContentHash, suppressed);
                if (!suppressed) Enqueue(notification);
            }

            baselineSucceeded = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { MoveTo(ListenerState.Retrying); }
        finally { baselining = false; }

        loopCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        loopTask = ReconcileLoopAsync(loopCancellation.Token);
        if (baselineSucceeded) MoveTo(ListenerState.Watching);
        if (Interlocked.Exchange(ref reconcileRequested, 0) == 1) await ReconcileSafeAsync(loopCancellation.Token);
    }

    public async Task PauseAsync()
    {
        await StopLoopAsync();
        tracked.Clear();
        MoveTo(ListenerState.Paused);
    }

    public async Task ReconfigureAsync(CancellationToken cancellationToken = default)
    {
        await PauseAsync();
        await StartAsync(false, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await StopLoopAsync();
        reconcileLock.Dispose();
    }

    private async Task ReconcileLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings().Runtime.ReconcileSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken)) await ReconcileSafeAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async void OnSourceChanged(object? sender, EventArgs args)
    {
        if (baselining)
        {
            Interlocked.Exchange(ref reconcileRequested, 1);
            return;
        }
        var cancellationToken = loopCancellation?.Token ?? CancellationToken.None;
        await ReconcileSafeAsync(cancellationToken);
    }

    private async Task ReconcileSafeAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;
        try { await reconcileLock.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
        try
        {
            if (source.GetAccessStatus() != NotificationAccess.Allowed)
            {
                MoveTo(ListenerState.PermissionDenied);
                await StopLoopFromLoopAsync();
                return;
            }

            await ReconcileAsync(cancellationToken);
            if (State == ListenerState.Retrying) MoveTo(ListenerState.Watching);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            MoveTo(ListenerState.Retrying);
        }
        finally
        {
            reconcileLock.Release();
        }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        var normalized = Normalize(await GetSnapshotAsync(cancellationToken));
        var current = normalized.Select(notification => notification.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var notification in normalized)
        {
            if (tracked.TryGetValue(notification.Key, out var known))
            {
                if (known.Suppressed)
                {
                    tracked[notification.Key] = known with { ContentHash = notification.ContentHash };
                    continue;
                }

                if (known.ContentHash == notification.ContentHash) continue;
            }
            else if (notification.CreatedAt <= baselineCutoff)
            {
                tracked[notification.Key] = new TrackedToast(notification.ContentHash, true);
                continue;
            }

            tracked[notification.Key] = new TrackedToast(notification.ContentHash, false);
            Enqueue(notification);
        }

        foreach (var key in tracked.Keys.Where(key => !current.Contains(key)).ToArray()) tracked.Remove(key);
    }

    private async Task<IReadOnlyList<WindowsToast>> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(settings().Runtime.NotificationReadTimeoutMilliseconds));
        return await source.GetSnapshotAsync(timeout.Token);
    }

    private IReadOnlyList<ObservedNotification> Normalize(IReadOnlyList<WindowsToast> snapshot)
    {
        var currentSettings = settings();
        var matcher = new SourceMatcher(currentSettings.SourceRules);
        var redactor = new Redactor(currentSettings.RedactionPatterns);
        var result = new List<ObservedNotification>();
        foreach (var toast in snapshot)
        {
            var sourceName = matcher.Match(toast.SourceAppId);
            if (sourceName is null) continue;
            var title = redactor.Apply(toast.Title);
            var body = redactor.Apply(toast.Body);
            var key = $"{toast.SourceAppId}\u001f{toast.WindowsNotificationId}\u001f{toast.CreatedAt.UtcTicks}";
            var hash = TextTools.Sha256($"{sourceName}\u001f{title}\u001f{body}\u001f{toast.CreatedAt.UtcTicks}");
            result.Add(new ObservedNotification(key, toast.SourceAppId, sourceName, toast.WindowsNotificationId, toast.CreatedAt, title, body, hash));
        }

        return result;
    }

    private void Enqueue(ObservedNotification notification)
    {
        var currentSettings = settings();
        var now = clock.UtcNow;
        if (store.UpsertObserved(
            notification,
            now,
            TimeSpan.FromMilliseconds(currentSettings.Runtime.DebounceMilliseconds),
            now.AddHours(currentSettings.Runtime.DeliveryExpiryHours)))
        {
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task StopLoopAsync()
    {
        source.Changed -= OnSourceChanged;
        var cancellation = loopCancellation;
        var task = loopTask;
        loopCancellation = null;
        loopTask = null;
        cancellation?.Cancel();
        if (task is not null)
        {
            try { await task; }
            catch (OperationCanceledException) { }
        }
        cancellation?.Dispose();
    }

    private Task StopLoopFromLoopAsync()
    {
        source.Changed -= OnSourceChanged;
        loopCancellation?.Cancel();
        return Task.CompletedTask;
    }

    private void MoveTo(ListenerState state)
    {
        stateMachine.MoveTo(state);
        StateChanged?.Invoke(this, state);
    }

    private sealed record TrackedToast(string ContentHash, bool Suppressed);
}
