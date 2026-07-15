using System;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface IClock
    {
        DateTime Now { get; }
        DateTime UtcNow { get; }
    }
}
