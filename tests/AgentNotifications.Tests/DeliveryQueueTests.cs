using AgentNotifications.App.Configuration;
using AgentNotifications.App.Domain;
using AgentNotifications.App.Infrastructure;
using AgentNotifications.App.Persistence;
using AgentNotifications.App.Services;

namespace AgentNotifications.Tests;

public sealed class DeliveryQueueTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "agent-notifications-queue-tests", Guid.NewGuid().ToString("N"));

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(directory);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Missing_update_sends_replacement_card()
    {
        var now = DateTimeOffset.Parse("2026-09-19T12:00:00Z");
        using var store = Store();
        var original = Notification("hash-1", "first", "body");
        store.UpsertObserved(original, now, TimeSpan.Zero, now.AddHours(1));
        var first = Assert.Single(store.GetDue(now));
        store.TryBegin(first, now);
        store.MarkDelivered(first.Key, first.ContentHash, "old-message", now);
        store.UpsertObserved(Notification("hash-2", "second", "new body"), now, TimeSpan.Zero, now.AddHours(1));

        var feishu = new FakeFeishuClient
        {
            UpdateHandler = (_, _, _, _) => throw new FeishuException(FeishuFailureKind.NotFound, "missing"),
            SendHandler = (_, _, _) => Task.FromResult(new FeishuMessage("replacement")),
        };
        await using var queue = Queue(store, feishu, new FakeClock(now));
        queue.Start();
        queue.Wake();
        await TestWait.UntilAsync(() => store.Get(original.Key)?.State == DeliveryState.Delivered);

        Assert.Equal(1, feishu.UpdateCalls);
        Assert.Equal(1, feishu.SendCalls);
        Assert.Equal("replacement", store.Get(original.Key)!.FeishuMessageId);
        Assert.DoesNotContain("new body", ReadLogs());
    }

    [Fact]
    public async Task Rate_limit_honors_retry_after()
    {
        var now = DateTimeOffset.Parse("2026-09-19T12:00:00Z");
        using var store = Store();
        var notification = Notification("hash", "title", "body");
        store.UpsertObserved(notification, now, TimeSpan.Zero, now.AddHours(1));
        var feishu = new FakeFeishuClient
        {
            SendHandler = (_, _, _) => throw new FeishuException(FeishuFailureKind.RateLimited, "slow down", TimeSpan.FromSeconds(9)),
        };
        await using var queue = Queue(store, feishu, new FakeClock(now));
        queue.Start();
        queue.Wake();
        await TestWait.UntilAsync(() => store.Get(notification.Key)?.State == DeliveryState.RetryScheduled);
        Assert.Equal(now.AddSeconds(9), store.Get(notification.Key)!.NextAttemptAt);
    }

    [Fact]
    public async Task Permanent_failure_is_redacted_and_dead_lettered()
    {
        var now = DateTimeOffset.Parse("2026-09-19T12:00:00Z");
        using var store = Store();
        var notification = Notification("hash", "title", "body");
        store.UpsertObserved(notification, now, TimeSpan.Zero, now.AddHours(1));
        var settings = Settings();
        settings.RedactionPatterns = ["token=[^ ]+"];
        var feishu = new FakeFeishuClient
        {
            SendHandler = (_, _, _) => throw new FeishuException(FeishuFailureKind.Permanent, "token=super-secret rejected"),
        };
        await using var queue = Queue(store, feishu, new FakeClock(now), settings);
        queue.Start();
        queue.Wake();
        await TestWait.UntilAsync(() => store.Get(notification.Key)?.State == DeliveryState.DeadLetter);
        Assert.Equal("[REDACTED] rejected", store.Get(notification.Key)!.LastError);
    }

    [Fact]
    public async Task Expired_work_is_dead_lettered_without_network_call()
    {
        var now = DateTimeOffset.Parse("2026-09-19T12:00:00Z");
        using var store = Store();
        var notification = Notification("hash", "title", "body");
        store.UpsertObserved(notification, now.AddDays(-2), TimeSpan.Zero, now.AddSeconds(-1));
        var feishu = new FakeFeishuClient();
        await using var queue = Queue(store, feishu, new FakeClock(now));
        queue.Start();
        queue.Wake();
        await TestWait.UntilAsync(() => store.Get(notification.Key)?.State == DeliveryState.DeadLetter);
        Assert.Equal(0, feishu.SendCalls);
    }

    private DeliveryStore Store() => new(Path.Combine(directory, "deliveries.db"));

    private DeliveryQueue Queue(DeliveryStore store, FakeFeishuClient feishu, FakeClock clock, AppSettings? settings = null)
    {
        settings ??= Settings();
        var paths = new AppPaths(
            directory,
            Path.Combine(directory, "config.json"),
            Path.Combine(directory, "secrets.dat"),
            Path.Combine(directory, "deliveries.db"),
            Path.Combine(directory, "logs"));
        return new DeliveryQueue(
            store,
            feishu,
            () => new FeishuDestination("feishu", "app-id", "secret", "email", "owner@example.com"),
            () => settings,
            clock,
            new FakeRandom(0),
            new AppLogger(paths, settings.Runtime));
    }

    private string ReadLogs() => Directory.Exists(Path.Combine(directory, "logs"))
        ? string.Join(Environment.NewLine, Directory.GetFiles(Path.Combine(directory, "logs")).Select(File.ReadAllText))
        : string.Empty;

    private static AppSettings Settings() => new()
    {
        Feishu = new FeishuSettings { AppId = "app-id", Recipient = "owner@example.com" },
        SourceRules = [new SourceRuleSettings { Name = "Qoder", Pattern = "com.qoder.app", MatchMode = "exact" }],
        Runtime = new RuntimeSettings { DispatcherPollMilliseconds = 50 },
    };

    private static ObservedNotification Notification(string hash, string title, string body) => new(
        "source\u001f1\u001f100", "source", "Qoder", 1, DateTimeOffset.FromUnixTimeMilliseconds(100), title, body, hash);
}
