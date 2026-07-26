namespace CS2QuantWeb.Core;

public static class ResearchExecutionCostCalculator
{
    public static ExecutionCostBreakdown CalculateRoundTrip(
        double rawBuyPrice,
        double rawSellPrice,
        ResearchExecutionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        double effectiveBuy = BuyFill(rawBuyPrice, policy);
        double effectiveSell = SellFill(rawSellPrice, policy);
        double gross = rawBuyPrice <= 0 ? 0 : rawSellPrice / rawBuyPrice - 1;
        double sellAfterFee = effectiveSell * (1 - policy.PlatformFeeRate);
        double net = effectiveBuy <= 0 ? 0 : sellAfterFee / effectiveBuy - 1;
        return new ExecutionCostBreakdown(
            effectiveBuy,
            effectiveSell,
            gross * 100,
            (gross - net) * 100,
            net * 100);
    }

    internal static double BuyFill(double price, ResearchExecutionPolicy policy) =>
        price * (1 + (policy.SpreadBps / 2 + policy.SlippageBps) / 10_000);

    internal static double SellFill(double price, ResearchExecutionPolicy policy) =>
        price * (1 - (policy.SpreadBps / 2 + policy.SlippageBps) / 10_000);
}
