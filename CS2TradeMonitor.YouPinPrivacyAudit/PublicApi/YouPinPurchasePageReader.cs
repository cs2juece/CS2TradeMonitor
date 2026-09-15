using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Safety;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi;

/// <summary>Bounded shop matching shared by anonymous audit and authenticated monitoring.</summary>
public sealed class YouPinPurchasePageReader
{
    private const string ReasonPrefix = "youpin.public_purchase";
    private const int MaximumPagesPerTemplate = YouPinPublicAuditClient.MaximumPagesPerTemplate;
    private readonly Func<long, int, CancellationToken, Task<PageResponse>> ReadPurchasePageAsync;
    public YouPinPurchasePageReader(Func<long, int, CancellationToken, Task<PageResponse>> readPage)
        => ReadPurchasePageAsync = readPage ?? throw new ArgumentNullException(nameof(readPage));
    public sealed record PageResponse(HttpStatusCode StatusCode, PurchasePageEnvelope Content);
    public static bool IsAccessFailure(YouPinTemplateQueryFailure failure)
        => failure.PlatformCode == 84101 || failure.StatusCode is HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests;
    public async Task<YouPinPurchaseExposureResult> FindPurchaseExposureAsync(
        long targetUserId,
        YouPinPurchaseAuditScope scope,
        CancellationToken cancellationToken = default)
    {
        if (targetUserId <= 0) throw new ArgumentOutOfRangeException(nameof(targetUserId));
        ArgumentNullException.ThrowIfNull(scope);

        var matches = new List<YouPinPurchaseExposureMatch>();
        var failures = new List<YouPinTemplateQueryFailure>();
        foreach (long templateId in scope.TemplateIds)
        {
            YouPinTemplateQueryFailure? rejection = failures.LastOrDefault(failure => IsAccessFailure(failure));
            if (rejection is not null)
            {
                failures.Add(new YouPinTemplateQueryFailure(templateId,
                    "youpin.public_purchase.batch_aborted", rejection.StatusCode, rejection.PlatformCode));
                continue;
            }
            await QueryTemplateAsync(
                targetUserId,
                templateId,
                matches,
                failures,
                cancellationToken).ConfigureAwait(false);
        }

        return new YouPinPurchaseExposureResult(
            targetUserId,
            scope.TemplateIds,
            matches,
            failures);
    }

    public Task<YouPinPurchaseExposureResult> FindPurchaseExposureAsync(
        long targetUserId,
        IReadOnlyCollection<long> templateIds,
        CancellationToken cancellationToken = default)
        => FindPurchaseExposureAsync(
            targetUserId,
            YouPinPurchaseAuditScope.Create(templateIds),
            cancellationToken);

    private async Task QueryTemplateAsync(
        long targetUserId,
        long templateId,
        ICollection<YouPinPurchaseExposureMatch> matches,
        ICollection<YouPinTemplateQueryFailure> failures,
        CancellationToken cancellationToken)
    {
        bool retriedEmptyResponse = false;
        for (int pageIndex = 1; pageIndex <= MaximumPagesPerTemplate; pageIndex++)
        {
            PageResponse response;
            try
            {
                try
                {
                    response = await ReadPurchasePageAsync(templateId, pageIndex, cancellationToken).ConfigureAwait(false);
                }
                catch (YouPinPublicApiException error) when (!retriedEmptyResponse
                    && error.ReasonCode == "youpin.public_purchase.empty_response")
                {
                    // A transient HTTP 200 with no bytes is not an empty order list.
                    // Retry once per template through the same rate-limited transport.
                    retriedEmptyResponse = true;
                    response = await ReadPurchasePageAsync(templateId, pageIndex, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (YouPinPublicApiException error)
            {
                failures.Add(new YouPinTemplateQueryFailure(
                    templateId,
                    error.ReasonCode,
                    error.StatusCode,
                    error.PlatformCode));
                return;
            }

            PurchasePageEnvelope envelope = response.Content;
            if (envelope.Code != 0)
            {
                failures.Add(new YouPinTemplateQueryFailure(
                    templateId,
                    $"{ReasonPrefix}.platform_error",
                    response.StatusCode,
                    envelope.Code));
                return;
            }

            if (envelope.Data?.ResponseList is null
                || !TryReadBoolean(envelope.Data.HasNext, out bool hasNext))
            {
                failures.Add(new YouPinTemplateQueryFailure(
                    templateId,
                    $"{ReasonPrefix}.missing_data",
                    response.StatusCode));
                return;
            }

            foreach (PurchasePageItem item in envelope.Data.ResponseList)
            {
                if (item.UserId != targetUserId)
                    continue;

                matches.Add(new YouPinPurchaseExposureMatch(
                    templateId,
                    pageIndex,
                    YouPinSafeText.Normalize(item.CommodityName, 200),
                    item.PurchasePrice,
                    item.SurplusQuantity,
                    YouPinSafeText.Normalize(item.AbradeText, 80),
                    YouPinSafeText.Normalize(item.FadeText, 80),
                    ReadOptionalBoolean(item.AutoReceived),
                    ReadOptionalBoolean(item.IsRankFirst)));
            }

            if (!hasNext)
                return;
        }

        failures.Add(new YouPinTemplateQueryFailure(
            templateId,
            $"{ReasonPrefix}.page_limit"));
    }

    private static bool? ReadOptionalBoolean(JsonElement value)
        => TryReadBoolean(value, out bool result) ? result : null;

    private static bool TryReadBoolean(JsonElement value, out bool result)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.True:
                result = true;
                return true;
            case JsonValueKind.False:
                result = false;
                return true;
            case JsonValueKind.Number when value.TryGetInt32(out int number)
                && (number == 0 || number == 1):
                result = number == 1;
                return true;
            case JsonValueKind.String when bool.TryParse(value.GetString(), out bool boolean):
                result = boolean;
                return true;
            case JsonValueKind.String when int.TryParse(
                value.GetString(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int number) && (number == 0 || number == 1):
                result = number == 1;
                return true;
            default:
                result = false;
                return false;
        }
    }

    public sealed class PurchasePageEnvelope
    {
        [JsonPropertyName("code")]
        public int Code { get; init; } = int.MinValue;

        [JsonPropertyName("data")]
        public PurchasePageData? Data { get; init; }
    }

    public sealed class PurchasePageData
    {
        [JsonPropertyName("hasNext")]
        public JsonElement HasNext { get; init; }

        [JsonPropertyName("responseList")]
        public IReadOnlyList<PurchasePageItem>? ResponseList { get; init; }
    }

    public sealed class PurchasePageItem
    {
        [JsonPropertyName("userId")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long UserId { get; init; }

        [JsonPropertyName("commodityName")]
        public string? CommodityName { get; init; }

        [JsonPropertyName("purchasePrice")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal? PurchasePrice { get; init; }

        [JsonPropertyName("surplusQuantity")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int? SurplusQuantity { get; init; }

        [JsonPropertyName("abradeText")]
        public string? AbradeText { get; init; }

        [JsonPropertyName("fadeText")]
        public string? FadeText { get; init; }

        [JsonPropertyName("autoReceived")]
        public JsonElement AutoReceived { get; init; }

        [JsonPropertyName("isRankFirst")]
        public JsonElement IsRankFirst { get; init; }
    }
}
