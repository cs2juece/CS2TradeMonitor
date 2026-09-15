namespace CS2QuantWeb.Core;

public sealed record QuantCandle(
    DateOnly Date,
    double Open,
    double High,
    double Low,
    double Close,
    double Volume = 0,
    double? Turnover = null);

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

public sealed record SeriesQualityWarning(
    string Code,
    string Message,
    int Count = 1);

public sealed record SeriesQualityReport(
    bool IsUsable,
    int InputCandleCount,
    int ValidCandleCount,
    int OutOfOrderCount,
    int SuspiciousGapCount,
    int StaleDays,
    IReadOnlyList<SeriesQualityWarning> Warnings);

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

public enum ChanSignalType
{
    FirstBuy,
    FirstSell,
    SecondBuy,
    SecondSell,
    ThirdBuy,
    ThirdSell
}

public sealed record ChanFractal(
    int Index,
    DateOnly Date,
    FractalKind Kind,
    double Price)
{
    public int ConfirmedIndex { get; init; } = Index;
    public DateOnly ConfirmedDate { get; init; } = Date;
}

public sealed record ChanStroke(
    int StartIndex,
    int EndIndex,
    DateOnly StartDate,
    DateOnly EndDate,
    double StartPrice,
    double EndPrice,
    bool IsUp)
{
    public int ConfirmedIndex { get; init; } = EndIndex;
    public DateOnly ConfirmedDate { get; init; } = EndDate;
}

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
    string Level = "research")
{
    public ChanSignalType? ChanType { get; init; }
    public DateOnly AvailableDate { get; init; } = Date;
}

public sealed record ResearchExecutionPolicy(
    int MinimumCandleCount,
    int TradeLockDays,
    double PlatformFeeRate,
    double SpreadBps,
    double SlippageBps)
{
    public static ResearchExecutionPolicy Default { get; } = new(
        90,
        0,
        0,
        50,
        30);
}

public enum ExecutionLockMode
{
    None,
    TPlusSeven
}

public enum IndicatorPlacement
{
    Main,
    Sub
}

public sealed record IndicatorParameterDefinition(
    string Key,
    string Label,
    double DefaultValue,
    double Minimum,
    double Maximum,
    int DecimalPlaces = 0);

public sealed record IndicatorOutputDefinition(
    string Key,
    string Label,
    string Figure = "line");

public sealed record IndicatorDefinition(
    string Code,
    string Name,
    string Description,
    IReadOnlyList<IndicatorPlacement> Placements,
    IReadOnlyList<IndicatorParameterDefinition> Parameters,
    IReadOnlyList<IndicatorOutputDefinition> Outputs,
    bool RequiresVolume = false,
    bool RequiresTurnover = false);

public sealed record IndicatorSelection(
    string Id,
    string Code,
    IndicatorPlacement Placement,
    IReadOnlyList<double> Parameters);

public sealed record IndicatorValuePoint(
    DateOnly Date,
    IReadOnlyDictionary<string, double?> Values);

public sealed record IndicatorSeries(
    string Id,
    string Code,
    string Name,
    IndicatorPlacement Placement,
    IReadOnlyList<double> Parameters,
    IReadOnlyList<IndicatorOutputDefinition> Outputs,
    IReadOnlyList<IndicatorValuePoint> Points,
    bool IsAvailable,
    string Status);

public enum StrategyMatchMode
{
    All,
    Any
}

public enum StrategyComparison
{
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual,
    CrossAbove,
    CrossBelow
}

public sealed record StrategyCondition(
    string Left,
    StrategyComparison Comparison,
    string? Right = null,
    double? Constant = null);

public sealed record StrategyRuleSet(
    StrategyMatchMode MatchMode,
    IReadOnlyList<StrategyCondition> Conditions);

public sealed record StrategyDefinition(
    string Id,
    string Name,
    string Description,
    StrategyRuleSet Entry,
    StrategyRuleSet Exit,
    bool IsBuiltIn = false,
    string? SourceStrategyId = null,
    IReadOnlyList<IndicatorSelection>? Indicators = null);

public sealed record StrategyConditionPreset(
    string Id,
    string Name,
    string Description,
    StrategyCondition Condition,
    IndicatorSelection Indicator);

public sealed record ResearchCostInputs(
    double? PlatformFeeRate = null,
    double? SpreadBps = null,
    double? SlippageBps = null);

public sealed record ResearchAnalysisOptions(
    ResearchCostInputs? CostInputs = null,
    IReadOnlyList<IndicatorSelection>? Indicators = null,
    StrategyDefinition? Strategy = null,
    ExecutionLockMode LockMode = ExecutionLockMode.None);

public sealed record ExecutionCostScenario(
    string Code,
    string Label,
    ResearchExecutionPolicy Policy);

public sealed record ExecutionCostBreakdown(
    double EffectiveBuyPrice,
    double EffectiveSellPrice,
    double GrossReturnPercent,
    double CostPercent,
    double NetReturnPercent);

