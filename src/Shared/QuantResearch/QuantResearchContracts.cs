using CS2QuantWeb.Core;
using CS2TradeMonitor.Shared.Market;

namespace CS2TradeMonitor.Shared.QuantResearch;

public sealed record SearchQuantResearchItemsCommand(string Keyword);

public sealed record RunQuantResearchCommand(
    string MarketHashName,
    string DisplayName,
    string Range,
    string StrategyId,
    bool TPlusSevenEnabled);

public sealed record QuantResearchSeries(
    string Symbol,
    string Source,
    IReadOnlyList<QuantCandle> Candles,
    CandleInterval Interval);

public interface IQuantResearchSeriesProvider
{
    Task<IReadOnlyList<MarketItemCandidate>> SearchAsync(
        string keyword,
        string steamDtApiKey,
        CancellationToken cancellationToken = default);

    Task<QuantResearchSeries> LoadItemAsync(
        string marketHashName,
        string displayName,
        string range,
        string steamDtApiKey,
        CancellationToken cancellationToken = default);
}

public sealed record QuantResearchCatalogItemProjection(
    string Id,
    string Name,
    string Description);

public sealed record QuantResearchCandleProjection(
    string Date,
    double Open,
    double High,
    double Low,
    double Close,
    double Volume);

public sealed record QuantResearchBacktestProjection(
    string Strategy,
    bool IsAvailable,
    string Status,
    int TradeCount,
    double TotalReturnPercent,
    double WinRatePercent,
    double MaxDrawdownPercent,
    double AverageHoldingDays,
    double CostPercent,
    double UnrealizedReturnPercent,
    int OpenPositionCount,
    int BlockedExitCount);

public sealed record QuantResearchSignalProjection(
    string Date,
    string Strategy,
    string Side,
    double Price,
    string Reason,
    string Level);

public sealed record QuantResearchRunProjection(
    string Symbol,
    string Source,
    string Interval,
    string Strategy,
    bool TPlusSevenEnabled,
    int CandleCount,
    string StartDate,
    string EndDate,
    double LatestClose,
    double PeriodReturnPercent,
    double MarketMaxDrawdownPercent,
    string ResultStatus,
    string BestStrategy,
    double BestNetReturnPercent,
    double WeightedWinRatePercent,
    double WorstMaxDrawdownPercent,
    double AverageHoldingDays,
    int StableStrategyCount,
    bool QualityUsable,
    IReadOnlyList<string> QualityWarnings,
    IReadOnlyList<QuantResearchBacktestProjection> Backtests,
    IReadOnlyList<QuantResearchSignalProjection> Signals,
    IReadOnlyList<QuantResearchCandleProjection> Candles,
    string MethodNote,
    DateTimeOffset CompletedAt);

public sealed record QuantResearchFeatureProjection(
    bool CoreReady,
    bool SteamDtConfigured,
    string RuntimeMode,
    string ServiceAddress,
    string Status,
    string StatusDetail,
    string SearchKeyword,
    IReadOnlyList<MarketItemCandidate> Candidates,
    IReadOnlyList<QuantResearchCatalogItemProjection> Indicators,
    IReadOnlyList<QuantResearchCatalogItemProjection> Strategies,
    QuantResearchRunProjection? LastRun);

public sealed class QuantResearchSeriesException : Exception
{
    public QuantResearchSeriesException(string message)
        : base(message)
    {
    }
}
