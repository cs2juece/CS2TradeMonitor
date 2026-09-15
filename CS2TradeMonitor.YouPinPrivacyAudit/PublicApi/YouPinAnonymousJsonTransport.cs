using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi
{
    /// <summary>
    /// Centralizes the credential-free, rate-limited HTTP policy for all public audit reads.
    /// </summary>
    internal sealed class YouPinAnonymousJsonTransport : IDisposable
    {
        internal static TimeSpan DefaultMinimumInterval { get; } = TimeSpan.FromSeconds(2);
        internal static TimeSpan DefaultRequestTimeout { get; } = TimeSpan.FromSeconds(10);
        internal const long MaximumResponseBytes = 1024 * 1024;

        private static readonly HashSet<string> SensitiveHeaderNames = new(
            new[]
            {
                "Authorization",
                "Cookie",
                "Device-Info",
                "DeviceId",
                "DeviceToken",
                "deviceUk",
                "Proxy-Authorization",
                "requestTag",
                "signature",
                "uk",
                "X-Api-Key"
            },
            StringComparer.OrdinalIgnoreCase);

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly HttpClient _httpClient;
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _minimumInterval;
        private readonly TimeSpan _requestTimeout;
        private readonly SemaphoreSlim _requestGate = new(initialCount: 1, maxCount: 1);
        private DateTimeOffset? _lastRequestStartedAt;
        private int _disposed;

        internal YouPinAnonymousJsonTransport(
            HttpClient httpClient,
            TimeProvider? timeProvider,
            TimeSpan? minimumInterval,
            TimeSpan? requestTimeout)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            EnsureAnonymousClient(httpClient);

            _timeProvider = timeProvider ?? TimeProvider.System;
            _minimumInterval = minimumInterval ?? DefaultMinimumInterval;
            _requestTimeout = requestTimeout ?? DefaultRequestTimeout;

            if (_minimumInterval < TimeSpan.Zero || _minimumInterval > TimeSpan.FromMinutes(1))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumInterval),
                    "最小请求间隔必须在 0 到 1 分钟之间。");
            }

            if (_requestTimeout <= TimeSpan.Zero || _requestTimeout > TimeSpan.FromMinutes(1))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requestTimeout),
                    "请求超时必须大于 0 且不超过 1 分钟。");
            }
        }

        internal async Task<YouPinJsonResponse<TResponse>> PostAsync<TRequest, TResponse>(
            Uri endpoint,
            TRequest body,
            YouPinPublicOperation operation,
            CancellationToken cancellationToken)
            where TResponse : class
        {
            ArgumentNullException.ThrowIfNull(endpoint);

            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await WaitForRateLimitAsync(cancellationToken).ConfigureAwait(false);
                _lastRequestStartedAt = _timeProvider.GetUtcNow();

                return await SendAsync<TRequest, TResponse>(
                    endpoint,
                    body,
                    operation,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _requestGate.Release();
            }
        }

        private async Task<YouPinJsonResponse<TResponse>> SendAsync<TRequest, TResponse>(
            Uri endpoint,
            TRequest body,
            YouPinPublicOperation operation,
            CancellationToken cancellationToken)
            where TResponse : class
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(body, options: JsonOptions)
            };
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using ITimer timeoutTimer = _timeProvider.CreateTimer(
                _ => timeoutSource.Cancel(),
                state: null,
                _requestTimeout,
                Timeout.InfiniteTimeSpan);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw operation.Failure("timeout", $"{operation.DisplayName}超时。");
            }
            catch (HttpRequestException)
            {
                throw operation.Failure("network_error", $"无法连接{operation.DisplayName}。");
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw operation.Failure(
                        "http_error",
                        $"{operation.DisplayName}返回 HTTP {(int)response.StatusCode}。",
                        response.StatusCode);
                }

                if (response.Content.Headers.ContentLength > MaximumResponseBytes)
                {
                    throw operation.Failure(
                        "response_too_large",
                        $"{operation.DisplayName}返回的数据超过安全上限。",
                        response.StatusCode);
                }

                try
                {
                    await response.Content.LoadIntoBufferAsync(
                        MaximumResponseBytes,
                        timeoutSource.Token).ConfigureAwait(false);

                    if ((await response.Content.ReadAsByteArrayAsync(timeoutSource.Token).ConfigureAwait(false)).Length == 0)
                        throw operation.Failure("empty_response", $"{operation.DisplayName}返回了空响应。", response.StatusCode);

                    TResponse? content = await response.Content.ReadFromJsonAsync<TResponse>(
                        JsonOptions,
                        timeoutSource.Token).ConfigureAwait(false);

                    return content is null
                        ? throw operation.Failure(
                            "empty_response",
                            $"{operation.DisplayName}返回了空数据。",
                            response.StatusCode)
                        : new YouPinJsonResponse<TResponse>(response.StatusCode, content);
                }
                catch (JsonException)
                {
                    throw operation.Failure(
                        "invalid_response",
                        $"{operation.DisplayName}返回了无法识别的数据。",
                        response.StatusCode);
                }
                catch (NotSupportedException)
                {
                    throw operation.Failure(
                        "invalid_response",
                        $"{operation.DisplayName}返回了无法识别的数据。",
                        response.StatusCode);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw operation.Failure(
                        "timeout",
                        $"{operation.DisplayName}超时。",
                        response.StatusCode);
                }
                catch (HttpRequestException)
                {
                    throw operation.Failure(
                        "invalid_response",
                        $"{operation.DisplayName}返回的数据不符合安全限制。",
                        response.StatusCode);
                }
            }
        }

        private async Task WaitForRateLimitAsync(CancellationToken cancellationToken)
        {
            if (_minimumInterval == TimeSpan.Zero || _lastRequestStartedAt is null)
                return;

            TimeSpan remaining = _minimumInterval
                - (_timeProvider.GetUtcNow() - _lastRequestStartedAt.Value);
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, _timeProvider, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private static void EnsureAnonymousClient(HttpClient httpClient)
        {
            string? sensitiveHeader = httpClient.DefaultRequestHeaders
                .Select(header => header.Key)
                .FirstOrDefault(SensitiveHeaderNames.Contains);

            if (sensitiveHeader is not null)
            {
                throw new ArgumentException(
                    $"公开验证必须使用不含 {sensitiveHeader} 的专用匿名 HttpClient。",
                    nameof(httpClient));
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _requestGate.Dispose();
        }
    }

    internal readonly record struct YouPinJsonResponse<T>(HttpStatusCode StatusCode, T Content);

    internal readonly record struct YouPinPublicOperation(string ReasonCodePrefix, string DisplayName)
    {
        internal YouPinPublicApiException Failure(
            string reason,
            string message,
            HttpStatusCode? statusCode = null,
            int? platformCode = null)
            => new($"{ReasonCodePrefix}.{reason}", message, statusCode, platformCode);
    }
}
