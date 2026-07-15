using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class YouPinStopProfitLossStatusBlock
    {
        public YouPinStopProfitLossStatusBlock(
            Panel panel,
            Label statusLabel,
            Label lastCheckLabel,
            Label alertCountLabel)
        {
            Panel = panel ?? throw new ArgumentNullException(nameof(panel));
            StatusLabel = statusLabel ?? throw new ArgumentNullException(nameof(statusLabel));
            LastCheckLabel = lastCheckLabel ?? throw new ArgumentNullException(nameof(lastCheckLabel));
            AlertCountLabel = alertCountLabel ?? throw new ArgumentNullException(nameof(alertCountLabel));
        }

        public Panel Panel { get; }
        public Label StatusLabel { get; }
        public Label LastCheckLabel { get; }
        public Label AlertCountLabel { get; }
    }

    internal static class YouPinStopProfitLossStatusBlockFactory
    {
        public static YouPinStopProfitLossStatusBlock Create(Func<LiteButton, Task> runScanAsync)
        {
            ArgumentNullException.ThrowIfNull(runScanAsync);

            var panel = new Panel
            {
                Height = UIUtils.S(72),
                BackColor = UIColors.CardBg,
                Padding = UIUtils.S(new Padding(12, 10, 12, 10))
            };
            panel.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
            };

            var statusLabel = CreateValueLabel();
            var lastCheckLabel = CreateValueLabel();
            var alertCountLabel = CreateValueLabel();

            var status = CreateMetricBlock("运行状态", statusLabel);
            var last = CreateMetricBlock("最近扫描", lastCheckLabel);
            var alerts = CreateMetricBlock("最近报警", alertCountLabel);
            var scan = CreateScanActionBlock(runScanAsync);

            panel.Controls.Add(status);
            panel.Controls.Add(last);
            panel.Controls.Add(alerts);
            panel.Controls.Add(scan);

            panel.Layout += (_, __) =>
            {
                var layout = YouPinStopProfitLossStatusBlockModel.BuildLayout(panel.ClientSize.Width, panel.Padding);
                status.Bounds = layout.StatusBounds;
                last.Bounds = layout.LastCheckBounds;
                alerts.Bounds = layout.AlertCountBounds;
                scan.Bounds = layout.ScanActionBounds;
            };

            return new YouPinStopProfitLossStatusBlock(panel, statusLabel, lastCheckLabel, alertCountLabel);
        }

        private static Control CreateScanActionBlock(Func<LiteButton, Task> runScanAsync)
        {
            var block = new Panel
            {
                BackColor = Color.Transparent,
                Padding = UIUtils.S(new Padding(0, 6, 0, 6))
            };
            var scanButton = new LiteButton("立即扫描", true)
            {
                Width = UIUtils.S(118),
                Height = UIUtils.S(32)
            };
            scanButton.Click += async (_, __) => await runScanAsync(scanButton);

            block.Controls.Add(scanButton);
            block.Layout += (_, __) =>
            {
                scanButton.SetBounds(
                    Math.Max(0, block.Width - scanButton.Width),
                    Math.Max(0, (block.Height - scanButton.Height) / 2),
                    scanButton.Width,
                    scanButton.Height);
            };
            return block;
        }

        private static Panel CreateMetricBlock(string caption, Label value)
        {
            var block = new Panel
            {
                BackColor = UIColors.ControlBg,
                Padding = UIUtils.S(new Padding(10, 6, 10, 6))
            };
            block.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawRectangle(pen, 0, 0, block.Width - 1, block.Height - 1);
            };
            var label = new Label
            {
                Text = caption,
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = UIUtils.S(16),
                Font = new Font("Microsoft YaHei UI", 8F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            value.Dock = DockStyle.Fill;
            value.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            block.Controls.Add(value);
            block.Controls.Add(label);
            return block;
        }

        private static Label CreateValueLabel()
        {
            return new Label
            {
                AutoSize = false,
                Height = UIUtils.S(30),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
        }
    }

    internal static class YouPinStopProfitLossStatusBlockModel
    {
        public static YouPinStopProfitLossStatusBlockLayout BuildLayout(int clientWidth, Padding padding)
        {
            int gap = UIUtils.S(10);
            int top = UIUtils.S(10);
            int blockHeight = UIUtils.S(50);
            int contentWidth = Math.Max(1, clientWidth - padding.Horizontal);
            int actionWidth = Math.Min(UIUtils.S(150), Math.Max(UIUtils.S(118), contentWidth / 5));
            int metricWidth = Math.Max(1, (contentWidth - actionWidth - gap * 3) / 3);
            int x = padding.Left;

            var statusBounds = new Rectangle(x, top, metricWidth, blockHeight);
            x += metricWidth + gap;
            var lastCheckBounds = new Rectangle(x, top, metricWidth, blockHeight);
            x += metricWidth + gap;
            var alertCountBounds = new Rectangle(x, top, metricWidth, blockHeight);
            x += metricWidth + gap;
            var scanActionBounds = new Rectangle(
                x,
                top,
                Math.Max(actionWidth, clientWidth - padding.Right - x),
                blockHeight);

            return new YouPinStopProfitLossStatusBlockLayout(
                statusBounds,
                lastCheckBounds,
                alertCountBounds,
                scanActionBounds);
        }
    }

    internal readonly record struct YouPinStopProfitLossStatusBlockLayout(
        Rectangle StatusBounds,
        Rectangle LastCheckBounds,
        Rectangle AlertCountBounds,
        Rectangle ScanActionBounds);
}
