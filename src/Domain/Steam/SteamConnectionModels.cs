using System;

namespace CS2TradeMonitor.Domain.Steam
{
    public enum SteamConnectionMode
    {
        Unknown,
        ManualProxy,
        AutoProxy,
        Direct,
        Failed
    }

    public sealed class SteamConnectionProfile
    {
        public SteamConnectionMode Mode { get; set; } = SteamConnectionMode.Unknown;
        public string ProxyUri { get; set; } = "";
        public string RouteName { get; set; } = "";
        public DateTime LastSuccessAt { get; set; } = DateTime.MinValue;
        public int FailureCount { get; set; }
        public DateTime CooldownUntil { get; set; } = DateTime.MinValue;
        public string FailureReason { get; set; } = "";

        public bool IsUsable => Mode is SteamConnectionMode.ManualProxy or SteamConnectionMode.AutoProxy or SteamConnectionMode.Direct;

        public SteamConnectionProfile Clone() => new()
        {
            Mode = Mode,
            ProxyUri = ProxyUri,
            RouteName = RouteName,
            LastSuccessAt = LastSuccessAt,
            FailureCount = FailureCount,
            CooldownUntil = CooldownUntil,
            FailureReason = FailureReason
        };
    }
}
