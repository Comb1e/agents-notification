using System.Net;
using System.Text;
using AgentNotifications.App.Domain;
using AgentNotifications.App.Feishu;
using AgentNotifications.App.Infrastructure;
using AgentNotifications.App.Notifications;

namespace AgentNotifications.Tests;

internal sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

internal sealed class FakeRandom(double value) : IRandomSource
{
    public double NextDouble() => value;
}

internal sealed class FakeNotificationSource : IWindowsNotificationSource
{
    public event EventHandler? Changed;

    public NotificationAccess Access { get; set; } = NotificationAccess.Allowed;

    public IReadOnlyList<WindowsToast> Snapshot { get; set; } = [];

    public Func<CancellationToken, Task<IReadOnlyList<WindowsToast>>>? SnapshotHandler { get; set; }

    public NotificationAccess GetAccessStatus() => Access;

    public Task<NotificationAccess> RequestAccessAsync() => Task.FromResult(Access);

    public Task<IReadOnlyList<WindowsToast>> GetSnapshotAsync(CancellationToken cancellationToken) =>
        SnapshotHandler?.Invoke(cancellationToken) ?? Task.FromResult(Snapshot);

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

internal sealed class QueueHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> responses = new();

    public List<HttpRequestMessageSnapshot> Requests { get; } = [];

    public void Enqueue(HttpStatusCode status, string json, TimeSpan? retryAfter = null, DateTimeOffset? retryAfterDate = null)
    {
        responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            if (retryAfter is not null) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter.Value);
            if (retryAfterDate is not null) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfterDate.Value);
            return response;
        });
    }

    public void EnqueueException(Exception exception) => responses.Enqueue(_ => throw exception);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var content = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new HttpRequestMessageSnapshot(request.Method, request.RequestUri!, request.Headers.Authorization?.Parameter, content));
        if (responses.Count == 0) throw new InvalidOperationException("No queued HTTP response");
        return responses.Dequeue()(request);
    }
}

internal sealed record HttpRequestMessageSnapshot(HttpMethod Method, Uri Uri, string? Bearer, string Content);

internal sealed class FakeFeishuClient : IFeishuClient
{
    public int SendCalls { get; private set; }

    public int UpdateCalls { get; private set; }

    public Func<FeishuDestination, DeliveryRecord, CancellationToken, Task<FeishuMessage>>? SendHandler { get; set; }

    public Func<FeishuDestination, string, DeliveryRecord, CancellationToken, Task>? UpdateHandler { get; set; }

    public Task<FeishuMessage> SendAsync(FeishuDestination destination, DeliveryRecord notification, CancellationToken cancellationToken)
    {
        SendCalls++;
        return SendHandler?.Invoke(destination, notification, cancellationToken) ?? Task.FromResult(new FeishuMessage($"message-{SendCalls}"));
    }

    public Task UpdateAsync(FeishuDestination destination, string messageId, DeliveryRecord notification, CancellationToken cancellationToken)
    {
        UpdateCalls++;
        return UpdateHandler?.Invoke(destination, messageId, notification, cancellationToken) ?? Task.CompletedTask;
    }
}

internal static class TestWait
{
    public static async Task UntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var expires = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(3));
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= expires) throw new TimeoutException("Condition was not met");
            await Task.Delay(20);
        }
    }
}