public sealed record ExecutionCostProfile(
    bool IsComplete,
    string Status,
    string Source,
    IReadOnlyList<string> MissingInputs,
    ResearchExecutionPolicy BaselinePolicy,
    IReadOnlyList<ExecutionCostScenario> Scenarios)
{
    public static ExecutionCostProfile Unconfigured { get; } = new(
        false,
        "成本参数尚未解析。",
        "未配置",
        ["卖出平台手续费", "可见买卖价差", "滑点"],
        ResearchExecutionPolicy.Default,
        []);
}

public sealed record BacktestNotice(
    DateOnly Date,
    string Code,
    string Message);

public sealed record BacktestExecution(
    DateOnly BuySignalDate,
    DateOnly BuyDate,
    double BuyPrice,
    DateOnly? SellSignalDate,
    DateOnly? SellDate,
    double? SellPrice,
    bool IsOpen,
    int HoldingDays,
    string ExitReason,
    double GrossReturnPercent,
    double CostPercent,
    double NetReturnPercent);

public sealed record BacktestSummary(
    string Strategy,
    int TradeCount,
    double TotalReturnPercent,
    double WinRatePercent,
    double MaxDrawdownPercent,
    double AverageHoldingDays)
{
    public bool IsAvailable { get; init; } = true;
    public string Status { get; init; } = "已完成";
    public double GrossReturnPercent { get; init; }
    public double CostPercent { get; init; }
    public double UnrealizedReturnPercent { get; init; }
    public int OpenPositionCount { get; init; }
    public int BlockedExitCount { get; init; }
    public ResearchExecutionPolicy Policy { get; init; } = ResearchExecutionPolicy.Default;
    public IReadOnlyList<BacktestCostScenarioSummary> CostSensitivity { get; init; } = [];
    public IReadOnlyList<BacktestExecution> Executions { get; init; } = [];
    public IReadOnlyList<BacktestNotice> Notices { get; init; } = [];
}

public sealed record BacktestCostScenarioSummary(
    string Code,
    string Label,
    double TotalReturnPercent,
    double UnrealizedReturnPercent,
    double CostPercent,
    double MaxDrawdownPercent,
    int TradeCount,
    ResearchExecutionPolicy Policy);

public sealed record WalkForwardSplitSummary(
    DateOnly StartDate,
    DateOnly EndDate,
    int CandleCount,
    int TradeCount,
    double NetReturnPercent,
    double MaxDrawdownPercent,
    int BlockedExitCount);

public sealed record WalkForwardWindowReport(
    int Sequence,
    DateOnly TrainEndDate,
    DateOnly ValidationStartDate,
    DateOnly ValidationEndDate,
    DateOnly TestStartDate,
    DateOnly TestEndDate,
    WalkForwardSplitSummary Train,
    WalkForwardSplitSummary Validation,
    WalkForwardSplitSummary Test,
    bool IsEvaluated,
    bool Passed,
    string Status);

public sealed record WalkForwardStrategyReport(
    string Strategy,
    bool IsAvailable,
    bool IsStable,
    string Status,
    int EvaluatedWindowCount,
    int PassedWindowCount,
    IReadOnlyList<WalkForwardWindowReport> Windows);

public sealed record WalkForwardReport(
    bool IsAvailable,
    string Method,
    string Status,
    int MinimumCandleCount,
    IReadOnlyList<WalkForwardStrategyReport> Strategies)
{
    public static WalkForwardReport Unavailable { get; } = new(
        false,
        "expanding_common_calendar",
        "尚未运行样本外验证。",
        180,
        []);
}

public sealed record ResearchResultSummary(
    string Status,
    int StrategyCount,
    int AvailableStrategyCount,
    int TradeCount,
    string BestStrategy,
    double BestNetReturnPercent,
    double WeightedWinRatePercent,
    double WorstMaxDrawdownPercent,
    double AverageHoldingDays,
    double WorstCostScenarioReturnPercent,
    int StableStrategyCount)
{
    public static ResearchResultSummary Empty { get; } = new(
        "尚未生成结果摘要。",
        0,
        0,
        0,
        "暂无",
        0,
        0,
        0,
        0,
        0,
        0);
}

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
    IReadOnlyList<string> Conclusions)
{
    public IReadOnlyList<ResearchSignal> ExecutionSignals { get; init; } = [];
}

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
    public SeriesQualityReport Quality { get; init; } = new(
        true,
        0,
        0,
        0,
        0,
        0,
        []);
    public ExecutionCostProfile ExecutionCosts { get; init; } = ExecutionCostProfile.Unconfigured;
    public WalkForwardReport WalkForward { get; init; } = WalkForwardReport.Unavailable;
    public ResearchResultSummary ResultSummary { get; init; } = ResearchResultSummary.Empty;
    public IReadOnlyList<IndicatorSeries> IndicatorSeries { get; init; } = [];
    public StrategyDefinition? ActiveStrategy { get; init; }
    public ExecutionLockMode LockMode { get; init; } = ExecutionLockMode.None;
}

public interface IQuantResearchModule
{
    QuantResearchResult Analyze(
        string symbol,
        string source,
        IReadOnlyList<QuantCandle> candles,
        ResearchAnalysisOptions? options = null);
}
