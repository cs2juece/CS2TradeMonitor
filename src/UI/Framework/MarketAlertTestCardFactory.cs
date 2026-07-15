using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal readonly record struct MarketAlertTestRowLayout(
        Rectangle TitleBounds,
        Rectangle DescriptionBounds,
        Rectangle StatusBounds,
        Rectangle ButtonBounds);

    internal static class MarketAlertTestCardModel
    {
        public static Color GetRowBackColor()
        {
            return UIColors.IsDark ? UIColors.ControlBg : Color.FromArgb(248, 250, 252);
        }

        public static MarketAlertTestRowLayout BuildLayout(int rowWidth, int rowHeight, int buttonHeight)
        {
            int paddingLeft = UIUtils.S(10);
            int paddingTop = UIUtils.S(6);
            int gap = UIUtils.S(12);
            int buttonWidth = UIUtils.S(120);
            int statusWidth = UIUtils.S(150);
            int buttonLeft = Math.Max(paddingLeft, rowWidth - paddingLeft - buttonWidth);
            int mid = rowHeight / 2;
            var buttonBounds = new Rectangle(buttonLeft, mid - buttonHeight / 2, buttonWidth, buttonHeight);
            int statusLeft = Math.Max(paddingLeft, buttonBounds.Left - gap - statusWidth);
            var statusBounds = new Rectangle(statusLeft, mid - UIUtils.S(10), statusWidth, UIUtils.S(20));
            int textRight = Math.Max(paddingLeft + UIUtils.S(220), statusBounds.Left - gap);
            int textWidth = Math.Max(UIUtils.S(180), textRight - paddingLeft);
            var titleBounds = new Rectangle(paddingLeft, paddingTop, textWidth, UIUtils.S(22));
            var descriptionBounds = new Rectangle(paddingLeft, titleBounds.Bottom, textWidth, UIUtils.S(20));

            return new MarketAlertTestRowLayout(titleBounds, descriptionBounds, statusBounds, buttonBounds);
        }
    }

    internal sealed class MarketAlertTestCard
    {
        public MarketAlertTestCard(Panel row, Label statusLabel)
        {
            Row = row;
            StatusLabel = statusLabel;
        }

        public Panel Row { get; }

        public Label StatusLabel { get; }
    }

    internal static class MarketAlertTestCardFactory
    {
        public static MarketAlertTestCard Create(Action sendTestAlert)
        {
            ArgumentNullException.ThrowIfNull(sendTestAlert);

            var row = new Panel
            {
                Height = UIUtils.S(58),
                BackColor = MarketAlertTestCardModel.GetRowBackColor(),
                Margin = new Padding(0),
                Padding = UIUtils.S(new Padding(10, 6, 10, 6))
            };
            var title = CreateLabel("手动测试", strong: true, 9F, UIColors.TextMain, ContentAlignment.MiddleLeft);
            var description = CreateLabel("发送一条测试提醒，检查当前提醒方式和弹窗样式。", strong: false, 8.5F, UIColors.TextSub, ContentAlignment.MiddleLeft);
            var status = CreateLabel("未测试", strong: false, 8.5F, UIColors.TextSub, ContentAlignment.MiddleRight);
            var testButton = new LiteButton("发送测试预警", true) { Width = UIUtils.S(120), Height = UIUtils.S(32) };
            testButton.Click += (_, __) => sendTestAlert();

            row.Controls.Add(title);
            row.Controls.Add(description);
            row.Controls.Add(status);
            row.Controls.Add(testButton);
            row.Resize += (_, __) => ApplyLayout(row, title, description, status, testButton);
            ApplyLayout(row, title, description, status, testButton);

            return new MarketAlertTestCard(row, status);
        }

        private static Label CreateLabel(string text, bool strong, float fontSize, Color color, ContentAlignment alignment)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", fontSize, strong ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = color,
                BackColor = Color.Transparent,
                TextAlign = alignment
            };
        }

        private static void ApplyLayout(Panel row, Label title, Label description, Label status, Control button)
        {
            MarketAlertTestRowLayout layout = MarketAlertTestCardModel.BuildLayout(row.Width, row.Height, button.Height);
            title.Bounds = layout.TitleBounds;
            description.Bounds = layout.DescriptionBounds;
            status.Bounds = layout.StatusBounds;
            button.Bounds = layout.ButtonBounds;
        }
    }
}
