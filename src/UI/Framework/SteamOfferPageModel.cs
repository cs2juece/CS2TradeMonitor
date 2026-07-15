using System;
using CS2TradeMonitor.Application.Steam;

namespace CS2TradeMonitor.src.UI.Framework.SteamOffers
{
    internal static class SteamOfferPageModel
    {
        public const int DefaultAutoCheckIntervalSeconds = 180;
        public const int MinAutoCheckIntervalSeconds = 30;
        public const int MaxAutoCheckIntervalSeconds = 3600;
        public const string SteamOffersPageUrl = SteamUrls.MyTradeOffersWithSlash;

        public static int NormalizeAutoCheckInterval(int value)
        {
            int normalized = value <= 0 ? DefaultAutoCheckIntervalSeconds : value;
            return Math.Clamp(
                normalized,
                MinAutoCheckIntervalSeconds,
                MaxAutoCheckIntervalSeconds);
        }

        public static SteamOfferAutoConfirmSettingsViewModel BuildAutoConfirmSettings(
            int intervalSeconds,
            bool enabled,
            bool autoAccept,
            bool allowYouPinVerified)
        {
            return new SteamOfferAutoConfirmSettingsViewModel(
                NormalizeAutoCheckInterval(intervalSeconds),
                enabled,
                autoAccept,
                allowYouPinVerified);
        }

        public static double BuildTotpCountdownPercent(int secondsLeft)
        {
            return Math.Clamp(secondsLeft / 30.0, 0, 1);
        }

        public static string? BuildTradeOfferUrl(string? tradeOfferId)
        {
            if (string.IsNullOrWhiteSpace(tradeOfferId))
                return null;

            return SteamUrls.TradeOffer(tradeOfferId.Trim());
        }

        public static SteamOfferConnectionDetectionViewModel BuildConnectionDetectionView(bool detecting)
        {
            return detecting
                ? new SteamOfferConnectionDetectionViewModel(
                    "检测中",
                    false,
                    "Steam 网络：正在检测连接",
                    SteamConnectionStatusTone.Muted)
                : new SteamOfferConnectionDetectionViewModel(
                    "检测连接",
                    true,
                    null,
                    null);
        }
    }

    internal sealed record SteamOfferAutoConfirmSettingsViewModel(
        int IntervalSeconds,
        bool Enabled,
        bool AutoAccept,
        bool AllowYouPinVerified);

    internal sealed record SteamOfferConnectionDetectionViewModel(
        string ButtonText,
        bool ButtonEnabled,
        string? StatusText,
        SteamConnectionStatusTone? StatusTone);
}
