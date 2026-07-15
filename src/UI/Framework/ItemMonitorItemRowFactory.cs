using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using static CS2TradeMonitor.src.UI.Framework.ItemMonitorPageControls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal readonly record struct ItemMonitorItemRowLayout(
        Rectangle TitleBounds,
        Rectangle StatusBounds,
        Rectangle ShortNameLabelBounds,
        Rectangle ShortNameBounds,
        Rectangle VisibilityLabelBounds,
        Point EnabledLocation,
        Point PanelLocation,
        Point TaskbarLocation,
        Rectangle FieldsLabelBounds,
        Rectangle FieldsPanelBounds,
        Rectangle AlertLabelBounds,
        Rectangle AlertPanelBounds,
        Point UpLocation,
        Point DownLocation,
        Point RefreshLocation,
        Point DeleteLocation);

    internal static class ItemMonitorItemRowModel
    {
        public static ItemMonitorItemRowLayout BuildLayout(
            int rowWidth,
            int shortNameHeight,
            int upWidth,
            int downWidth,
            int refreshWidth,
            int deleteWidth,
            int enabledWidth,
            int panelWidth)
        {
            int gap = UIUtils.S(8);
            int labelWidth = UIUtils.S(72);
            int contentLeft = labelWidth + UIUtils.S(12);
            int buttonWidth = upWidth + downWidth + refreshWidth + deleteWidth + gap * 4;
            int buttonLeft = Math.Max(0, rowWidth - buttonWidth);
            int textRight = Math.Max(UIUtils.S(220), buttonLeft - gap);

            int visibilityY = UIUtils.S(96) + UIUtils.S(3);
            var enabledLocation = new Point(contentLeft, visibilityY);
            var panelLocation = new Point(enabledLocation.X + enabledWidth + UIUtils.S(18), visibilityY);
            var taskbarLocation = new Point(panelLocation.X + panelWidth + UIUtils.S(18), visibilityY);

            return new ItemMonitorItemRowLayout(
                TitleBounds: new Rectangle(0, UIUtils.S(8), Math.Max(UIUtils.S(160), textRight), UIUtils.S(24)),
                StatusBounds: new Rectangle(0, UIUtils.S(34), Math.Max(1, rowWidth - gap), UIUtils.S(22)),
                ShortNameLabelBounds: new Rectangle(0, UIUtils.S(62) + UIUtils.S(4), labelWidth, UIUtils.S(22)),
                ShortNameBounds: new Rectangle(contentLeft, UIUtils.S(62), UIUtils.S(170), shortNameHeight),
                VisibilityLabelBounds: new Rectangle(0, UIUtils.S(96) + UIUtils.S(4), labelWidth, UIUtils.S(22)),
                EnabledLocation: enabledLocation,
                PanelLocation: panelLocation,
                TaskbarLocation: taskbarLocation,
                FieldsLabelBounds: new Rectangle(0, UIUtils.S(128) + UIUtils.S(4), labelWidth, UIUtils.S(22)),
                FieldsPanelBounds: new Rectangle(contentLeft, UIUtils.S(128), Math.Max(1, rowWidth - contentLeft), UIUtils.S(30)),
                AlertLabelBounds: new Rectangle(0, UIUtils.S(158) + UIUtils.S(4), labelWidth, UIUtils.S(22)),
                AlertPanelBounds: new Rectangle(contentLeft, UIUtils.S(158), Math.Max(1, rowWidth - contentLeft), UIUtils.S(30)),
                UpLocation: new Point(buttonLeft, UIUtils.S(8)),
                DownLocation: new Point(buttonLeft + upWidth + gap, UIUtils.S(8)),
                RefreshLocation: new Point(buttonLeft + upWidth + downWidth + gap * 2, UIUtils.S(7)),
                DeleteLocation: new Point(buttonLeft + upWidth + downWidth + refreshWidth + gap * 3, UIUtils.S(7)));
        }
    }

    internal static class ItemMonitorItemRowFactory
    {
        public static Panel Create(
            ItemMonitorConfig item,
            int index,
            int totalCount,
            Action commitItemConfigs,
            Action<ItemMonitorConfig, int> moveItem,
            Func<ItemMonitorConfig, Control, Task> refreshItemPriceAsync,
            Action<ItemMonitorConfig> deleteItem)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(commitItemConfigs);
            ArgumentNullException.ThrowIfNull(moveItem);
            ArgumentNullException.ThrowIfNull(refreshItemPriceAsync);
            ArgumentNullException.ThrowIfNull(deleteItem);

            var row = new Panel { Height = UIUtils.S(188), Dock = DockStyle.Top, BackColor = Color.Transparent };
            var title = CreateLabel(item.Name, strong: true);
            var status = CreateLabel(ItemMonitorPageModel.BuildItemStatusText(item));
            status.ForeColor = ItemMonitorPageModel.GetItemStatusColor(item);

            var shortName = new LiteUnderlineInput(item.ShortName, "", "", 170);
            shortName.Inner.TextChanged += (_, __) =>
            {
                item.ShortName = shortName.Inner.Text.Trim();
                ItemMonitorPageModel.SyncItemKey(item);
                commitItemConfigs();
            };

            var enabled = CreateCheck("启用", item.Enabled, value => item.Enabled = value, commitItemConfigs);
            var panel = CreateCheck("悬浮窗", item.VisibleInPanel, value => item.VisibleInPanel = value, commitItemConfigs);
            var taskbar = CreateCheck("任务栏", item.VisibleInTaskbar, value => item.VisibleInTaskbar = value, commitItemConfigs);
            var fieldsPanel = CreateFieldsPanel(item, commitItemConfigs);
            var alertPanel = CreateAlertPanel(item, commitItemConfigs);

            var up = new LiteSortBtn("↑") { Enabled = index > 0 };
            var down = new LiteSortBtn("↓") { Enabled = index < totalCount - 1 };
            var refresh = new LiteButton("刷新", false) { Width = UIUtils.S(62), Height = UIUtils.S(28) };
            var delete = new LiteButton("删除", false) { Width = UIUtils.S(62), Height = UIUtils.S(28) };

            up.Click += (_, __) => moveItem(item, -1);
            down.Click += (_, __) => moveItem(item, 1);
            refresh.Click += async (_, __) => await refreshItemPriceAsync(item, refresh);
            delete.Click += (_, __) => deleteItem(item);

            var shortNameLabel = CreateTinyLabel("短名称");
            var visibilityLabel = CreateTinyLabel("显示位置");
            var fieldsLabel = CreateTinyLabel("显示字段");
            var alertLabel = CreateTinyLabel("覆盖提醒");

            row.Controls.Add(title);
            row.Controls.Add(status);
            row.Controls.Add(shortNameLabel);
            row.Controls.Add(shortName);
            row.Controls.Add(visibilityLabel);
            row.Controls.Add(enabled);
            row.Controls.Add(panel);
            row.Controls.Add(taskbar);
            row.Controls.Add(fieldsLabel);
            row.Controls.Add(fieldsPanel);
            row.Controls.Add(alertLabel);
            row.Controls.Add(alertPanel);
            row.Controls.Add(up);
            row.Controls.Add(down);
            row.Controls.Add(refresh);
            row.Controls.Add(delete);

            row.Layout += (_, __) =>
            {
                ItemMonitorItemRowLayout layout = ItemMonitorItemRowModel.BuildLayout(
                    row.Width,
                    shortName.Height,
                    up.Width,
                    down.Width,
                    refresh.Width,
                    delete.Width,
                    enabled.Width,
                    panel.Width);

                title.Bounds = layout.TitleBounds;
                status.Bounds = layout.StatusBounds;
                shortNameLabel.Bounds = layout.ShortNameLabelBounds;
                shortName.Bounds = layout.ShortNameBounds;
                visibilityLabel.Bounds = layout.VisibilityLabelBounds;
                enabled.Location = layout.EnabledLocation;
                panel.Location = layout.PanelLocation;
                taskbar.Location = layout.TaskbarLocation;
                fieldsLabel.Bounds = layout.FieldsLabelBounds;
                fieldsPanel.Bounds = layout.FieldsPanelBounds;
                alertLabel.Bounds = layout.AlertLabelBounds;
                alertPanel.Bounds = layout.AlertPanelBounds;
                up.Location = layout.UpLocation;
                down.Location = layout.DownLocation;
                refresh.Location = layout.RefreshLocation;
                delete.Location = layout.DeleteLocation;
            };
            row.Paint += PaintBottomLine;
            return row;
        }

        private static FlowLayoutPanel CreateFieldsPanel(ItemMonitorConfig item, Action commitItemConfigs)
        {
            var panel = new FlowLayoutPanel
            {
                AutoSize = false,
                Height = UIUtils.S(30),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent
            };
            AddFlagCheck(panel, item, "名称", ItemMonitorPageModel.FieldName, commitItemConfigs);
            AddFlagCheck(panel, item, "价格", ItemMonitorPageModel.FieldPrice, commitItemConfigs);
            AddFlagCheck(panel, item, "涨跌", ItemMonitorPageModel.FieldChange, commitItemConfigs);
            AddFlagCheck(panel, item, "涨跌幅", ItemMonitorPageModel.FieldPercent, commitItemConfigs);
            AddFlagCheck(panel, item, "来源", ItemMonitorPageModel.FieldSource, commitItemConfigs);
            AddFlagCheck(panel, item, "时间", ItemMonitorPageModel.FieldRefreshTime, commitItemConfigs);
            return panel;
        }

        private static FlowLayoutPanel CreateAlertPanel(ItemMonitorConfig item, Action commitItemConfigs)
        {
            var panel = new FlowLayoutPanel
            {
                AutoSize = false,
                Height = UIUtils.S(30),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent
            };
            var alertEnabled = CreateCheck("启用提醒", item.PriceAlertEnabled, value => item.PriceAlertEnabled = value, commitItemConfigs);
            alertEnabled.Width = UIUtils.S(92);
            alertEnabled.Margin = new Padding(0, 0, UIUtils.S(8), 0);
            panel.Controls.Add(alertEnabled);
            panel.Controls.Add(CreateAlertNumberInput("高于¥", "", item.PriceAlertAbove, value => item.PriceAlertAbove = value, 88, commitItemConfigs));
            panel.Controls.Add(CreateAlertNumberInput("低于¥", "", item.PriceAlertBelow, value => item.PriceAlertBelow = value, 88, commitItemConfigs));
            panel.Controls.Add(CreateAlertNumberInput("上涨>", "%", item.PriceAlertRisePercent, value => item.PriceAlertRisePercent = value, 76, commitItemConfigs));
            panel.Controls.Add(CreateAlertNumberInput("下跌>", "%", item.PriceAlertFallPercent, value => item.PriceAlertFallPercent = value, 76, commitItemConfigs));
            return panel;
        }

        private static void AddFlagCheck(FlowLayoutPanel panel, ItemMonitorConfig item, string text, int flag, Action commitItemConfigs)
        {
            int flags = item.DisplayFieldFlags == 0 ? ItemMonitorPageModel.DefaultFields : item.DisplayFieldFlags;
            var check = new LiteCheck((flags & flag) != 0, text)
            {
                Width = UIUtils.S(text.Length >= 3 ? 76 : 62),
                Margin = new Padding(0, 0, UIUtils.S(8), 0)
            };
            check.CheckedChanged += (_, __) =>
            {
                int current = item.DisplayFieldFlags == 0 ? ItemMonitorPageModel.DefaultFields : item.DisplayFieldFlags;
                current = check.Checked ? current | flag : current & ~flag;
                item.DisplayFieldFlags = ItemMonitorPageModel.NormalizeDisplayFields(current);
                commitItemConfigs();
            };
            panel.Controls.Add(check);
        }

        private static LiteCheck CreateCheck(string text, bool value, Action<bool> set, Action commitItemConfigs)
        {
            var check = new LiteCheck(value, text) { Width = UIUtils.S(88) };
            check.CheckedChanged += (_, __) =>
            {
                set(check.Checked);
                commitItemConfigs();
            };
            return check;
        }

        private static LiteNumberInput CreateAlertNumberInput(
            string label,
            string unit,
            double value,
            Action<double> set,
            int width,
            Action commitItemConfigs)
        {
            var input = new LiteNumberInput(ItemMonitorPageModel.FormatAlertNumber(value), unit, label, width, null, 9)
            {
                Margin = new Padding(0, 0, UIUtils.S(8), 0)
            };
            input.Inner.TextChanged += (_, __) =>
            {
                if (ItemMonitorPageModel.TryParseNonNegativeDouble(input.Inner.Text, out double parsed))
                {
                    set(parsed);
                    commitItemConfigs();
                }
            };
            return input;
        }
    }
}
