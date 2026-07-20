namespace CS2QuantWeb.Core;

public sealed record QuantCandle(
    DateOnly Date,
    double Open,
    double High,
    double Low,
    double Close,
    double Volume = 0);

public enum CandleInterval
{
    Day,
    Week
}

public sealed record IndicatorPoint(
    DateOnly Date,
    double Close,
    double? Ma5,
    double? Ma10,
    double? Ma20,
    double? VolumeMa20,
    double Macd,
    double Signal,
    double Histogram);

public enum FractalKind
{
    Top,
    Bottom
}

public enum SignalSide
{
    Buy,
    Sell,
    Risk
}

public sealed record ChanFractal(
    int Index,
    DateOnly Date,
    FractalKind Kind,
    double Price);

public sealed record ChanStroke(
    int StartIndex,
    int EndIndex,
    DateOnly StartDate,
    DateOnly EndDate,
    double StartPrice,
    double EndPrice,
    bool IsUp);

public sealed record ChanSegment(
    int StartIndex,
    int EndIndex,
    DateOnly StartDate,
    DateOnly EndDate,
    double StartPrice,
    double EndPrice,
    bool IsUp);

public sealed record ChanCenter(
    int StartIndex,
    int EndIndex,
    DateOnly StartDate,
    DateOnly EndDate,
    double Lower,
    double Upper);

public sealed record ResearchSignal(
    DateOnly Date,
    string Strategy,
    SignalSide Side,
    double Price,
    string Reason,
    string Level = "research");

public sealed record BacktestSummary(
    string Strategy,
    int TradeCount,
    double TotalReturnPercent,
    double WinRatePercent,
    double MaxDrawdownPercent,
    double AverageHoldingDays);

public sealed record MarketSummary(
    int CandleCount,
    DateOnly StartDate,
    DateOnly EndDate,
    double LatestClose,
    double PeriodReturnPercent,
    double MaxDrawdownPercent);

public sealed record ChanAnalysis(
    IReadOnlyList<ChanFractal> Fractals,
    IReadOnlyList<ChanStroke> Strokes,
    IReadOnlyList<ChanSegment> Segments,
    IReadOnlyList<ChanCenter> Centers,
    IReadOnlyList<ResearchSignal> Signals,
    IReadOnlyList<string> Conclusions);

public sealed record QuantResearchResult(
    string Symbol,
    string Source,
    string MethodNote,
    MarketSummary Summary,
    IReadOnlyList<QuantCandle> Candles,
    IReadOnlyList<IndicatorPoint> Indicators,
    ChanAnalysis Chan,
    IReadOnlyList<ResearchSignal> StrategySignals,
    IReadOnlyList<BacktestSummary> Backtests)
{
    public CandleInterval Interval { get; init; } = CandleInterval.Day;
}

public interface IQuantResearchModule
{
    QuantResearchResult Analyze(
        string symbol,
        string source,
        IReadOnlyList<QuantCandle> candles);
}
