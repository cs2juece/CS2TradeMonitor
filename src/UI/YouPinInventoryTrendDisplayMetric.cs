using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.Core.State;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

namespace CS2TradeMonitor
{
    internal static class YouPinInventoryTrendDisplayMetric
    {
        public const string Key = "YOUPIN.InventoryTrend";
        public const string TaskbarDisplayLabel = "库存";

        public static List<MonitorItemConfig> IncludeConfigured(Settings cfg, IEnumerable<MonitorItemConfig> source)
        {
            var items = source.ToList();
            if (!cfg.YouPinTrendIndicatorVisibleInPanel && !cfg.YouPinTrendIndicatorVisibleInTaskbar)
                return items;

            if (items.Any(item => string.Equals(item.Key, Key, StringComparison.OrdinalIgnoreCase)))
                return items;

            items.Add(new MonitorItemConfig
            {
                Key = Key,
                UserLabel = "库存涨跌",
                TaskbarLabel = TaskbarDisplayLabel,
                VisibleInPanel = cfg.YouPinTrendIndicatorVisibleInPanel,
                VisibleInTaskbar = cfg.YouPinTrendIndicatorVisibleInTaskbar,
                SortIndex = 503,
                TaskbarSortIndex = 5003,
                UnitPanel = "",
                UnitTaskbar = ""
            });
            return items;
        }

        public static bool IsKey(string? key)
        {
            return string.Equals(key, Key, StringComparison.OrdinalIgnoreCase);
        }

        public static string FormatValue(Settings? settings)
        {
            YouPinInventoryTrendState state = GetState();
            if (!HasDisplayData(state))
                return "暂无";

            var cfg = GetDisplayConfig(settings);
            bool showSign = cfg.SignMode == 0;
            string amount = FormatMoney(state.TotalDelta, showSign);
            string percent = FormatPercent(state.TotalDeltaPercent, showSign);
            return cfg.DisplayMode switch
            {
                1 => amount,
                2 => percent,
                _ => amount + " " + percent
            };
        }

        public static int GetColorState(Settings? settings)
        {
            YouPinInventoryTrendState state = GetState();
            if (!HasDisplayData(state) || Math.Abs(state.TotalDelta) < 0.005)
                return MetricUtils.STATE_NEUTRAL;

            return state.TotalDelta > 0
                ? MetricUtils.STATE_CRIT
                : MetricUtils.STATE_SAFE;
        }

        public static Color GetTextColor(Settings? settings, Theme theme)
        {
            YouPinInventoryTrendState state = GetState();
            var cfg = GetDisplayConfig(settings);
            string hex = !HasDisplayData(state) || Math.Abs(state.TotalDelta) < 0.005
                ? cfg.ZeroColor
                : state.TotalDelta > 0
                    ? cfg.ProfitColor
                    : cfg.LossColor;

            return ParseColor(hex, UIUtils.GetStateColor(GetColorState(settings), theme, true));
        }

        private static YouPinTrendIndicatorConfigSnapshot GetDisplayConfig(Settings? settings)
        {
            if (settings != null)
            {
                return new YouPinTrendIndicatorConfigSnapshot(
                    settings.YouPinTrendIndicatorDisplayMode,
                    settings.YouPinTrendIndicatorSignMode,
                    settings.YouPinTrendIndicatorProfitColor ?? "",
                    settings.YouPinTrendIndicatorLossColor ?? "",
                    settings.YouPinTrendIndicatorZeroColor ?? "");
            }

            return MetricRuntimeServices.Resolve().AppConfigState.YouPinTrendIndicator;
        }

        private static YouPinInventoryTrendState GetState()
        {
            try
            {
                return YouPinInventoryService.Instance.GetTrendState();
            }
            catch
            {
                return new YouPinInventoryTrendState { LastStatus = "暂无" };
            }
        }

        private static bool HasDisplayData(YouPinInventoryTrendState state)
        {
            return state.LastFetch != DateTime.MinValue
                || state.TotalCount > 0
                || Math.Abs(state.TotalValue) > 0.005
                || Math.Abs(state.TotalDelta) > 0.005
                || state.HasOfficialProfitAndLoss;
        }

        private static string FormatMoney(double value, bool showSign)
        {
            string sign = showSign
                ? value > 0 ? "+" : value < 0 ? "-" : ""
                : "";
            return sign + "¥" + Math.Abs(value).ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string FormatPercent(double value, bool showSign)
        {
            string sign = showSign
                ? value > 0 ? "+" : value < 0 ? "-" : ""
                : "";
            return sign + Math.Abs(value).ToString("0.00", CultureInfo.InvariantCulture) + "%";
        }

        private static Color ParseColor(string? hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return fallback;

            try
            {
                return ColorTranslator.FromHtml(hex);
            }
            catch
            {
                return fallback;
            }
        }
    }
}
