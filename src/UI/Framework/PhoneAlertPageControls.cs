using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal readonly record struct PhoneAlertStatusRowLayout(
        Rectangle BindStatusBounds,
        Rectangle EnabledStatusBounds,
        Rectangle LastSendBounds,
        Rectangle FailureBounds);

    internal static class PhoneAlertPageControls
    {
        public static Label CreateValueLabel()
        {
            return new Label
            {
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
        }

        public static Label CreateHeaderHintLabel()
        {
            return new Label
            {
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
        }

        public static Panel CreateStatusBlock(string caption, Label value)
        {
            ArgumentNullException.ThrowIfNull(value);

            var block = new Panel { BackColor = Color.Transparent };
            var label = new Label
            {
                Text = caption,
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            block.Controls.Add(label);
            block.Controls.Add(value);
            block.Layout += (_, __) =>
            {
                label.SetBounds(0, 0, block.Width, PhoneAlertPageControlModel.BuildStatusCaptionHeight());
                value.SetBounds(
                    0,
                    PhoneAlertPageControlModel.BuildStatusValueTop(),
                    block.Width,
                    PhoneAlertPageControlModel.BuildStatusValueHeight());
            };
            return block;
        }

        public static Panel CreateEnabledStatusBlock(Label value, LiteCheck check)
        {
            ArgumentNullException.ThrowIfNull(check);

            var block = CreateStatusBlock("是否启用", value);
            block.Controls.Add(check);
            block.Layout += (_, __) =>
            {
                int checkWidth = PhoneAlertPageControlModel.BuildEnabledCheckWidth();
                check.SetBounds(
                    Math.Max(PhoneAlertPageControlModel.BuildEnabledCheckMinLeft(), block.Width - checkWidth),
                    PhoneAlertPageControlModel.BuildStatusValueTop() - UIUtils.S(1),
                    checkWidth,
                    PhoneAlertPageControlModel.BuildEnabledCheckHeight());
                value.SetBounds(
                    0,
                    PhoneAlertPageControlModel.BuildStatusValueTop(),
                    Math.Max(1, check.Left - UIUtils.S(6)),
                    PhoneAlertPageControlModel.BuildStatusValueHeight());
            };
            return block;
        }

        public static Panel CreateStatusRow(
            Control bindStatus,
            Control enabledStatus,
            Control lastSend,
            Control failure)
        {
            ArgumentNullException.ThrowIfNull(bindStatus);
            ArgumentNullException.ThrowIfNull(enabledStatus);
            ArgumentNullException.ThrowIfNull(lastSend);
            ArgumentNullException.ThrowIfNull(failure);

            var row = new Panel
            {
                Height = PhoneAlertPageControlModel.BuildStatusRowHeight(),
                BackColor = Color.Transparent
            };
            row.Controls.Add(bindStatus);
            row.Controls.Add(enabledStatus);
            row.Controls.Add(lastSend);
            row.Controls.Add(failure);
            row.Layout += (_, __) =>
            {
                PhoneAlertStatusRowLayout layout = PhoneAlertPageControlModel.BuildStatusRowLayout(row.Width);
                bindStatus.Bounds = layout.BindStatusBounds;
                enabledStatus.Bounds = layout.EnabledStatusBounds;
                lastSend.Bounds = layout.LastSendBounds;
                failure.Bounds = layout.FailureBounds;
            };
            row.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1);
            };
            return row;
        }

        public static void SetLabel(Label? label, string text, Color color)
        {
            if (label == null || label.IsDisposed)
                return;
            label.Text = text;
            label.ForeColor = color;
        }
    }

    internal static class PhoneAlertPageControlModel
    {
        public static int BuildStatusRowHeight()
        {
            return UIUtils.S(104);
        }

        public static PhoneAlertStatusRowLayout BuildStatusRowLayout(int rowWidth)
        {
            int gap = UIUtils.S(18);
            int top = UIUtils.S(12);
            int blockHeight = UIUtils.S(42);
            int blockWidth = Math.Max(UIUtils.S(180), (rowWidth - gap) / 2);
            var bind = new Rectangle(0, top, blockWidth, blockHeight);
            var enabled = new Rectangle(bind.Right + gap, top, blockWidth, blockHeight);
            int secondTop = top + UIUtils.S(48);
            var lastSend = new Rectangle(0, secondTop, blockWidth, blockHeight);
            var failure = new Rectangle(lastSend.Right + gap, secondTop, blockWidth, blockHeight);
            return new PhoneAlertStatusRowLayout(bind, enabled, lastSend, failure);
        }

        public static int BuildStatusCaptionHeight()
        {
            return UIUtils.S(16);
        }

        public static int BuildStatusValueTop()
        {
            return UIUtils.S(18);
        }

        public static int BuildStatusValueHeight()
        {
            return UIUtils.S(22);
        }

        public static int BuildEnabledCheckMinLeft()
        {
            return UIUtils.S(76);
        }

        public static int BuildEnabledCheckWidth()
        {
            return UIUtils.S(86);
        }

        public static int BuildEnabledCheckHeight()
        {
            return UIUtils.S(24);
        }
    }
}
