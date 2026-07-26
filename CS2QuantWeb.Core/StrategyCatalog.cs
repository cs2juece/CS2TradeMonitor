namespace CS2QuantWeb.Core;

public static class StrategyCatalog
{
    public static IReadOnlyList<StrategyDefinition> BuiltIns { get; } =
    [
        new(
            "trend-ma",
            "均线趋势",
            "MA5 上穿 MA10 后进入，MA5 下穿 MA10 后退出。",
            new StrategyRuleSet(StrategyMatchMode.All,
            [
                new StrategyCondition("ma-main.value1", StrategyComparison.CrossAbove, "ma-main.value2"),
                new StrategyCondition("candle.close", StrategyComparison.GreaterThan, "ma-main.value3")
            ]),
            new StrategyRuleSet(StrategyMatchMode.Any,
            [
                new StrategyCondition("ma-main.value1", StrategyComparison.CrossBelow, "ma-main.value2"),
                new StrategyCondition("candle.close", StrategyComparison.LessThan, "ma-main.value3")
            ]),
            true,
            Indicators: [new IndicatorSelection("ma-main", "MA", IndicatorPlacement.Main, [5, 10, 20])]),
        new(
            "rsi-reversal",
            "RSI 反转",
            "RSI6 从 30 下方向上穿越时进入，从 70 上方向下穿越时退出。",
            new StrategyRuleSet(StrategyMatchMode.All,
            [
                new StrategyCondition("rsi-sub.value1", StrategyComparison.CrossAbove, Constant: 30)
            ]),
            new StrategyRuleSet(StrategyMatchMode.All,
            [
                new StrategyCondition("rsi-sub.value1", StrategyComparison.CrossBelow, Constant: 70)
            ]),
            true,
            Indicators: [new IndicatorSelection("rsi-sub", "RSI", IndicatorPlacement.Sub, [6, 12, 24])])
    ];

    public static StrategyDefinition Get(string id) => BuiltIns.FirstOrDefault(
        item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"不支持内置策略 {id}。", nameof(id));
}

internal static class ConfigurableStrategyEvaluator
{
    public static IReadOnlyList<ResearchSignal> Evaluate(
        StrategyDefinition strategy,
        IReadOnlyList<QuantCandle> candles,
        IReadOnlyList<IndicatorSeries> indicatorSeries)
    {
        Validate(strategy, indicatorSeries);
        var signals = new List<ResearchSignal>();
        bool inPosition = false;
        bool previousEntry = false;
        bool previousExit = false;
        for (int index = 1; index < candles.Count; index++)
        {
            bool entry = Match(strategy.Entry, index, candles, indicatorSeries);
            bool exit = Match(strategy.Exit, index, candles, indicatorSeries);
            if (!inPosition && entry && !previousEntry)
            {
                signals.Add(new ResearchSignal(
                    candles[index].Date,
                    strategy.Name,
                    SignalSide.Buy,
                    candles[index].Close,
                    Describe(strategy.Entry)));
                inPosition = true;
            }
            else if (inPosition && exit && !previousExit)
            {
                signals.Add(new ResearchSignal(
                    candles[index].Date,
                    strategy.Name,
                    SignalSide.Sell,
                    candles[index].Close,
                    Describe(strategy.Exit)));
                inPosition = false;
            }
            previousEntry = entry;
            previousExit = exit;
        }
        return signals;
    }

    private static bool Match(
        StrategyRuleSet rules,
        int index,
        IReadOnlyList<QuantCandle> candles,
        IReadOnlyList<IndicatorSeries> indicatorSeries)
    {
        if (rules.Conditions.Count == 0)
            return false;
        IEnumerable<bool> results = rules.Conditions.Select(condition => Match(condition, index, candles, indicatorSeries));
        return rules.MatchMode == StrategyMatchMode.All ? results.All(value => value) : results.Any(value => value);
    }

