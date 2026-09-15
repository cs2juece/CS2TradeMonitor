using System.Net;
using System.Text.Json;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi;
using CS2TradeMonitor.Domain.YouPin;

namespace CS2TradeMonitor.Application.YouPin.PurchaseMonitoring;

/// <summary>Dedicated authenticated read transport. No anonymous fallback, cookies, redirects or trade writes.</summary>
internal sealed class AuthenticatedPurchaseClient : IYouPinPublicAuditClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly PurchaseAccounts _accounts;
    private readonly PurchaseAccountBinding _binding;
    private readonly PurchaseReadGate _gate;
    public AuthenticatedPurchaseClient(PurchaseAccounts accounts, PurchaseAccountBinding binding, PurchaseReadGate gate,
        HttpClient? http = null)
    {
        _accounts = accounts;
        _binding = binding;
        _gate = gate;
        _http = http ?? new HttpClient(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(20) };
    }

    public Task<YouPinPurchaseExposureResult> FindPurchaseExposureAsync(long targetUserId,
        YouPinPurchaseAuditScope scope, CancellationToken cancellationToken = default)
        => new YouPinPurchasePageReader(ReadAsync).FindPurchaseExposureAsync(targetUserId, scope, cancellationToken);

    // Store metadata remains public and does not receive login headers.
    public async Task<YouPinPublicStoreSummary> GetStoreSummaryAsync(long userId, CancellationToken cancellationToken = default)
    {
        using var client = YouPinPublicAuditClient.CreateStandalone();
        return await client.GetStoreSummaryAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<YouPinPurchasePageReader.PageResponse> ReadAsync(long templateId, int pageIndex, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            YouPinCredential credential = _accounts.Resolve(_binding);
            using var request = new HttpRequestMessage(HttpMethod.Post, YouPinPublicAuditClient.PurchaseOrderPageEndpoint)
            {
                Content = YouPinMobileApiClient.JsonContent(new
                { pageIndex, pageSize = YouPinPublicAuditClient.PageSize, showMaxPriceFlag = false, templateId })
            };
            YouPinMobileApiClient.ApplyHeaders(request, credential.Token, credential.DeviceToken, credential.Uk);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                    _accounts.PauseAccess($"求购读取 HTTP {(int)response.StatusCode}，自动读取已暂停，请检查登录或平台限制后手动恢复。");
                throw Failure("http_error", response.StatusCode);
            }
            await response.Content.LoadIntoBufferAsync(1024 * 1024, timeout.Token).ConfigureAwait(false);
            byte[] bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
            if (bytes.Length == 0) throw Failure("empty_response", response.StatusCode);
            var content = JsonSerializer.Deserialize<YouPinPurchasePageReader.PurchasePageEnvelope>(bytes, JsonOptions);
            if (content is null) throw Failure("empty_response", response.StatusCode);
            // Prevent publishing a response from an account replaced while the request was in flight.
            _accounts.Resolve(_binding);
            if (content.Code == 84101)
                _accounts.PauseAccess("求购读取要求登录（84101），自动读取已暂停，请验证所选账号后手动恢复。");
            return new(response.StatusCode, content);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw Failure("timeout"); }
        catch (HttpRequestException) { throw Failure("network_error"); }
        catch (JsonException) { throw Failure("invalid_response"); }
        finally { _gate.Release(); }
    }

    private static YouPinPublicApiException Failure(string reason, HttpStatusCode? status = null)
        => new("youpin.public_purchase." + reason, "求购读取失败，旧数据已保留。", status);
    public void Dispose() => _http.Dispose();
}

/// <summary>One queue across shops and batches; changing account never resets its interval.</summary>
internal sealed class PurchaseReadGate(TimeSpan? interval = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _interval = interval ?? TimeSpan.FromSeconds(10);
    private DateTimeOffset _lastRequest;
    public async Task WaitAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            TimeSpan delay = _interval - (DateTimeOffset.UtcNow - _lastRequest);
            if (delay > TimeSpan.Zero) await Task.Delay(delay, token).ConfigureAwait(false);
            _lastRequest = DateTimeOffset.UtcNow;
        }
        catch { _gate.Release(); throw; }
    }
    public void Release() => _gate.Release();
}
