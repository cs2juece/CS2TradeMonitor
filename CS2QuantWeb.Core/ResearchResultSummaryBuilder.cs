namespace CS2QuantWeb.Core;

public static class ResearchResultSummaryBuilder
{
    public static ResearchResultSummary Build(
        IReadOnlyList<BacktestSummary> backtests,
        WalkForwardReport walkForward)
    {
        ArgumentNullException.ThrowIfNull(backtests);
        ArgumentNullException.ThrowIfNull(walkForward);

        BacktestSummary[] available = backtests.Where(item => item.IsAvailable).ToArray();
        if (available.Length == 0)
        {
            return new ResearchResultSummary(
                "暂无可用回测结果",
                backtests.Count,
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

        var ranked = available
            .Select(item => new
            {
                Backtest = item,
                NetReturn = CombineReturns(item.TotalReturnPercent, item.UnrealizedReturnPercent)
            })
            .OrderByDescending(item => item.NetReturn)
            .ToArray();
        int tradeCount = available.Sum(item => item.TradeCount);
        double weightedWinRate = tradeCount == 0
            ? 0
            : available.Sum(item => item.WinRatePercent * item.TradeCount) / tradeCount;
        int holdingWeight = available.Sum(item => item.TradeCount + item.OpenPositionCount);
        double weightedHoldingDays = holdingWeight == 0
            ? available.Average(item => item.AverageHoldingDays)
            : available.Sum(item => item.AverageHoldingDays * (item.TradeCount + item.OpenPositionCount)) / holdingWeight;
        double worstCostScenarioReturn = available
            .SelectMany(item => item.CostSensitivity)
            .Select(item => CombineReturns(item.TotalReturnPercent, item.UnrealizedReturnPercent))
            .DefaultIfEmpty(ranked[^1].NetReturn)
            .Min();
        int stableStrategies = walkForward.Strategies.Count(item => item.IsStable);

        return new ResearchResultSummary(
            BuildStatus(tradeCount, walkForward, stableStrategies),
            backtests.Count,
            available.Length,
            tradeCount,
            ranked[0].Backtest.Strategy,
            ranked[0].NetReturn,
            weightedWinRate,
            available.Min(item => item.MaxDrawdownPercent),
            weightedHoldingDays,
            worstCostScenarioReturn,
            stableStrategies);
    }

    private static string BuildStatus(
        int tradeCount,
        WalkForwardReport walkForward,
        int stableStrategies)
    {
        if (tradeCount == 0)
            return "回测已完成，但尚未形成已平仓交易。";
        if (!walkForward.IsAvailable)
            return "回测已完成；样本外验证当前不可判定。";
        if (stableStrategies > 0)
            return $"回测已完成；{stableStrategies} 个策略形成样本外稳定候选。";
        return "回测已完成；样本外验证暂未形成稳定候选。";
    }

    private static double CombineReturns(double realizedPercent, double unrealizedPercent)
    {
        return ((1 + realizedPercent / 100.0) * (1 + unrealizedPercent / 100.0) - 1) * 100.0;
    }
}
