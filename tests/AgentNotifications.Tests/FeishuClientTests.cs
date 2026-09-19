using System.Net;
using AgentNotifications.App.Configuration;
using AgentNotifications.App.Domain;
using AgentNotifications.App.Feishu;

namespace AgentNotifications.Tests;

public sealed class FeishuClientTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-19T12:00:00Z");

    [Fact]
    public async Task Sends_card_with_cached_bearer_token()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"code\":0,\"tenant_access_token\":\"token-1\",\"expire\":7200}");
        handler.Enqueue(HttpStatusCode.OK, "{\"code\":0,\"data\":{\"message_id\":\"message-1\"}}");
        handler.Enqueue(HttpStatusCode.OK, "{\"code\":0,\"data\":{\"message_id\":\"message-2\"}}");
        var client = Client(handler);
        Assert.Equal("message-1", (await client.SendAsync(Destination(), Record(), CancellationToken.None)).MessageId);
        Assert.Equal("message-2", (await client.SendAsync(Destination(), Record(), CancellationToken.None)).MessageId);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("token-1", handler.Requests[1].Bearer);
        Assert.Contains("interactive", handler.Requests[1].Content);
    }

    [Fact]
    public async Task Refreshes_token_once_after_authentication_failure()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"code\":0,\"tenant_access_token\":\"old\",\"expire\":7200}");
        handler.Enqueue(HttpStatusCode.Unauthorized, "{\"code\":99991663,\"msg\":\"expired\"}");
        handler.Enqueue(HttpStatusCode.OK, "{\"code\":0,\"tenant_access_token\":\"new\",\"expire\":7200}");
        handler.Enqueue(HttpStatusCode.OK, "{\"code\":0,\"data\":{\"message_id\":\"message-1\"}}");
        var result = await Client(handler).SendAsync(Destination(), Record(), CancellationToken.None);
        Assert.Equal("message-1", result.MessageId);
        Assert.Equal("new", handler.Requests[^1].Bearer);
    }

    [Fact]
    public async Task Classifies_rate_limit_and_retry_after()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"code\":0,\"tenant_access_token\":\"token\",\"expire\":7200}");
        handler.Enqueue((HttpStatusCode)429, "{\"code\":1,\"msg\":\"slow down\"}", TimeSpan.FromSeconds(9));
        var exception = await Assert.ThrowsAsync<FeishuException>(() => Client(handler).SendAsync(Destination(), Record(), CancellationToken.None));
        Assert.Equal(FeishuFailureKind.RateLimited, exception.Kind);
        Assert.Equal(TimeSpan.FromSeconds(9), exception.RetryAfter);
    }

    [Fact]
    public async Task Classifies_missing_message_on_update()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"code\":0,\"tenant_access_token\":\"token\",\"expire\":7200}");
        handler.Enqueue(HttpStatusCode.NotFound, "{\"code\":230001,\"msg\":\"not found\"}");
        var exception = await Assert.ThrowsAsync<FeishuException>(() => Client(handler).UpdateAsync(Destination(), "missing", Record(), CancellationToken.None));
        Assert.Equal(FeishuFailureKind.NotFound, exception.Kind);
    }

    [Fact]
    public async Task Classifies_invalid_credentials()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, "{\"code\":99991663,\"msg\":\"invalid credentials\"}");
        var exception = await Assert.ThrowsAsync<FeishuException>(() => Client(handler).SendAsync(Destination(), Record(), CancellationToken.None));
        Assert.Equal(FeishuFailureKind.Authentication, exception.Kind);
    }

    [Fact]
    public async Task Classifies_token_timeout_as_transient()
    {
        var handler = new QueueHttpHandler();
        handler.EnqueueException(new TaskCanceledException("timeout"));
        var exception = await Assert.ThrowsAsync<FeishuException>(() => Client(handler).SendAsync(Destination(), Record(), CancellationToken.None));
        Assert.Equal(FeishuFailureKind.Transient, exception.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, FeishuFailureKind.Permanent)]
    [InlineData(HttpStatusCode.InternalServerError, FeishuFailureKind.Transient)]
    [InlineData(HttpStatusCode.RequestTimeout, FeishuFailureKind.Transient)]
    public async Task Classifies_http_failures(HttpStatusCode status, FeishuFailureKind expected)
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"code\":0,\"tenant_access_token\":\"token\",\"expire\":7200}");
        handler.Enqueue(status, "{\"code\":1,\"msg\":\"request failed\"}");
        var exception = await Assert.ThrowsAsync<FeishuException>(() => Client(handler).SendAsync(Destination(), Record(), CancellationToken.None));
        Assert.Equal(expected, exception.Kind);
    }

    [Fact]
    public async Task Updates_cards_with_patch()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"code\":0,\"tenant_access_token\":\"token\",\"expire\":7200}");
        handler.Enqueue(HttpStatusCode.OK, "{\"code\":0}");
        await Client(handler).UpdateAsync(Destination(), "message-1", Record(), CancellationToken.None);
        Assert.Equal(HttpMethod.Patch, handler.Requests[1].Method);
        Assert.EndsWith("/open-apis/im/v1/messages/message-1", handler.Requests[1].Uri.AbsoluteUri);
    }

    [Fact]
    public async Task Honors_retry_after_http_date()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"code\":0,\"tenant_access_token\":\"token\",\"expire\":7200}");
        handler.Enqueue((HttpStatusCode)429, "{\"code\":1,\"msg\":\"slow down\"}", retryAfterDate: Now.AddSeconds(12));
        var exception = await Assert.ThrowsAsync<FeishuException>(() => Client(handler).SendAsync(Destination(), Record(), CancellationToken.None));
        Assert.Equal(TimeSpan.FromSeconds(12), exception.RetryAfter);
    }

    [Fact]
    public async Task Uses_lark_domain()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"code\":0,\"tenant_access_token\":\"token\",\"expire\":7200}");
        handler.Enqueue(HttpStatusCode.OK, "{\"code\":0,\"data\":{\"message_id\":\"message-1\"}}");
        await Client(handler).SendAsync(Destination() with { Domain = "lark" }, Record(), CancellationToken.None);
        Assert.All(handler.Requests, request => Assert.Equal("open.larksuite.com", request.Uri.Host));
    }

    private static FeishuClient Client(QueueHttpHandler handler) => new(
        new HttpClient(handler),
        new FeishuCardBuilder(new RuntimeSettings()),
        new FakeClock(Now));

    private static FeishuDestination Destination() => new("feishu", "app-id", "secret", "email", "owner@example.com");

    private static DeliveryRecord Record() => new(
        "key", "source", "Qoder", 1, Now, Now, "Finished", "Task completed", "hash", null,
        DeliveryState.Sending, 1, Now, Now.AddHours(1), null, Now);
}
