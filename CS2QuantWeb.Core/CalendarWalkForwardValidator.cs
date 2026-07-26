namespace CS2QuantWeb.Core;

internal static class CalendarWalkForwardValidator
{
    private const int MinimumCandleCount = 180;
    private const int WindowCount = 3;
    private const double InitialTrainRatio = 0.50;
    private const double StepRatio = 0.10;
    private const double EvaluationRatio = 0.15;

    public static WalkForwardReport Evaluate(
        IReadOnlyList<QuantCandle> candles,
        IReadOnlyDictionary<string, IReadOnlyList<ResearchSignal>> signalsByStrategy,
        ResearchExecutionPolicy policy)
    {
        string[] strategies = signalsByStrategy.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        if (candles.Count < MinimumCandleCount)
        {
            string status = $"样本不足：共同日历滚动验证至少需要 {MinimumCandleCount} 根K线。";
            return new WalkForwardReport(
                false,
                "expanding_common_calendar",
                status,
                MinimumCandleCount,
                strategies.Select(strategy => new WalkForwardStrategyReport(
                    strategy,
                    false,
                    false,
                    status,
                    0,
                    0,
                    [])).ToArray());
        }

        CalendarWindow[] windows = BuildWindows(candles[0].Date, candles[^1].Date);
        WalkForwardStrategyReport[] reports = strategies
            .Select(strategy => EvaluateStrategy(
                strategy,
                candles,
                signalsByStrategy[strategy],
                policy,
                windows))
            .ToArray();
        int stableCount = reports.Count(report => report.IsStable);
        int availableCount = reports.Count(report => report.IsAvailable);
        return new WalkForwardReport(
            true,
            "expanding_common_calendar",
            availableCount == 0
                ? "固定规则没有达到最低有效窗口和测试交易门槛，结果不可判定。"
                : stableCount == 0
                ? "固定规则尚未通过样本外稳定性门槛，不应据此给出交易结论。"
                : $"{stableCount}/{reports.Length} 个固定规则通过样本外稳定性门槛。",
            MinimumCandleCount,
            reports);
    }

    private static WalkForwardStrategyReport EvaluateStrategy(
        string strategy,
        IReadOnlyList<QuantCandle> candles,
        IReadOnlyList<ResearchSignal> signals,
        ResearchExecutionPolicy policy,
        IReadOnlyList<CalendarWindow> windows)
    {
        WalkForwardWindowReport[] results = windows
            .Select(window => EvaluateWindow(strategy, candles, signals, policy, window))
            .ToArray();
        WalkForwardWindowReport[] evaluated = results.Where(window => window.IsEvaluated).ToArray();
        int passedCount = evaluated.Count(window => window.Passed);
        int testTradeCount = evaluated.Sum(window => window.Test.TradeCount);
        bool available = evaluated.Length >= 2 && testTradeCount >= 3;
        bool stable = available
            && passedCount >= Math.Ceiling(evaluated.Length * 0.60);
        string status = evaluated.Length < 2
            ? "不可判定：至少需要 2 个验证段和测试段都形成交易的窗口。"
            : testTradeCount < 3
                ? "不可判定：有效窗口的测试段合计至少需要 3 笔交易。"
            : stable
                ? "样本外表现基本稳定。"
                : "样本外表现不稳定，保留为研究信号。";
        return new WalkForwardStrategyReport(
            strategy,
            available,
            stable,
            status,
            evaluated.Length,
            passedCount,
            results);
    }

