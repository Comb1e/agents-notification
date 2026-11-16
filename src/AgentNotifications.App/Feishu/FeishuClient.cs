using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentNotifications.App.Domain;
using AgentNotifications.App.Infrastructure;

namespace AgentNotifications.App.Feishu;

public sealed class FeishuClient(HttpClient httpClient, FeishuCardBuilder cardBuilder, IClock clock) : IFeishuClient
{
    private static readonly HashSet<int> AuthenticationCodes = [99991663, 99991664, 99991668];
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string? token;
    private string? tokenKey;
    private DateTimeOffset tokenExpiresAt;

    public async Task<FeishuMessage> SendAsync(FeishuDestination destination, DeliveryRecord notification, CancellationToken cancellationToken)
    {
        var endpoint = $"{BaseUrl(destination.Domain)}/open-apis/im/v1/messages?receive_id_type={Uri.EscapeDataString(destination.RecipientType)}";
        var response = await SendAuthorizedAsync(destination, HttpMethod.Post, endpoint, new
        {
            receive_id = destination.Recipient,
            msg_type = "interactive",
            content = cardBuilder.Build(notification),
        }, cancellationToken);
        var messageId = response.Data?.MessageId;
        if (string.IsNullOrWhiteSpace(messageId)) throw new FeishuException(FeishuFailureKind.Permanent, "Feishu response did not contain a message ID");
        return new FeishuMessage(messageId);
    }

    public async Task UpdateAsync(FeishuDestination destination, string messageId, DeliveryRecord notification, CancellationToken cancellationToken)
    {
        var endpoint = $"{BaseUrl(destination.Domain)}/open-apis/im/v1/messages/{Uri.EscapeDataString(messageId)}";
        await SendAuthorizedAsync(destination, HttpMethod.Patch, endpoint, new { content = cardBuilder.Build(notification) }, cancellationToken);
    }

    private async Task<ApiResponse> SendAuthorizedAsync(FeishuDestination destination, HttpMethod method, string endpoint, object payload, CancellationToken cancellationToken)
    {
        var bearer = await GetTokenAsync(destination, false, cancellationToken);
        var response = await SendCoreAsync(method, endpoint, payload, bearer, cancellationToken);
        if (response.AuthenticationFailed)
        {
            bearer = await GetTokenAsync(destination, true, cancellationToken);
            response = await SendCoreAsync(method, endpoint, payload, bearer, cancellationToken);
        }

        if (!response.Success) throw response.Exception!;
        return response.Value!;
    }

    private async Task<string> GetTokenAsync(FeishuDestination destination, bool forceRefresh, CancellationToken cancellationToken)
    {
        var destinationKey = $"{destination.Domain}\u001f{destination.AppId}\u001f{TextTools.Sha256(destination.AppSecret)}";
        if (!forceRefresh && token is not null && tokenKey == destinationKey && tokenExpiresAt > clock.UtcNow.AddMinutes(1)) return token;
        await tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && token is not null && tokenKey == destinationKey && tokenExpiresAt > clock.UtcNow.AddMinutes(1)) return token;
            try
            {
                using var response = await httpClient.PostAsJsonAsync(
                    $"{BaseUrl(destination.Domain)}/open-apis/auth/v3/tenant_access_token/internal",
                    new { app_id = destination.AppId, app_secret = destination.AppSecret },
                    cancellationToken);
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var parsed = Parse(json);
                if (!response.IsSuccessStatusCode || parsed.Code != 0 || string.IsNullOrWhiteSpace(parsed.TenantAccessToken))
                    throw Classify(response.StatusCode, parsed.Code, parsed.Message, RetryAfter(response));
                token = parsed.TenantAccessToken;
                tokenKey = destinationKey;
                tokenExpiresAt = clock.UtcNow.AddSeconds(Math.Max(60, parsed.Expire));
                return token;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new FeishuException(FeishuFailureKind.Transient, "Feishu token request timed out");
            }
            catch (HttpRequestException exception)
            {
                throw new FeishuException(FeishuFailureKind.Transient, "Feishu token request failed", innerException: exception);
            }
        }
        finally
        {
            tokenLock.Release();
        }
    }

    private async Task<ResponseResult> SendCoreAsync(HttpMethod method, string endpoint, object payload, string bearer, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, endpoint)
            {
                Content = JsonContent.Create(payload),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = Parse(json);
            if (response.IsSuccessStatusCode && parsed.Code == 0) return ResponseResult.FromValue(parsed);
            var exception = Classify(response.StatusCode, parsed.Code, parsed.Message, RetryAfter(response));
            return new ResponseResult(null, exception, exception.Kind == FeishuFailureKind.Authentication);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ResponseResult.FromError(new FeishuException(FeishuFailureKind.Transient, "Feishu request timed out"));
        }
        catch (HttpRequestException exception)
        {
            return ResponseResult.FromError(new FeishuException(FeishuFailureKind.Transient, "Feishu network request failed", innerException: exception));
        }
    }

    private static FeishuException Classify(HttpStatusCode status, int code, string? message, TimeSpan? retryAfter)
    {
        var safeMessage = string.IsNullOrWhiteSpace(message) ? $"Feishu returned HTTP {(int)status}" : message;
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden || AuthenticationCodes.Contains(code))
            return new FeishuException(FeishuFailureKind.Authentication, safeMessage);
        if (status == HttpStatusCode.NotFound)
            return new FeishuException(FeishuFailureKind.NotFound, safeMessage);
        if ((int)status == 429)
            return new FeishuException(FeishuFailureKind.RateLimited, safeMessage, retryAfter);
        if ((int)status >= 500 || status == HttpStatusCode.RequestTimeout)
            return new FeishuException(FeishuFailureKind.Transient, safeMessage);
        return new FeishuException(FeishuFailureKind.Permanent, safeMessage);
    }

    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta) return delta;
        if (response.Headers.RetryAfter?.Date is not { } date) return null;
        return date <= clock.UtcNow ? TimeSpan.Zero : date - clock.UtcNow;
    }

    private static ApiResponse Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ApiResponse>(json, JsonOptions) ?? new ApiResponse { Code = -1, Message = "Empty Feishu response" };
        }
        catch (JsonException)
        {
            return new ApiResponse { Code = -1, Message = "Malformed Feishu response" };
        }
    }

    private static string BaseUrl(string domain) => domain == "lark" ? "https://open.larksuite.com" : "https://open.feishu.cn";

    private sealed class ApiResponse
    {
        public int Code { get; set; }

        [JsonPropertyName("msg")]
        public string? Message { get; set; }

        [JsonPropertyName("tenant_access_token")]
        public string? TenantAccessToken { get; set; }

        [JsonPropertyName("expire")]
        public int Expire { get; set; }

        public ApiData? Data { get; set; }
    }

    private sealed class ApiData
    {
        [JsonPropertyName("message_id")]
        public string? MessageId { get; set; }
    }

    private sealed record ResponseResult(ApiResponse? Value, FeishuException? Exception, bool AuthenticationFailed)
    {
        public bool Success => Value is not null;

        public static ResponseResult FromValue(ApiResponse value) => new(value, null, false);

        public static ResponseResult FromError(FeishuException exception) => new(null, exception, exception.Kind == FeishuFailureKind.Authentication);
    }
}
