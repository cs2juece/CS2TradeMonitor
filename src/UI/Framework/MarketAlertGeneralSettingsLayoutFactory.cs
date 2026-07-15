using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal readonly record struct MarketAlertGeneralIntroLayout(
        Rectangle FirstLineBounds,
        Rectangle SecondLineBounds);

    internal readonly record struct MarketAlertGeneralSettingsShellLayout(
        Rectangle LeftBounds,
        Rectangle RightBounds,
        int PanelHeight);

    internal readonly record struct MarketAlertGeneralSectionRowLayout(
        Rectangle TitleBounds,
        Rectangle EditorBounds);

    internal static class MarketAlertGeneralSettingsLayoutModel
    {
        public static MarketAlertGeneralIntroLayout BuildIntroLayout(int clientWidth, Padding padding)
        {
            int width = Math.Max(1, clientWidth - padding.Horizontal);
            int x = padding.Left;
            int y = padding.Top;
            var first = new Rectangle(x, y, width, UIUtils.S(22));
            var second = new Rectangle(x, first.Bottom + UIUtils.S(2), width, UIUtils.S(22));
            return new MarketAlertGeneralIntroLayout(first, second);
        }

        public static MarketAlertGeneralSettingsShellLayout BuildShellLayout(int panelWidth, int clientWidth, Padding padding)
        {
            bool compact = panelWidth < UIUtils.S(620);
            int gap = UIUtils.S(48);
            int x = padding.Left;
            int y = padding.Top;
            int width = Math.Max(1, clientWidth - padding.Horizontal);

            if (compact)
            {
                int sectionHeight = UIUtils.S(112);
                int panelHeight = sectionHeight * 2 + gap + padding.Vertical;
                var left = new Rectangle(x, y, width, sectionHeight);
                var right = new Rectangle(x, left.Bottom + gap, width, sectionHeight);
                return new MarketAlertGeneralSettingsShellLayout(left, right, panelHeight);
            }

            int height = UIUtils.S(124);
            int contentHeight = Math.Max(1, height - padding.Vertical);
            int leftWidth = Math.Max(UIUtils.S(360), (width - gap) / 2);
            int rightWidth = Math.Max(1, width - leftWidth - gap);
            var leftBounds = new Rectangle(x, y, leftWidth, contentHeight);
            var rightBounds = new Rectangle(leftBounds.Right + gap, y, rightWidth, contentHeight);
            return new MarketAlertGeneralSettingsShellLayout(leftBounds, rightBounds, height);
        }

        public static MarketAlertGeneralSectionRowLayout BuildSectionRowLayout(
            int rowWidth,
            int rowHeight,
            int editorWidth,
            int editorHeight)
        {
            int effectiveEditorWidth = Math.Max(UIUtils.S(70), editorWidth);
            int gap = UIUtils.S(12);
            int textWidth = Math.Max(1, rowWidth - effectiveEditorWidth - gap);
            var titleBounds = new Rectangle(0, 0, textWidth, rowHeight);
            var editorBounds = new Rectangle(
                rowWidth - effectiveEditorWidth,
                Math.Max(0, (rowHeight - editorHeight) / 2),
                effectiveEditorWidth,
                editorHeight);
            return new MarketAlertGeneralSectionRowLayout(titleBounds, editorBounds);
        }
    }

    internal sealed class MarketAlertGeneralSettingsShell
    {
        public MarketAlertGeneralSettingsShell(Panel panel, Panel leftColumn, Panel rightColumn)
        {
            Panel = panel ?? throw new ArgumentNullException(nameof(panel));
            LeftColumn = leftColumn ?? throw new ArgumentNullException(nameof(leftColumn));
            RightColumn = rightColumn ?? throw new ArgumentNullException(nameof(rightColumn));
        }

        public Panel Panel { get; }

        public Panel LeftColumn { get; }

        public Panel RightColumn { get; }
    }

    internal static class MarketAlertGeneralSettingsLayoutFactory
    {
        public static Control CreateIntroPanel()
        {
            var panel = new Panel
            {
                Height = UIUtils.S(52),
                BackColor = Color.Transparent,
                Padding = UIUtils.S(new Padding(0, 4, 0, 6))
            };
            var line1 = CreateSubLabel("状态：总开关和单条规则同时启用后才会提醒；指数达到点位，或在统计时间内涨跌达到阈值时触发。");
            var line2 = CreateSubLabel("下一步：选择提醒方式，再勾选 QAQ/SteamDT 规则并填写阈值。");

            panel.Controls.Add(line1);
            panel.Controls.Add(line2);
            panel.Layout += (_, __) =>
            {
                MarketAlertGeneralIntroLayout layout = MarketAlertGeneralSettingsLayoutModel.BuildIntroLayout(
                    panel.ClientSize.Width,
                    panel.Padding);
                line1.Bounds = layout.FirstLineBounds;
                line2.Bounds = layout.SecondLineBounds;
            };
            return panel;
        }

        public static MarketAlertGeneralSettingsShell CreateSettingsShell()
        {
            var panel = new Panel
            {
                Height = UIUtils.S(124),
                BackColor = Color.Transparent,
                Padding = UIUtils.S(new Padding(0, 2, 0, 2))
            };

            var left = CreateSettingColumn();
            var right = CreateSettingColumn();

            panel.Controls.Add(left);
            panel.Controls.Add(right);
            panel.Layout += (_, __) => ApplyShellLayout(panel, left, right);

            return new MarketAlertGeneralSettingsShell(panel, left, right);
        }

        public static void AddSectionRow(Control section, string title, string hint, Control editor)
        {
            var row = new Panel
            {
                Height = UIUtils.S(28),
                BackColor = Color.Transparent,
                Margin = Padding.Empty
            };
            var titleLabel = new Label
            {
                Text = title,
                AutoSize = false,
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AccessibleDescription = hint
            };
            row.Controls.Add(titleLabel);
            row.Controls.Add(editor);
            row.Layout += (_, __) => ApplySectionRowLayout(row, titleLabel, editor);
            section.Controls.Add(row);
        }

        private static Label CreateSubLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static Panel CreateSettingColumn()
        {
            var column = new Panel
            {
                BackColor = Color.Transparent,
                Padding = Padding.Empty
            };
            column.Layout += (_, __) => LayoutSettingColumn(column);
            return column;
        }

        private static void ApplyShellLayout(Panel panel, Control left, Control right)
        {
            MarketAlertGeneralSettingsShellLayout layout = MarketAlertGeneralSettingsLayoutModel.BuildShellLayout(
                panel.Width,
                panel.ClientSize.Width,
                panel.Padding);
            if (panel.Height != layout.PanelHeight)
                panel.Height = layout.PanelHeight;

            left.Bounds = layout.LeftBounds;
            right.Bounds = layout.RightBounds;
        }

        private static void ApplySectionRowLayout(Control row, Control titleLabel, Control editor)
        {
            MarketAlertGeneralSectionRowLayout layout = MarketAlertGeneralSettingsLayoutModel.BuildSectionRowLayout(
                row.Width,
                row.Height,
                editor.Width,
                editor.Height);
            titleLabel.Bounds = layout.TitleBounds;
            editor.Bounds = layout.EditorBounds;
        }

        private static void LayoutSettingColumn(Control section)
        {
            int x = 0;
            int y = 0;
            int width = Math.Max(1, section.ClientSize.Width);

            foreach (Control row in section.Controls)
            {
                row.SetBounds(x, y, width, row.Height);
                y = row.Bottom + UIUtils.S(6);
            }
        }
    }
}