    private static bool Match(
        StrategyCondition condition,
        int index,
        IReadOnlyList<QuantCandle> candles,
        IReadOnlyList<IndicatorSeries> indicatorSeries)
    {
        double? left = Resolve(condition.Left, index, candles, indicatorSeries);
        double? right = condition.Constant ?? Resolve(condition.Right, index, candles, indicatorSeries);
        if (!left.HasValue || !right.HasValue)
            return false;
        if (condition.Comparison is StrategyComparison.CrossAbove or StrategyComparison.CrossBelow)
        {
            double? previousLeft = Resolve(condition.Left, index - 1, candles, indicatorSeries);
            double? previousRight = condition.Constant ?? Resolve(condition.Right, index - 1, candles, indicatorSeries);
            if (!previousLeft.HasValue || !previousRight.HasValue)
                return false;
            return condition.Comparison == StrategyComparison.CrossAbove
                ? previousLeft <= previousRight && left > right
                : previousLeft >= previousRight && left < right;
        }
        return condition.Comparison switch
        {
            StrategyComparison.GreaterThan => left > right,
            StrategyComparison.LessThan => left < right,
            StrategyComparison.GreaterThanOrEqual => left >= right,
            StrategyComparison.LessThanOrEqual => left <= right,
            _ => false
        };
    }

    private static double? Resolve(
        string? reference,
        int index,
        IReadOnlyList<QuantCandle> candles,
        IReadOnlyList<IndicatorSeries> indicatorSeries)
    {
        if (index < 0 || index >= candles.Count || string.IsNullOrWhiteSpace(reference))
            return null;
        if (reference.StartsWith("candle.", StringComparison.OrdinalIgnoreCase))
        {
            return reference[7..].ToLowerInvariant() switch
            {
                "open" => candles[index].Open,
                "high" => candles[index].High,
                "low" => candles[index].Low,
                "close" => candles[index].Close,
                "volume" => candles[index].Volume,
                "turnover" => candles[index].Turnover,
                _ => null
            };
        }
        int separator = reference.LastIndexOf('.');
        if (separator <= 0 || separator == reference.Length - 1)
            return null;
        string id = reference[..separator];
        string output = reference[(separator + 1)..];
        IndicatorSeries? series = indicatorSeries.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        return series?.Points[index].Values.TryGetValue(output, out double? value) == true ? value : null;
    }

    private static void Validate(StrategyDefinition strategy, IReadOnlyList<IndicatorSeries> indicatorSeries)
    {
        if (string.IsNullOrWhiteSpace(strategy.Id) || string.IsNullOrWhiteSpace(strategy.Name))
            throw new ArgumentException("策略 ID 和名称不能为空。", nameof(strategy));
        if (strategy.Entry.Conditions.Count is < 1 or > 12 || strategy.Exit.Conditions.Count is < 1 or > 12)
            throw new ArgumentException("买入和卖出规则各需 1 至 12 个条件。", nameof(strategy));
        foreach (StrategyCondition condition in strategy.Entry.Conditions.Concat(strategy.Exit.Conditions))
        {
            _ = ResolveReference(condition.Left, indicatorSeries);
            if (condition.Constant is null)
                _ = ResolveReference(condition.Right, indicatorSeries);
        }
    }

    private static bool ResolveReference(string? reference, IReadOnlyList<IndicatorSeries> series)
    {
        if (string.IsNullOrWhiteSpace(reference))
            throw new ArgumentException("策略条件引用不能为空。", nameof(reference));
        if (reference.StartsWith("candle.", StringComparison.OrdinalIgnoreCase))
            return true;
        int separator = reference.LastIndexOf('.');
        if (separator <= 0)
            throw new ArgumentException($"无法识别策略条件引用 {reference}。", nameof(reference));
        IndicatorSeries? indicator = series.FirstOrDefault(item => item.Id.Equals(reference[..separator], StringComparison.OrdinalIgnoreCase));
        if (indicator is null || !indicator.Outputs.Any(output => output.Key.Equals(reference[(separator + 1)..], StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"策略条件引用 {reference} 不存在。", nameof(reference));
        return true;
    }

    private static string Describe(StrategyRuleSet rules)
    {
        string join = rules.MatchMode == StrategyMatchMode.All ? " 且 " : " 或 ";
        return string.Join(join, rules.Conditions.Select(condition =>
            $"{condition.Left} {Symbol(condition.Comparison)} {condition.Right ?? condition.Constant?.ToString("0.####")}"));
    }

    private static string Symbol(StrategyComparison comparison) => comparison switch
    {
        StrategyComparison.GreaterThan => ">",
        StrategyComparison.LessThan => "<",
        StrategyComparison.GreaterThanOrEqual => ">=",
        StrategyComparison.LessThanOrEqual => "<=",
        StrategyComparison.CrossAbove => "上穿",
        StrategyComparison.CrossBelow => "下穿",
        _ => comparison.ToString()
    };
}
