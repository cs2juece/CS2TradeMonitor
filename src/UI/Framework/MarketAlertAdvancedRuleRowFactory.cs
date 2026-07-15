using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class MarketAlertAdvancedRuleRowModel
    {
        public static IReadOnlyList<Point> BuildControlLocations(IReadOnlyList<int> controlWidths)
        {
            ArgumentNullException.ThrowIfNull(controlWidths);

            int x = 0;
            int y = UIUtils.S(4);
            int gap = UIUtils.S(8);
            var locations = new List<Point>(controlWidths.Count);
            foreach (int width in controlWidths)
            {
                locations.Add(new Point(x, y));
                x += Math.Max(0, width) + gap;
            }

            return locations;
        }
    }

    internal sealed class MarketAlertAdvancedRuleRowCallbacks
    {
        public MarketAlertAdvancedRuleRowCallbacks(Action saveRules, Action<MarketAlertRule> deleteRule)
        {
            SaveRules = saveRules ?? throw new ArgumentNullException(nameof(saveRules));
            DeleteRule = deleteRule ?? throw new ArgumentNullException(nameof(deleteRule));
        }

        public Action SaveRules { get; }

        public Action<MarketAlertRule> DeleteRule { get; }
    }

    internal static class MarketAlertAdvancedRuleRowFactory
    {
        public static Control Create(MarketAlertRule rule, MarketAlertAdvancedRuleRowCallbacks callbacks)
        {
            ArgumentNullException.ThrowIfNull(rule);
            ArgumentNullException.ThrowIfNull(callbacks);

            var row = new Panel { Width = UIUtils.S(760), Height = UIUtils.S(38), BackColor = Color.Transparent, Margin = new Padding(0) };
            var check = new LiteCheck(rule.Enabled, "") { Width = UIUtils.S(42) };
            check.CheckedChanged += (_, __) => { rule.Enabled = check.Checked; callbacks.SaveRules(); };
            var name = new LiteUnderlineInput(rule.Name, "", "", 120);
            name.Inner.TextChanged += (_, __) => { rule.Name = name.Inner.Text; callbacks.SaveRules(); };
            var source = new LiteComboBox { Width = UIUtils.S(92) };
            source.AddItem("QAQ", MarketDataSourceManager.QaqId);
            source.AddItem("SteamDT", MarketDataSourceManager.SteamDtId);
            source.SelectValue(rule.SourceId);
            source.Inner.SelectedIndexChanged += (_, __) => { rule.SourceId = source.SelectedValue; callbacks.SaveRules(); };
            var type = new LiteComboBox { Width = UIUtils.S(118) };
            type.AddItem("突破点位", ((int)MarketAlertRuleType.CrossAbove).ToString());
            type.AddItem("跌破点位", ((int)MarketAlertRuleType.CrossBelow).ToString());
            type.AddItem("上涨百分比报警", ((int)MarketAlertRuleType.RiseByPercent).ToString());
            type.AddItem("下跌百分比报警", ((int)MarketAlertRuleType.FallByPercent).ToString());
            type.SelectValue(((int)rule.RuleType).ToString());
            type.Inner.SelectedIndexChanged += (_, __) =>
            {
                if (int.TryParse(type.SelectedValue, out int value) && Enum.IsDefined(typeof(MarketAlertRuleType), value))
                {
                    rule.RuleType = (MarketAlertRuleType)value;
                    callbacks.SaveRules();
                }
            };
            var threshold = new LiteNumberInput(rule.Threshold.ToString("0.##"), "", "", 74);
            threshold.Inner.TextChanged += (_, __) => { if (double.TryParse(threshold.Inner.Text, out double value)) { rule.Threshold = Math.Max(0, value); callbacks.SaveRules(); } };
            var window = new LiteNumberInput(rule.WindowMinutes.ToString(), "分", "", 64);
            window.Inner.TextChanged += (_, __) => { if (int.TryParse(window.Inner.Text, out int value)) { rule.WindowMinutes = Math.Clamp(value, 1, 1440); callbacks.SaveRules(); } };
            var cooldown = new LiteNumberInput(rule.CooldownMinutes.ToString(), "分", "", 64);
            cooldown.Inner.TextChanged += (_, __) => { if (int.TryParse(cooldown.Inner.Text, out int value)) { rule.CooldownMinutes = Math.Clamp(value, 1, 1440); callbacks.SaveRules(); } };
            var delete = new LiteButton("删除", false) { Width = UIUtils.S(58), Height = UIUtils.S(30) };
            delete.Click += (_, __) => callbacks.DeleteRule(rule);

            Control[] controls = { check, name, source, type, threshold, window, cooldown, delete };
            IReadOnlyList<Point> locations = MarketAlertAdvancedRuleRowModel.BuildControlLocations(
                controls.Select(control => control.Width).ToArray());
            for (int i = 0; i < controls.Length; i++)
            {
                row.Controls.Add(controls[i]);
                controls[i].Location = locations[i];
            }

            row.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1);
            };
            return row;
        }
    }
}
