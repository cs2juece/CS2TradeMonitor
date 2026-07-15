using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class ItemMonitorListCardModel
    {
        public static int NormalizeDefaultInterval(int value)
        {
            return Math.Max(60, value <= 0 ? 600 : value);
        }

        public static double NormalizeDefaultPercent(double value)
        {
            return Math.Clamp(value, 0, 1000);
        }

        public static int NormalizeDefaultWindowMinutes(int value)
        {
            return Math.Clamp(value, 1, 10080);
        }

        public static int NormalizeDefaultCooldownMinutes(int value)
        {
            return Math.Clamp(value, 1, 1440);
        }
    }

    internal static class ItemMonitorListCardFactory
    {
        public static LiteSettingsGroup Create(
            SettingsStore? settingsStore,
            IReadOnlyList<ItemMonitorConfig> items,
            int defaultRefreshIntervalSec,
            Action<int> onDefaultRefreshIntervalChanged,
            Action commitItemConfigs,
            Action<ItemMonitorConfig, int> moveItem,
            Func<ItemMonitorConfig, Control, Task> refreshItemPriceAsync,
            Action<ItemMonitorConfig> deleteItem)
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(onDefaultRefreshIntervalChanged);
            ArgumentNullException.ThrowIfNull(commitItemConfigs);
            ArgumentNullException.ThrowIfNull(moveItem);
            ArgumentNullException.ThrowIfNull(refreshItemPriceAsync);
            ArgumentNullException.ThrowIfNull(deleteItem);

            var group = new LiteSettingsGroup("已监控单品");
            group.AddFullItem(new LiteHintRow("全局设置先统一控制抓取和百分比提醒；单品行只填写需要覆盖的价格或涨跌条件。"));
            group.AddItem(CreateDefaultIntervalRow(defaultRefreshIntervalSec, onDefaultRefreshIntervalChanged));
            group.AddItem(CreateGlobalAlertNumberRow(
                settingsStore,
                "默认上涨提醒",
                "%",
                nameof(Settings.DefaultItemPriceAlertRisePercent),
                fallback: 0,
                width: 76,
                ItemMonitorListCardModel.NormalizeDefaultPercent));
            group.AddItem(CreateGlobalAlertNumberRow(
                settingsStore,
                "默认下跌提醒",
                "%",
                nameof(Settings.DefaultItemPriceAlertFallPercent),
                fallback: 0,
                width: 76,
                ItemMonitorListCardModel.NormalizeDefaultPercent));
            group.AddItem(CreateGlobalAlertIntRow(
                settingsStore,
                "默认统计窗口",
                "分",
                nameof(Settings.DefaultItemPriceAlertWindowMinutes),
                fallback: 10,
                width: 76,
                ItemMonitorListCardModel.NormalizeDefaultWindowMinutes));
            group.AddItem(CreateGlobalAlertIntRow(
                settingsStore,
                "默认提醒冷却",
                "分",
                nameof(Settings.DefaultItemPriceAlertCooldownMinutes),
                fallback: 10,
                width: 76,
                ItemMonitorListCardModel.NormalizeDefaultCooldownMinutes));
            group.AddFullItem(new LiteHintRow("默认上涨/下跌填 0 表示不启用全局百分比提醒；单品行里的覆盖值优先生效。"));

            if (items.Count == 0)
            {
                group.AddFullItem(new LiteHintRow("还没有监控单品。输入饰品名，选择候选后添加到监控。"));
            }
            else
            {
                for (int i = 0; i < items.Count; i++)
                {
                    group.AddFullItem(ItemMonitorItemRowFactory.Create(
                        items[i],
                        i,
                        items.Count,
                        commitItemConfigs,
                        moveItem,
                        refreshItemPriceAsync,
                        deleteItem));
                }
            }

            return group;
        }

        private static Control CreateDefaultIntervalRow(int currentValue, Action<int> onDefaultRefreshIntervalChanged)
        {
            int current = ItemMonitorListCardModel.NormalizeDefaultInterval(currentValue);
            var input = new LiteNumberInput(current.ToString(CultureInfo.InvariantCulture), "秒", "", 80)
            {
                Padding = UIUtils.S(new Padding(0, 5, 0, 1))
            };
            input.Inner.TextChanged += (_, __) =>
            {
                int next = ItemMonitorListCardModel.NormalizeDefaultInterval(input.ValueInt);
                if (current == next)
                    return;

                current = next;
                onDefaultRefreshIntervalChanged(next);
            };

            return new LiteSettingsItem("全局抓取间隔", input);
        }

        private static Control CreateGlobalAlertNumberRow(
            SettingsStore? settingsStore,
            string title,
            string unit,
            string settingKey,
            double fallback,
            int width,
            Func<double, double> normalize)
        {
            double current = settingsStore?.Get(settingKey, fallback) ?? fallback;
            var input = new LiteNumberInput(ItemMonitorPageModel.FormatAlertNumber(normalize(current)), unit, "", width)
            {
                Padding = UIUtils.S(new Padding(0, 5, 0, 1))
            };
            input.Inner.TextChanged += (_, __) =>
            {
                if (ItemMonitorPageModel.TryParseNonNegativeDouble(input.Inner.Text, out double parsed))
                    settingsStore?.Set(settingKey, normalize(parsed));
            };
            return new LiteSettingsItem(title, input);
        }

        private static Control CreateGlobalAlertIntRow(
            SettingsStore? settingsStore,
            string title,
            string unit,
            string settingKey,
            int fallback,
            int width,
            Func<int, int> normalize)
        {
            int current = settingsStore?.Get(settingKey, fallback) ?? fallback;
            var input = new LiteNumberInput(normalize(current).ToString(CultureInfo.InvariantCulture), unit, "", width)
            {
                Padding = UIUtils.S(new Padding(0, 5, 0, 1))
            };
            input.Inner.TextChanged += (_, __) =>
            {
                if (int.TryParse(input.Inner.Text, out int parsed))
                    settingsStore?.Set(settingKey, normalize(parsed));
            };
            return new LiteSettingsItem(title, input);
        }
    }
}
