using System;

namespace CS2TradeMonitor.src.Core.Modules
{
    public enum MonitorModuleState
    {
        NotStarted,
        Starting,
        Running,
        Paused,
        Faulted,
        Stopping,
        Stopped
    }

    public sealed class MonitorModuleHealth
    {
        public string Id { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public MonitorModuleState State { get; init; } = MonitorModuleState.NotStarted;
        public string Message { get; init; } = "未启动";
        public DateTimeOffset LastChanged { get; init; } = DateTimeOffset.Now;
        public string Scope { get; init; } = string.Empty;
        public bool IsHighRisk { get; init; }
        public bool ProcessIsolationCandidate { get; init; }

        public bool IsHealthy => State == MonitorModuleState.Running || State == MonitorModuleState.Paused;
    }
}
