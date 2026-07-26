namespace CS2QuantWeb.Core;

public sealed class QuantResearchModule : IQuantResearchModule
{
    private readonly IExecutionCostModel _executionCostModel;

    public QuantResearchModule()
        : this(new ResearchExecutionCostModel())
    {
    }

    public QuantResearchModule(IExecutionCostModel executionCostModel)
    {
        _executionCostModel = executionCostModel ?? throw new ArgumentNullException(nameof(executionCostModel));
    }

    public QuantResearchResult Analyze(
        string symbol,
        string source,
        IReadOnlyList<QuantCandle> candles,
        ResearchAnalysisOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(candles);
        QuantCandle? invalid = candles.FirstOrDefault(candle => !IsValid(candle));
        if (invalid is not null)
        {
            throw new ArgumentException(
                $"{invalid.Date:yyyy-MM-dd} 的 OHLC 数据无效，请检查开高低收价格。",
                nameof(candles));
        }

        IGrouping<DateOnly, QuantCandle>? duplicate = candles
            .GroupBy(candle => candle.Date)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"K线包含重复日期 {duplicate.Key:yyyy-MM-dd}，请先清理重复记录。",
                nameof(candles));
        }

        QuantCandle[] normalized = candles
            .OrderBy(candle => candle.Date)
            .ToArray();
        if (normalized.Length < 5)
            throw new ArgumentException("至少需要 5 根有效 K 线。", nameof(candles));

        SeriesQualityReport quality = SeriesQualityAnalyzer.Evaluate(candles, normalized);

        IReadOnlyList<IndicatorPoint> indicators = TechnicalIndicators.Calculate(normalized);
        IReadOnlyList<IndicatorSelection> indicatorSelections = MergeIndicatorSelections(options);
        IReadOnlyList<IndicatorSeries> indicatorSeries = TechnicalIndicators.CalculateSeries(
            normalized,
            indicatorSelections);
        ChanAnalysis chan = ChanStructureAnalyzer.Analyze(normalized, indicators);
        IReadOnlyList<ResearchSignal> chanExecutionSignals = ChanSignalReplayAnalyzer.Replay(normalized);
        chan = chan with { ExecutionSignals = chanExecutionSignals };
        IReadOnlyList<ResearchSignal> strategySignals = options?.Strategy is null
            ? StrategyAnalyzer.Analyze(normalized, indicators)
            : ConfigurableStrategyEvaluator.Evaluate(options.Strategy, normalized, indicatorSeries);
        string normalizedSymbol = string.IsNullOrWhiteSpace(symbol) ? "未命名序列" : symbol.Trim();
        string normalizedSource = string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim();
        ExecutionCostProfile executionCosts = ApplyLockMode(_executionCostModel.Resolve(
            normalizedSource,
            normalizedSymbol,
            options?.CostInputs), options?.LockMode ?? ExecutionLockMode.None);
        IReadOnlyList<BacktestSummary> backtests = BuildBacktests(
            normalized,
            strategySignals,
            chanExecutionSignals,
            executionCosts,
            options?.Strategy);
        Dictionary<string, IReadOnlyList<ResearchSignal>> signalsByStrategy = options?.Strategy is null
            ? new Dictionary<string, IReadOnlyList<ResearchSignal>>
            {
                ["趋势突破"] = strategySignals,
                ["恐慌底"] = strategySignals,
                ["缠论买卖点"] = chanExecutionSignals
            }
            : new Dictionary<string, IReadOnlyList<ResearchSignal>>
            {
                [options.Strategy.Name] = strategySignals,
                ["缠论买卖点"] = chanExecutionSignals
            };
        WalkForwardReport walkForward = CalendarWalkForwardValidator.Evaluate(
            normalized,
            signalsByStrategy,
            executionCosts.BaselinePolicy);
        return new QuantResearchResult(
            normalizedSymbol,
            normalizedSource,
            "可解释研究模块：MA/MACD、包含处理、分型、笔、线段、中枢与完整一、二、三类核心买卖点；回测按逐K确认日期重放信号。无账户、持仓、订单或自动交易。",
            BuildSummary(normalized),
            normalized,
            indicators,
            chan,
            strategySignals,
            backtests)
        {
            Quality = quality,
            ExecutionCosts = executionCosts,
            WalkForward = walkForward,
            ResultSummary = ResearchResultSummaryBuilder.Build(backtests, walkForward),
            IndicatorSeries = indicatorSeries,
            ActiveStrategy = options?.Strategy,
            LockMode = options?.LockMode ?? ExecutionLockMode.None
        };
    }

    private static IReadOnlyList<IndicatorSelection> MergeIndicatorSelections(ResearchAnalysisOptions? options)
    {
        IEnumerable<IndicatorSelection> requested = options?.Indicators is { Count: > 0 }
            ? options.Indicators
            : IndicatorCatalog.DefaultSelections;
        if (options?.Strategy?.Indicators is { Count: > 0 } dependencies)
            requested = requested.Concat(dependencies);
        return requested
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static ExecutionCostProfile ApplyLockMode(
        ExecutionCostProfile profile,
        ExecutionLockMode lockMode)
    {
        int lockDays = lockMode == ExecutionLockMode.TPlusSeven ? 7 : 0;
        ResearchExecutionPolicy baseline = profile.BaselinePolicy with { TradeLockDays = lockDays };
        return profile with
        {
            BaselinePolicy = baseline,
            Scenarios = profile.Scenarios.Select(scenario => scenario with
            {
                Policy = scenario.Policy with { TradeLockDays = lockDays }
            }).ToArray()
        };
    }

    private static bool IsValid(QuantCandle candle)
    {
        return candle.Open > 0
            && candle.High > 0
            && candle.Low > 0
            && candle.Close > 0
            && candle.High >= Math.Max(candle.Open, candle.Close)
            && candle.Low <= Math.Min(candle.Open, candle.Close)
            && double.IsFinite(candle.Open)
            && double.IsFinite(candle.High)
            && double.IsFinite(candle.Low)
            && double.IsFinite(candle.Close)
            && double.IsFinite(candle.Volume)
            && (!candle.Turnover.HasValue || double.IsFinite(candle.Turnover.Value));
    }

    private static MarketSummary BuildSummary(IReadOnlyList<QuantCandle> candles)
    {
        double peak = candles[0].Close;
        double maxDrawdown = 0;
        foreach (QuantCandle candle in candles)
        {
            peak = Math.Max(peak, candle.Close);
            maxDrawdown = Math.Min(maxDrawdown, candle.Close / peak - 1);
        }

        double periodReturn = candles[^1].Close / candles[0].Close - 1;
        return new MarketSummary(
            candles.Count,
            candles[0].Date,
            candles[^1].Date,
            candles[^1].Close,
            periodReturn * 100,
            maxDrawdown * 100);
    }

    private static IReadOnlyList<BacktestSummary> BuildBacktests(
        IReadOnlyList<QuantCandle> candles,
        IReadOnlyList<ResearchSignal> strategySignals,
        IReadOnlyList<ResearchSignal> chanSignals,
        ExecutionCostProfile executionCosts,
        StrategyDefinition? activeStrategy)
    {
        if (activeStrategy is not null)
        {
            return [
                RunWithCostSensitivity(activeStrategy.Name, candles, strategySignals, executionCosts),
                RunWithCostSensitivity("缠论买卖点", candles, chanSignals, executionCosts)
            ];
        }
        return [
            RunWithCostSensitivity("趋势突破", candles, strategySignals, executionCosts),
            RunWithCostSensitivity("恐慌底", candles, strategySignals, executionCosts),
            RunWithCostSensitivity("缠论买卖点", candles, chanSignals, executionCosts)
        ];
    }

    private static BacktestSummary RunWithCostSensitivity(
        string strategy,
        IReadOnlyList<QuantCandle> candles,
        IReadOnlyList<ResearchSignal> signals,
        ExecutionCostProfile executionCosts)
    {
        BacktestSummary baseline = ResearchBacktestEngine.Run(
            strategy,
            candles,
            signals,
            executionCosts.BaselinePolicy);
        BacktestCostScenarioSummary[] sensitivity = executionCosts.Scenarios
            .Select(scenario =>
            {
                BacktestSummary result = scenario.Code == "baseline"
                    ? baseline
                    : ResearchBacktestEngine.Run(strategy, candles, signals, scenario.Policy);
                return new BacktestCostScenarioSummary(
                    scenario.Code,
                    scenario.Label,
                    result.TotalReturnPercent,
                    result.UnrealizedReturnPercent,
                    result.CostPercent,
                    result.MaxDrawdownPercent,
                    result.TradeCount,
                    scenario.Policy);
            })
            .ToArray();
        return baseline with { CostSensitivity = sensitivity };
    }
}
