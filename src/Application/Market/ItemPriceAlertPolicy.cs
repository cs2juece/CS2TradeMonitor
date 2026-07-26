using CS2TradeMonitor.src.Core;

namespace CS2TradeMonitor.Application.Market
{
    internal static class ItemPriceAlertPolicy
    {
        public static ItemPriceAlertTriggerMode ResolveTriggerMode(ItemMonitorConfig item)
        {
            ArgumentNullException.ThrowIfNull(item);

            if (item.PriceAlertTriggerMode is ItemPriceAlertTriggerMode.Breakthrough
                or ItemPriceAlertTriggerMode.Percent)
            {
                return item.PriceAlertTriggerMode;
            }

            return item.PriceAlertAbove > 0 || item.PriceAlertBelow > 0
                ? ItemPriceAlertTriggerMode.Breakthrough
                : ItemPriceAlertTriggerMode.Percent;
        }

        public static bool IsDeliveryEnabled(ItemMonitorConfig item)
        {
            ArgumentNullException.ThrowIfNull(item);
            return item.PriceAlertDesktopEnabled || item.PriceAlertPhoneEnabled;
        }

        public static bool HasConfiguredThreshold(ItemMonitorConfig item, Settings settings)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(settings);

            if (ResolveTriggerMode(item) == ItemPriceAlertTriggerMode.Breakthrough)
                return item.PriceAlertAbove > 0 || item.PriceAlertBelow > 0;

            double rise = item.PriceAlertRisePercent > 0
                ? item.PriceAlertRisePercent
                : settings.DefaultItemPriceAlertRisePercent;
            double fall = item.PriceAlertFallPercent > 0
                ? item.PriceAlertFallPercent
                : settings.DefaultItemPriceAlertFallPercent;
            return rise > 0 || fall > 0;
        }
    }
}
