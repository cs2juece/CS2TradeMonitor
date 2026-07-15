using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class YouPinSaleReminderPageControls
    {
        public static Label CreateMutedLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        public static Control CreateCheckActionsRow(
            string primaryText,
            string secondaryText,
            Action<Label> captureStatus,
            Func<Control, Task> primaryAction,
            Func<Control, Task> secondaryAction,
            Action<LiteButton> trackActionButton)
        {
            ArgumentNullException.ThrowIfNull(captureStatus);
            ArgumentNullException.ThrowIfNull(primaryAction);
            ArgumentNullException.ThrowIfNull(secondaryAction);
            ArgumentNullException.ThrowIfNull(trackActionButton);

            var row = new Panel
            {
                Height = YouPinSaleReminderPageControlModel.BuildCheckActionsRowHeight(),
                BackColor = Color.Transparent
            };
            var primary = new LiteButton(primaryText, true) { Height = UIUtils.S(30) };
            var secondary = new LiteButton(secondaryText, false) { Height = UIUtils.S(30) };
            var status = CreateMutedLabel("未检查");

            trackActionButton(primary);
            trackActionButton(secondary);
            captureStatus(status);

            primary.Click += async (_, __) => await primaryAction(primary);
            secondary.Click += async (_, __) => await secondaryAction(secondary);

            row.Controls.Add(primary);
            row.Controls.Add(secondary);
            row.Controls.Add(status);
            row.Layout += (_, __) =>
            {
                int mid = row.Height / 2;
                primary.Location = new Point(0, mid - primary.Height / 2);
                secondary.Location = new Point(primary.Right + UIUtils.S(10), mid - secondary.Height / 2);
                status.SetBounds(secondary.Right + UIUtils.S(14), UIUtils.S(6), Math.Max(1, row.Width - secondary.Right - UIUtils.S(14)), UIUtils.S(42));
            };
            return row;
        }

        public static Control CreateStatusBlock(
            Label status,
            Label summary,
            Label? extra = null,
            Func<Control, Task>? refreshAction = null,
            Action<LiteButton>? trackActionButton = null)
        {
            ArgumentNullException.ThrowIfNull(status);
            ArgumentNullException.ThrowIfNull(summary);

            var panel = new Panel
            {
                Height = YouPinSaleReminderPageControlModel.BuildStatusBlockHeight(extra != null),
                BackColor = Color.Transparent
            };
            status.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            summary.Font = new Font("Microsoft YaHei UI", 8.5F);
            panel.Controls.Add(status);
            panel.Controls.Add(summary);
            LiteButton? refreshButton = null;
            if (refreshAction != null)
            {
                refreshButton = new LiteButton("立即刷新", false)
                {
                    Width = UIUtils.S(86),
                    Height = UIUtils.S(30)
                };
                trackActionButton?.Invoke(refreshButton);
                refreshButton.Click += async (_, __) => await refreshAction(refreshButton);
                panel.Controls.Add(refreshButton);
            }

            if (extra != null)
            {
                extra.Font = new Font("Microsoft YaHei UI", 8.5F);
                panel.Controls.Add(extra);
            }
            panel.Layout += (_, __) =>
            {
                int top = UIUtils.S(8);
                int gap = UIUtils.S(18);
                int buttonWidth = refreshButton?.Width ?? 0;
                int rightPadding = refreshButton == null ? 0 : buttonWidth + gap;
                int statusWidth = Math.Min(UIUtils.S(260), Math.Max(UIUtils.S(140), panel.Width / 3));
                status.SetBounds(0, top, statusWidth, UIUtils.S(24));
                summary.SetBounds(status.Right + gap, top, Math.Max(1, panel.Width - status.Right - gap - rightPadding), UIUtils.S(24));
                if (refreshButton != null)
                    refreshButton.SetBounds(Math.Max(0, panel.Width - buttonWidth), top - UIUtils.S(3), buttonWidth, refreshButton.Height);
                if (extra != null)
                    extra.SetBounds(0, status.Bottom + UIUtils.S(6), panel.Width, UIUtils.S(22));
            };
            panel.Paint += PaintBottomLine;
            return panel;
        }

        public static Control CreateInfoLine(string title, Label value)
        {
            ArgumentNullException.ThrowIfNull(value);

            var row = new Panel
            {
                Height = YouPinSaleReminderPageControlModel.BuildInfoLineHeight(),
                BackColor = Color.Transparent
            };
            var label = new Label
            {
                Text = title,
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            row.Controls.Add(label);
            row.Controls.Add(value);
            row.Layout += (_, __) =>
            {
                int labelWidth = UIUtils.S(90);
                label.SetBounds(0, 0, labelWidth, row.Height);
                value.SetBounds(labelWidth + UIUtils.S(14), 0, Math.Max(1, row.Width - labelWidth - UIUtils.S(14)), row.Height);
            };
            row.Paint += PaintBottomLine;
            return row;
        }

        public static YouPinSaleReminderOrderListPanel CreateOrderListPanel(
            YouPinSaleReminderOrderListActions actions,
            bool waitDeliverActions,
            int height = 340,
            bool compactQuoteStyle = false,
            bool groupCompactQuotes = true)
        {
            ArgumentNullException.ThrowIfNull(actions);

            return new YouPinSaleReminderOrderListPanel(actions, waitDeliverActions, compactQuoteStyle, groupCompactQuotes)
            {
                Height = YouPinSaleReminderPageControlModel.BuildOrderListHeight(height),
                Dock = DockStyle.Top,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0),
                OverscanRowCount = 2
            };
        }

        private static void PaintBottomLine(object? sender, PaintEventArgs e)
        {
            if (sender is not Control control)
                return;

            using var pen = new Pen(UIColors.Border);
            e.Graphics.DrawLine(pen, 0, control.Height - 1, control.Width, control.Height - 1);
        }
    }

    internal static class YouPinSaleReminderPageControlModel
    {
        public static int BuildCheckActionsRowHeight()
        {
            return UIUtils.S(54);
        }

        public static int BuildStatusBlockHeight(bool hasExtra)
        {
            return UIUtils.S(hasExtra ? 70 : 48);
        }

        public static int BuildInfoLineHeight()
        {
            return UIUtils.S(36);
        }

        public static int BuildOrderListHeight(int height)
        {
            return UIUtils.S(height);
        }
    }
}
