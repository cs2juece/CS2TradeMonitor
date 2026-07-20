namespace CS2QuantWeb.Core;

internal static class ChanStructureAnalyzer
{
    private sealed record MergedBar(
        int StartIndex,
        int EndIndex,
        DateOnly Date,
        double Open,
        double High,
        double Low,
        double Close);

    public static ChanAnalysis Analyze(
        IReadOnlyList<QuantCandle> candles,
        IReadOnlyList<IndicatorPoint> indicators)
    {
        IReadOnlyList<MergedBar> merged = MergeContainment(candles);
        IReadOnlyList<ChanFractal> fractals = FindFractals(merged);
        IReadOnlyList<ChanStroke> strokes = BuildStrokes(fractals);
        IReadOnlyList<ChanSegment> segments = BuildSegments(strokes);
        IReadOnlyList<ChanCenter> centers = BuildCenters(strokes);
        IReadOnlyList<ResearchSignal> signals = BuildSignals(strokes, centers, indicators);
        IReadOnlyList<string> conclusions = BuildConclusions(fractals, strokes, centers, signals);
        return new ChanAnalysis(fractals, strokes, segments, centers, signals, conclusions);
    }

    private static IReadOnlyList<MergedBar> MergeContainment(IReadOnlyList<QuantCandle> candles)
    {
        var merged = new List<MergedBar>();
        for (int i = 0; i < candles.Count; i++)
        {
            QuantCandle candle = candles[i];
            var current = new MergedBar(i, i, candle.Date, candle.Open, candle.High, candle.Low, candle.Close);
            if (merged.Count == 0)
            {
                merged.Add(current);
                continue;
            }

            MergedBar previous = merged[^1];
            bool contains = previous.High >= current.High && previous.Low <= current.Low;
            bool isContained = current.High >= previous.High && current.Low <= previous.Low;
            if (!contains && !isContained)
            {
                merged.Add(current);
                continue;
            }

            bool rising = merged.Count < 2
                ? current.Close >= previous.Close
                : previous.High >= merged[^2].High;
            double high = rising ? Math.Max(previous.High, current.High) : Math.Min(previous.High, current.High);
            double low = rising ? Math.Max(previous.Low, current.Low) : Math.Min(previous.Low, current.Low);
            if (low > high)
                (low, high) = (high, low);

            merged[^1] = new MergedBar(
                previous.StartIndex,
                current.EndIndex,
                current.Date,
                previous.Open,
                high,
                low,
                current.Close);
        }

        return merged;
    }

    private static IReadOnlyList<ChanFractal> FindFractals(IReadOnlyList<MergedBar> bars)
    {
        var candidates = new List<ChanFractal>();
        for (int i = 1; i < bars.Count - 1; i++)
        {
            MergedBar left = bars[i - 1];
            MergedBar middle = bars[i];
            MergedBar right = bars[i + 1];
            bool isTop = middle.High > left.High && middle.High >= right.High
                && middle.Low > left.Low && middle.Low >= right.Low;
            bool isBottom = middle.Low < left.Low && middle.Low <= right.Low
                && middle.High < left.High && middle.High <= right.High;
            if (isTop)
                candidates.Add(new ChanFractal(middle.EndIndex, middle.Date, FractalKind.Top, middle.High));
            else if (isBottom)
                candidates.Add(new ChanFractal(middle.EndIndex, middle.Date, FractalKind.Bottom, middle.Low));
        }

        var alternating = new List<ChanFractal>();
        foreach (ChanFractal candidate in candidates)
        {
            if (alternating.Count == 0 || alternating[^1].Kind != candidate.Kind)
            {
                alternating.Add(candidate);
                continue;
            }

            ChanFractal previous = alternating[^1];
            bool moreExtreme = candidate.Kind == FractalKind.Top
                ? candidate.Price >= previous.Price
                : candidate.Price <= previous.Price;
            if (moreExtreme)
                alternating[^1] = candidate;
        }

        return alternating;
    }

    private static IReadOnlyList<ChanStroke> BuildStrokes(IReadOnlyList<ChanFractal> fractals)
    {
        var strokes = new List<ChanStroke>();
        ChanFractal? start = null;
        foreach (ChanFractal fractal in fractals)
        {
            if (start is null)
            {
                start = fractal;
                continue;
            }

            if (start.Kind == fractal.Kind)
            {
                bool moreExtreme = fractal.Kind == FractalKind.Top
                    ? fractal.Price >= start.Price
                    : fractal.Price <= start.Price;
                if (moreExtreme)
                    start = fractal;
                continue;
            }

            if (fractal.Index - start.Index < 4)
                continue;

            strokes.Add(new ChanStroke(
                start.Index,
                fractal.Index,
                start.Date,
                fractal.Date,
                start.Price,
                fractal.Price,
                fractal.Price > start.Price));
            start = fractal;
        }

        return strokes;
    }

    private static IReadOnlyList<ChanSegment> BuildSegments(IReadOnlyList<ChanStroke> strokes)
    {
        var segments = new List<ChanSegment>();
        for (int i = 0; i + 2 < strokes.Count; i += 2)
        {
            ChanStroke first = strokes[i];
            ChanStroke third = strokes[i + 2];
            bool extends = first.IsUp == third.IsUp
                && (first.IsUp ? third.EndPrice > first.EndPrice : third.EndPrice < first.EndPrice);
            if (!extends)
                continue;

            segments.Add(new ChanSegment(
                first.StartIndex,
                third.EndIndex,
                first.StartDate,
                third.EndDate,
                first.StartPrice,
                third.EndPrice,
                first.IsUp));
        }

        return segments;
    }

