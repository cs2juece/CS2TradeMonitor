using CS2TradeMonitor.src.Core;

namespace CS2TradeMonitor.src.UI.Framework
{
    /// <summary>
    /// SettingsStore keys shared by the production data-source page.
    /// </summary>
    internal static class SettingsKeys
    {
        public const string SteamDtApiKey = nameof(Settings.SteamDtApiKey);
        public const string SteamDtRefreshSec = nameof(Settings.SteamDtRefreshSec);
        public const string SteamDtShowPercent = nameof(Settings.SteamDtShowPercent);
        public const string CsqaqApiToken = nameof(Settings.CsqaqApiToken);
        public const string CsqaqRefreshSec = nameof(Settings.CsqaqRefreshSec);
        public const string MarketFormat = nameof(Settings.MarketFormat);
    }
}
