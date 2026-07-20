namespace CS2QuantWeb.Core;

internal static class StrategyAnalyzer
{
    public static IReadOnlyList<ResearchSignal> Analyze(
        IReadOnlyList<QuantCandle> candles,
        IReadOnlyList<IndicatorPoint> indicators)
    {
        var signals = new List<ResearchSignal>();
        for (int i = 0; i < candles.Count; i++)
        {
            AddTrendSignals(candles, indicators, i, signals);
            AddPanicSignals(candles, i, signals);
            AddHighRiskSignals(candles, i, signals);
        }

        return signals
            .OrderBy(signal => signal.Date)
            .ThenBy(signal => signal.Strategy, StringComparer.Ordinal)
            .ThenBy(signal => signal.Side)
            .ToArray();
    }

    private static void AddTrendSignals(
        IReadOnlyList<QuantCandle> candles,
        IReadOnlyList<IndicatorPoint> indicators,
        int i,
        ICollection<ResearchSignal> signals)
    {
        if (i < 22)
            return;

        IndicatorPoint current = indicators[i];
        if (current.Ma5 is null || current.Ma10 is null || current.Ma20 is null)
            return;

        bool buyCandidate = IsTrendBuyCandidate(candles, indicators, i);
        bool previousBuyCandidate = i > 22 && IsTrendBuyCandidate(candles, indicators, i - 1);
        if (buyCandidate && !previousBuyCandidate)
        {
            signals.Add(new ResearchSignal(
                candles[i].Date,
                "趋势突破",
                SignalSide.Buy,
                candles[i].Close,
                "MA5 > MA10 > MA20，收盘站上 MA5，回踩未破 MA10 的 3% 缓冲"));
        }

        IndicatorPoint previous = indicators[i - 1];
        bool bearish = current.Ma5 < current.Ma10 && current.Ma10 < current.Ma20;
        bool previousBearish = previous.Ma5 < previous.Ma10 && previous.Ma10 < previous.Ma20;
        if (bearish && !previousBearish && candles[i].Close < current.Ma10.Value)
        {
            signals.Add(new ResearchSignal(
                candles[i].Date,
                "趋势突破",
                SignalSide.Sell,
                candles[i].Close,
                "均线空头首次形成，收盘跌破 MA10"));
        }
    }

    private static bool IsTrendBuyCandidate(
        IReadOnlyList<QuantCandle> candles,
        IReadOnlyList<IndicatorPoint> indicators,
        int index)
    {
        if (index < 0)
            return false;

        IndicatorPoint point = indicators[index];
        return point.Ma5 is not null
            && point.Ma10 is not null
            && point.Ma20 is not null
            && point.Ma5 > point.Ma10
            && point.Ma10 > point.Ma20
            && candles[index].Low > point.Ma10.Value * 0.97
            && candles[index].Close > point.Ma5.Value;
    }

    private static void AddPanicSignals(
        IReadOnlyList<QuantCandle> candles,
        int i,
        ICollection<ResearchSignal> signals)
    {
        const int declineDays = 3;
        if (i < declineDays + 2)
            return;

        var recentReturns = new double[declineDays];
        for (int offset = 0; offset < declineDays; offset++)
        {
            int currentIndex = i - offset;
            recentReturns[offset] = Return(candles[currentIndex - 1].Close, candles[currentIndex].Close);
        }

        int declineCount = recentReturns.Count(value => value < 0);
        double totalDecline = Return(candles[i - declineDays].Close, candles[i].Close);
        double earlierAverage = recentReturns.Skip(1).Average();
        bool declineDecelerating = Math.Abs(recentReturns[0]) < Math.Abs(earlierAverage);
        bool repaired = candles[i].Close > candles[i].Open || recentReturns[0] > -0.005;
        bool panicCandidate = declineCount >= 2 && totalDecline < -0.02 && declineDecelerating && repaired;
        if (panicCandidate)
        {
            signals.Add(new ResearchSignal(
                candles[i].Date,
                "恐慌底",
                SignalSide.Buy,
                candles[i].Close,
                "近 3 日至少 2 日下跌且累计跌超 2%，跌幅衰减并出现当日修复"));
        }

        double recentGain = i >= 10 ? Return(candles[i - 10].Close, candles[i].Close) : 0;
        double previousGain = i >= 11 ? Return(candles[i - 11].Close, candles[i - 1].Close) : 0;
        if (i >= 20 && recentGain > 0.15 && previousGain <= 0.15)
        {
            signals.Add(new ResearchSignal(
                candles[i].Date,
                "恐慌底",
                SignalSide.Sell,
                candles[i].Close,
                "10 日反弹超过 15%，触发研究性止盈信号"));
        }
    }

    private static void AddHighRiskSignals(
        IReadOnlyList<QuantCandle> candles,
        int i,
        ICollection<ResearchSignal> signals)
    {
        const int riseDays = 3;
        if (i < riseDays + 4)
            return;

        var recentReturns = new double[riseDays];
        for (int offset = 0; offset < riseDays; offset++)
        {
            int currentIndex = i - offset;
            recentReturns[offset] = Return(candles[currentIndex - 1].Close, candles[currentIndex].Close);
        }

        int riseCount = recentReturns.Count(value => value > 0);
        double totalRise = Return(candles[i - riseDays].Close, candles[i].Close);
        if (riseCount < 2 || totalRise <= 0.03 || candles[i].Close <= 0)
            return;

        double bodyTop = Math.Max(candles[i].Open, candles[i].Close);
        double upperShadow = (candles[i].High - bodyTop) / candles[i].Close;
        bool closedDownAtHigh = candles[i].Close < candles[i].Open && totalRise > 0.05;
        if (upperShadow <= 0.015 && !closedDownAtHigh)
            return;

        signals.Add(new ResearchSignal(
            candles[i].Date,
            "高位风险",
            SignalSide.Risk,
            candles[i].Close,
            "近 3 日上涨且出现长上影或高位收阴",
            "warning"));

        if (upperShadow > 0.03 || (candles[i].Close < candles[i].Open && totalRise > 0.08))
        {
            signals.Add(new ResearchSignal(
                candles[i].Date,
                "高位风险",
                SignalSide.Sell,
                candles[i].Close,
                "冲高回落强度达到高风险阈值",
                "warning"));
        }
    }

    private static double Return(double start, double end) => start == 0 ? 0 : end / start - 1;
}
