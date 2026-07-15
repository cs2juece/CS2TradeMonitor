using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal readonly record struct SystemSettingsActionRowLayout(
        Rectangle TitleBounds,
        Rectangle DescriptionBounds,
        Rectangle NoteBounds,
        Point PrimaryButtonLocation,
        Point? SecondaryButtonLocation);

    internal readonly record struct SystemSettingsSupportPanelLayout(
        Rectangle TitleBounds,
        Rectangle GroupBounds,
        Point CopyButtonLocation,
        Rectangle LinkBounds,
        Point JoinButtonLocation);

    internal static class SystemSettingsActionControlModel
    {
        public static int BuildRowHeight() => UIUtils.S(104);

        public static Size BuildActionButtonSize(string text)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(text);

            return new Size(UIUtils.S(Math.Max(104, text.Length * 16 + 28)), UIUtils.S(32));
        }

        public static Size BuildSupportButtonSize() => new(UIUtils.S(94), UIUtils.S(30));

        public static SystemSettingsActionRowLayout BuildActionRowLayout(
            int rowWidth,
            int rowHeight,
            Size primaryButtonSize,
            Size? secondaryButtonSize = null)
        {
            int padX = UIUtils.S(4);
            int gap = UIUtils.S(10);
            int top = UIUtils.S(14);
            int buttonGap = UIUtils.S(10);
            int buttonTotalWidth = primaryButtonSize.Width;
            if (secondaryButtonSize.HasValue)
                buttonTotalWidth += buttonGap + secondaryButtonSize.Value.Width;

            int buttonLeft = Math.Max(UIUtils.S(300), rowWidth - buttonTotalWidth - padX);
            int textRight = Math.Max(UIUtils.S(220), buttonLeft - gap);
            int textWidth = Math.Max(UIUtils.S(220), textRight - padX);
            int buttonTop = Math.Max(UIUtils.S(10), (rowHeight - primaryButtonSize.Height) / 2);
            var primaryLocation = new Point(buttonLeft, buttonTop);

            Point? secondaryLocation = secondaryButtonSize.HasValue
                ? new Point(primaryLocation.X + primaryButtonSize.Width + buttonGap, buttonTop)
                : null;

            var titleBounds = new Rectangle(padX, top, textWidth, UIUtils.S(22));
            var descriptionBounds = new Rectangle(padX, titleBounds.Bottom + UIUtils.S(4), textWidth, UIUtils.S(22));
            var noteBounds = new Rectangle(padX, descriptionBounds.Bottom + UIUtils.S(5), textWidth, UIUtils.S(20));
            return new SystemSettingsActionRowLayout(
                titleBounds,
                descriptionBounds,
                noteBounds,
                primaryLocation,
                secondaryLocation);
        }

        public static SystemSettingsSupportPanelLayout BuildSupportPanelLayout(
            int rowWidth,
            Size copyButtonSize,
            Size joinButtonSize,
            int measuredGroupWidth,
            int measuredLinkWidth)
        {
            int padX = UIUtils.S(4);
            int gap = UIUtils.S(10);
            int top = UIUtils.S(14);
            int textWidth = Math.Max(UIUtils.S(220), rowWidth - padX * 2);

            var titleBounds = new Rectangle(padX, top, textWidth, UIUtils.S(22));

            int groupTop = titleBounds.Bottom + UIUtils.S(5);
            int groupWidth = Math.Min(measuredGroupWidth, Math.Max(UIUtils.S(120), textWidth - copyButtonSize.Width - gap));
            var groupBounds = new Rectangle(padX, groupTop, groupWidth, UIUtils.S(28));
            var copyButtonLocation = new Point(groupBounds.Right + gap, groupTop - UIUtils.S(2));

            int linkTop = Math.Max(groupBounds.Bottom, copyButtonLocation.Y + copyButtonSize.Height) + UIUtils.S(3);
            int maxJoinLeft = Math.Max(padX + UIUtils.S(220), rowWidth - joinButtonSize.Width - padX);
            int joinLeft = Math.Min(maxJoinLeft, padX + measuredLinkWidth + gap);
            int linkWidth = Math.Max(UIUtils.S(220), joinLeft - gap - padX);
            var linkBounds = new Rectangle(padX, linkTop, linkWidth, UIUtils.S(24));
            var joinButtonLocation = new Point(joinLeft, linkTop - UIUtils.S(5));

            return new SystemSettingsSupportPanelLayout(
                titleBounds,
                groupBounds,
                copyButtonLocation,
                linkBounds,
                joinButtonLocation);
        }
    }

    internal static class SystemSettingsActionControls
    {
        public static Control CreateActionRow(
            string title,
            string description,
            string note,
            string primaryText,
            Action primaryAction,
            bool primaryStyle,
            string? secondaryText = null,
            Action? secondaryAction = null,
            bool secondaryDanger = false,
            bool primaryDanger = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(title);
            ArgumentException.ThrowIfNullOrWhiteSpace(description);
            ArgumentException.ThrowIfNullOrWhiteSpace(note);
            ArgumentException.ThrowIfNullOrWhiteSpace(primaryText);
            ArgumentNullException.ThrowIfNull(primaryAction);

            var row = CreateBaseRow();
            var titleLabel = CreateTitleLabel(title);
            var descLabel = CreateTextLabel(description, UIColors.TextMain, 9F);
            var noteLabel = CreateTextLabel(note, UIColors.TextSub, 8.5F);

            var primaryButton = new LiteButton(primaryText, primaryStyle)
            {
                Size = SystemSettingsActionControlModel.BuildActionButtonSize(primaryText)
            };
            if (primaryDanger)
                primaryButton.ForeColor = UIColors.TextCrit;
            primaryButton.Click += (_, __) => primaryAction();

            LiteButton? secondaryButton = null;
            if (!string.IsNullOrWhiteSpace(secondaryText) && secondaryAction != null)
            {
                secondaryButton = new LiteButton(secondaryText, false)
                {
                    Size = SystemSettingsActionControlModel.BuildActionButtonSize(secondaryText)
                };
                if (secondaryDanger)
                    secondaryButton.ForeColor = UIColors.TextCrit;
                secondaryButton.Click += (_, __) => secondaryAction();
                row.Controls.Add(secondaryButton);
            }

            row.Controls.Add(titleLabel);
            row.Controls.Add(descLabel);
            row.Controls.Add(noteLabel);
            row.Controls.Add(primaryButton);

            row.Layout += (_, __) =>
            {
                SystemSettingsActionRowLayout layout = SystemSettingsActionControlModel.BuildActionRowLayout(
                    row.ClientSize.Width,
                    row.ClientSize.Height,
                    primaryButton.Size,
                    secondaryButton?.Size);

                titleLabel.Bounds = layout.TitleBounds;
                descLabel.Bounds = layout.DescriptionBounds;
                noteLabel.Bounds = layout.NoteBounds;
                primaryButton.Location = layout.PrimaryButtonLocation;
                if (secondaryButton != null && layout.SecondaryButtonLocation.HasValue)
                    secondaryButton.Location = layout.SecondaryButtonLocation.Value;
            };

            AddBottomBorder(row);
            return row;
        }

        public static Control CreateSupportPanel(
            string qqGroupNumber,
            string qqGroupUrl,
            Action copyAction,
            Action joinAction)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(qqGroupNumber);
            ArgumentException.ThrowIfNullOrWhiteSpace(qqGroupUrl);
            ArgumentNullException.ThrowIfNull(copyAction);
            ArgumentNullException.ThrowIfNull(joinAction);

            var row = CreateBaseRow();
            var titleLabel = CreateTitleLabel("官方反馈群");
            var groupLabel = CreateTextLabel($"QQ群号：{qqGroupNumber}", UIColors.TextMain, 9F);
            var linkLabel = CreateTextLabel($"点击链接加入【CS2交易监控】官方群：{qqGroupUrl}", UIColors.TextSub, 8.5F);

            var copyButton = new LiteButton("一键复制", false)
            {
                Size = SystemSettingsActionControlModel.BuildSupportButtonSize()
            };
            copyButton.Click += (_, __) => copyAction();

            var joinButton = new LiteButton("一键加入", true)
            {
                Size = SystemSettingsActionControlModel.BuildSupportButtonSize()
            };
            joinButton.Click += (_, __) => joinAction();

            row.Controls.Add(titleLabel);
            row.Controls.Add(groupLabel);
            row.Controls.Add(copyButton);
            row.Controls.Add(linkLabel);
            row.Controls.Add(joinButton);

            row.Layout += (_, __) =>
            {
                SystemSettingsSupportPanelLayout layout = SystemSettingsActionControlModel.BuildSupportPanelLayout(
                    row.ClientSize.Width,
                    copyButton.Size,
                    joinButton.Size,
                    TextRenderer.MeasureText(groupLabel.Text, groupLabel.Font).Width + UIUtils.S(8),
                    TextRenderer.MeasureText(linkLabel.Text, linkLabel.Font).Width + UIUtils.S(4));

                titleLabel.Bounds = layout.TitleBounds;
                groupLabel.Bounds = layout.GroupBounds;
                copyButton.Location = layout.CopyButtonLocation;
                linkLabel.Bounds = layout.LinkBounds;
                joinButton.Location = layout.JoinButtonLocation;
            };

            AddBottomBorder(row);
            return row;
        }

        private static Panel CreateBaseRow()
        {
            return new Panel
            {
                Height = SystemSettingsActionControlModel.BuildRowHeight(),
                BackColor = Color.Transparent,
                Padding = new Padding(0)
            };
        }

        private static Label CreateTitleLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent
            };
        }

        private static Label CreateTextLabel(string text, Color color, float size)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", size),
                ForeColor = color,
                BackColor = Color.Transparent
            };
        }

        private static void AddBottomBorder(Control row)
        {
            row.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1);
            };
        }
    }
}
