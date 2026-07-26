namespace CS2QuantWeb.Core;

internal static class ResearchBacktestEngine
{
    private sealed record OpenPosition(
        DateOnly SignalDate,
        DateOnly BuyDate,
        double RawBuyPrice,
        double EffectiveBuyPrice);

    private sealed record PendingExit(
        DateOnly SignalDate,
        string Reason,
        int EarliestExecutionIndex,
        bool LockNoticeAdded);

    public static BacktestSummary Run(
        string strategy,
        IReadOnlyList<QuantCandle> candles,
        IReadOnlyList<ResearchSignal> signals,
        ResearchExecutionPolicy? policy = null)
    {
        ResearchExecutionPolicy activePolicy = policy ?? ResearchExecutionPolicy.Default;
        if (candles.Count < activePolicy.MinimumCandleCount)
        {
            return new BacktestSummary(strategy, 0, 0, 0, 0, 0)
            {
                IsAvailable = false,
                Status = $"样本不足：至少需要 {activePolicy.MinimumCandleCount} 根K线",
                Policy = activePolicy
            };
        }

        ResearchSignal[] relevant = signals
            .Where(signal => signal.Strategy == strategy && signal.Side != SignalSide.Risk)
            .OrderBy(signal => signal.AvailableDate)
            .ThenBy(signal => signal.Date)
            .ToArray();
        var signalsByDate = relevant
            .GroupBy(signal => signal.AvailableDate)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var executions = new List<BacktestExecution>();
        var notices = new List<BacktestNotice>();
        OpenPosition? position = null;
        ResearchSignal? pendingBuy = null;
        int pendingBuyIndex = -1;
        PendingExit? pendingExit = null;
        int blockedExitCount = 0;
        double realizedEquity = 1;
        double peakEquity = 1;
        double maxDrawdown = 0;

        for (int index = 0; index < candles.Count; index++)
        {
            QuantCandle candle = candles[index];
            if (pendingBuy is not null && index >= pendingBuyIndex && position is null)
            {
                position = new OpenPosition(
                    pendingBuy.AvailableDate,
                    candle.Date,
                    candle.Open,
                    ResearchExecutionCostCalculator.BuyFill(candle.Open, activePolicy));
                pendingBuy = null;
                pendingBuyIndex = -1;
            }

            if (pendingExit is not null && position is not null && index >= pendingExit.EarliestExecutionIndex)
            {
                DateOnly unlockDate = position.BuyDate.AddDays(activePolicy.TradeLockDays);
                if (candle.Date < unlockDate)
                {
                    if (!pendingExit.LockNoticeAdded)
                    {
                        blockedExitCount++;
                        notices.Add(new BacktestNotice(
                            candle.Date,
                            "trade_lock",
                            $"{pendingExit.SignalDate:yyyy-MM-dd} 的卖出信号受T+{activePolicy.TradeLockDays}限制，最早可在 {unlockDate:yyyy-MM-dd} 执行。"));
                        pendingExit = pendingExit with { LockNoticeAdded = true };
                    }
                }
                else
                {
                    BacktestExecution execution = Close(position, pendingExit, candle, activePolicy);
                    executions.Add(execution);
                    realizedEquity *= 1 + execution.NetReturnPercent / 100;
                    position = null;
                    pendingExit = null;
                }
            }

            if (signalsByDate.TryGetValue(candle.Date, out ResearchSignal[]? dailySignals))
            {
                foreach (ResearchSignal signal in dailySignals)
                {
                    if (signal.Side == SignalSide.Buy && position is null && pendingBuy is null)
                    {
                        if (index + 1 < candles.Count)
                        {
                            pendingBuy = signal;
                            pendingBuyIndex = index + 1;
                        }
                    }
                    else if (signal.Side == SignalSide.Sell && position is not null && pendingExit is null)
                    {
                        pendingExit = new PendingExit(signal.AvailableDate, signal.Reason, index + 1, false);
                    }
                }
            }

            double markedEquity = position is null
                ? realizedEquity
                : realizedEquity * MarkToMarket(position, candle.Close, activePolicy);
            peakEquity = Math.Max(peakEquity, markedEquity);
            maxDrawdown = Math.Min(maxDrawdown, markedEquity / peakEquity - 1);
        }

        if (position is not null)
        {
            QuantCandle last = candles[^1];
            ExecutionCostBreakdown openReturn = ResearchExecutionCostCalculator.CalculateRoundTrip(
                position.RawBuyPrice,
                last.Close,
                activePolicy);
            executions.Add(new BacktestExecution(
                position.SignalDate,
                position.BuyDate,
                position.EffectiveBuyPrice,
                pendingExit?.SignalDate,
                null,
                null,
                true,
                Math.Max(0, last.Date.DayNumber - position.BuyDate.DayNumber),
                pendingExit is null ? "样本结束时仍持仓" : "卖出信号待解锁执行",
                openReturn.GrossReturnPercent,
                openReturn.CostPercent,
                openReturn.NetReturnPercent));
        }

        BacktestExecution[] closed = executions.Where(execution => !execution.IsOpen).ToArray();
        BacktestExecution? open = executions.LastOrDefault(execution => execution.IsOpen);
        string status = executions.Count == 0
            ? "没有形成可执行交易"
            : open is not null
                ? "存在未平仓仓位"
                : "已完成";
        return new BacktestSummary(
            strategy,
            closed.Length,
            (realizedEquity - 1) * 100,
            closed.Length == 0 ? 0 : closed.Count(execution => execution.NetReturnPercent > 0) * 100d / closed.Length,
            maxDrawdown * 100,
            closed.Length == 0 ? 0 : closed.Average(execution => execution.HoldingDays))
        {
            Status = status,
            GrossReturnPercent = closed.Sum(execution => execution.GrossReturnPercent),
            CostPercent = closed.Sum(execution => execution.CostPercent),
            UnrealizedReturnPercent = open?.NetReturnPercent ?? 0,
            OpenPositionCount = open is null ? 0 : 1,
            BlockedExitCount = blockedExitCount,
            Policy = activePolicy,
            Executions = executions,
            Notices = notices
        };
    }

    private static BacktestExecution Close(
        OpenPosition position,
        PendingExit exit,
        QuantCandle candle,
        ResearchExecutionPolicy policy)
    {
        ExecutionCostBreakdown result = ResearchExecutionCostCalculator.CalculateRoundTrip(
            position.RawBuyPrice,
            candle.Open,
            policy);
        return new BacktestExecution(
            position.SignalDate,
            position.BuyDate,
            position.EffectiveBuyPrice,
            exit.SignalDate,
            candle.Date,
            result.EffectiveSellPrice,
            false,
            candle.Date.DayNumber - position.BuyDate.DayNumber,
            exit.Reason,
            result.GrossReturnPercent,
            result.CostPercent,
            result.NetReturnPercent);
    }

    private static double MarkToMarket(
        OpenPosition position,
        double close,
        ResearchExecutionPolicy policy)
    {
        ExecutionCostBreakdown result = ResearchExecutionCostCalculator.CalculateRoundTrip(
            position.RawBuyPrice,
            close,
            policy);
        return 1 + result.NetReturnPercent / 100;
    }
}
