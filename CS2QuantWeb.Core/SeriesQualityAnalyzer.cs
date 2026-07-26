namespace CS2QuantWeb.Core;

internal static class SeriesQualityAnalyzer
{
    private const int SuspiciousGapDays = 10;
    private const int StaleWarningDays = 10;

    public static SeriesQualityReport Evaluate(
        IReadOnlyList<QuantCandle> input,
        IReadOnlyList<QuantCandle> normalized)
    {
        int outOfOrderCount = 0;
        for (int i = 1; i < input.Count; i++)
        {
            if (input[i].Date < input[i - 1].Date)
                outOfOrderCount++;
        }

        int suspiciousGapCount = 0;
        for (int i = 1; i < normalized.Count; i++)
        {
            if (normalized[i].Date.DayNumber - normalized[i - 1].Date.DayNumber > SuspiciousGapDays)
                suspiciousGapCount++;
        }

        int staleDays = normalized.Count == 0
            ? 0
            : Math.Max(0, DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - normalized[^1].Date.DayNumber);
        var warnings = new List<SeriesQualityWarning>();
        if (outOfOrderCount > 0)
        {
            warnings.Add(new SeriesQualityWarning(
                "out_of_order",
                "原始K线日期存在乱序，分析前已按日期重新排序。",
                outOfOrderCount));
        }

        if (suspiciousGapCount > 0)
        {
            warnings.Add(new SeriesQualityWarning(
                "suspicious_gap",
                "K线日期存在超过10天的间隔，请确认数据源是否缺失。",
                suspiciousGapCount));
        }

        if (staleDays > StaleWarningDays)
        {
            warnings.Add(new SeriesQualityWarning(
                "stale_data",
                $"最新K线距当前日期已超过{StaleWarningDays}天。",
                staleDays));
        }

        if (normalized.Count > 0 && normalized.All(candle => candle.Volume == 0))
        {
            warnings.Add(new SeriesQualityWarning(
                "volume_unavailable",
                "数据源未提供可靠成交量；成交量不参与研究结论。"));
        }

        return new SeriesQualityReport(
            normalized.Count >= 5,
            input.Count,
            normalized.Count,
            outOfOrderCount,
            suspiciousGapCount,
            staleDays,
            warnings);
    }
}
