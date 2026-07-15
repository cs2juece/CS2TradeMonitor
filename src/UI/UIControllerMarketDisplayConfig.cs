using CS2TradeMonitor.src.Core;
using System.Collections.Generic;
using System.Linq;

namespace CS2TradeMonitor
{
    internal static class UIControllerMarketDisplayConfig
    {
        public static bool IsMarketDisplayKey(string key)
        {
            return MarketDisplayFormatter.IsMarketDisplayKey(key);
        }

        public static bool IsMarketDisplayOnly(IEnumerable<MonitorItemConfig> items)
        {
            MonitorItemConfig[] itemArray = items as MonitorItemConfig[] ?? items.ToArray();
            return itemArray.Length > 0 && itemArray.All(item => IsMarketDisplayKey(item.Key));
        }

        public static bool IsMarketDisplayOnlyKeys(IEnumerable<string> keys)
        {
            string[] keyArray = keys as string[] ?? keys.ToArray();
            return keyArray.Length > 0 && keyArray.All(IsMarketDisplayKey);
        }

        public static int GetMarketDisplayOrder(string key)
        {
            return MarketDisplayFormatter.GetMarketDisplayOrder(key);
        }

        public static void NormalizeMarketDisplayItem(MonitorItemConfig cfg)
        {
            if (!IsMarketDisplayKey(cfg.Key)) return;

            cfg.UserLabel = " ";
            cfg.TaskbarLabel = " ";
            cfg.UnitPanel = "";
            cfg.UnitTaskbar = "";
        }
    }
}
