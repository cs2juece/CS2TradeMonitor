namespace CS2TradeMonitor.src.Core
{
    public enum MarketRefreshTrigger
    {
        Automatic,
        Display,
        Startup,
        Manual,
        RouteRecovered
    }

    public sealed record MarketRefreshRequest(
        MarketRefreshTrigger Trigger,
        string Reason,
        bool ForceSources,
        bool WaitForSteamDtLock)
    {
        public static MarketRefreshRequest For(MarketRefreshTrigger trigger, string? reason = null)
        {
            bool force = trigger is MarketRefreshTrigger.Manual
                or MarketRefreshTrigger.RouteRecovered
                or MarketRefreshTrigger.Startup;
            bool waitForSteamDtLock = force;
            string resolvedReason = string.IsNullOrWhiteSpace(reason) ? DefaultReason(trigger) : reason.Trim();
            return new MarketRefreshRequest(trigger, resolvedReason, force, waitForSteamDtLock);
        }

        private static string DefaultReason(MarketRefreshTrigger trigger)
        {
            return trigger switch
            {
                MarketRefreshTrigger.Manual => "手动刷新",
                MarketRefreshTrigger.RouteRecovered => "网络路由恢复",
                MarketRefreshTrigger.Startup => "启动立即刷新",
                MarketRefreshTrigger.Display => "显示触发刷新",
                _ => "自动刷新"
            };
        }
    }
}
