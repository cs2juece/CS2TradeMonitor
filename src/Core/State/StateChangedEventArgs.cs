using System;

namespace CS2TradeMonitor.src.Core.State
{
    public sealed class StateChangedEventArgs : EventArgs
    {
        public StateChangedEventArgs(string section, string reason, DateTime changedAt)
        {
            Section = section;
            Reason = reason;
            ChangedAt = changedAt;
        }

        public string Section { get; }

        public string Reason { get; }

        public DateTime ChangedAt { get; }
    }

    public sealed class ConfigChangedEventArgs : EventArgs
    {
        public ConfigChangedEventArgs(string reason, DateTime changedAt)
        {
            Reason = reason;
            ChangedAt = changedAt;
        }

        public string Reason { get; }

        public DateTime ChangedAt { get; }
    }
}