    private static WalkForwardWindowReport EvaluateWindow(
        string strategy,
        IReadOnlyList<QuantCandle> candles,
        IReadOnlyList<ResearchSignal> signals,
        ResearchExecutionPolicy policy,
        CalendarWindow window)
    {
        WalkForwardSplitSummary train = EvaluateSplit(
            strategy,
            candles,
            signals,
            candles[0].Date,
            window.TrainEnd,
            policy);
        WalkForwardSplitSummary validation = EvaluateSplit(
            strategy,
            candles,
            signals,
            window.ValidationStart,
            window.ValidationEnd,
            policy);
        WalkForwardSplitSummary test = EvaluateSplit(
            strategy,
            candles,
            signals,
            window.TestStart,
            window.TestEnd,
            policy);
        bool evaluated = validation.TradeCount > 0 && test.TradeCount > 0;
        bool passed = evaluated
            && validation.NetReturnPercent > 0
            && test.NetReturnPercent > 0
            && test.MaxDrawdownPercent >= -30;
        string status = !evaluated
            ? "不可判定：验证段或测试段没有形成交易。"
            : passed
                ? "通过：验证段与测试段同向为正，测试段回撤未超过 30%。"
                : "未通过：样本外收益方向或回撤门槛不一致。";
        return new WalkForwardWindowReport(
            window.Sequence,
            window.TrainEnd,
            window.ValidationStart,
            window.ValidationEnd,
            window.TestStart,
            window.TestEnd,
            train,
            validation,
            test,
            evaluated,
            passed,
            status);
    }

    private static WalkForwardSplitSummary EvaluateSplit(
        string strategy,
        IReadOnlyList<QuantCandle> candles,
        IReadOnlyList<ResearchSignal> signals,
        DateOnly start,
        DateOnly end,
        ResearchExecutionPolicy policy)
    {
        QuantCandle[] splitCandles = candles
            .Where(candle => candle.Date >= start && candle.Date <= end)
            .ToArray();
        if (splitCandles.Length < 5)
            return new WalkForwardSplitSummary(start, end, splitCandles.Length, 0, 0, 0, 0);

        ResearchSignal[] splitSignals = signals
            .Where(signal => signal.AvailableDate >= start && signal.AvailableDate <= end)
            .ToArray();
        BacktestSummary summary = ResearchBacktestEngine.Run(
            strategy,
            splitCandles,
            splitSignals,
            policy with { MinimumCandleCount = 5 });
        return new WalkForwardSplitSummary(
            start,
            end,
            splitCandles.Length,
            summary.TradeCount + summary.OpenPositionCount,
            CombineReturns(summary.TotalReturnPercent, summary.UnrealizedReturnPercent),
            summary.MaxDrawdownPercent,
            summary.BlockedExitCount);
    }

    private static double CombineReturns(double realizedPercent, double unrealizedPercent)
    {
        double realizedMultiplier = 1 + realizedPercent / 100;
        double unrealizedMultiplier = 1 + unrealizedPercent / 100;
        return (realizedMultiplier * unrealizedMultiplier - 1) * 100;
    }

    private static CalendarWindow[] BuildWindows(DateOnly start, DateOnly end)
    {
        int spanDays = Math.Max(1, end.DayNumber - start.DayNumber + 1);
        int evaluationDays = Math.Max(1, (int)Math.Floor(spanDays * EvaluationRatio));
        var windows = new CalendarWindow[WindowCount];
        for (int index = 0; index < WindowCount; index++)
        {
            int trainDays = Math.Max(1, (int)Math.Floor(
                spanDays * (InitialTrainRatio + StepRatio * index)));
            DateOnly trainEnd = start.AddDays(trainDays - 1);
            DateOnly validationStart = trainEnd.AddDays(1);
            DateOnly validationEnd = validationStart.AddDays(evaluationDays - 1);
            DateOnly testStart = validationEnd.AddDays(1);
            DateOnly testEnd = index == WindowCount - 1
                ? end
                : DateOnly.FromDayNumber(Math.Min(
                    end.DayNumber,
                    testStart.DayNumber + evaluationDays - 1));
            windows[index] = new CalendarWindow(
                index + 1,
                trainEnd,
                validationStart,
                validationEnd,
                testStart,
                testEnd);
        }

        return windows;
    }

    private sealed record CalendarWindow(
        int Sequence,
        DateOnly TrainEnd,
        DateOnly ValidationStart,
        DateOnly ValidationEnd,
        DateOnly TestStart,
        DateOnly TestEnd);
}
