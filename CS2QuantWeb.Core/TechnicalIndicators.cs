namespace CS2QuantWeb.Core;

internal static class TechnicalIndicators
{
    public static IReadOnlyList<IndicatorPoint> Calculate(IReadOnlyList<QuantCandle> candles)
    {
        var result = new List<IndicatorPoint>(candles.Count);
        double? ema12 = null;
        double? ema26 = null;
        double dea = 0;

        for (int i = 0; i < candles.Count; i++)
        {
            double close = candles[i].Close;
            ema12 = NextEma(ema12, close, 12);
            ema26 = NextEma(ema26, close, 26);
            double dif = ema12.Value - ema26.Value;
            dea = i == 0 ? dif : NextEma(dea, dif, 9);

            result.Add(new IndicatorPoint(
                candles[i].Date,
                close,
                SimpleAverage(candles, i, 5, static candle => candle.Close),
                SimpleAverage(candles, i, 10, static candle => candle.Close),
                SimpleAverage(candles, i, 20, static candle => candle.Close),
                SimpleAverage(candles, i, 20, static candle => candle.Volume),
                dif,
                dea,
                (dif - dea) * 2));
        }

        return result;
    }

    private static double NextEma(double? previous, double value, int period)
    {
        if (!previous.HasValue)
            return value;

        double alpha = 2d / (period + 1d);
        return alpha * value + (1d - alpha) * previous.Value;
    }

    private static double? SimpleAverage(
        IReadOnlyList<QuantCandle> candles,
        int endIndex,
        int period,
        Func<QuantCandle, double> selector)
    {
        if (endIndex + 1 < period)
            return null;

        double total = 0;
        for (int i = endIndex - period + 1; i <= endIndex; i++)
            total += selector(candles[i]);

        return total / period;
    }
}
