namespace CS2QuantWeb.Core;

public interface IExecutionCostModel
{
    ExecutionCostProfile Resolve(
        string source,
        string symbol,
        ResearchCostInputs? inputs = null);
}

public sealed class ResearchExecutionCostModel : IExecutionCostModel
{
    private const double EstimatedSpreadBps = 50;
    private const double EstimatedSlippageBps = 30;

    public ExecutionCostProfile Resolve(
        string source,
        string symbol,
        ResearchCostInputs? inputs = null)
    {
        Validate(inputs);
        var missing = new List<string>();
        if (inputs?.PlatformFeeRate is null)
            missing.Add("卖出平台手续费");
        if (inputs?.SpreadBps is null)
            missing.Add("可见买卖价差");
        if (inputs?.SlippageBps is null)
            missing.Add("滑点");

        double platformFeeRate = inputs?.PlatformFeeRate ?? 0;
        double spreadBps = inputs?.SpreadBps ?? EstimatedSpreadBps;
        double slippageBps = inputs?.SlippageBps ?? EstimatedSlippageBps;
        var baseline = new ResearchExecutionPolicy(
            ResearchExecutionPolicy.Default.MinimumCandleCount,
            ResearchExecutionPolicy.Default.TradeLockDays,
            platformFeeRate,
            spreadBps,
            slippageBps);
        ResearchExecutionPolicy doubled = baseline with
        {
            PlatformFeeRate = platformFeeRate * 2,
            SpreadBps = spreadBps * 2,
            SlippageBps = slippageBps * 2
        };
        ResearchExecutionPolicy lowLiquidity = baseline with
        {
            SpreadBps = Math.Max(spreadBps * 3, 150),
            SlippageBps = Math.Max(slippageBps * 3, 100)
        };
        bool isComplete = missing.Count == 0;
        string normalizedSource = string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim();
        string normalizedSymbol = string.IsNullOrWhiteSpace(symbol) ? "未命名序列" : symbol.Trim();
        return new ExecutionCostProfile(
            isComplete,
            isComplete
                ? "成本参数已完整配置，可复核基准与压力情景。"
                : "成本参数不完整：缺失项采用明确估算，仅用于敏感性研究。",
            isComplete
                ? $"用户本次输入 · {normalizedSource} · {normalizedSymbol}"
                : $"用户输入与保守估算 · {normalizedSource} · {normalizedSymbol}",
            missing,
            baseline,
            [
                new ExecutionCostScenario("baseline", "基准", baseline),
                new ExecutionCostScenario("double_cost", "成本翻倍", doubled),
                new ExecutionCostScenario("low_liquidity", "低流动性", lowLiquidity)
            ]);
    }

    private static void Validate(ResearchCostInputs? inputs)
    {
        if (inputs is null)
            return;

        ValidateRange(inputs.PlatformFeeRate, 0, 0.5, "卖出平台手续费率");
        ValidateRange(inputs.SpreadBps, 0, 5_000, "买卖价差");
        ValidateRange(inputs.SlippageBps, 0, 5_000, "滑点");
    }

    private static void ValidateRange(double? value, double minimum, double maximum, string name)
    {
        if (value is null)
            return;
        if (!double.IsFinite(value.Value) || value.Value < minimum || value.Value > maximum)
            throw new ArgumentOutOfRangeException(nameof(value), $"{name}必须在 {minimum} 至 {maximum} 之间。");
    }
}
