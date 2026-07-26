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
        IReadOnlyList<ResearchSignal> signals = ChanBuySellPointAnalyzer.Analyze(strokes, centers, indicators);
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
            {
                candidates.Add(new ChanFractal(middle.EndIndex, middle.Date, FractalKind.Top, middle.High)
                {
                    ConfirmedIndex = right.EndIndex,
                    ConfirmedDate = right.Date
                });
            }
            else if (isBottom)
            {
                candidates.Add(new ChanFractal(middle.EndIndex, middle.Date, FractalKind.Bottom, middle.Low)
                {
                    ConfirmedIndex = right.EndIndex,
                    ConfirmedDate = right.Date
                });
            }
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
                fractal.Price > start.Price)
            {
                ConfirmedIndex = fractal.ConfirmedIndex,
                ConfirmedDate = fractal.ConfirmedDate
            });
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

            int endStrokeIndex = i + 2;
            while (endStrokeIndex + 1 < strokes.Count)
            {
                ChanStroke next = strokes[endStrokeIndex + 1];
                if (next.EndPrice < lower || next.EndPrice > upper)
                    break;

                endStrokeIndex++;
            }

            ChanStroke last = strokes[endStrokeIndex];
            centers.Add(new ChanCenter(
                window[0].StartIndex,
                last.EndIndex,
                window[0].StartDate,
                last.EndDate,
                lower,
                upper));
            i = Math.Max(i, endStrokeIndex - 2);
        }

        return centers;
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
        conclusions.Add("一、二、三类买卖点采用可解释的结构规则，属于研究候选信号，需人工复核。");
        return conclusions;
    }
}
