using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal readonly record struct DataPageCredentialBodyLayout(
        int BodyHeight,
        Rectangle SteamDtRowBounds,
        Rectangle SteamDtResultBounds,
        Rectangle CsqaqRowBounds,
        Rectangle CsqaqResultBounds);

    internal readonly record struct DataPageCredentialRowLayout(
        Rectangle LabelBounds,
        Rectangle InputBounds,
        Rectangle TestButtonBounds,
        Rectangle HelpButtonBounds);

    internal static class DataPageInterfaceControlModel
    {
        public static DataPageCredentialBodyLayout BuildCredentialBodyLayout(int bodyWidth)
        {
            int y = UIUtils.S(4);
            int rowHeight = UIUtils.S(42);
            int resultHeight = UIUtils.S(26);
            var steamDtRow = new Rectangle(0, y, bodyWidth, rowHeight);
            var steamDtResult = new Rectangle(0, steamDtRow.Bottom, bodyWidth, resultHeight);
            var qaqRow = new Rectangle(0, steamDtResult.Bottom + UIUtils.S(6), bodyWidth, rowHeight);
            var qaqResult = new Rectangle(0, qaqRow.Bottom, bodyWidth, resultHeight);
            return new DataPageCredentialBodyLayout(UIUtils.S(150), steamDtRow, steamDtResult, qaqRow, qaqResult);
        }

        public static DataPageCredentialRowLayout BuildCredentialRowLayout(
            int rowWidth,
            int rowHeight,
            int inputHeight,
            int testButtonWidth,
            int testButtonHeight,
            int helpButtonWidth,
            int helpButtonHeight)
        {
            int mid = rowHeight / 2;
            int labelWidth = UIUtils.S(170);
            int gap = UIUtils.S(10);
            int inputLeft = labelWidth + UIUtils.S(12);
            int helpLeft = rowWidth - helpButtonWidth;
            int testLeft = helpLeft - gap - testButtonWidth;
            int inputWidth = Math.Max(UIUtils.S(180), testLeft - inputLeft - gap);

            return new DataPageCredentialRowLayout(
                new Rectangle(0, 0, labelWidth, rowHeight),
                new Rectangle(inputLeft, Math.Max(0, mid - inputHeight / 2), inputWidth, inputHeight),
                new Rectangle(testLeft, Math.Max(0, mid - testButtonHeight / 2), testButtonWidth, testButtonHeight),
                new Rectangle(helpLeft, Math.Max(0, mid - helpButtonHeight / 2), helpButtonWidth, helpButtonHeight));
        }
    }

    internal static class DataPageInterfaceControls
    {
        public static Control CreateApiRecommendationHeaderNotice()
        {
            return new Label
            {
                Text = "强烈建议添加 SteamDT API 数据源，最好 SteamDT 和 QAQ 两个都添加，数据更稳定。",
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = UIColors.IsDark ? Color.FromArgb(255, 202, 68) : Color.FromArgb(156, 96, 0),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Height = UIUtils.S(24)
            };
        }

        public static Control CreateCredentialBody(
            Control steamDtRow,
            Label steamDtResult,
            Control csqaqRow,
            Label csqaqResult)
        {
            ArgumentNullException.ThrowIfNull(steamDtRow);
            ArgumentNullException.ThrowIfNull(steamDtResult);
            ArgumentNullException.ThrowIfNull(csqaqRow);
            ArgumentNullException.ThrowIfNull(csqaqResult);

            var body = new Panel
            {
                Height = UIUtils.S(150),
                BackColor = Color.Transparent,
                Visible = true
            };
            body.Controls.AddRange(new Control[] { steamDtRow, steamDtResult, csqaqRow, csqaqResult });
            body.Layout += (_, __) =>
            {
                DataPageCredentialBodyLayout layout = DataPageInterfaceControlModel.BuildCredentialBodyLayout(body.Width);
                if (body.Height != layout.BodyHeight)
                    body.Height = layout.BodyHeight;

                steamDtRow.Bounds = layout.SteamDtRowBounds;
                steamDtResult.Bounds = layout.SteamDtResultBounds;
                csqaqRow.Bounds = layout.CsqaqRowBounds;
                csqaqResult.Bounds = layout.CsqaqResultBounds;
            };
            return body;
        }

        public static Control CreateCredentialRow(string title, LiteUnderlineInput input, LiteButton testButton, LiteButton helpButton)
        {
            var row = new Panel
            {
                Height = UIUtils.S(42),
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
            row.Controls.AddRange(new Control[] { label, input, testButton, helpButton });
            row.Layout += (_, __) =>
            {
                DataPageCredentialRowLayout layout = DataPageInterfaceControlModel.BuildCredentialRowLayout(
                    row.Width,
                    row.Height,
                    input.Height,
                    testButton.Width,
                    testButton.Height,
                    helpButton.Width,
                    helpButton.Height);
                label.Bounds = layout.LabelBounds;
                input.Bounds = layout.InputBounds;
                testButton.Bounds = layout.TestButtonBounds;
                helpButton.Bounds = layout.HelpButtonBounds;
            };
            row.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1);
            };
            return row;
        }

        public static Label CreateValueLabel() => new()
        {
            AutoSize = false,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            ForeColor = UIColors.TextMain,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

        public static Label CreateResultLabel(string text) => new()
        {
            Text = text,
            Font = new Font("Microsoft YaHei UI", 8.5F),
            ForeColor = UIColors.TextSub,
            AutoSize = false,
            Height = UIUtils.S(24),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(UIUtils.S(182), 0, 0, 0)
        };

        public static Label CreateHeaderRefreshLabel() => new()
        {
            AutoSize = false,
            Font = new Font("Microsoft YaHei UI", 8.5F),
            ForeColor = UIColors.TextSub,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
    }
}
