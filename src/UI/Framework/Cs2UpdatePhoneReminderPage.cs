using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Notify;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    public sealed class Cs2UpdatePhoneReminderHostPage : FrameworkSettingsHostPage<Cs2UpdatePhoneReminderPage>
    {
        public Cs2UpdatePhoneReminderHostPage()
            : base(new Cs2UpdatePhoneReminderPage(SystemPageRuntimeServices.Resolve(), YouPinPageRuntimeServices.Resolve()))
        {
        }
    }

    public sealed class Cs2UpdatePhoneReminderPage : FrameworkSettingsPageBase
    {
        private readonly ICs2UpdateReminderService _updateReminder;
        private readonly IPhoneAlertDispatchService _phoneAlerts;
        private readonly Dictionary<string, Label> _channelStatusLabels = new();
        private readonly PhoneAlertPageStatusRenderer _statusRenderer;

        private Panel? _content;
        private Panel? _header;
        private YouPinCcRoundedPanel? _overviewCard;
        private YouPinCcRoundedPanel? _updateCard;
        private YouPinCcRoundedPanel? _phoneCard;
        private YouPinCcRoundedPanel? _flowCard;
        private YouPinCcRoundedPanel? _historyCard;
        private YouPinCcRoundedPanel? _backupCard;

        private Label? _titleLabel;
        private Label? _subtitleLabel;
        private StatusPillLabel? _cs2Pill;
        private StatusPillLabel? _phonePill;

        private readonly Label[] _overviewCaptions = new Label[4];
        private readonly Label[] _overviewValues = new Label[4];
        private readonly Label[] _overviewDetails = new Label[4];

        private LiteNumberInput? _intervalInput;
        private LiteCheck? _wechatCheck;
        private LiteCheck? _soundCheck;
        private Label? _reminderScopeLabel;
        private Label? _baselineInlineLabel;
        private LiteButton? _checkButton;
        private LiteButton? _resetButton;

        private StatusPillLabel? _sendKeyPill;
        private LiteUnderlineInput? _sendKeyInput;
        private LiteButton? _scanButton;
        private LiteButton? _testMainButton;
        private LiteButton? _openHelpButton;
        private Label? _sendHealthLabel;

        private Label? _readStrategyLabel;
        private Label? _latestTitleLabel;
        private Label? _latestTimeLabel;
        private Label? _lastCheckHistoryLabel;
        private Label? _lastCheckIntervalLabel;

        private Label? _backupEnabledLabel;
        private LiteButton? _manageBackupButton;
        private LiteButton? _testBackupButton;

        private bool _building;
        private bool _busy;
        private bool _testBusy;

        public Cs2UpdatePhoneReminderPage()
            : this(SystemPageRuntimeServices.Resolve(), YouPinPageRuntimeServices.Resolve())
        {
        }

        internal Cs2UpdatePhoneReminderPage(
            SystemPageRuntimeServices systemServices,
            YouPinPageRuntimeServices youPinServices)
        {
            ArgumentNullException.ThrowIfNull(systemServices);
            ArgumentNullException.ThrowIfNull(youPinServices);

            _updateReminder = systemServices.Cs2UpdateReminder;
            _phoneAlerts = youPinServices.PhoneAlerts;
            _statusRenderer = new PhoneAlertPageStatusRenderer(_channelStatusLabels);
            Container.Padding = FrameworkSettingsPageLayoutHelper.CreateDefaultPagePadding();
            Container.SizeChanged += (_, __) => LayoutContent();
            BuildPage();
        }

        protected override void OnStoreAttached()
        {
            EnsureChannels();
            RefreshView();
            LayoutContent();
        }

        public override void Activate()
        {
            base.Activate();
            EnsureChannels();
            RefreshView();
            LayoutContent();
        }

        public override void ApplySystemTheme()
        {
            base.ApplySystemTheme();
            FrameworkSettingsPageLayoutHelper.RefreshTheme(Container);
            RefreshView();
            Invalidate(true);
        }

        public override void RequestViewportRelayout()
        {
            base.RequestViewportRelayout();
            LayoutContent();
        }

        private void BuildPage()
        {
            ClearPage();
            _building = true;
            try
            {
                Rectangle initialBounds = FrameworkSettingsPageLayoutHelper.CalculateDefaultContentBounds(Container);
                _content = new Panel
                {
                    BackColor = Color.Transparent,
                    Location = new Point(initialBounds.Left, initialBounds.Top)
                };
                Container.Controls.Add(_content);

                _header = CreateHeader();
                _overviewCard = CreateOverviewCard();
                _updateCard = CreateUpdateCard();
                _phoneCard = CreatePhoneCard();
                _flowCard = CreateFlowCard();
                _historyCard = CreateHistoryCard();
                _backupCard = CreateBackupCard();

                _content.Controls.Add(_header);
                _content.Controls.Add(_overviewCard);
                _content.Controls.Add(_updateCard);
                _content.Controls.Add(_phoneCard);
                _content.Controls.Add(_flowCard);
                _content.Controls.Add(_historyCard);
                _content.Controls.Add(_backupCard);

                RegisterRefresh(() =>
                {
                    RefreshInputsFromStore();
                    RefreshView();
                });
                RegisterSave(SaveInteractiveState);
                UIColors.ApplyNativeThemeRecursively(this);
            }
            finally
            {
                _building = false;
            }

            LayoutContent();
        }

        private Panel CreateHeader()
        {
            var header = new Panel { BackColor = Color.Transparent };
            _titleLabel = CreateTextLabel("更新与手机提醒", 15.5F, FontStyle.Bold, UIColors.TextMain);
            _subtitleLabel = CreateTextLabel("把 CS2 更新检测和手机送达合并到一个入口：更新负责触发，手机通道负责送达。", 9F, FontStyle.Regular, UIColors.TextSub);
            _cs2Pill = new StatusPillLabel { Width = UIUtils.S(158), Height = UIUtils.S(30) };
            _phonePill = new StatusPillLabel { Width = UIUtils.S(138), Height = UIUtils.S(30) };
            header.Controls.Add(_titleLabel);
            header.Controls.Add(_subtitleLabel);
            header.Controls.Add(_cs2Pill);
            header.Controls.Add(_phonePill);
            header.Layout += (_, __) => LayoutHeader(header);
            return header;
        }

        private YouPinCcRoundedPanel CreateOverviewCard()
        {
            var card = CreateCard();
            string[] captions = { "当前状态", "最近检查", "手机通道", "备用入口" };
            for (int i = 0; i < captions.Length; i++)
            {
                _overviewCaptions[i] = CreateTextLabel(captions[i], 8F, FontStyle.Regular, UIColors.TextSub);
                _overviewValues[i] = CreateTextLabel("", 12F, FontStyle.Bold, UIColors.TextMain);
                _overviewDetails[i] = CreateTextLabel("", 8F, FontStyle.Regular, UIColors.TextSub);
                card.Controls.Add(_overviewCaptions[i]);
                card.Controls.Add(_overviewValues[i]);
                card.Controls.Add(_overviewDetails[i]);
            }

            card.Layout += (_, __) => LayoutOverview(card);
            card.Paint += (_, e) => PaintOverviewSeparators(card, e.Graphics);
            return card;
        }

        private YouPinCcRoundedPanel CreateUpdateCard()
        {
            var card = CreateCard();
            var title = CreateTextLabel("CS2 更新检测", 11F, FontStyle.Bold, UIColors.TextMain);
            _checkButton = new LiteButton("立即检查", true) { Width = UIUtils.S(112), Height = UIUtils.S(34) };
            _checkButton.Click += async (_, __) => await RunCheckAsync(_checkButton, false);

            var intervalLabel = CreateTextLabel("检测间隔", 9F, FontStyle.Bold, UIColors.TextMain);
            _intervalInput = new LiteNumberInput("600", "秒", "", 92);
            _intervalInput.Inner.TextChanged += (_, __) =>
            {
                if (_building || IsUpdatingControls)
                    return;

                if (int.TryParse(_intervalInput.Inner.Text, out int value))
                {
                    int normalized = Cs2UpdatePhoneReminderPageModel.NormalizeInterval(value);
                    Set(nameof(Settings.Cs2UpdateReminderRefreshSec), normalized);
                    _updateReminder.ResetSchedule();
                    RefreshView();
                }
            };

            var remindLabel = CreateTextLabel("提醒方式", 9F, FontStyle.Bold, UIColors.TextMain);
            _wechatCheck = new LiteCheck(Get(nameof(Settings.Cs2UpdateReminderWechatEnabled), true), "手机提醒");
            _wechatCheck.CheckedChanged += (_, __) =>
            {
                if (_building || IsUpdatingControls)
                    return;

                Set(nameof(Settings.Cs2UpdateReminderWechatEnabled), _wechatCheck.Checked);
                RefreshView();
            };
            _soundCheck = new LiteCheck(Get(nameof(Settings.Cs2UpdateReminderSoundEnabled), false), "电脑提示音");
            _soundCheck.CheckedChanged += (_, __) =>
            {
                if (_building || IsUpdatingControls)
                    return;

                Set(nameof(Settings.Cs2UpdateReminderSoundEnabled), _soundCheck.Checked);
                RefreshView();
            };
            var scopeCaption = CreateTextLabel("提醒范围", 9F, FontStyle.Bold, UIColors.TextSub);
            _reminderScopeLabel = CreateTextLabel("发现 CS2 更新时提醒；主通道失败后尝试备用入口。", 9F, FontStyle.Regular, UIColors.TextMain);
            var baselineCaption = CreateTextLabel("当前基准", 9F, FontStyle.Bold, UIColors.TextSub);
            _baselineInlineLabel = CreateTextLabel("", 9F, FontStyle.Regular, UIColors.TextMain);
            _resetButton = new LiteButton("重置基准", false) { Width = UIUtils.S(104), Height = UIUtils.S(34) };
            _resetButton.Click += async (_, __) => await RunCheckAsync(_resetButton, true);

            card.Controls.Add(title);
            card.Controls.Add(_checkButton);
            card.Controls.Add(intervalLabel);
            card.Controls.Add(_intervalInput);
            card.Controls.Add(remindLabel);
            card.Controls.Add(_wechatCheck);
            card.Controls.Add(_soundCheck);
            card.Controls.Add(scopeCaption);
            card.Controls.Add(_reminderScopeLabel);
            card.Controls.Add(baselineCaption);
            card.Controls.Add(_baselineInlineLabel);
            card.Controls.Add(_resetButton);
            card.Layout += (_, __) =>
            {
                int pad = UIUtils.S(22);
                int y = UIUtils.S(20);
                title.SetBounds(pad, y + UIUtils.S(2), Math.Max(1, card.Width - pad * 2 - _checkButton.Width - UIUtils.S(12)), UIUtils.S(26));
                _checkButton.SetBounds(card.Width - pad - _checkButton.Width, y, _checkButton.Width, _checkButton.Height);
                y = UIUtils.S(70);
                intervalLabel.SetBounds(pad, y + UIUtils.S(3), UIUtils.S(92), UIUtils.S(28));
                _intervalInput.SetBounds(pad + UIUtils.S(108), y, UIUtils.S(92), UIUtils.S(28));
                y += UIUtils.S(52);
                remindLabel.SetBounds(pad, y + UIUtils.S(2), UIUtils.S(92), UIUtils.S(28));
                _wechatCheck.SetBounds(pad + UIUtils.S(108), y + UIUtils.S(2), UIUtils.S(104), UIUtils.S(26));
                _soundCheck.SetBounds(pad + UIUtils.S(232), y + UIUtils.S(2), UIUtils.S(120), UIUtils.S(26));
                y += UIUtils.S(52);
                scopeCaption.SetBounds(pad, y, UIUtils.S(92), UIUtils.S(26));
                _reminderScopeLabel.SetBounds(pad + UIUtils.S(108), y, Math.Max(1, card.Width - pad * 2 - UIUtils.S(108)), UIUtils.S(26));
                y += UIUtils.S(52);
                baselineCaption.SetBounds(pad, y + UIUtils.S(2), UIUtils.S(92), UIUtils.S(30));
                _resetButton.SetBounds(card.Width - pad - _resetButton.Width, y - UIUtils.S(2), _resetButton.Width, _resetButton.Height);
                _baselineInlineLabel.SetBounds(pad + UIUtils.S(108), y + UIUtils.S(2), Math.Max(1, _resetButton.Left - pad - UIUtils.S(122)), UIUtils.S(30));
            };
            card.Paint += (_, e) => PaintCardDivider(e.Graphics, card.Width, UIUtils.S(56));
            return card;
        }

        private YouPinCcRoundedPanel CreatePhoneCard()
        {
            var card = CreateCard();
            var title = CreateTextLabel("手机主通道", 11F, FontStyle.Bold, UIColors.TextMain);
            _sendKeyPill = new StatusPillLabel { Width = UIUtils.S(118), Height = UIUtils.S(24) };
            var keyLabel = CreateTextLabel("Server 酱 SendKey", 9F, FontStyle.Bold, UIColors.TextMain);
            _sendKeyInput = new LiteUnderlineInput("", "", "", 420, null, HorizontalAlignment.Left);
            _sendKeyInput.Inner.UseSystemPasswordChar = true;
            _sendKeyInput.Placeholder = "扫码绑定后粘贴 SendKey，保存后仅显示掩码";
            _sendKeyInput.Inner.TextChanged += (_, __) =>
            {
                if (_building || IsUpdatingControls)
                    return;

                var server = GetOrCreateChannel(PhoneAlertChannelType.ServerChan);
                server.Secret = _sendKeyInput.Inner.Text.Trim();
                server.Enabled = !string.IsNullOrWhiteSpace(server.Secret);
                Set(nameof(Settings.PhoneAlertEnabled), server.Enabled);
                Set(nameof(Settings.PhoneAlertDispatchMode), PhoneAlertDispatchMode.Failover);
                SyncLegacyFields();
                SaveChannels();
                RefreshView();
            };

            _scanButton = new LiteButton("扫码绑定", true) { Width = UIUtils.S(108), Height = UIUtils.S(36) };
            _scanButton.Click += (_, __) => OpenHelp(PhoneAlertChannelType.ServerChan);
            _testMainButton = new LiteButton("测试主通道", false) { Width = UIUtils.S(120), Height = UIUtils.S(36) };
            _testMainButton.Click += async (_, __) => await TestChannelAsync(GetOrCreateChannel(PhoneAlertChannelType.ServerChan), _testMainButton);
            _openHelpButton = new LiteButton("打开官网", false) { Width = UIUtils.S(112), Height = UIUtils.S(36) };
            _openHelpButton.Click += (_, __) => OpenHelp(PhoneAlertChannelType.ServerChan);
            var healthCaption = CreateTextLabel("发送健康", 9F, FontStyle.Regular, UIColors.TextSub);
            _sendHealthLabel = CreateTextLabel("", 9F, FontStyle.Bold, UIColors.TextWarn);

            card.Controls.Add(title);
            card.Controls.Add(_sendKeyPill);
            card.Controls.Add(keyLabel);
            card.Controls.Add(_sendKeyInput);
            card.Controls.Add(_scanButton);
            card.Controls.Add(_testMainButton);
            card.Controls.Add(_openHelpButton);
            card.Controls.Add(healthCaption);
            card.Controls.Add(_sendHealthLabel);
            card.Layout += (_, __) =>
            {
                int pad = UIUtils.S(22);
                int y = UIUtils.S(20);
                title.SetBounds(pad, y + UIUtils.S(1), UIUtils.S(170), UIUtils.S(28));
                _sendKeyPill.SetBounds(title.Right + UIUtils.S(12), y + UIUtils.S(2), _sendKeyPill.Width, _sendKeyPill.Height);
                y = UIUtils.S(76);
                keyLabel.SetBounds(pad, y, card.Width - pad * 2, UIUtils.S(24));
                y += UIUtils.S(30);
                _sendKeyInput.SetBounds(pad, y, Math.Max(1, card.Width - pad * 2), UIUtils.S(30));
                y += UIUtils.S(52);
                _scanButton.SetBounds(pad, y, _scanButton.Width, _scanButton.Height);
                _testMainButton.SetBounds(_scanButton.Right + UIUtils.S(12), y, _testMainButton.Width, _testMainButton.Height);
                _openHelpButton.SetBounds(_testMainButton.Right + UIUtils.S(12), y, _openHelpButton.Width, _openHelpButton.Height);
                y += UIUtils.S(76);
                healthCaption.SetBounds(pad, y, UIUtils.S(92), UIUtils.S(28));
                _sendHealthLabel.SetBounds(pad + UIUtils.S(104), y, Math.Max(1, card.Width - pad * 2 - UIUtils.S(104)), UIUtils.S(28));
            };
            card.Paint += (_, e) =>
            {
                PaintCardDivider(e.Graphics, card.Width, UIUtils.S(56));
                PaintCardDivider(e.Graphics, card.Width, UIUtils.S(214));
            };
            return card;
        }

        private YouPinCcRoundedPanel CreateFlowCard()
        {
            var card = CreateCard();
            var title = CreateTextLabel("配置路径", 10F, FontStyle.Bold, UIColors.TextMain);
            card.Controls.Add(title);
            var steps = new[]
            {
                ("1", "设置更新检测", "间隔、提示音、提醒范围"),
                ("2", "绑定手机通道", "扫码并粘贴 SendKey"),
                ("3", "发送测试", "验证手机能收到提醒"),
                ("4", "备用入口", "需要时进入管理")
            };
            var badges = new List<StepBadgeLabel>();
            var titles = new List<Label>();
            var details = new List<Label>();
            foreach (var step in steps)
            {
                var badge = new StepBadgeLabel(step.Item1, step.Item1 == "1");
                var stepTitle = CreateTextLabel(step.Item2, 8.5F, FontStyle.Bold, UIColors.TextMain);
                var detail = CreateTextLabel(step.Item3, 8F, FontStyle.Regular, UIColors.TextSub);
                badges.Add(badge);
                titles.Add(stepTitle);
                details.Add(detail);
                card.Controls.Add(badge);
                card.Controls.Add(stepTitle);
                card.Controls.Add(detail);
            }

            card.Layout += (_, __) =>
            {
                int pad = UIUtils.S(22);
                bool compact = card.Width < UIUtils.S(760);
                title.SetBounds(pad, UIUtils.S(20), UIUtils.S(98), UIUtils.S(28));
                int left = compact ? pad : UIUtils.S(132);
                int top = compact ? UIUtils.S(54) : UIUtils.S(18);
                int columns = compact ? 2 : 4;
                int rowGap = UIUtils.S(52);
                int available = Math.Max(1, card.Width - left - pad);
                int stepW = Math.Max(UIUtils.S(150), (available - UIUtils.S(18) * (columns - 1)) / columns);
                for (int i = 0; i < badges.Count; i++)
                {
                    int row = compact ? i / columns : 0;
                    int col = compact ? i % columns : i;
                    int x = left + col * (stepW + UIUtils.S(18));
                    int y = top + row * rowGap;
                    badges[i].SetBounds(x, y, UIUtils.S(34), UIUtils.S(34));
                    titles[i].SetBounds(x + UIUtils.S(46), y - UIUtils.S(1), Math.Max(1, stepW - UIUtils.S(46)), UIUtils.S(20));
                    details[i].SetBounds(x + UIUtils.S(46), y + UIUtils.S(20), Math.Max(1, stepW - UIUtils.S(46)), UIUtils.S(18));
                }
            };
            card.Paint += (_, e) => PaintFlowConnectors(card, e.Graphics);
            return card;
        }

        private YouPinCcRoundedPanel CreateHistoryCard()
        {
            var card = CreateCard();
            var title = CreateTextLabel("历史记录与读取策略", 11F, FontStyle.Bold, UIColors.TextMain);
            var source = new StatusPillLabel { Width = UIUtils.S(78), Height = UIUtils.S(24) };
            source.Apply("公开来源", Cs2UpdatePhoneReminderTone.Primary);
            var readCaption = CreateTextLabel("读取策略", 8.5F, FontStyle.Bold, UIColors.TextSub);
            _readStrategyLabel = CreateTextLabel("", 9F, FontStyle.Regular, UIColors.TextMain);
            var latestCaption = CreateTextLabel("最新记录", 8.5F, FontStyle.Bold, UIColors.TextSub);
            _latestTitleLabel = CreateTextLabel("", 9F, FontStyle.Bold, UIColors.TextMain);
            _latestTimeLabel = CreateTextLabel("", 8.5F, FontStyle.Regular, UIColors.TextSub);
            var checkCaption = CreateTextLabel("上次检查", 8.5F, FontStyle.Bold, UIColors.TextSub);
            _lastCheckHistoryLabel = CreateTextLabel("", 9F, FontStyle.Regular, UIColors.TextMain);
            _lastCheckIntervalLabel = CreateTextLabel("", 8.5F, FontStyle.Regular, UIColors.TextSub);

            card.Controls.Add(title);
            card.Controls.Add(source);
            card.Controls.Add(readCaption);
            card.Controls.Add(_readStrategyLabel);
            card.Controls.Add(latestCaption);
            card.Controls.Add(_latestTitleLabel);
            card.Controls.Add(_latestTimeLabel);
            card.Controls.Add(checkCaption);
            card.Controls.Add(_lastCheckHistoryLabel);
            card.Controls.Add(_lastCheckIntervalLabel);
            card.Layout += (_, __) =>
            {
                int pad = UIUtils.S(22);
                title.SetBounds(pad, UIUtils.S(22), UIUtils.S(180), UIUtils.S(28));
                source.SetBounds(title.Right + UIUtils.S(10), UIUtils.S(24), source.Width, source.Height);
                int captionW = UIUtils.S(112);
                int y = UIUtils.S(74);
                readCaption.SetBounds(pad, y, captionW, UIUtils.S(28));
                _readStrategyLabel.SetBounds(pad + captionW + UIUtils.S(10), y, Math.Max(1, card.Width - pad * 2 - captionW - UIUtils.S(10)), UIUtils.S(28));
                y += UIUtils.S(56);
                latestCaption.SetBounds(pad, y, captionW, UIUtils.S(28));
                _latestTitleLabel.SetBounds(pad + captionW + UIUtils.S(10), y - UIUtils.S(2), Math.Max(1, card.Width - pad * 2 - captionW - UIUtils.S(10)), UIUtils.S(24));
                _latestTimeLabel.SetBounds(pad + captionW + UIUtils.S(10), y + UIUtils.S(22), Math.Max(1, card.Width - pad * 2 - captionW - UIUtils.S(10)), UIUtils.S(22));
                y += UIUtils.S(58);
                checkCaption.SetBounds(pad, y, captionW, UIUtils.S(28));
                _lastCheckHistoryLabel.SetBounds(pad + captionW + UIUtils.S(10), y, UIUtils.S(200), UIUtils.S(28));
                _lastCheckIntervalLabel.SetBounds(pad + captionW + UIUtils.S(230), y, Math.Max(1, card.Width - pad * 2 - captionW - UIUtils.S(230)), UIUtils.S(28));
            };
            card.Paint += (_, e) =>
            {
                PaintCardDivider(e.Graphics, card.Width, UIUtils.S(56));
                PaintCardDivider(e.Graphics, card.Width, UIUtils.S(116));
                PaintCardDivider(e.Graphics, card.Width, UIUtils.S(172));
            };
            return card;
        }

        private YouPinCcRoundedPanel CreateBackupCard()
        {
            var card = CreateCard();
            var title = CreateTextLabel("备用通道入口", 11F, FontStyle.Bold, UIColors.TextMain);
            var pill = new StatusPillLabel { Width = UIUtils.S(80), Height = UIUtils.S(24) };
            pill.Apply("常态收起", Cs2UpdatePhoneReminderTone.Primary);
            var badge = new StepBadgeLabel("6", false);
            var manageTitle = CreateTextLabel("备用通道管理", 10F, FontStyle.Bold, UIColors.TextMain);
            var desc = CreateTextLabel("6 个通道收进二级入口，仅配置或排查时打开。", 8.5F, FontStyle.Regular, UIColors.TextSub);
            var enabledCaption = CreateTextLabel("当前启用", 8.5F, FontStyle.Bold, UIColors.TextSub);
            _backupEnabledLabel = CreateTextLabel("", 12F, FontStyle.Bold, UIColors.Primary);
            var triggerCaption = CreateTextLabel("触发", 8.5F, FontStyle.Bold, UIColors.TextSub);
            var triggerValue = CreateTextLabel("主通道失败时", 8.5F, FontStyle.Bold, UIColors.TextMain);
            _manageBackupButton = new LiteButton("管理备用通道", true) { Width = UIUtils.S(140), Height = UIUtils.S(36) };
            _manageBackupButton.Click += (_, __) => ShowAdvancedBackupDialog();
            _testBackupButton = new LiteButton("测试备用链路", false) { Width = UIUtils.S(132), Height = UIUtils.S(36) };
            _testBackupButton.Click += async (_, __) => await TestAllAsync(_testBackupButton);

            card.Controls.Add(title);
            card.Controls.Add(pill);
            card.Controls.Add(badge);
            card.Controls.Add(manageTitle);
            card.Controls.Add(desc);
            card.Controls.Add(enabledCaption);
            card.Controls.Add(_backupEnabledLabel);
            card.Controls.Add(triggerCaption);
            card.Controls.Add(triggerValue);
            card.Controls.Add(_manageBackupButton);
            card.Controls.Add(_testBackupButton);
            card.Layout += (_, __) =>
            {
                int pad = UIUtils.S(22);
                title.SetBounds(pad, UIUtils.S(22), UIUtils.S(150), UIUtils.S(28));
                pill.SetBounds(title.Right + UIUtils.S(10), UIUtils.S(24), pill.Width, pill.Height);
                badge.SetBounds(pad + UIUtils.S(10), UIUtils.S(84), UIUtils.S(38), UIUtils.S(38));
                manageTitle.SetBounds(badge.Right + UIUtils.S(18), UIUtils.S(80), Math.Max(1, card.Width - badge.Right - pad - UIUtils.S(18)), UIUtils.S(24));
                desc.SetBounds(badge.Right + UIUtils.S(18), UIUtils.S(104), Math.Max(1, card.Width - badge.Right - pad - UIUtils.S(18)), UIUtils.S(22));
                enabledCaption.SetBounds(pad, UIUtils.S(142), UIUtils.S(82), UIUtils.S(24));
                _backupEnabledLabel.SetBounds(pad + UIUtils.S(94), UIUtils.S(138), UIUtils.S(74), UIUtils.S(30));
                triggerCaption.SetBounds(pad + UIUtils.S(188), UIUtils.S(142), UIUtils.S(48), UIUtils.S(24));
                triggerValue.SetBounds(pad + UIUtils.S(238), UIUtils.S(142), Math.Max(1, card.Width - pad - UIUtils.S(238)), UIUtils.S(24));
                int buttonTop = card.Height - UIUtils.S(48);
                _manageBackupButton.SetBounds(pad, buttonTop, _manageBackupButton.Width, _manageBackupButton.Height);
                _testBackupButton.SetBounds(_manageBackupButton.Right + UIUtils.S(14), buttonTop, _testBackupButton.Width, _testBackupButton.Height);
            };
            card.Paint += (_, e) => PaintCardDivider(e.Graphics, card.Width, UIUtils.S(56));
            return card;
        }

        private void LayoutContent()
        {
            if (_content == null
                || _header == null
                || _overviewCard == null
                || _updateCard == null
                || _phoneCard == null
                || _flowCard == null
                || _historyCard == null
                || _backupCard == null)
            {
                return;
            }

            Rectangle bounds = FrameworkSettingsPageLayoutHelper.CalculateDefaultContentBounds(Container);
            int width = bounds.Width;
            Cs2UpdatePhoneReminderLayout layout = Cs2UpdatePhoneReminderPageModel.BuildLayout(width);
            _content.SetBounds(bounds.Left, bounds.Top, width, layout.TotalHeight);
            _header.Bounds = layout.Header;
            _overviewCard.Bounds = layout.Overview;
            _updateCard.Bounds = layout.UpdateCard;
            _phoneCard.Bounds = layout.PhoneCard;
            _flowCard.Bounds = layout.FlowCard;
            _historyCard.Bounds = layout.HistoryCard;
            _backupCard.Bounds = layout.BackupCard;
            Container.AutoScrollMinSize = new Size(0, layout.TotalHeight + Container.Padding.Vertical);
            HideHorizontalScroll(Container);
            _content.PerformLayout();
        }

        private void LayoutHeader(Panel header)
        {
            if (_titleLabel == null || _subtitleLabel == null || _cs2Pill == null || _phonePill == null)
                return;

            int gap = UIUtils.S(12);
            if (header.Width >= UIUtils.S(760))
            {
                _phonePill.SetBounds(header.Width - _phonePill.Width, UIUtils.S(8), _phonePill.Width, _phonePill.Height);
                _cs2Pill.SetBounds(_phonePill.Left - gap - _cs2Pill.Width, UIUtils.S(8), _cs2Pill.Width, _cs2Pill.Height);
                int textW = Math.Max(1, _cs2Pill.Left - UIUtils.S(24));
                _titleLabel.SetBounds(0, UIUtils.S(6), textW, UIUtils.S(28));
                _subtitleLabel.SetBounds(0, UIUtils.S(36), textW, UIUtils.S(24));
            }
            else
            {
                _titleLabel.SetBounds(0, UIUtils.S(2), header.Width, UIUtils.S(28));
                _subtitleLabel.SetBounds(0, UIUtils.S(30), header.Width, UIUtils.S(22));
                _cs2Pill.SetBounds(0, UIUtils.S(54), _cs2Pill.Width, _cs2Pill.Height);
                _phonePill.SetBounds(_cs2Pill.Right + gap, UIUtils.S(54), _phonePill.Width, _phonePill.Height);
            }
        }

        private void LayoutOverview(Control card)
        {
            bool compact = card.Width < UIUtils.S(760);
            int padX = UIUtils.S(22);
            int padY = UIUtils.S(22);
            int columns = compact ? 2 : 4;
            int rows = compact ? 2 : 1;
            int gapX = compact ? UIUtils.S(12) : 0;
            int cellW = Math.Max(1, (card.Width - padX * 2 - gapX * (columns - 1)) / columns);
            int cellH = Math.Max(1, (card.Height - padY * 2) / rows);
            for (int i = 0; i < 4; i++)
            {
                int row = compact ? i / columns : 0;
                int col = compact ? i % columns : i;
                int x = padX + col * (cellW + gapX);
                int y = padY + row * cellH;
                _overviewCaptions[i].SetBounds(x, y, cellW, UIUtils.S(20));
                _overviewValues[i].SetBounds(x, y + UIUtils.S(22), cellW, UIUtils.S(30));
                _overviewDetails[i].SetBounds(x, y + UIUtils.S(56), cellW, UIUtils.S(22));
            }
        }

        private void RefreshInputsFromStore()
        {
            RunWithUpdateGuard(() =>
            {
                if (_intervalInput != null)
                    _intervalInput.Inner.Text = Cs2UpdatePhoneReminderPageModel.NormalizeInterval(Get(nameof(Settings.Cs2UpdateReminderRefreshSec), 600)).ToString();
                if (_wechatCheck != null)
                    _wechatCheck.Checked = Get(nameof(Settings.Cs2UpdateReminderWechatEnabled), true);
                if (_soundCheck != null)
                    _soundCheck.Checked = Get(nameof(Settings.Cs2UpdateReminderSoundEnabled), false);
                if (_sendKeyInput != null)
                    _sendKeyInput.Inner.Text = GetOrCreateChannel(PhoneAlertChannelType.ServerChan).Secret;
            });
        }

        private void SaveInteractiveState()
        {
            if (_intervalInput != null && int.TryParse(_intervalInput.Inner.Text, out int interval))
                Set(nameof(Settings.Cs2UpdateReminderRefreshSec), Cs2UpdatePhoneReminderPageModel.NormalizeInterval(interval));
            if (_wechatCheck != null)
                Set(nameof(Settings.Cs2UpdateReminderWechatEnabled), _wechatCheck.Checked);
            if (_soundCheck != null)
                Set(nameof(Settings.Cs2UpdateReminderSoundEnabled), _soundCheck.Checked);
            if (_sendKeyInput != null)
            {
                var server = GetOrCreateChannel(PhoneAlertChannelType.ServerChan);
                server.Secret = _sendKeyInput.Inner.Text.Trim();
                SyncLegacyFields();
                SaveChannels();
            }
        }

        private void RefreshView()
        {
            if (Config == null)
                return;

            EnsureChannels();
            var server = GetOrCreateChannel(PhoneAlertChannelType.ServerChan);
            bool serverConfigured = _phoneAlerts.IsChannelConfigured(server);
            var view = Cs2UpdatePhoneReminderPageModel.BuildView(
                Config,
                _updateReminder.LastResult,
                _updateReminder.RecentItems,
                server,
                serverConfigured,
                _phoneAlerts.MaskSecret(server));

            _cs2Pill?.Apply(view.Cs2Pill.Text, view.Cs2Pill.Tone);
            _phonePill?.Apply(view.PhonePill.Text, view.PhonePill.Tone);

            ApplyOverviewBlock(0, view.CurrentStatus);
            ApplyOverviewBlock(1, view.LastCheck);
            ApplyOverviewBlock(2, view.PhoneChannel);
            ApplyOverviewBlock(3, view.BackupEntry);

            if (_sendKeyPill != null)
                _sendKeyPill.Apply(serverConfigured ? "SendKey 已填写" : "SendKey 未填写", serverConfigured ? Cs2UpdatePhoneReminderTone.Positive : Cs2UpdatePhoneReminderTone.Warning);
            if (_baselineInlineLabel != null)
                _baselineInlineLabel.Text = view.BaselineTitle == "暂无基准"
                    ? "暂无基准"
                    : $"{view.BaselineTitle}  {view.BaselineTime}";
            if (_sendHealthLabel != null)
            {
                _sendHealthLabel.Text = view.SendHealth;
                _sendHealthLabel.ForeColor = Cs2UpdatePhoneReminderPageModel.ResolveToneColor(view.SendHealthTone);
            }
            if (_readStrategyLabel != null)
                _readStrategyLabel.Text = view.ReadStrategy;
            if (_latestTitleLabel != null)
                _latestTitleLabel.Text = view.LatestTitle;
            if (_latestTimeLabel != null)
                _latestTimeLabel.Text = view.LatestTime;
            if (_lastCheckHistoryLabel != null)
                _lastCheckHistoryLabel.Text = view.LastCheck.Value.Text;
            if (_lastCheckIntervalLabel != null)
                _lastCheckIntervalLabel.Text = view.LastCheck.Detail;
            if (_backupEnabledLabel != null)
                _backupEnabledLabel.Text = $"{view.EnabledBackupCount} / {view.TotalBackupCount}";

            _statusRenderer.ApplyChannelStatus(server, PhoneAlertPagePresenter.BuildChannelStatus(server, serverConfigured, _phoneAlerts.MaskSecret(server)));
        }

        private void ApplyOverviewBlock(int index, Cs2UpdatePhoneReminderStatusBlock block)
        {
            _overviewCaptions[index].Text = block.Caption;
            _overviewValues[index].Text = block.Value.Text;
            _overviewValues[index].ForeColor = Cs2UpdatePhoneReminderPageModel.ResolveToneColor(block.Value.Tone);
            _overviewDetails[index].Text = block.Detail;
        }

        private async Task RunCheckAsync(LiteButton button, bool resetBaseline)
        {
            if (_busy || Config == null)
                return;

            Save();
            _busy = true;
            button.Enabled = false;
            string oldText = button.Text;
            button.Text = resetBaseline ? "重置中..." : "检查中...";
            try
            {
                var result = await _updateReminder.ManualCheckAsync(Config, resetBaseline);
                Set(nameof(Settings.Cs2UpdateBaselineKey), Config.Cs2UpdateBaselineKey);
                Set(nameof(Settings.Cs2UpdateBaselineTitle), Config.Cs2UpdateBaselineTitle);
                Set(nameof(Settings.Cs2UpdateBaselinePublishedAt), Config.Cs2UpdateBaselinePublishedAt);
                Set(nameof(Settings.Cs2UpdateLastCheckTime), Config.Cs2UpdateLastCheckTime);
                Set(nameof(Settings.Cs2UpdateLastStatus), Config.Cs2UpdateLastStatus);
                RefreshView();
            }
            finally
            {
                button.Text = oldText;
                button.Enabled = true;
                _busy = false;
            }
        }

        private async Task TestChannelAsync(PhoneAlertChannelConfig channel, Control sourceButton)
        {
            if (_testBusy)
                return;

            if (!_phoneAlerts.IsChannelConfigured(channel))
            {
                ApplySendHealth("未配置完整，先填写 SendKey", Cs2UpdatePhoneReminderTone.Warning);
                return;
            }

            _testBusy = true;
            sourceButton.Enabled = false;
            ApplySendHealth("正在发送测试提醒...", Cs2UpdatePhoneReminderTone.Subtle);
            try
            {
                var result = await _phoneAlerts.SendChannelAsync(
                    channel,
                    "CS2交易监控测试提醒",
                    $"这是一条来自 {PhoneAlertChannelDefinitionCatalog.Get(channel.Type).Title} 的手机提醒测试。");
                channel.LastTestTime = DateTimeOffset.Now.ToUnixTimeSeconds();
                channel.LastTestResult = result.Success ? "测试成功" : result.Message;
                SaveChannels();
                ApplySendHealth(result.Success ? "测试成功，主通道可用于推送" : result.Message, result.Success ? Cs2UpdatePhoneReminderTone.Positive : Cs2UpdatePhoneReminderTone.Warning);
                _statusRenderer.ApplyChannelStatus(channel, PhoneAlertPagePresenter.BuildTestResultStatus(channel, result, _phoneAlerts.MaskSecret(channel)));
                RefreshView();
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
                ApplySendHealth("没有启用的备用通道", Cs2UpdatePhoneReminderTone.Warning);
                return;
            }

            _testBusy = true;
            sourceButton.Enabled = false;
            ApplySendHealth("正在测试全部启用通道...", Cs2UpdatePhoneReminderTone.Subtle);
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
                ApplySendHealth($"备用链路测试完成：成功 {ok} 个，失败 {fail} 个。", fail == 0 ? Cs2UpdatePhoneReminderTone.Positive : Cs2UpdatePhoneReminderTone.Warning);
                RefreshView();
            }
            finally
            {
                _testBusy = false;
                if (!sourceButton.IsDisposed)
                    sourceButton.Enabled = true;
            }
        }

        private void ApplySendHealth(string text, Cs2UpdatePhoneReminderTone tone)
        {
            if (_sendHealthLabel == null)
                return;

            _sendHealthLabel.Text = text;
            _sendHealthLabel.ForeColor = Cs2UpdatePhoneReminderPageModel.ResolveToneColor(tone);
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
                    RefreshView,
                    RefreshChannelStatus,
                    TestChannelAsync,
                    OpenHelp,
                    MoveChannel));
        }

        private void RefreshChannelStatus(PhoneAlertChannelConfig channel)
        {
            bool configured = _phoneAlerts.IsChannelConfigured(channel);
            _statusRenderer.ApplyChannelStatus(channel, PhoneAlertPagePresenter.BuildChannelStatus(channel, configured, _phoneAlerts.MaskSecret(channel)));
            RefreshView();
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
            RefreshView();
        }

        private void OpenHelp(PhoneAlertChannelType type)
        {
            string url = _phoneAlerts.GetHelpUrl(type);
            if (string.IsNullOrWhiteSpace(url))
            {
                ApplySendHealth("自定义 Webhook 没有固定官网，请填写自己的接口地址。", Cs2UpdatePhoneReminderTone.Subtle);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                ApplySendHealth("已打开：" + PhoneAlertChannelDefinitionCatalog.Get(type).Title, Cs2UpdatePhoneReminderTone.Subtle);
            }
            catch
            {
                ApplySendHealth("打开官网失败，请手动访问：" + url, Cs2UpdatePhoneReminderTone.Warning);
            }
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

        private static YouPinCcRoundedPanel CreateCard()
        {
            return new YouPinCcRoundedPanel
            {
                Radius = UIUtils.S(6),
                BackColor = Color.Transparent
            };
        }

        private static Label CreateTextLabel(string text, float size, FontStyle style, Color color)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", size, style),
                ForeColor = color,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static void PaintCardDivider(Graphics graphics, int width, int y)
        {
            using var pen = new Pen(UIColors.Border);
            graphics.DrawLine(pen, UIUtils.S(22), y, Math.Max(UIUtils.S(22), width - UIUtils.S(22)), y);
        }

        private static void PaintOverviewSeparators(Control card, Graphics graphics)
        {
            using var pen = new Pen(Color.FromArgb(130, UIColors.Border));
            int padX = UIUtils.S(22);
            if (card.Width < UIUtils.S(760))
            {
                int midX = card.Width / 2;
                int midY = card.Height / 2;
                graphics.DrawLine(pen, midX, UIUtils.S(18), midX, card.Height - UIUtils.S(18));
                graphics.DrawLine(pen, padX, midY, card.Width - padX, midY);
            }
            else
            {
                int cellW = Math.Max(1, (card.Width - padX * 2) / 4);
                for (int i = 1; i < 4; i++)
                {
                    int x = padX + cellW * i;
                    graphics.DrawLine(pen, x, UIUtils.S(24), x, card.Height - UIUtils.S(24));
                }
            }
        }

        private static void PaintFlowConnectors(Control card, Graphics graphics)
        {
            if (card.Width < UIUtils.S(760))
                return;

            using var pen = new Pen(Color.FromArgb(120, UIColors.Border));
            int y = UIUtils.S(35);
            int left = UIUtils.S(250);
            int right = card.Width - UIUtils.S(260);
            if (right > left)
                graphics.DrawLine(pen, left, y, right, y);
        }

        private sealed class StatusPillLabel : Control
        {
            private Cs2UpdatePhoneReminderTone _tone = Cs2UpdatePhoneReminderTone.Subtle;

            public StatusPillLabel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            }

            public void Apply(string text, Cs2UpdatePhoneReminderTone tone)
            {
                Text = text;
                _tone = tone;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Color accent = Cs2UpdatePhoneReminderPageModel.ResolveToneColor(_tone);
                Color fill = _tone switch
                {
                    Cs2UpdatePhoneReminderTone.Positive => Color.FromArgb(34, UIColors.Positive),
                    Cs2UpdatePhoneReminderTone.Warning => Color.FromArgb(38, UIColors.TextWarn),
                    Cs2UpdatePhoneReminderTone.Primary => Color.FromArgb(34, UIColors.Primary),
                    _ => UIColors.ControlBg
                };
                using var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Math.Max(2, Height / 2));
                using var fillBrush = new SolidBrush(fill);
                using var borderPen = new Pen(Color.FromArgb(170, accent));
                e.Graphics.FillPath(fillBrush, path);
                e.Graphics.DrawPath(borderPen, path);
                TextRenderer.DrawText(
                    e.Graphics,
                    Text,
                    Font,
                    new Rectangle(UIUtils.S(8), 0, Math.Max(1, Width - UIUtils.S(16)), Height),
                    accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        private sealed class StepBadgeLabel : Control
        {
            private readonly bool _active;

            public StepBadgeLabel(string text, bool active)
            {
                Text = text;
                _active = active;
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var rect = new Rectangle(1, 1, Width - 3, Height - 3);
                Color fill = _active ? UIColors.Primary : UIColors.ControlBg;
                Color border = _active ? UIColors.Primary : UIColors.Border;
                Color text = _active ? Color.White : UIColors.TextSub;
                using var brush = new SolidBrush(fill);
                using var pen = new Pen(border);
                e.Graphics.FillEllipse(brush, rect);
                e.Graphics.DrawEllipse(pen, rect);
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            int d = radius * 2;
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
