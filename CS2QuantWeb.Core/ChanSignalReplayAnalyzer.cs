namespace CS2QuantWeb.Core;

internal static class ChanSignalReplayAnalyzer
{
    public static IReadOnlyList<ResearchSignal> Replay(IReadOnlyList<QuantCandle> candles)
    {
        var seen = new HashSet<(DateOnly Date, ChanSignalType? Type, SignalSide Side)>();
        var confirmed = new List<ResearchSignal>();
        for (int endIndex = 4; endIndex < candles.Count; endIndex++)
        {
            QuantCandle[] prefix = candles.Take(endIndex + 1).ToArray();
            IReadOnlyList<IndicatorPoint> indicators = TechnicalIndicators.Calculate(prefix);
            ChanAnalysis analysis = ChanStructureAnalyzer.Analyze(prefix, indicators);
            DateOnly replayDate = prefix[^1].Date;
            foreach (ResearchSignal signal in analysis.Signals)
            {
                var identity = (signal.Date, signal.ChanType, signal.Side);
                if (signal.AvailableDate > replayDate || !seen.Add(identity))
                    continue;

                confirmed.Add(signal with { AvailableDate = replayDate });
            }
        }

        return confirmed
            .OrderBy(signal => signal.AvailableDate)
            .ThenBy(signal => signal.Date)
            .ThenBy(signal => signal.ChanType)
            .ToArray();
    }
}
