using System;

namespace CS2TradeMonitor.src.Core.State
{
    public sealed record YouPinInventorySnapshot(
        int ItemCount,
        double MarketValue,
        double BuyValue,
        double ProfitLoss,
        double ProfitLossPercent,
        DateTime RetrievedAt,
        string Status)
    {
        public static YouPinInventorySnapshot Empty { get; } = new(0, 0, 0, 0, 0, DateTime.MinValue, "未获取");
    }

}
