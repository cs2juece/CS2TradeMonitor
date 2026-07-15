using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Notify;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;
using static CS2TradeMonitor.src.UI.Framework.PhoneAlertPageControls;

namespace CS2TradeMonitor.src.UI.Framework
{
    public sealed class PhoneAlertPage : FrameworkSettingsPageBase
    {
        private readonly Dictionary<string, Label> _channelStatusLabels = new Dictionary<string, Label>();
        private LiteCheck? _enabledCheck;
        private Label? _summaryLabel;
        private Label? _bindStatusLabel;
        private Label? _enabledStatusLabel;
        private Label? _lastSendLabel;
        private Label? _failureLabel;
        private Label? _secretConfigStatusLabel;
        private bool _built;
        private bool _building;
        private bool _testBusy;
        private readonly IPhoneAlertDispatchService _phoneAlerts;
        private readonly PhoneAlertPageStatusRenderer _statusRenderer;

        public PhoneAlertPage()
            : this(YouPinPageRuntimeServices.Resolve())
        {
        }

        internal PhoneAlertPage(YouPinPageRuntimeServices runtimeServices)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);

            _phoneAlerts = runtimeServices.PhoneAlerts;
            _statusRenderer = new PhoneAlertPageStatusRenderer(_channelStatusLabels);
        }

        protected override void OnStoreAttached()
        {
            BuildPage(force: true);
        }

        public override void Activate()
        {
            base.Activate();
            BuildPage();
            RefreshSummary();
            RequestRelayoutGroups();
        }

        public override void ApplySystemTheme()
        {
            base.ApplySystemTheme();
            RefreshSummary();
            foreach (var channel in Channels)
                RefreshChannelStatus(channel);
            RequestRelayoutGroups();
        }

        private void BuildPage(bool force = false)
        {
            if (_building || (_built && !force))
                return;

            _building = true;
            try
            {
                EnsureChannels();
                _channelStatusLabels.Clear();
                ClearPage();
                var help = AddGroupToPage(CreateHelpGroup());
                var tools = AddGroupToPage(CreateLowFrequencyToolsGroup());
                var config = AddGroupToPage(CreateSecretConfigGroup());
                var status = AddGroupToPage(CreateServerChanGroup());
                Container.Controls.SetChildIndex(help, 0);
                Container.Controls.SetChildIndex(tools, 1);
                Container.Controls.SetChildIndex(config, 2);
                Container.Controls.SetChildIndex(status, 3);
                UIColors.ApplyNativeThemeRecursively(this);
                _built = true;
                RefreshSummary();
                RequestRelayoutGroups();
            }
            finally
            {
                _building = false;
            }
        }

        private LiteSettingsGroup CreateServerChanGroup()
        {
            var group = new LiteSettingsGroup("手机提醒状态");
            group.AddFullItem(CreateStatusActionRow());
            return group;
        }

        private Control CreateStatusActionRow()
        {
            var server = GetOrCreateChannel(PhoneAlertChannelType.ServerChan);
            server.Enabled = Get(nameof(Settings.PhoneAlertEnabled), false);

            _bindStatusLabel = CreateValueLabel();
            _enabledStatusLabel = CreateValueLabel();
            _lastSendLabel = CreateValueLabel();
            _failureLabel = CreateValueLabel();

            _enabledCheck = new LiteCheck(server.Enabled, "启用");
            _enabledCheck.CheckedChanged += (_, __) =>
            {
                if (_building)
                    return;
                Set(nameof(Settings.PhoneAlertEnabled), _enabledCheck.Checked);
                Set(nameof(Settings.PhoneAlertDispatchMode), PhoneAlertDispatchMode.Failover);
                server.Enabled = _enabledCheck.Checked;
                SyncLegacyFields();
                SaveChannels();
                RefreshSummary();
                RefreshChannelStatus(server);
            };

            var bindStatus = CreateStatusBlock("绑定状态", _bindStatusLabel);
            var enabledStatus = CreateEnabledStatusBlock(_enabledStatusLabel, _enabledCheck);
            var lastSend = CreateStatusBlock("最近一次发送", _lastSendLabel);
            var failure = CreateStatusBlock("失败原因", _failureLabel);
            Panel row = CreateStatusRow(bindStatus, enabledStatus, lastSend, failure);
            RefreshChannelStatus(server);
            return row;
        }

        private LiteSettingsGroup CreateLowFrequencyToolsGroup()
        {
            var server = GetOrCreateChannel(PhoneAlertChannelType.ServerChan);
            var group = new LiteSettingsGroup("测试与备用通道");
            AddHint(group, "低频操作：只在首次配置、排查收不到提醒，或 Server酱不可用时使用。");

            var row = new Panel { Height = UIUtils.S(58), BackColor = Color.Transparent };
            var btnTest = new LiteButton("发送测试提醒", true) { Width = UIUtils.S(170), Height = UIUtils.S(34) };
            btnTest.Click += async (_, __) => await TestChannelAsync(server, btnTest);
            var btnBackup = new LiteButton("备用通道", false) { Width = UIUtils.S(120), Height = UIUtils.S(34) };
            btnBackup.Click += (_, __) => ShowAdvancedBackupDialog();
            var hint = new Label
            {
                Text = "低频操作：测试提醒、备用通道。",
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            row.Controls.Add(btnTest);
            row.Controls.Add(btnBackup);
            row.Controls.Add(hint);
            row.Layout += (_, __) =>
            {
                int top = UIUtils.S(12);
                btnTest.SetBounds(0, top, btnTest.Width, btnTest.Height);
                btnBackup.SetBounds(btnTest.Right + UIUtils.S(12), top, btnBackup.Width, btnBackup.Height);
                hint.SetBounds(btnBackup.Right + UIUtils.S(14), top + UIUtils.S(4), Math.Max(1, row.Width - btnBackup.Right - UIUtils.S(14)), UIUtils.S(26));
            };
            row.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1);
            };
            group.AddFullItem(row);
            return group;
        }

        private LiteSettingsGroup CreateSecretConfigGroup()
        {
            var server = GetOrCreateChannel(PhoneAlertChannelType.ServerChan);
            var group = new LiteSettingsGroup("接口配置");
            _secretConfigStatusLabel = CreateHeaderHintLabel();
            group.AddHeaderInlineAction(_secretConfigStatusLabel);

            var body = new Panel
            {
                Height = UIUtils.S(128),
                BackColor = Color.Transparent,
                Visible = true
            };
            var sendKeyLabel = CreateLabel("Server酱 SendKey", true);
            var sendKeyInput = new LiteUnderlineInput(server.Secret, "", "", 420, null, HorizontalAlignment.Left);
            sendKeyInput.Inner.UseSystemPasswordChar = true;
            sendKeyInput.Inner.TextChanged += (_, __) =>
            {
                if (_building)
                    return;
                server.Secret = sendKeyInput.Inner.Text.Trim();
                SyncLegacyFields();
                SaveChannels();
                RefreshSummary();
                RefreshChannelStatus(server);
            };
            var bindLabel = CreateLabel("扫码绑定", true);
            var btnHelp = new LiteButton("进行扫码绑定", false) { Width = UIUtils.S(170), Height = UIUtils.S(36) };
            btnHelp.Click += (_, __) => OpenHelp(PhoneAlertChannelType.ServerChan);
            _summaryLabel = new Label
            {
                Text = "",
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            var safety = new Label
            {
                Text = "SendKey 只保存在本机，界面仅脱敏显示；泄漏后请到 Server酱重新生成。",
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            body.Controls.Add(sendKeyLabel);
            body.Controls.Add(sendKeyInput);
            body.Controls.Add(bindLabel);
            body.Controls.Add(btnHelp);
            body.Controls.Add(_summaryLabel);
            body.Controls.Add(safety);
            body.Layout += (_, __) =>
            {
                int labelW = UIUtils.S(170);
                int top = UIUtils.S(10);
                int inputLeft = labelW + UIUtils.S(20);
                sendKeyLabel.SetBounds(0, top, labelW, UIUtils.S(30));
                sendKeyInput.SetBounds(inputLeft, top, Math.Max(UIUtils.S(320), body.Width - inputLeft - UIUtils.S(20)), UIUtils.S(30));
                int bindTop = sendKeyInput.Bottom + UIUtils.S(12);
                bindLabel.SetBounds(0, bindTop + UIUtils.S(3), labelW, UIUtils.S(30));
                btnHelp.SetBounds(inputLeft, bindTop, btnHelp.Width, btnHelp.Height);
                _summaryLabel.SetBounds(btnHelp.Right + UIUtils.S(14), bindTop + UIUtils.S(6), Math.Max(1, body.Width - btnHelp.Right - UIUtils.S(34)), UIUtils.S(24));
                safety.SetBounds(inputLeft, btnHelp.Bottom + UIUtils.S(10), Math.Max(1, body.Width - inputLeft - UIUtils.S(20)), UIUtils.S(26));
            };
            body.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawLine(pen, 0, body.Height - 1, body.Width, body.Height - 1);
            };
            group.AddFullItem(body);

            RefreshSecretConfigStatus(server);
            return group;
        }

        private Control CreateTestAllRow()
        {
            var row = new Panel { Height = UIUtils.S(52), BackColor = Color.Transparent };
            var btnTestAll = new LiteButton("测试全部启用通道", true) { Width = UIUtils.S(170), Height = UIUtils.S(34) };
            btnTestAll.Click += async (_, __) => await TestAllAsync(btnTestAll);
            var hint = new Label
            {
                Text = "手动测试不要求大盘预警或 CS2 更新提醒开启，也不消耗规则冷却。",
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            row.Controls.Add(btnTestAll);
            row.Controls.Add(hint);
            row.Layout += (_, __) =>
            {
                btnTestAll.SetBounds(0, UIUtils.S(9), btnTestAll.Width, btnTestAll.Height);
                hint.SetBounds(btnTestAll.Right + UIUtils.S(14), UIUtils.S(9), row.Width - btnTestAll.Right - UIUtils.S(14), btnTestAll.Height);
            };
            return row;
        }

        private LiteSettingsGroup CreateAdvancedBackupEntryGroup()
        {
            var group = new LiteSettingsGroup("备用通道");
            AddHint(group, "状态：备用通道默认收起。原因：日常只需要 Server酱。下一步：Server酱不可用时再打开这里配置。");
            group.AddFullItem(CreateAdvancedBackupEntryRow());
            return group;
        }

        private Control CreateAdvancedBackupEntryRow()
        {
            var row = new Panel { Height = UIUtils.S(58), BackColor = Color.Transparent };
            var label = new Label
            {
                Text = "Server酱不可用，或你需要其他推送方式时，再打开这里配置备用通道。",
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            var btnOpen = new LiteButton("打开备用通道", false) { Width = UIUtils.S(140), Height = UIUtils.S(34) };
            btnOpen.Click += (_, __) => ShowAdvancedBackupDialog();
            row.Controls.Add(label);
            row.Controls.Add(btnOpen);
            row.Layout += (_, __) =>
            {
                btnOpen.SetBounds(row.Width - btnOpen.Width, UIUtils.S(12), btnOpen.Width, btnOpen.Height);
                label.SetBounds(0, UIUtils.S(12), Math.Max(1, btnOpen.Left - UIUtils.S(16)), btnOpen.Height);
            };
            return row;
        }

        private void ShowAdvancedBackupDialog()
        {
            PhoneAlertBackupChannelDialog.Show(
                FindForm(),
                new PhoneAlertBackupChannelDialogContext(
                    EnsureChannels,
                    GetOrCreateChannel,
                    _channelStatusLabels,
                    () => _building,
                    SyncLegacyFields,
                    SaveChannels,
                    () => RefreshSummary(),
                    RefreshChannelStatus,
                    TestChannelAsync,
                    OpenHelp,
                    MoveChannel));
        }

        private LiteSettingsGroup CreateChannelGroup(string title, IEnumerable<PhoneAlertChannelType> types)
        {
            var group = new LiteSettingsGroup(title);
            foreach (PhoneAlertChannelType type in types)
            {
                var channel = GetOrCreateChannel(type);
                group.AddFullItem(CreateChannelCard(channel));
            }
            return group;
        }

        private Control CreateChannelCard(PhoneAlertChannelConfig channel)
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
                if (_building)
                    return;
                channel.Enabled = enabled.Checked;
                SyncLegacyFields();
                SaveChannels();
                RefreshChannelStatus(channel);
                RefreshSummary();
            };
            var priority = CreateLabel($"优先级 {channel.Priority}", false);
            priority.TextAlign = ContentAlignment.MiddleRight;
            var btnUp = new LiteButton("↑", false) { Width = UIUtils.S(34), Height = UIUtils.S(28) };
            var btnDown = new LiteButton("↓", false) { Width = UIUtils.S(34), Height = UIUtils.S(28) };
            btnUp.Click += (_, __) => MoveChannel(channel, -1);
            btnDown.Click += (_, __) => MoveChannel(channel, 1);

            var secretLabel = CreateLabel(definition.SecretLabel, false);
            var secretInput = new LiteUnderlineInput(channel.Secret, "", "", 300, null, HorizontalAlignment.Left);
            secretInput.Inner.UseSystemPasswordChar = true;
            secretInput.Inner.TextChanged += (_, __) =>
            {
                if (_building)
                    return;
                channel.Secret = secretInput.Inner.Text.Trim();
                SyncLegacyFields();
                SaveChannels();
                RefreshChannelStatus(channel);
                RefreshSummary();
            };

            Label? serverLabel = null;
            LiteUnderlineInput? serverInput = null;
            if (needServer)
            {
                serverLabel = CreateLabel(definition.ServerLabel, false);
                serverInput = new LiteUnderlineInput(channel.ServerUrl, "", "", 360, null, HorizontalAlignment.Left);
                serverInput.Inner.TextChanged += (_, __) =>
                {
                    if (_building)
                        return;
                    channel.ServerUrl = serverInput.Inner.Text.Trim();
                    SaveChannels();
                    RefreshChannelStatus(channel);
                    RefreshSummary();
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
                    if (_building)
                        return;
                    channel.Extra = extraInput.Inner.Text.Trim();
                    SaveChannels();
                    RefreshChannelStatus(channel);
                    RefreshSummary();
                };
            }

            var btnHelp = new LiteButton("打开官网", false) { Width = UIUtils.S(96), Height = UIUtils.S(30) };
            btnHelp.Click += (_, __) => OpenHelp(channel.Type);
            var btnTest = new LiteButton("测试本通道", true) { Width = UIUtils.S(110), Height = UIUtils.S(30) };
            btnTest.Click += async (_, __) => await TestChannelAsync(channel, btnTest);
            var status = new Label
            {
                Text = "",
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _channelStatusLabels[channel.Id] = status;

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
                status.SetBounds(actionLeft, inputTop + UIUtils.S(34), panel.Width - actionLeft, UIUtils.S(28));
            };
            panel.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawLine(pen, 0, panel.Height - 1, panel.Width, panel.Height - 1);
            };
            RefreshChannelStatus(channel);
            return panel;
        }

        private LiteSettingsGroup CreateHelpGroup()
        {
            var group = new LiteSettingsGroup("使用流程");
            AddHint(group, "状态：未绑定时不会发送手机提醒。");
            AddHint(group, "原因：需要 Server酱 SendKey 或备用通道凭据。");
            AddHint(group, "下一步：进行扫码绑定 -> 在接口配置粘贴 SendKey -> 发送测试提醒。");
            return group;
        }

        private async Task TestChannelAsync(PhoneAlertChannelConfig channel, Control sourceButton)
        {
            if (_testBusy)
                return;
            if (!_phoneAlerts.IsChannelConfigured(channel))
            {
                _statusRenderer.ApplyChannelStatus(channel, "未配置完整，先填写必需字段", UIColors.TextWarn);
                RefreshSummary("状态：未绑定。原因：SendKey 未填写或格式不完整。下一步：点击“进行扫码绑定”，再在“接口配置”粘贴 SendKey。");
                return;
            }

            _testBusy = true;
            sourceButton.Enabled = false;
            _statusRenderer.ApplyChannelStatus(channel, "正在发送测试提醒...", UIColors.TextSub);
            try
            {
                var result = await _phoneAlerts.SendChannelAsync(
                    channel,
                    "CS2交易监控测试提醒",
                    $"这是一条来自 {PhoneAlertChannelDefinitionCatalog.Get(channel.Type).Title} 的手机提醒测试。");
                channel.LastTestTime = DateTimeOffset.Now.ToUnixTimeSeconds();
                channel.LastTestResult = result.Success ? "测试成功" : result.Message;
                SaveChannels();
                _statusRenderer.ApplyChannelStatus(channel, PhoneAlertPagePresenter.BuildTestResultStatus(channel, result, _phoneAlerts.MaskSecret(channel)));
                RefreshSummary(result.Success ? "测试提醒已发送，微信收到消息后表示配置成功。" : result.Message);
            }
            finally
            {
                _testBusy = false;
                if (!sourceButton.IsDisposed)
                    sourceButton.Enabled = true;
            }
        }

        private async Task TestAllAsync(Control sourceButton)
        {
            if (Config == null || _testBusy)
                return;
            EnsureChannels();
            var enabled = Channels.Where(channel => channel.Enabled).OrderBy(channel => channel.Priority).ToList();
            if (enabled.Count == 0)
            {
                RefreshSummary("状态：未启用。原因：没有启用的手机提醒通道。下一步：启用 Server酱或配置备用通道。");
                return;
            }

            _testBusy = true;
            sourceButton.Enabled = false;
            RefreshSummary("正在测试全部启用通道...");
            try
            {
                var results = await _phoneAlerts.TestAllEnabledAsync(
                    Config,
                    "CS2交易监控测试提醒",
                    "这是一条手机提醒多通道测试。");
                foreach (var item in results)
                {
                    item.Channel.LastTestTime = DateTimeOffset.Now.ToUnixTimeSeconds();
                    item.Channel.LastTestResult = item.Result.Success ? "测试成功" : item.Result.Message;
                    _statusRenderer.ApplyChannelStatus(item.Channel, PhoneAlertPagePresenter.BuildTestResultStatus(item.Channel, item.Result, _phoneAlerts.MaskSecret(item.Channel)));
                }
                SaveChannels();
                int ok = results.Count(item => item.Result.Success);
                int fail = results.Count - ok;
                RefreshSummary($"测试完成：成功 {ok} 个，失败 {fail} 个。");
            }
            finally
            {
                _testBusy = false;
                if (!sourceButton.IsDisposed)
                    sourceButton.Enabled = true;
            }
        }

        private void MoveChannel(PhoneAlertChannelConfig channel, int direction)
        {
            if (Channels.Count <= 1)
                return;
            var list = Channels.OrderBy(item => item.Priority).ToList();
            int index = list.FindIndex(item => item.Id == channel.Id);
            int target = index + direction;
            if (index < 0 || target < 0 || target >= list.Count)
                return;
            (list[index].Priority, list[target].Priority) = (list[target].Priority, list[index].Priority);
            list = list.OrderBy(item => item.Priority).ToList();
            for (int i = 0; i < list.Count; i++)
                list[i].Priority = i + 1;
            Set(nameof(Settings.PhoneAlertChannels), list);
            SyncLegacyFields();
            BuildPage(force: true);
            RefreshSummary();
        }

        private void OpenHelp(PhoneAlertChannelType type)
        {
            string url = _phoneAlerts.GetHelpUrl(type);
            if (string.IsNullOrWhiteSpace(url))
            {
                RefreshSummary("自定义 Webhook 没有固定官网，请填写你自己的接口地址。");
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                RefreshSummary("已打开：" + PhoneAlertChannelDefinitionCatalog.Get(type).Title);
            }
            catch
            {
                RefreshSummary("打开官网失败，请手动访问：" + url);
            }
        }

        private void RefreshSummary(string? overrideText = null)
        {
            var server = GetOrCreateChannel(PhoneAlertChannelType.ServerChan);
            bool configured = _phoneAlerts.IsChannelConfigured(server);
            bool enabled = Get(nameof(Settings.PhoneAlertEnabled), false);

            PhoneAlertSummaryViewModel view = PhoneAlertPagePresenter.BuildSummary(server, configured, enabled, _phoneAlerts.MaskSecret(server));
            _statusRenderer.ApplySummary(BuildSummaryRenderTargets(), view, enabled, overrideText);
        }

        private void RefreshChannelStatus(PhoneAlertChannelConfig channel)
        {
            bool configured = _phoneAlerts.IsChannelConfigured(channel);
            _statusRenderer.ApplyChannelStatus(channel, PhoneAlertPagePresenter.BuildChannelStatus(channel, configured, _phoneAlerts.MaskSecret(channel)));
            if (channel.Type == PhoneAlertChannelType.ServerChan)
                RefreshSummary();
        }

        private void RefreshSecretConfigStatus(PhoneAlertChannelConfig server)
        {
            bool configured = _phoneAlerts.IsChannelConfigured(server);
            _statusRenderer.ApplySecretConfigStatus(_secretConfigStatusLabel, PhoneAlertPagePresenter.BuildSecretConfigStatus(configured));
        }

        private PhoneAlertSummaryRenderTargets BuildSummaryRenderTargets()
        {
            return new PhoneAlertSummaryRenderTargets(
                _summaryLabel,
                _enabledCheck,
                _bindStatusLabel,
                _enabledStatusLabel,
                _lastSendLabel,
                _failureLabel,
                _secretConfigStatusLabel);
        }

        private void EnsureChannels()
        {
            PhoneAlertChannelCatalog.NormalizeChannels(
                Channels,
                Get(nameof(Settings.ServerChanSendKey), ""),
                Get(nameof(Settings.WxPusherSpt), ""));
            Set(nameof(Settings.PhoneAlertChannels), Channels);
            SyncLegacyFields();
        }

        private PhoneAlertChannelConfig GetOrCreateChannel(PhoneAlertChannelType type)
        {
            return PhoneAlertChannelCatalog.GetOrCreateChannel(
                Channels,
                type,
                Get(nameof(Settings.ServerChanSendKey), ""),
                Get(nameof(Settings.WxPusherSpt), ""));
        }

        private void SyncLegacyFields()
        {
            var legacy = PhoneAlertChannelCatalog.BuildLegacyFields(Channels);
            Set(nameof(Settings.PhoneAlertProvider), ServerChanPushService.ProviderName);
            Set(nameof(Settings.ServerChanSendKey), legacy.ServerChanSendKey);
            Set(nameof(Settings.WxPusherSpt), legacy.WxPusherSpt);
        }

        private void SaveChannels()
        {
            Set(nameof(Settings.PhoneAlertChannels), Channels);
        }

        private List<PhoneAlertChannelConfig> Channels => GetList<PhoneAlertChannelConfig>(nameof(Settings.PhoneAlertChannels));

    }
}
