using CS2TradeMonitor.src.Core;
using System;

namespace CS2TradeMonitor.Application.Steam
{
    internal static class SteamAutoTradeSettingsPersistence
    {
        public static SteamAutoTradeSettings Normalize(SteamAutoTradeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            bool enabled = settings.AcceptPureIncomingEnabled
                || settings.AcceptYouPinPurchaseEnabled
                || settings.SendYouPinSaleEnabled
                || settings.SendYouPinRentalEnabled;

            return new SteamAutoTradeSettings
            {
                Enabled = enabled,
                AcceptPureIncomingEnabled = settings.AcceptPureIncomingEnabled,
                AcceptYouPinPurchaseEnabled = settings.AcceptYouPinPurchaseEnabled,
                SendYouPinSaleEnabled = settings.SendYouPinSaleEnabled,
                SendYouPinRentalEnabled = settings.SendYouPinRentalEnabled,
                IntervalSeconds = ClampInterval(settings.IntervalSeconds)
            };
        }

        public static SteamAutoTradeSettings ReadFrom(Settings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            bool acceptPure = settings.SteamAutoTradeAcceptPureIncomingEnabled;
            bool acceptYouPinPurchase = settings.SteamAutoTradeAcceptYouPinPurchaseEnabled;
            bool sendYouPinSale = settings.SteamAutoTradeSendYouPinSaleEnabled;
            bool sendYouPinRental = settings.SteamAutoTradeSendYouPinRentalEnabled;

            return new SteamAutoTradeSettings
            {
                Enabled = acceptPure || acceptYouPinPurchase || sendYouPinSale || sendYouPinRental,
                AcceptPureIncomingEnabled = acceptPure,
                AcceptYouPinPurchaseEnabled = acceptYouPinPurchase,
                SendYouPinSaleEnabled = sendYouPinSale,
                SendYouPinRentalEnabled = sendYouPinRental,
                IntervalSeconds = ClampInterval(settings.SteamAutoTradeIntervalSeconds)
            };
        }

        public static void ApplyTo(Settings settings, SteamAutoTradeSettings autoTradeSettings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(autoTradeSettings);

            SteamAutoTradeSettings normalized = Normalize(autoTradeSettings);

            settings.SteamAutoTradeAcceptPureIncomingEnabled = normalized.AcceptPureIncomingEnabled;
            settings.SteamAutoTradeAcceptYouPinPurchaseEnabled = normalized.AcceptYouPinPurchaseEnabled;
            settings.SteamAutoTradeSendYouPinSaleEnabled = normalized.SendYouPinSaleEnabled;
            settings.SteamAutoTradeSendYouPinRentalEnabled = normalized.SendYouPinRentalEnabled;
            settings.SteamAutoTradeEnabled = normalized.Enabled;
            settings.SteamAutoTradeIntervalSeconds = normalized.IntervalSeconds;
        }

        private static int ClampInterval(int intervalSeconds)
        {
            return Math.Clamp(intervalSeconds <= 0 ? 300 : intervalSeconds, 30, 3600);
        }
    }
}
