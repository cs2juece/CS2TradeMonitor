namespace CS2QuantWeb.Core;

internal static class ChanBuySellPointAnalyzer
{
    private const double DivergenceRate = 0.9;
    private const double MaximumSecondPointRetraceRate = 1;

    private sealed record FirstPoint(int StrokeIndex, ResearchSignal Signal);

    public static IReadOnlyList<ResearchSignal> Analyze(
        IReadOnlyList<ChanStroke> strokes,
        IReadOnlyList<ChanCenter> centers,
        IReadOnlyList<IndicatorPoint> indicators)
    {
        IReadOnlyList<FirstPoint> firstPoints = FindFirstPoints(strokes, indicators);
        return firstPoints.Select(point => point.Signal)
            .Concat(FindSecondPoints(strokes, firstPoints))
            .Concat(FindThirdPoints(strokes, centers))
            .DistinctBy(signal => (signal.Date, signal.ChanType))
            .OrderBy(signal => signal.Date)
            .ThenBy(signal => signal.ChanType)
            .ToArray();
    }

    private static IReadOnlyList<FirstPoint> FindFirstPoints(
        IReadOnlyList<ChanStroke> strokes,
        IReadOnlyList<IndicatorPoint> indicators)
    {
        var points = new List<FirstPoint>();
        for (int index = 2; index < strokes.Count; index++)
        {
            ChanStroke previous = strokes[index - 2];
            ChanStroke current = strokes[index];
            if (previous.IsUp != current.IsUp)
                continue;

            bool priceExtends = current.IsUp
                ? current.EndPrice > previous.EndPrice
                : current.EndPrice < previous.EndPrice;
            double previousArea = MacdArea(previous, indicators);
            double currentArea = MacdArea(current, indicators);
            if (!priceExtends || previousArea <= 0 || currentArea >= previousArea * DivergenceRate)
                continue;

            ChanSignalType type = current.IsUp ? ChanSignalType.FirstSell : ChanSignalType.FirstBuy;
            points.Add(new FirstPoint(index, CreateSignal(
                current,
                type,
                current.IsUp
                    ? "一卖：价格创新高，MACD 动能较前一同向笔衰减"
                    : "一买：价格创新低，MACD 动能较前一同向笔衰减")));
        }

        return points;
    }

    private static IEnumerable<ResearchSignal> FindSecondPoints(
        IReadOnlyList<ChanStroke> strokes,
        IReadOnlyList<FirstPoint> firstPoints)
    {
        foreach (FirstPoint firstPoint in firstPoints)
        {
            if (firstPoint.StrokeIndex + 2 >= strokes.Count)
                continue;

            ChanStroke first = strokes[firstPoint.StrokeIndex];
            ChanStroke departure = strokes[firstPoint.StrokeIndex + 1];
            ChanStroke retrace = strokes[firstPoint.StrokeIndex + 2];
            if (departure.IsUp == first.IsUp || retrace.IsUp != first.IsUp)
                continue;

            double departureAmplitude = Math.Abs(departure.EndPrice - departure.StartPrice);
            double retraceAmplitude = Math.Abs(retrace.EndPrice - retrace.StartPrice);
            if (departureAmplitude <= 0
                || retraceAmplitude / departureAmplitude > MaximumSecondPointRetraceRate)
            {
                continue;
            }

            bool holdsFirstPoint = first.IsUp
                ? retrace.EndPrice < first.EndPrice
                : retrace.EndPrice > first.EndPrice;
            if (!holdsFirstPoint)
                continue;

            ChanSignalType type = first.IsUp ? ChanSignalType.SecondSell : ChanSignalType.SecondBuy;
            yield return CreateSignal(
                retrace,
                type,
                first.IsUp
                    ? "二卖：一卖后反弹未创新高，回升幅度未超过前一离开笔"
                    : "二买：一买后回调未创新低，回撤幅度未超过前一离开笔");
        }
    }

    private static IEnumerable<ResearchSignal> FindThirdPoints(
        IReadOnlyList<ChanStroke> strokes,
        IReadOnlyList<ChanCenter> centers)
    {
        foreach (ChanCenter center in centers)
        {
            int departureIndex = FindDepartureIndex(strokes, center);
            if (departureIndex < 0 || departureIndex + 1 >= strokes.Count)
                continue;

            ChanStroke departure = strokes[departureIndex];
            ChanStroke retrace = strokes[departureIndex + 1];
            if (departure.IsUp == retrace.IsUp)
                continue;

            if (departure.IsUp
                && departure.EndPrice > center.Upper
                && !retrace.IsUp
                && retrace.EndPrice > center.Upper)
            {
                yield return CreateSignal(
                    retrace,
                    ChanSignalType.ThirdBuy,
                    $"三买：向上离开中枢后回抽不进入上沿 {center.Upper:N2}");
            }
            else if (!departure.IsUp
                && departure.EndPrice < center.Lower
                && retrace.IsUp
                && retrace.EndPrice < center.Lower)
            {
                yield return CreateSignal(
                    retrace,
                    ChanSignalType.ThirdSell,
                    $"三卖：向下离开中枢后回抽不进入下沿 {center.Lower:N2}");
            }
        }
    }

    private static int FindDepartureIndex(IReadOnlyList<ChanStroke> strokes, ChanCenter center)
    {
        for (int index = 0; index < strokes.Count; index++)
        {
            ChanStroke stroke = strokes[index];
            if (stroke.StartIndex < center.EndIndex)
                continue;

            if (stroke.EndPrice > center.Upper || stroke.EndPrice < center.Lower)
                return index;
        }

        return -1;
    }

    private static ResearchSignal CreateSignal(
        ChanStroke stroke,
        ChanSignalType type,
        string reason)
    {
        return new ResearchSignal(
            stroke.EndDate,
            "缠论买卖点",
            IsBuy(type) ? SignalSide.Buy : SignalSide.Sell,
            stroke.EndPrice,
            reason,
            DisplayName(type))
        {
            ChanType = type,
            AvailableDate = stroke.ConfirmedDate
        };
    }

    private static bool IsBuy(ChanSignalType type) =>
        type is ChanSignalType.FirstBuy or ChanSignalType.SecondBuy or ChanSignalType.ThirdBuy;

    private static string DisplayName(ChanSignalType type) => type switch
    {
        ChanSignalType.FirstBuy => "一买",
        ChanSignalType.FirstSell => "一卖",
        ChanSignalType.SecondBuy => "二买",
        ChanSignalType.SecondSell => "二卖",
        ChanSignalType.ThirdBuy => "三买",
        ChanSignalType.ThirdSell => "三卖",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static double MacdArea(ChanStroke stroke, IReadOnlyList<IndicatorPoint> indicators)
    {
        if (indicators.Count == 0)
            return 0;

        int start = Math.Clamp(stroke.StartIndex, 0, indicators.Count - 1);
        int end = Math.Clamp(stroke.EndIndex, 0, indicators.Count - 1);
        double area = 0;
        for (int index = start; index <= end; index++)
            area += Math.Abs(indicators[index].Histogram);

        return area;
    }
}
