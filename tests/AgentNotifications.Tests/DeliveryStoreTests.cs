using AgentNotifications.App.Domain;
using AgentNotifications.App.Persistence;

namespace AgentNotifications.Tests;

public sealed class DeliveryStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "agent-notifications-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Successful_delivery_clears_sensitive_content()
    {
        using var store = CreateStore();
        var now = DateTimeOffset.UtcNow;
        var observed = Notification("hash-1", "title", "body");
        Assert.True(store.UpsertObserved(observed, now, TimeSpan.Zero, now.AddHours(1)));
        var due = Assert.Single(store.GetDue(now));
        Assert.True(store.TryBegin(due, now));
        store.MarkDelivered(due.Key, due.ContentHash, "message-1", now);
        var saved = store.Get(due.Key)!;
        Assert.Equal(DeliveryState.Delivered, saved.State);
        Assert.Null(saved.Title);
        Assert.Null(saved.Body);
        Assert.Equal("message-1", saved.FeishuMessageId);
    }

    [Fact]
    public void Newer_revision_survives_older_in_flight_send()
    {
        using var store = CreateStore();
        var now = DateTimeOffset.UtcNow;
        var original = Notification("hash-1", "first", "body");
        store.UpsertObserved(original, now, TimeSpan.Zero, now.AddHours(1));
        var sending = Assert.Single(store.GetDue(now));
        Assert.True(store.TryBegin(sending, now));
        var changed = Notification("hash-2", "second", "new body");
        store.UpsertObserved(changed, now.AddMilliseconds(1), TimeSpan.Zero, now.AddHours(1));
        store.MarkDelivered(sending.Key, sending.ContentHash, "message-1", now.AddMilliseconds(2));
        var saved = store.Get(sending.Key)!;
        Assert.Equal(DeliveryState.Debouncing, saved.State);
        Assert.Equal("second", saved.Title);
        Assert.Equal("new body", saved.Body);
        Assert.Equal("message-1", saved.FeishuMessageId);
    }

    [Fact]
    public void Interrupted_sends_are_recovered_for_retry()
    {
        using var store = CreateStore();
        var now = DateTimeOffset.UtcNow;
        store.UpsertObserved(Notification("hash", "title", "body"), now, TimeSpan.Zero, now.AddHours(1));
        var record = Assert.Single(store.GetDue(now));
        store.TryBegin(record, now);
        store.RecoverInterrupted(now.AddSeconds(1));
        Assert.Equal(DeliveryState.RetryScheduled, store.Get(record.Key)!.State);
    }

    [Fact]
    public void Duplicate_content_is_suppressed()
    {
        using var store = CreateStore();
        var now = DateTimeOffset.UtcNow;
        var notification = Notification("hash", "title", "body");
        Assert.True(store.UpsertObserved(notification, now, TimeSpan.Zero, now.AddHours(1)));
        Assert.False(store.UpsertObserved(notification, now.AddSeconds(1), TimeSpan.Zero, now.AddHours(1)));
        Assert.Equal(1, store.GetCounts().Pending);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private DeliveryStore CreateStore() => new(Path.Combine(directory, "deliveries.db"));

    private static ObservedNotification Notification(string hash, string title, string body) => new(
        "source\u001f1\u001f100", "source", "App", 1, DateTimeOffset.FromUnixTimeMilliseconds(100), title, body, hash);
}