    private static IReadOnlyList<ChanCenter> BuildCenters(IReadOnlyList<ChanStroke> strokes)
    {
        var centers = new List<ChanCenter>();
        for (int i = 0; i + 2 < strokes.Count; i++)
        {
            ChanStroke[] window = [strokes[i], strokes[i + 1], strokes[i + 2]];
            double lower = window.Max(stroke => Math.Min(stroke.StartPrice, stroke.EndPrice));
            double upper = window.Min(stroke => Math.Max(stroke.StartPrice, stroke.EndPrice));
            if (lower > upper)
                continue;

            var center = new ChanCenter(
                window[0].StartIndex,
                window[^1].EndIndex,
                window[0].StartDate,
                window[^1].EndDate,
                lower,
                upper);
            if (centers.Count > 0 && center.StartIndex <= centers[^1].EndIndex)
            {
                ChanCenter previous = centers[^1];
                double mergedLower = Math.Max(previous.Lower, center.Lower);
                double mergedUpper = Math.Min(previous.Upper, center.Upper);
                if (mergedLower <= mergedUpper)
                {
                    centers[^1] = previous with
                    {
                        EndIndex = center.EndIndex,
                        EndDate = center.EndDate,
                        Lower = mergedLower,
                        Upper = mergedUpper
                    };
                    continue;
                }
            }

            centers.Add(center);
        }

        return centers;
    }

    private static IReadOnlyList<ResearchSignal> BuildSignals(
        IReadOnlyList<ChanStroke> strokes,
        IReadOnlyList<ChanCenter> centers,
        IReadOnlyList<IndicatorPoint> indicators)
    {
        var signals = new List<ResearchSignal>();
        for (int i = 2; i < strokes.Count; i++)
        {
            ChanStroke previous = strokes[i - 2];
            ChanStroke current = strokes[i];
            if (previous.IsUp != current.IsUp)
                continue;

            bool priceExtends = current.IsUp
                ? current.EndPrice > previous.EndPrice
                : current.EndPrice < previous.EndPrice;
            double previousArea = MacdArea(previous, indicators);
            double currentArea = MacdArea(current, indicators);
            if (!priceExtends || previousArea <= 0 || currentArea >= previousArea * 0.9)
                continue;

            signals.Add(new ResearchSignal(
                current.EndDate,
                "精简缠论",
                current.IsUp ? SignalSide.Sell : SignalSide.Buy,
                current.EndPrice,
                current.IsUp ? "疑似一卖：价格创新高但 MACD 动能减弱" : "疑似一买：价格创新低但 MACD 动能减弱",
                "candidate"));
        }

        foreach (ChanCenter center in centers)
        {
            ChanStroke? exit = strokes.FirstOrDefault(stroke => stroke.StartIndex >= center.EndIndex);
            if (exit is null)
                continue;

            if (exit.IsUp && exit.EndPrice > center.Upper)
            {
                signals.Add(new ResearchSignal(
                    exit.EndDate,
                    "精简缠论",
                    SignalSide.Buy,
                    exit.EndPrice,
                    "疑似三买：离开中枢上沿",
                    "candidate"));
            }
            else if (!exit.IsUp && exit.EndPrice < center.Lower)
            {
                signals.Add(new ResearchSignal(
                    exit.EndDate,
                    "精简缠论",
                    SignalSide.Sell,
                    exit.EndPrice,
                    "疑似三卖：离开中枢下沿",
                    "candidate"));
            }
        }

        return signals
            .DistinctBy(signal => (signal.Date, signal.Side, signal.Reason))
            .OrderBy(signal => signal.Date)
            .ToArray();
    }

    private static double MacdArea(ChanStroke stroke, IReadOnlyList<IndicatorPoint> indicators)
    {
        int start = Math.Clamp(stroke.StartIndex, 0, indicators.Count - 1);
        int end = Math.Clamp(stroke.EndIndex, 0, indicators.Count - 1);
        double area = 0;
        for (int i = start; i <= end; i++)
            area += Math.Abs(indicators[i].Histogram);

        return area;
    }

    private static IReadOnlyList<string> BuildConclusions(
        IReadOnlyList<ChanFractal> fractals,
        IReadOnlyList<ChanStroke> strokes,
        IReadOnlyList<ChanCenter> centers,
        IReadOnlyList<ResearchSignal> signals)
    {
        var conclusions = new List<string>
        {
            $"识别 {fractals.Count} 个分型、{strokes.Count} 笔、{centers.Count} 个中枢。"
        };

        if (strokes.Count > 0)
            conclusions.Add(strokes[^1].IsUp ? "最新一笔方向向上。" : "最新一笔方向向下。");
        if (signals.Count > 0)
            conclusions.Add($"最近候选信号：{signals[^1].Reason}（{signals[^1].Date:yyyy-MM-dd}）。");
        else
            conclusions.Add("当前没有满足阈值的缠论候选买卖点。");
        conclusions.Add("本模块采用可解释的精简结构规则，不等同于完整 chan.py。候选信号需人工复核。 ");
        return conclusions;
    }
}
