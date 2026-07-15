using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class MainPanelItemMonitorRowFactory
    {
        public static Panel Create(
            ItemMonitorConfig item,
            List<ItemMonitorConfig> items,
            Action saveItems,
            Action refreshMonitorDisplay)
        {
            var row = new Panel
            {
                Height = UIUtils.S(104),
                BackColor = Color.Transparent
            };

            var title = new Label
            {
                Text = string.IsNullOrWhiteSpace(item.Name) ? item.ItemId : item.Name,
                AutoEllipsis = true,
                Font = UIFonts.Bold(9f),
                ForeColor = UIColors.TextMain,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var shortName = new LiteUnderlineInput(item.ShortName, "", "", 150)
            {
                Placeholder = "短名称"
            };
            shortName.Inner.TextChanged += (_, __) =>
            {
                item.ShortName = shortName.Inner.Text.Trim();
                saveItems();
                refreshMonitorDisplay();
            };

            var enabled = CreateInlineCheck("启用", item.Enabled, value => item.Enabled = value);
            var panelCheck = CreateInlineCheck("悬浮窗", item.VisibleInPanel, value => item.VisibleInPanel = value);
            var taskbar = CreateInlineCheck("任务栏", item.VisibleInTaskbar, value => item.VisibleInTaskbar = value);

            var flags = new FlowLayoutPanel
            {
                Height = UIUtils.S(28),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent
            };
            foreach (ItemMonitorDisplayFieldOption option in ItemMonitorDisplayFields.Options)
                AddItemFieldCheck(flags, item, items, saveItems, refreshMonitorDisplay, option.Text, option.Flag);

            var status = new Label
            {
                Text = item.LastPrice > 0
                    ? $"当前 ¥{item.LastPrice:0.##}" + (item.HasChangeData ? $"  {item.LastChangeRatio:+0.##;-0.##;0}%" : "")
                    : "暂无价格数据",
                AutoEllipsis = true,
                Font = UIFonts.Regular(8.5f),
                ForeColor = item.LastPrice > 0 ? UIColors.TextSub : UIColors.TextWarn,
                TextAlign = ContentAlignment.MiddleLeft
            };

            row.Controls.Add(title);
            row.Controls.Add(shortName);
            row.Controls.Add(enabled);
            row.Controls.Add(panelCheck);
            row.Controls.Add(taskbar);
            row.Controls.Add(flags);
            row.Controls.Add(status);
            bool inRowLayout = false;
            row.Layout += (_, __) =>
            {
                if (inRowLayout)
                    return;

                inRowLayout = true;
                try
                {
                    int gap = UIUtils.S(10);
                    int top = UIUtils.S(6);
                    int rightWidth = enabled.Width + panelCheck.Width + taskbar.Width + gap * 3;
                    LiteLayoutHelpers.SetBoundsIfChanged(title, 0, top, Math.Max(1, row.Width - rightWidth - gap), UIUtils.S(24));
                    LiteLayoutHelpers.SetLocationIfChanged(enabled, new Point(Math.Max(0, row.Width - rightWidth), top + UIUtils.S(2)));
                    LiteLayoutHelpers.SetLocationIfChanged(panelCheck, new Point(enabled.Right + gap, enabled.Top));
                    LiteLayoutHelpers.SetLocationIfChanged(taskbar, new Point(panelCheck.Right + gap, enabled.Top));
                    LiteLayoutHelpers.SetBoundsIfChanged(shortName, 0, UIUtils.S(38), UIUtils.S(170), shortName.Height);
                    LiteLayoutHelpers.SetBoundsIfChanged(status, shortName.Right + gap * 2, UIUtils.S(38), Math.Max(1, row.Width - shortName.Right - gap * 2), UIUtils.S(24));
                    LiteLayoutHelpers.SetBoundsIfChanged(flags, 0, UIUtils.S(70), row.Width, UIUtils.S(28));
                }
                finally
                {
                    inRowLayout = false;
                }
            };
            row.Paint += PaintSubtleBottomLine;
            return row;
        }

        public static int GetFieldCheckLogicalWidth(string text)
        {
            return text.Length >= 3 ? 76 : 62;
        }

        private static void AddItemFieldCheck(
            FlowLayoutPanel panel,
            ItemMonitorConfig item,
            List<ItemMonitorConfig> items,
            Action saveItems,
            Action refreshMonitorDisplay,
            string text,
            int flag)
        {
            int currentFlags = item.DisplayFieldFlags == 0 ? ItemMonitorDisplayFields.Default : item.DisplayFieldFlags;
            var check = new LiteCheck((currentFlags & flag) != 0, text)
            {
                Width = UIUtils.S(GetFieldCheckLogicalWidth(text)),
                Margin = new Padding(0, 0, UIUtils.S(8), 0)
            };
            check.CheckedChanged += (_, __) =>
            {
                int flags = item.DisplayFieldFlags == 0 ? ItemMonitorDisplayFields.Default : item.DisplayFieldFlags;
                if (check.Checked) flags |= flag;
                else flags &= ~flag;
                item.DisplayFieldFlags = ItemMonitorDisplayFields.Normalize(flags);
                saveItems();
                refreshMonitorDisplay();
            };
            panel.Controls.Add(check);
        }

        private static LiteCheck CreateInlineCheck(string text, bool value, Action<bool> set)
        {
            var check = new LiteCheck(value, text)
            {
                Width = UIUtils.S(82),
                Height = UIUtils.S(24)
            };
            check.CheckedChanged += (_, __) => set(check.Checked);
            return check;
        }

        private static void PaintSubtleBottomLine(object? sender, PaintEventArgs e)
        {
            if (sender is not Control control)
                return;

            using var pen = new Pen(UIColors.Border);
            e.Graphics.DrawLine(pen, 0, control.Height - 1, control.Width, control.Height - 1);
        }
    }
}
