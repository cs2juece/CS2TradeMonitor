using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Safety;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi
{
    /// <summary>
    /// Deep public-audit module: callers provide explicit IDs and receive minimal projections;
    /// anonymous transport policy, pagination bounds, and response redaction stay internal.
    /// </summary>
    public sealed class YouPinPublicAuditClient : IYouPinPublicAuditClient, IDisposable
    {
        public static readonly Uri StoreSummaryEndpoint = new(
            "https://api.youpin898.com/api/youpin/bff/commodity/user/store/aggregation/info");
        public static readonly Uri PurchaseOrderPageEndpoint = new(
            "https://api.youpin898.com/api/youpin/bff/trade/purchase/order/getTemplatePurchaseOrderPageList");

        public static TimeSpan DefaultMinimumInterval => YouPinAnonymousJsonTransport.DefaultMinimumInterval;
        public static TimeSpan DefaultRequestTimeout => YouPinAnonymousJsonTransport.DefaultRequestTimeout;
        public const int PageSize = 100;
        public const int MaximumPagesPerTemplate = 3;

        private static readonly YouPinPublicOperation StoreOperation = new(
            "youpin.public_store",
            "悠悠公开店铺接口");
        private static readonly YouPinPublicOperation PurchaseOperation = new(
            "youpin.public_purchase",
            "悠悠公开求购接口");

        private readonly YouPinAnonymousJsonTransport _transport;
        private readonly HttpClient? _ownedHttpClient;
        private int _disposed;

        public YouPinPublicAuditClient(
            HttpClient httpClient,
            TimeProvider? timeProvider = null,
            TimeSpan? minimumInterval = null,
            TimeSpan? requestTimeout = null)
            : this(
                httpClient,
                ownsHttpClient: false,
                timeProvider,
                minimumInterval,
                requestTimeout)
        {
        }

        private YouPinPublicAuditClient(
            HttpClient httpClient,
            bool ownsHttpClient,
            TimeProvider? timeProvider,
            TimeSpan? minimumInterval,
            TimeSpan? requestTimeout)
        {
            _transport = new YouPinAnonymousJsonTransport(
                httpClient,
                timeProvider,
                minimumInterval,
                requestTimeout);
            _ownedHttpClient = ownsHttpClient ? httpClient : null;
        }

        /// <summary>
        /// Creates a standalone client whose handler cannot send cookies or follow redirects.
        /// Dispose the returned client when the audit is complete.
        /// </summary>
        public static YouPinPublicAuditClient CreateStandalone(
            TimeProvider? timeProvider = null,
            TimeSpan? minimumInterval = null,
            TimeSpan? requestTimeout = null)
        {
            var handler = new SocketsHttpHandler
            {
                UseCookies = false,
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            };
            var httpClient = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

            try
            {
                return new YouPinPublicAuditClient(
                    httpClient,
                    ownsHttpClient: true,
                    timeProvider,
                    minimumInterval,
                    requestTimeout);
            }
            catch
            {
                httpClient.Dispose();
                throw;
            }
        }

        public async Task<YouPinPublicStoreSummary> GetStoreSummaryAsync(
            long userId,
            CancellationToken cancellationToken = default)
        {
            EnsurePositiveId(userId, nameof(userId), "用户 ID 必须为正整数。");

            YouPinJsonResponse<StoreAggregationEnvelope> response = await _transport.PostAsync<
                StoreAggregationRequest,
                StoreAggregationEnvelope>(
                    StoreSummaryEndpoint,
                    new StoreAggregationRequest(
                        userId.ToString(CultureInfo.InvariantCulture),
                        "user_store"),
                    StoreOperation,
                    cancellationToken).ConfigureAwait(false);

            StoreAggregationEnvelope envelope = response.Content;
            if (envelope.Code != 0)
            {
                throw StoreOperation.Failure(
                    "platform_error",
                    $"悠悠公开店铺接口返回业务错误码 {envelope.Code}。",
                    response.StatusCode,
                    envelope.Code);
            }

            if (envelope.Data is null || string.IsNullOrWhiteSpace(envelope.Data.StoreName))
            {
                throw StoreOperation.Failure(
                    "missing_data",
                    "悠悠公开店铺接口缺少必要字段。",
                    response.StatusCode);
            }

            StoreAggregationData data = envelope.Data;
            return new YouPinPublicStoreSummary(
                userId,
                YouPinSafeText.Normalize(data.StoreName, 200)!,
                data.Status == 1,
                YouPinSafeText.Normalize(data.RegistrationDescription, 200),
                CombineMetric(data.DeliverySuccessRateNumber, data.DeliverySuccessRateUnit),
                CombineMetric(data.AverageDeliveryTimeNumber, data.AverageDeliveryTimeUnit),
                data.ShowStoreCommodity == 1,
                data.ShowStoreDynamicWall == 1);
        }

        public Task<YouPinPurchaseExposureResult> FindPurchaseExposureAsync(
            long targetUserId, YouPinPurchaseAuditScope scope, CancellationToken cancellationToken = default)
            => new YouPinPurchasePageReader(ReadPurchasePageAsync).FindPurchaseExposureAsync(targetUserId, scope, cancellationToken);

        public Task<YouPinPurchaseExposureResult> FindPurchaseExposureAsync(
            long targetUserId, IReadOnlyCollection<long> templateIds, CancellationToken cancellationToken = default)
            => FindPurchaseExposureAsync(targetUserId, YouPinPurchaseAuditScope.Create(templateIds), cancellationToken);

        private async Task<YouPinPurchasePageReader.PageResponse> ReadPurchasePageAsync(
            long templateId, int pageIndex, CancellationToken cancellationToken)
        {
            var response = await _transport.PostAsync<PurchasePageRequest, YouPinPurchasePageReader.PurchasePageEnvelope>(
                PurchaseOrderPageEndpoint, new PurchasePageRequest(pageIndex, PageSize, false, templateId),
                PurchaseOperation, cancellationToken).ConfigureAwait(false);
            return new(response.StatusCode, response.Content);
        }

        private static void EnsurePositiveId(long value, string parameterName, string message)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(parameterName, message);
        }

        private static string? CombineMetric(string? number, string? unit)
        {
            string? normalizedNumber = YouPinSafeText.Normalize(number, 40);
            return normalizedNumber is null
                ? null
                : normalizedNumber + (YouPinSafeText.Normalize(unit, 20) ?? string.Empty);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _transport.Dispose();
            _ownedHttpClient?.Dispose();
        }

        private sealed record StoreAggregationRequest(
            [property: JsonPropertyName("userId")] string UserId,
            [property: JsonPropertyName("pageType")] string PageType);

        private sealed class StoreAggregationEnvelope
        {
            [JsonPropertyName("code")]
            public int Code { get; init; }

            [JsonPropertyName("data")]
            public StoreAggregationData? Data { get; init; }
        }

        private sealed class StoreAggregationData
        {
            [JsonPropertyName("storeName")]
            public string? StoreName { get; init; }

            [JsonPropertyName("status")]
            public int Status { get; init; }

            [JsonPropertyName("regTime")]
            public string? RegistrationDescription { get; init; }

            [JsonPropertyName("deliverySuccessRateNumber")]
            public string? DeliverySuccessRateNumber { get; init; }

            [JsonPropertyName("deliverySuccessRateUnit")]
            public string? DeliverySuccessRateUnit { get; init; }

            [JsonPropertyName("avgDeliveryTimeNumber")]
            public string? AverageDeliveryTimeNumber { get; init; }

            [JsonPropertyName("avgDeliveryTimeUnit")]
            public string? AverageDeliveryTimeUnit { get; init; }

            [JsonPropertyName("showStoreCommodity")]
            public int ShowStoreCommodity { get; init; }

            [JsonPropertyName("showStoreDynamicWall")]
            public int ShowStoreDynamicWall { get; init; }
        }

        private sealed record PurchasePageRequest(
            [property: JsonPropertyName("pageIndex")] int PageIndex,
            [property: JsonPropertyName("pageSize")] int PageSize,
            [property: JsonPropertyName("showMaxPriceFlag")] bool ShowMaxPriceFlag,
            [property: JsonPropertyName("templateId")] long TemplateId);

    }
}
