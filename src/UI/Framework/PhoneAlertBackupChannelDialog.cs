using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class PhoneAlertBackupChannelDialogContext
    {
        public PhoneAlertBackupChannelDialogContext(
            Action ensureChannels,
            Func<PhoneAlertChannelType, PhoneAlertChannelConfig> getOrCreateChannel,
            Dictionary<string, Label> channelStatusLabels,
            Func<bool> isUpdating,
            Action syncLegacyFields,
            Action saveChannels,
            Action refreshSummary,
            Action<PhoneAlertChannelConfig> refreshChannelStatus,
            Func<PhoneAlertChannelConfig, Control, Task> testChannelAsync,
            Action<PhoneAlertChannelType> openHelp,
            Action<PhoneAlertChannelConfig, int> moveChannel)
        {
            EnsureChannels = ensureChannels ?? throw new ArgumentNullException(nameof(ensureChannels));
            GetOrCreateChannel = getOrCreateChannel ?? throw new ArgumentNullException(nameof(getOrCreateChannel));
            ChannelStatusLabels = channelStatusLabels ?? throw new ArgumentNullException(nameof(channelStatusLabels));
            IsUpdating = isUpdating ?? throw new ArgumentNullException(nameof(isUpdating));
            SyncLegacyFields = syncLegacyFields ?? throw new ArgumentNullException(nameof(syncLegacyFields));
            SaveChannels = saveChannels ?? throw new ArgumentNullException(nameof(saveChannels));
            RefreshSummary = refreshSummary ?? throw new ArgumentNullException(nameof(refreshSummary));
            RefreshChannelStatus = refreshChannelStatus ?? throw new ArgumentNullException(nameof(refreshChannelStatus));
            TestChannelAsync = testChannelAsync ?? throw new ArgumentNullException(nameof(testChannelAsync));
            OpenHelp = openHelp ?? throw new ArgumentNullException(nameof(openHelp));
            MoveChannel = moveChannel ?? throw new ArgumentNullException(nameof(moveChannel));
        }

        public Action EnsureChannels { get; }
        public Func<PhoneAlertChannelType, PhoneAlertChannelConfig> GetOrCreateChannel { get; }
        public Dictionary<string, Label> ChannelStatusLabels { get; }
        public Func<bool> IsUpdating { get; }
        public Action SyncLegacyFields { get; }
        public Action SaveChannels { get; }
        public Action RefreshSummary { get; }
        public Action<PhoneAlertChannelConfig> RefreshChannelStatus { get; }
        public Func<PhoneAlertChannelConfig, Control, Task> TestChannelAsync { get; }
        public Action<PhoneAlertChannelType> OpenHelp { get; }
        public Action<PhoneAlertChannelConfig, int> MoveChannel { get; }
    }

    internal static class PhoneAlertBackupChannelDialog
    {
        public static void Show(IWin32Window? owner, PhoneAlertBackupChannelDialogContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            context.EnsureChannels();
            var dialog = new Form
            {
                Text = "备用通道",
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(UIUtils.S(980), UIUtils.S(640)),
                MinimumSize = new Size(UIUtils.S(760), UIUtils.S(460)),
                BackColor = UIColors.MainBg,
                ForeColor = UIColors.TextMain,
                ShowIcon = false,
                ShowInTaskbar = false
            };

            var panel = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(UIUtils.S(18), UIUtils.S(18), UIUtils.S(18), UIUtils.S(18)),
                BackColor = UIColors.MainBg
            };
            dialog.Controls.Add(panel);

            var group = CreateChannelGroup("备用通道", PhoneAlertChannelCatalog.AdvancedBackupTypes, context);
            group.Width = Math.Max(UIUtils.S(600), panel.ClientSize.Width - UIUtils.S(30));
            panel.Controls.Add(group);
            panel.Resize += (_, __) => group.Width = Math.Max(UIUtils.S(600), panel.ClientSize.Width - UIUtils.S(30));

            UIColors.ApplyNativeThemeRecursively(dialog);
            dialog.FormClosed += (_, __) =>
            {
                foreach (PhoneAlertChannelType type in PhoneAlertChannelCatalog.AdvancedBackupTypes)
                {
                    var channel = context.GetOrCreateChannel(type);
                    context.ChannelStatusLabels.Remove(channel.Id);
                }
            };
            dialog.ShowDialog(owner);
            context.RefreshSummary();
        }

        private static LiteSettingsGroup CreateChannelGroup(
            string title,
            IEnumerable<PhoneAlertChannelType> types,
            PhoneAlertBackupChannelDialogContext context)
        {
            var group = new LiteSettingsGroup(title);
            foreach (PhoneAlertChannelType type in types)
            {
                var channel = context.GetOrCreateChannel(type);
                group.AddFullItem(CreateChannelCard(channel, context));
            }

            return group;
        }

        private static Control CreateChannelCard(
            PhoneAlertChannelConfig channel,
            PhoneAlertBackupChannelDialogContext context)
        {
            PhoneAlertChannelDefinition definition = PhoneAlertChannelDefinitionCatalog.Get(channel.Type);
            bool needServer = definition.ShowServerField;
            bool needExtra = definition.ShowExtraField;
            int height = UIUtils.S(needServer && needExtra ? 172 : (needServer || needExtra ? 138 : 112));
            var panel = new Panel { Height = height, BackColor = Color.Transparent, Padding = new Padding(0) };

            var titleLabel = CreateLabel(definition.Title, true);
            titleLabel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            var enabled = new LiteCheck(channel.Enabled, "启用");
            enabled.CheckedChanged += (_, __) =>
            {
                if (context.IsUpdating())
                    return;

                channel.Enabled = enabled.Checked;
                context.SyncLegacyFields();
                context.SaveChannels();
                context.RefreshChannelStatus(channel);
                context.RefreshSummary();
            };
            var priority = CreateLabel($"优先级 {channel.Priority}", false);
            priority.TextAlign = ContentAlignment.MiddleRight;
            var btnUp = new LiteButton("↑", false) { Width = UIUtils.S(34), Height = UIUtils.S(28) };
            var btnDown = new LiteButton("↓", false) { Width = UIUtils.S(34), Height = UIUtils.S(28) };
            btnUp.Click += (_, __) => context.MoveChannel(channel, -1);
            btnDown.Click += (_, __) => context.MoveChannel(channel, 1);

            var secretLabel = CreateLabel(definition.SecretLabel, false);
            var secretInput = new LiteUnderlineInput(channel.Secret, "", "", 300, null, HorizontalAlignment.Left);
            secretInput.Inner.UseSystemPasswordChar = true;
            secretInput.Inner.TextChanged += (_, __) =>
            {
                if (context.IsUpdating())
                    return;

                channel.Secret = secretInput.Inner.Text.Trim();
                context.SyncLegacyFields();
                context.SaveChannels();
                context.RefreshChannelStatus(channel);
                context.RefreshSummary();
            };

            Label? serverLabel = null;
            LiteUnderlineInput? serverInput = null;
            if (needServer)
            {
                serverLabel = CreateLabel(definition.ServerLabel, false);
                serverInput = new LiteUnderlineInput(channel.ServerUrl, "", "", 360, null, HorizontalAlignment.Left);
                serverInput.Inner.TextChanged += (_, __) =>
                {
                    if (context.IsUpdating())
                        return;

                    channel.ServerUrl = serverInput.Inner.Text.Trim();
                    context.SaveChannels();
                    context.RefreshChannelStatus(channel);
                    context.RefreshSummary();
                };
            }

            Label? extraLabel = null;
            LiteUnderlineInput? extraInput = null;
            if (needExtra)
            {
                extraLabel = CreateLabel(definition.ExtraLabel, false);
                extraInput = new LiteUnderlineInput(channel.Extra, "", "", 360, null, HorizontalAlignment.Left);
                extraInput.Inner.TextChanged += (_, __) =>
                {
                    if (context.IsUpdating())
                        return;

                    channel.Extra = extraInput.Inner.Text.Trim();
                    context.SaveChannels();
                    context.RefreshChannelStatus(channel);
                    context.RefreshSummary();
                };
            }

            var btnHelp = new LiteButton("打开官网", false) { Width = UIUtils.S(96), Height = UIUtils.S(30) };
            btnHelp.Click += (_, __) => context.OpenHelp(channel.Type);
            var btnTest = new LiteButton("测试本通道", true) { Width = UIUtils.S(110), Height = UIUtils.S(30) };
            btnTest.Click += async (_, __) => await context.TestChannelAsync(channel, btnTest);
            var status = new Label
            {
                Text = "",
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            context.ChannelStatusLabels[channel.Id] = status;

            panel.Controls.Add(titleLabel);
            panel.Controls.Add(enabled);
            panel.Controls.Add(priority);
            panel.Controls.Add(btnUp);
            panel.Controls.Add(btnDown);
            panel.Controls.Add(secretLabel);
            panel.Controls.Add(secretInput);
            if (serverLabel != null) panel.Controls.Add(serverLabel);
            if (serverInput != null) panel.Controls.Add(serverInput);
            if (extraLabel != null) panel.Controls.Add(extraLabel);
            if (extraInput != null) panel.Controls.Add(extraInput);
            panel.Controls.Add(btnHelp);
            panel.Controls.Add(btnTest);
            panel.Controls.Add(status);

            panel.Layout += (_, __) =>
            {
                int labelW = UIUtils.S(118);
                int top = UIUtils.S(10);
                int rowH = UIUtils.S(28);
                int gap = UIUtils.S(10);
                int inputLeft = UIUtils.S(150);
                int actionW = UIUtils.S(230);
                int inputW = Math.Max(UIUtils.S(240), panel.Width - inputLeft - actionW - UIUtils.S(24));
                int actionLeft = inputLeft + inputW + UIUtils.S(16);
                titleLabel.SetBounds(0, top, UIUtils.S(260), rowH);
                enabled.SetBounds(UIUtils.S(280), top + UIUtils.S(2), UIUtils.S(110), rowH);
                priority.SetBounds(Math.Max(UIUtils.S(420), actionLeft - UIUtils.S(170)), top, UIUtils.S(110), rowH);
                btnUp.SetBounds(panel.Width - UIUtils.S(86), top, btnUp.Width, btnUp.Height);
                btnDown.SetBounds(panel.Width - UIUtils.S(44), top, btnDown.Width, btnDown.Height);
                int inputTop = top + UIUtils.S(34);
                secretLabel.SetBounds(0, inputTop, labelW, rowH);
                secretInput.SetBounds(inputLeft, inputTop, inputW, rowH);
                int nextTop = inputTop + UIUtils.S(34);
                if (serverLabel != null && serverInput != null)
                {
                    serverLabel.SetBounds(0, nextTop, labelW, rowH);
                    serverInput.SetBounds(inputLeft, nextTop, inputW, rowH);
                    nextTop += UIUtils.S(34);
                }

                if (extraLabel != null && extraInput != null)
                {
                    extraLabel.SetBounds(0, nextTop, labelW, rowH);
                    extraInput.SetBounds(inputLeft, nextTop, inputW, rowH);
                }

                btnHelp.SetBounds(actionLeft, inputTop - UIUtils.S(2), btnHelp.Width, btnHelp.Height);
                btnTest.SetBounds(btnHelp.Right + gap, inputTop - UIUtils.S(2), btnTest.Width, btnTest.Height);
                status.SetBounds(actionLeft, inputTop + UIUtils.S(34), Math.Max(1, panel.Width - actionLeft), UIUtils.S(28));
            };
            panel.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawLine(pen, 0, panel.Height - 1, panel.Width, panel.Height - 1);
            };
            context.RefreshChannelStatus(channel);
            return panel;
        }

        private static Label CreateLabel(string text, bool strong)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 9F, strong ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
        }
    }
}
