namespace CS2QuantWeb.Core;

public sealed class QuantResearchModule : IQuantResearchModule
{
    public QuantResearchResult Analyze(
        string symbol,
        string source,
        IReadOnlyList<QuantCandle> candles)
    {
        ArgumentNullException.ThrowIfNull(candles);
        QuantCandle[] normalized = candles
            .Where(IsValid)
            .GroupBy(candle => candle.Date)
            .Select(group => group.Last())
            .OrderBy(candle => candle.Date)
            .ToArray();
        if (normalized.Length < 5)
            throw new ArgumentException("至少需要 5 根有效 K 线。", nameof(candles));

        IReadOnlyList<IndicatorPoint> indicators = TechnicalIndicators.Calculate(normalized);
        ChanAnalysis chan = ChanStructureAnalyzer.Analyze(normalized, indicators);
        IReadOnlyList<ResearchSignal> strategySignals = StrategyAnalyzer.Analyze(normalized, indicators);
        return new QuantResearchResult(
            string.IsNullOrWhiteSpace(symbol) ? "未命名序列" : symbol.Trim(),
            string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim(),
            "精简研究模块：MA/MACD、包含处理、分型、笔、线段、中枢、候选背驰与三类价格信号。无账户、持仓、订单或自动交易。",
            BuildSummary(normalized),
            normalized,
            indicators,
            chan,
            strategySignals,
            BuildBacktests(normalized, strategySignals));
    }

    private static bool IsValid(QuantCandle candle)
    {
        return candle.Open > 0
            && candle.High > 0
            && candle.Low > 0
            && candle.Close > 0
            && candle.High >= Math.Max(candle.Open, candle.Close)
            && candle.Low <= Math.Min(candle.Open, candle.Close)
            && double.IsFinite(candle.Open)
            && double.IsFinite(candle.High)
            && double.IsFinite(candle.Low)
            && double.IsFinite(candle.Close)
            && double.IsFinite(candle.Volume);
    }

    private static MarketSummary BuildSummary(IReadOnlyList<QuantCandle> candles)
    {
        double peak = candles[0].Close;
        double maxDrawdown = 0;
        foreach (QuantCandle candle in candles)
        {
            peak = Math.Max(peak, candle.Close);
            maxDrawdown = Math.Min(maxDrawdown, candle.Close / peak - 1);
        }

        double periodReturn = candles[^1].Close / candles[0].Close - 1;
        return new MarketSummary(
            candles.Count,
            candles[0].Date,
            candles[^1].Date,
            candles[^1].Close,
            periodReturn * 100,
            maxDrawdown * 100);
    }

    private static IReadOnlyList<BacktestSummary> BuildBacktests(
        IReadOnlyList<QuantCandle> candles,
        IReadOnlyList<ResearchSignal> signals)
    {
        return [
            Backtest("趋势突破", candles, signals),
            Backtest("恐慌底", candles, signals)
        ];
    }

    private static BacktestSummary Backtest(
        string strategy,
        IReadOnlyList<QuantCandle> candles,
        IReadOnlyList<ResearchSignal> signals)
    {
        ResearchSignal[] relevant = signals
            .Where(signal => signal.Strategy == strategy && signal.Side != SignalSide.Risk)
            .OrderBy(signal => signal.Date)
            .ToArray();
        var trades = new List<(ResearchSignal Buy, ResearchSignal Sell, double Return)>();
        ResearchSignal? pendingBuy = null;
        foreach (ResearchSignal signal in relevant)
        {
            if (pendingBuy is null && signal.Side == SignalSide.Buy)
            {
                pendingBuy = signal;
                continue;
            }

            if (pendingBuy is not null && signal.Side == SignalSide.Sell && signal.Date > pendingBuy.Date)
            {
                trades.Add((pendingBuy, signal, signal.Price / pendingBuy.Price - 1));
                pendingBuy = null;
            }
        }

        if (trades.Count == 0)
            return new BacktestSummary(strategy, 0, 0, 0, 0, 0);

        double equity = 1;
        double peak = 1;
        double maxDrawdown = 0;
        foreach (var trade in trades)
        {
            equity *= 1 + trade.Return;
            peak = Math.Max(peak, equity);
            maxDrawdown = Math.Min(maxDrawdown, equity / peak - 1);
        }

        return new BacktestSummary(
            strategy,
            trades.Count,
            (equity - 1) * 100,
            trades.Count(trade => trade.Return > 0) * 100d / trades.Count,
            maxDrawdown * 100,
            trades.Average(trade => trade.Sell.Date.DayNumber - trade.Buy.Date.DayNumber));
    }
}
