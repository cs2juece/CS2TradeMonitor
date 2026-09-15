using CS2MarketData.Core;
using CS2QuantWeb.Core;
using CS2TradeMonitor.Shared.Market;

namespace CS2TradeMonitor.Shared.QuantResearch;

public sealed class PortableQuantResearchSeriesProvider : IQuantResearchSeriesProvider
{
    private readonly IMarketDataClient _marketData;
    private readonly SteamDtKlineClient _steamDt;

    public PortableQuantResearchSeriesProvider(
        IMarketDataClient marketData,
        HttpClient httpClient)
    {
        _marketData = marketData ?? throw new ArgumentNullException(nameof(marketData));
        _steamDt = new SteamDtKlineClient(httpClient ?? throw new ArgumentNullException(nameof(httpClient)));
    }

    public Task<IReadOnlyList<MarketItemCandidate>> SearchAsync(
        string keyword,
        string steamDtApiKey,
        CancellationToken cancellationToken = default)
        => _marketData.SearchItemsAsync(keyword, steamDtApiKey, cancellationToken);

    public async Task<QuantResearchSeries> LoadItemAsync(
        string marketHashName,
        string displayName,
        string range,
        string steamDtApiKey,
        CancellationToken cancellationToken = default)
    {
        string normalizedName = marketHashName?.Trim() ?? string.Empty;
        if (normalizedName.Length == 0 || normalizedName.Length > 256)
            throw new QuantResearchSeriesException("请先搜索并选择一个单品。");

        SteamDtKlinePeriod period = ResolvePeriod(range);
        SteamDtKlineSeries series;
        try
        {
            series = await _steamDt.FetchAsync(
                normalizedName,
                steamDtApiKey,
                period,
                cancellationToken).ConfigureAwait(false);
        }
        catch (SteamDtKlineException exception)
        {
            throw new QuantResearchSeriesException(MapFailure(exception));
        }

        if (series.Candles.Count < 5)
            throw new QuantResearchSeriesException("SteamDT 响应中未识别到至少 5 根有效 OHLC K 线。");

        QuantCandle[] candles = series.Candles
            .Select(candle => new QuantCandle(
                candle.Date,
                candle.Open,
                candle.High,
                candle.Low,
                candle.Close,
                candle.Volume,
                candle.Turnover))
            .ToArray();
        return new QuantResearchSeries(
            string.IsNullOrWhiteSpace(displayName) ? normalizedName : displayName.Trim(),
            "steamdt-item",
            candles,
            period == SteamDtKlinePeriod.Weekly ? CandleInterval.Week : CandleInterval.Day);
    }

    private static SteamDtKlinePeriod ResolvePeriod(string? range)
    {
        string normalizedRange = string.IsNullOrWhiteSpace(range) ? "90" : range.Trim().ToLowerInvariant();
        return normalizedRange switch
        {
            "30" or "60" or "90" or "180" or "365" => SteamDtKlinePeriod.Daily,
            "730" or "all" => SteamDtKlinePeriod.Weekly,
            _ => throw new QuantResearchSeriesException("显示范围无效，仅支持 30 天、60 天、90 天、半年、一年、两年和全部。")
        };
    }

    private static string MapFailure(SteamDtKlineException exception)
        => exception.Kind switch
        {
            SteamDtKlineFailureKind.MissingCredential =>
                "未配置 SteamDT API Key。请先在“大盘数据源”页面填写并保存。",
            SteamDtKlineFailureKind.Authentication =>
                "SteamDT API Key 无效或没有 K 线权限。",
            SteamDtKlineFailureKind.RateLimited =>
                "SteamDT K 线调用频率已达上限，请稍后再试。",
            SteamDtKlineFailureKind.NoData =>
                "SteamDT 未返回该单品的 K 线数据。",
            _ => "SteamDT K 线读取失败，请稍后重试。"
        };
}
