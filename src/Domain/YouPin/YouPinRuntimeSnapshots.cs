using System;

namespace CS2TradeMonitor.Domain.YouPin
{
    public sealed record YouPinTodoSnapshot(
        int TodoCount,
        int WaitDeliverCount,
        DateTime RetrievedAt,
        string Status)
    {
        public static YouPinTodoSnapshot Empty { get; } = new(0, 0, DateTime.MinValue, "未获取");
    }
}
