using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal readonly record struct MarketAlertSourceRuleRowLayout(
        int RowHeight,
        Rectangle TitleBounds,
        Rectangle HintBounds,
        Rectangle CheckBounds,
        Rectangle WindowInputBounds,
        Rectangle ThresholdInputBounds);

    internal static class MarketAlertSourceRuleRowModel
    {
        public static bool IsPercentRule(MarketAlertRuleType ruleType)
        {
            return ruleType == MarketAlertRuleType.RiseByPercent || ruleType == MarketAlertRuleType.FallByPercent;
        }

        public static MarketAlertSourceRuleRowLayout BuildLayout(
            int rowWidth,
            bool isPercentRule,
            int checkWidth,
            int checkHeight,
            int windowWidth,
            int windowHeight,
            int thresholdWidth,
            int thresholdHeight)
        {
            int gap = UIUtils.S(14);
            int activeWindowWidth = isPercentRule ? windowWidth : 0;
            int rightWidth = checkWidth + thresholdWidth + gap + (isPercentRule ? activeWindowWidth + gap : 0);
            bool stacked = rowWidth < rightWidth + UIUtils.S(220);
            int rowHeight = stacked ? UIUtils.S(86) : UIUtils.S(66);

            if (stacked)
            {
                int labelWidth = Math.Max(1, rowWidth);
                int controlY = UIUtils.S(52);
                int controlLeft = Math.Max(0, rowWidth - rightWidth);
                var checkBounds = new Rectangle(controlLeft, controlY + UIUtils.S(2), checkWidth, checkHeight);
                var windowBounds = isPercentRule
                    ? new Rectangle(checkBounds.Right + gap, controlY, windowWidth, windowHeight)
                    : Rectangle.Empty;
                var inputBounds = new Rectangle(rowWidth - thresholdWidth, controlY, thresholdWidth, thresholdHeight);

                return new MarketAlertSourceRuleRowLayout(
                    rowHeight,
                    new Rectangle(0, UIUtils.S(5), labelWidth, UIUtils.S(20)),
                    new Rectangle(0, UIUtils.S(27), labelWidth, UIUtils.S(18)),
                    checkBounds,
                    windowBounds,
                    inputBounds);
            }

            int labelWidthNormal = Math.Max(UIUtils.S(180), rowWidth - rightWidth - UIUtils.S(16));
            int mid = rowHeight / 2;
            int normalControlLeft = Math.Max(labelWidthNormal + UIUtils.S(16), rowWidth - rightWidth);
            var normalCheckBounds = new Rectangle(normalControlLeft, mid - checkHeight / 2, checkWidth, checkHeight);
            var normalWindowBounds = isPercentRule
                ? new Rectangle(normalCheckBounds.Right + gap, mid - windowHeight / 2, windowWidth, windowHeight)
                : Rectangle.Empty;
            var normalInputBounds = new Rectangle(rowWidth - thresholdWidth, mid - thresholdHeight / 2, thresholdWidth, thresholdHeight);

            return new MarketAlertSourceRuleRowLayout(
                rowHeight,
                new Rectangle(0, UIUtils.S(7), labelWidthNormal, UIUtils.S(20)),
                new Rectangle(0, UIUtils.S(32), labelWidthNormal, UIUtils.S(18)),
                normalCheckBounds,
                normalWindowBounds,
                normalInputBounds);
        }
    }

    internal sealed class MarketAlertSourceRuleRowCallbacks
    {
        public MarketAlertSourceRuleRowCallbacks(
            Func<bool> isUpdatingControls,
            Func<string, MarketAlertRuleType, MarketAlertRule?> ensureBuiltinRule,
            Func<string, MarketAlertRuleType, MarketAlertRule?> findBuiltinRule,
            Action saveRules,
            Action refreshAdvancedRules,
            Action<Action> registerRefresh,
            Action<Action> runWithUpdateGuard)
        {
            IsUpdatingControls = isUpdatingControls ?? throw new ArgumentNullException(nameof(isUpdatingControls));
            EnsureBuiltinRule = ensureBuiltinRule ?? throw new ArgumentNullException(nameof(ensureBuiltinRule));
            FindBuiltinRule = findBuiltinRule ?? throw new ArgumentNullException(nameof(findBuiltinRule));
            SaveRules = saveRules ?? throw new ArgumentNullException(nameof(saveRules));
            RefreshAdvancedRules = refreshAdvancedRules ?? throw new ArgumentNullException(nameof(refreshAdvancedRules));
            RegisterRefresh = registerRefresh ?? throw new ArgumentNullException(nameof(registerRefresh));
            RunWithUpdateGuard = runWithUpdateGuard ?? throw new ArgumentNullException(nameof(runWithUpdateGuard));
        }

        public Func<bool> IsUpdatingControls { get; }

        public Func<string, MarketAlertRuleType, MarketAlertRule?> EnsureBuiltinRule { get; }

        public Func<string, MarketAlertRuleType, MarketAlertRule?> FindBuiltinRule { get; }

        public Action SaveRules { get; }

        public Action RefreshAdvancedRules { get; }

        public Action<Action> RegisterRefresh { get; }

        public Action<Action> RunWithUpdateGuard { get; }
    }

    internal static class MarketAlertSourceRuleRowFactory
    {
        public static Control Create(
            string sourceId,
            MarketAlertRuleType ruleType,
            string title,
            string unit,
            string placeholder,
            string hint,
            MarketAlertSourceRuleRowCallbacks callbacks)
        {
            ArgumentNullException.ThrowIfNull(callbacks);

            bool isPercentRule = MarketAlertSourceRuleRowModel.IsPercentRule(ruleType);
            var row = new Panel { Height = UIUtils.S(66), BackColor = Color.Transparent, Margin = new Padding(0) };
            var titleLabel = CreateTextLabel(title, 9F, UIColors.TextMain, ContentAlignment.BottomLeft);
            var hintLabel = CreateTextLabel(hint, 8F, UIColors.TextSub, ContentAlignment.TopLeft);
            var check = new LiteCheck(false, "启用") { Width = UIUtils.S(64) };
            var windowInput = new LiteNumberInput("", "分钟", "", 86) { Visible = isPercentRule };
            windowInput.Placeholder = "10";
            windowInput.Padding = UIUtils.S(new Padding(0, 5, 0, 1));
            var input = new LiteNumberInput("", unit, "", 92);
            input.Placeholder = placeholder;
            input.Padding = UIUtils.S(new Padding(0, 5, 0, 1));

            check.CheckedChanged += (_, __) =>
            {
                if (callbacks.IsUpdatingControls())
                    return;
                MarketAlertRule? rule = callbacks.EnsureBuiltinRule(sourceId, ruleType);
                if (rule != null)
                {
                    rule.Enabled = check.Checked;
                    callbacks.SaveRules();
                }
                callbacks.RefreshAdvancedRules();
            };
            input.Inner.TextChanged += (_, __) =>
            {
                if (callbacks.IsUpdatingControls())
                    return;
                if (double.TryParse(input.Inner.Text, out double value))
                {
                    MarketAlertRule? rule = callbacks.EnsureBuiltinRule(sourceId, ruleType);
                    if (rule != null)
                    {
                        rule.Threshold = Math.Max(0, value);
                        callbacks.SaveRules();
                    }
                    callbacks.RefreshAdvancedRules();
                }
            };
            windowInput.Inner.TextChanged += (_, __) =>
            {
                if (callbacks.IsUpdatingControls() || !isPercentRule)
                    return;
                if (int.TryParse(windowInput.Inner.Text, out int value))
                {
                    MarketAlertRule? rule = callbacks.EnsureBuiltinRule(sourceId, ruleType);
                    if (rule != null)
                    {
                        rule.WindowMinutes = Math.Clamp(value, 1, 1440);
                        callbacks.SaveRules();
                    }
                    callbacks.RefreshAdvancedRules();
                }
            };

            void RefreshRuleRow()
            {
                MarketAlertRule? rule = callbacks.FindBuiltinRule(sourceId, ruleType) ?? callbacks.EnsureBuiltinRule(sourceId, ruleType);
                if (rule == null)
                    return;
                check.Checked = rule.Enabled;
                if (isPercentRule)
                    windowInput.Inner.Text = Math.Clamp(rule.WindowMinutes, 1, 1440).ToString();
                input.Inner.Text = MarketAlertPageModel.FormatThreshold(rule);
                hintLabel.Text = MarketAlertPageModel.GetRuleHint(ruleType, rule.WindowMinutes);
            }

            callbacks.RegisterRefresh(RefreshRuleRow);
            callbacks.RunWithUpdateGuard(RefreshRuleRow);

            row.SuspendLayout();
            try
            {
                row.Controls.AddRange(new Control[] { titleLabel, hintLabel, check, windowInput, input });
            }
            finally
            {
                row.ResumeLayout(false);
            }
            row.Layout += (_, __) => ApplyLayout(row, titleLabel, hintLabel, check, windowInput, input, isPercentRule);
            row.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1);
            };
            return row;
        }

        private static Label CreateTextLabel(string text, float size, Color color, ContentAlignment alignment)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", size),
                ForeColor = color,
                BackColor = Color.Transparent,
                TextAlign = alignment
            };
        }

        private static void ApplyLayout(
            Panel row,
            Label titleLabel,
            Label hintLabel,
            Control check,
            Control windowInput,
            Control input,
            bool isPercentRule)
        {
            if (row.Width <= 0)
                return;

            MarketAlertSourceRuleRowLayout layout = MarketAlertSourceRuleRowModel.BuildLayout(
                row.Width,
                isPercentRule,
                check.Width,
                check.Height,
                windowInput.Width,
                windowInput.Height,
                input.Width,
                input.Height);
            if (row.Height != layout.RowHeight)
                row.Height = layout.RowHeight;

            titleLabel.Bounds = layout.TitleBounds;
            hintLabel.Bounds = layout.HintBounds;
            check.Bounds = layout.CheckBounds;
            if (isPercentRule)
                windowInput.Bounds = layout.WindowInputBounds;
            input.Bounds = layout.ThresholdInputBounds;
        }
    }
}
