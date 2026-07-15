using CS2TradeMonitor.Application.Abstractions;
using System;

namespace CS2TradeMonitor.Infrastructure.System
{
    public sealed class SystemClock : IClock
    {
        public DateTime Now => DateTime.Now;
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
