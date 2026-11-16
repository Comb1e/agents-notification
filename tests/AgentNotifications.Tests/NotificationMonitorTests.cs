using AgentNotifications.App.Configuration;
using AgentNotifications.App.Domain;
using AgentNotifications.App.Notifications;
using AgentNotifications.App.Persistence;

namespace AgentNotifications.Tests;

public sealed class NotificationMonitorTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "agent-notifications-monitor-tests", Guid.NewGuid().ToString("N"));
    private DeliveryStore? store;
    private NotificationMonitor? monitor;

    [Fact]
    public async Task Baselines_existing_and_enqueues_only_new_notifications()
    {
        var now = DateTimeOffset.UtcNow;
        var source = new FakeNotificationSource
        {
            Snapshot = [Toast("com.qoder.app", 1, now.AddMinutes(-1), "old")],
        };
        var clock = new FakeClock(now);
        store = new DeliveryStore(Path.Combine(directory, "deliveries.db"));
        monitor = new NotificationMonitor(source, store, () => Settings(), clock);
        await monitor.StartAsync(false);
        Assert.Equal(0, store.GetCounts().Pending);

        source.Snapshot = [
            Toast("com.qoder.app", 1, now.AddMinutes(-1), "old"),
            Toast("dev.zcode.app", 1, now.AddSeconds(1), "new"),
        ];
        source.RaiseChanged();
        var key = $"dev.zcode.app\u001f1\u001f{now.AddSeconds(1).UtcTicks}";
        await TestWait.UntilAsync(() => store.Get(key) is not null);
        Assert.Equal("new", store.Get(key)!.Title);
    }

    [Fact]
    public async Task Coalesces_changed_content_for_same_toast()
    {
        var now = DateTimeOffset.UtcNow;
        var source = new FakeNotificationSource { Snapshot = [] };
        var clock = new FakeClock(now);
        store = new DeliveryStore(Path.Combine(directory, "deliveries.db"));
        monitor = new NotificationMonitor(source, store, () => Settings(), clock);
        await monitor.StartAsync(false);
        var created = now.AddSeconds(1);
        source.Snapshot = [Toast("com.qoder.app", 5, created, "first")];
        source.RaiseChanged();
        var key = $"com.qoder.app\u001f5\u001f{created.UtcTicks}";
        await TestWait.UntilAsync(() => store.Get(key)?.Title == "first");
        source.Snapshot = [Toast("com.qoder.app", 5, created, "second")];
        source.RaiseChanged();
        await TestWait.UntilAsync(() => store.Get(key)?.Title == "second");
        Assert.Equal(1, store.GetCounts().Pending);
    }

    [Fact]
    public async Task Rejects_excluded_command_runner()
    {
        var now = DateTimeOffset.UtcNow;
        var source = new FakeNotificationSource { Snapshot = [] };
        var clock = new FakeClock(now);
        store = new DeliveryStore(Path.Combine(directory, "deliveries.db"));
        monitor = new NotificationMonitor(source, store, () => Settings(), clock);
        await monitor.StartAsync(false);
        source.Snapshot = [Toast("OpenAI.Codex_x!CodexCoreCommandRunner", 1, now.AddSeconds(1), "ignored")];
        source.RaiseChanged();
        await Task.Delay(100);
        Assert.Equal(0, store.GetCounts().Pending);
    }

    [Fact]
    public async Task Does_not_replay_baselined_toast_after_remove_and_reappear()
    {
        var now = DateTimeOffset.UtcNow;
        var oldToast = Toast("com.qoder.app", 7, now.AddMinutes(-2), "old");
        var source = new FakeNotificationSource { Snapshot = [oldToast] };
        var clock = new FakeClock(now);
        store = new DeliveryStore(Path.Combine(directory, "deliveries.db"));
        monitor = new NotificationMonitor(source, store, () => Settings(), clock);
        await monitor.StartAsync(false);

        source.Snapshot = [];
        source.RaiseChanged();
        await Task.Delay(50);
        source.Snapshot = [oldToast];
        source.RaiseChanged();
        await Task.Delay(100);

        Assert.Equal(0, store.GetCounts().Pending);
    }

    [Fact]
    public async Task Treats_duplicate_windows_ids_from_different_apps_as_distinct()
    {
        var now = DateTimeOffset.UtcNow;
        var source = new FakeNotificationSource { Snapshot = [] };
        var clock = new FakeClock(now);
        store = new DeliveryStore(Path.Combine(directory, "deliveries.db"));
        monitor = new NotificationMonitor(source, store, () => Settings(), clock);
        await monitor.StartAsync(false);
        var created = now.AddSeconds(1);
        source.Snapshot = [
            Toast("com.qoder.app", 9, created, "qoder"),
            Toast("electron.app.ZCode", 9, created, "zcode"),
        ];
        source.RaiseChanged();
        await TestWait.UntilAsync(() => store.GetCounts().Pending == 2);
    }

    [Fact]
    public async Task Accepts_empty_visual_content()
    {
        var now = DateTimeOffset.UtcNow;
        var source = new FakeNotificationSource { Snapshot = [] };
        var clock = new FakeClock(now);
        store = new DeliveryStore(Path.Combine(directory, "deliveries.db"));
        monitor = new NotificationMonitor(source, store, () => Settings(), clock);
        await monitor.StartAsync(false);
        var toast = new WindowsToast("com.qoder.app", "Qoder", 3, now.AddSeconds(1), string.Empty, string.Empty);
        source.Snapshot = [toast];
        source.RaiseChanged();
        await TestWait.UntilAsync(() => store.GetCounts().Pending == 1);
    }

    [Fact]
    public async Task Reports_permission_denial_and_revocation()
    {
        var now = DateTimeOffset.UtcNow;
        var source = new FakeNotificationSource { Access = NotificationAccess.Denied };
        var clock = new FakeClock(now);
        store = new DeliveryStore(Path.Combine(directory, "deliveries.db"));
        monitor = new NotificationMonitor(source, store, () => Settings(), clock);
        await monitor.StartAsync(false);
        Assert.Equal(ListenerState.PermissionDenied, monitor.State);

        source.Access = NotificationAccess.Allowed;
        await monitor.StartAsync(false);
        Assert.Equal(ListenerState.Watching, monitor.State);
        source.Access = NotificationAccess.Denied;
        source.RaiseChanged();
        await TestWait.UntilAsync(() => monitor.State == ListenerState.PermissionDenied);
    }

    [Fact]
    public async Task Retries_failed_initial_snapshot_without_replaying_old_notifications()
    {
        var now = DateTimeOffset.UtcNow;
        var source = new FakeNotificationSource
        {
            SnapshotHandler = _ => throw new IOException("Notification service is not ready"),
        };
        var clock = new FakeClock(now);
        store = new DeliveryStore(Path.Combine(directory, "deliveries.db"));
        monitor = new NotificationMonitor(source, store, () => Settings(), clock);

        await monitor.StartAsync(false);
        Assert.Equal(ListenerState.Retrying, monitor.State);

        source.SnapshotHandler = null;
        source.Snapshot = [
            Toast("com.qoder.app", 10, now.AddMinutes(-1), "old"),
            Toast("dev.zcode.app", 11, now.AddSeconds(1), "new"),
        ];
        source.RaiseChanged();

        var newKey = $"dev.zcode.app\u001f11\u001f{now.AddSeconds(1).UtcTicks}";
        await TestWait.UntilAsync(() => monitor.State == ListenerState.Watching && store.Get(newKey) is not null);
        Assert.Equal(1, store.GetCounts().Pending);
    }

    [Fact]
    public async Task Times_out_blocked_initial_snapshot_and_keeps_monitor_alive()
    {
        var now = DateTimeOffset.UtcNow;
        var source = new FakeNotificationSource
        {
            SnapshotHandler = async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return [];
            },
        };
        var settings = Settings();
        settings.Runtime.NotificationReadTimeoutMilliseconds = 100;
        var clock = new FakeClock(now);
        store = new DeliveryStore(Path.Combine(directory, "deliveries.db"));
        monitor = new NotificationMonitor(source, store, () => settings, clock);

        await monitor.StartAsync(false).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ListenerState.Retrying, monitor.State);
    }

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(directory);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (monitor is not null) await monitor.DisposeAsync();
        store?.Dispose();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private static AppSettings Settings() => new()
    {
        SourceRules = [
            BuiltInSourceRules.CreateChatGpt(true),
            BuiltInSourceRules.CreateQoder(true),
            BuiltInSourceRules.CreateZCode(true),
        ],
        Runtime = new RuntimeSettings { DebounceMilliseconds = 100, ReconcileSeconds = 60 },
    };

    private static WindowsToast Toast(string source, uint id, DateTimeOffset created, string title) => new(source, source, id, created, title, "body");
}
