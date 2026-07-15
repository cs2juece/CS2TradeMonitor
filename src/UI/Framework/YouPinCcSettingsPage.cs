using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.SettingsPage;
using AntdSwitch = AntdUI.Switch;

namespace CS2TradeMonitor.src.UI.Framework
{
    // 「悠悠有品」新主入口设置页。严格对齐审核通过的 Widget：
    // 仅一张「悠悠报价」卡 = 消息提醒（启用 / 提醒方式默认仅气泡 / 检查间隔 / 立即检查·测试提醒）
    // + 报价自动刷新。自动处理已迁移到独立「悠悠自动报价」标签。
    // 不改动共享的 YouPinSaleReminderPage(SettingsOnly)，因此线上「悠悠有品」设置页不受影响。
    public sealed class YouPinCcSettingsPage : FrameworkSettingsPageBase
    {
        private readonly YouPinSaleReminderPagePresenter _presenter;
        private LiteButton? _checkButton;
        private LiteButton? _testButton;
        private Label? _checkStatusLabel;
        private AntdSwitch? _reminderSwitch;
        private LiteComboBox? _notificationModeCombo;
        private LiteNumberInput? _intervalInput;
        private AntdSwitch? _quoteRefreshSwitch;
        private LiteNumberInput? _quoteRefreshIntervalInput;

        public YouPinCcSettingsPage()
            : this(YouPinPageRuntimeServices.Resolve())
        {
        }

        internal YouPinCcSettingsPage(YouPinPageRuntimeServices runtimeServices)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);
            _presenter = new YouPinSaleReminderPagePresenter(runtimeServices);
            BuildPage();
        }

        public override void Activate()
        {
            ConfigureService();
            base.Activate();
        }

        private void ConfigureService()
        {
            if (Config != null)
                _presenter.Configure(Config);
        }

        private void BuildPage()
        {
            ClearPage();
            Container.Padding = FrameworkSettingsPageLayoutHelper.CreateDefaultPagePadding();

            var card = new YouPinCcRoundedPanel
            {
                Height = UIUtils.S(324),
                Padding = UIUtils.S(new Padding(28, 26, 28, 24))
            };

            var title = YouPinCcUi.Label("悠悠报价", 11F, FontStyle.Bold);
            var reminderTitle = YouPinCcUi.Label("消息提醒", 9.5F);
            var reminderHint = YouPinCcUi.Label("仅在有需要发送报价的订单时提醒", 8.8F, FontStyle.Regular, UIColors.TextSub);
            var modeLabel = YouPinCcUi.Label("提醒方式", 9.5F, FontStyle.Regular, UIColors.TextSub);
            var intervalLabel = YouPinCcUi.Label("检查间隔", 9.5F, FontStyle.Regular, UIColors.TextSub);
            var quoteRefreshTitle = YouPinCcUi.Label("报价自动刷新", 11F, FontStyle.Bold);
            var quoteRefreshHint = YouPinCcUi.Label("默认每 180 秒同步待发货/报价处理列表，不会自动发送或确认报价。", 8.8F, FontStyle.Regular, UIColors.TextSub);
            var quoteRefreshIntervalLabel = YouPinCcUi.Label("刷新间隔", 9.5F, FontStyle.Regular, UIColors.TextSub);
            var quoteDivider = new Panel { Height = 1, BackColor = UIColors.Border };

            _reminderSwitch = CreateBoundSwitch(nameof(Settings.YouPinSaleReminderEnabled), false, _ => ConfigureService());
            _notificationModeCombo = CreateNotificationModeCombo(nameof(Settings.YouPinSaleReminderNotificationMode));
            _intervalInput = CreateBoundInterval(nameof(Settings.YouPinSaleReminderRefreshSec), 180, _ => ConfigureService());
            _quoteRefreshSwitch = CreateBoundSwitch(nameof(Settings.YouPinQuoteAutoRefreshEnabled), false, _ => ConfigureService());
            _quoteRefreshIntervalInput = CreateBoundInterval(nameof(Settings.YouPinQuoteAutoRefreshSec), 180, _ => ConfigureService());

            _checkButton = new LiteButton("立即检查", false) { Width = UIUtils.S(92), Height = UIUtils.S(34) };
            _testButton = new LiteButton("测试提醒", false) { Width = UIUtils.S(92), Height = UIUtils.S(34) };
            _checkStatusLabel = YouPinCcUi.Label("未检查", 9F, FontStyle.Regular, UIColors.TextSub);
            _checkButton.Click += async (_, __) => await RunCheckAsync(useMock: false);
            _testButton.Click += async (_, __) => await RunCheckAsync(useMock: true);

            card.Controls.Add(title);
            card.Controls.Add(reminderTitle);
            card.Controls.Add(reminderHint);
            card.Controls.Add(modeLabel);
            card.Controls.Add(intervalLabel);
            card.Controls.Add(_reminderSwitch);
            card.Controls.Add(_notificationModeCombo);
            card.Controls.Add(_intervalInput);
            card.Controls.Add(_checkButton);
            card.Controls.Add(_testButton);
            card.Controls.Add(_checkStatusLabel);
            card.Controls.Add(quoteDivider);
            card.Controls.Add(quoteRefreshTitle);
            card.Controls.Add(quoteRefreshHint);
            card.Controls.Add(quoteRefreshIntervalLabel);
            card.Controls.Add(_quoteRefreshSwitch);
            card.Controls.Add(_quoteRefreshIntervalInput);

            card.Layout += (_, __) =>
            {
                int padL = UIUtils.S(28);
                int padR = UIUtils.S(28);
                int right = card.Width - padR;
                title.SetBounds(padL, UIUtils.S(25), UIUtils.S(220), UIUtils.S(30));
                _reminderSwitch.SetBounds(right - _reminderSwitch.Width, UIUtils.S(66), _reminderSwitch.Width, _reminderSwitch.Height);
                reminderTitle.SetBounds(padL, UIUtils.S(67), UIUtils.S(220), UIUtils.S(26));
                reminderHint.SetBounds(padL, UIUtils.S(94), Math.Max(1, right - padL - UIUtils.S(120)), UIUtils.S(24));

                _notificationModeCombo.SetBounds(Math.Max(padL, right - UIUtils.S(260)), UIUtils.S(127), UIUtils.S(260), UIUtils.S(36));
                modeLabel.SetBounds(padL, UIUtils.S(128), UIUtils.S(180), UIUtils.S(34));
                _intervalInput.SetBounds(Math.Max(padL, right - UIUtils.S(96)), UIUtils.S(174), UIUtils.S(96), UIUtils.S(36));
                intervalLabel.SetBounds(padL, UIUtils.S(175), UIUtils.S(180), UIUtils.S(34));

                _checkButton.SetBounds(padL, UIUtils.S(218), _checkButton.Width, _checkButton.Height);
                _testButton.SetBounds(_checkButton.Right + UIUtils.S(10), UIUtils.S(218), _testButton.Width, _testButton.Height);
                _checkStatusLabel.SetBounds(_testButton.Right + UIUtils.S(12), UIUtils.S(218), Math.Max(1, right - _testButton.Right - UIUtils.S(12)), UIUtils.S(34));

                quoteDivider.SetBounds(padL, UIUtils.S(266), Math.Max(1, right - padL), 1);
                _quoteRefreshSwitch.SetBounds(right - _quoteRefreshSwitch.Width, UIUtils.S(288), _quoteRefreshSwitch.Width, _quoteRefreshSwitch.Height);
                quoteRefreshTitle.SetBounds(padL, UIUtils.S(283), UIUtils.S(240), UIUtils.S(28));
                quoteRefreshHint.SetBounds(padL, UIUtils.S(312), Math.Max(1, right - padL - UIUtils.S(80)), UIUtils.S(26));
                _quoteRefreshIntervalInput.SetBounds(Math.Max(padL, right - UIUtils.S(96)), UIUtils.S(345), UIUtils.S(96), UIUtils.S(36));
                quoteRefreshIntervalLabel.SetBounds(padL, UIUtils.S(346), UIUtils.S(180), UIUtils.S(34));
            };

            card.Height = UIUtils.S(410);
            YouPinCcUi.AddTopCard(Container, card);
        }

        private LiteComboBox CreateNotificationModeCombo(string settingKey)
        {
            var combo = new LiteComboBox { Width = UIUtils.S(180) };
            foreach (var option in YouPinSaleReminderPageModel.NotificationModeOptions)
                combo.AddItem(option.Text, ((int)option.Mode).ToString());
            combo.Inner.SelectedIndexChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                if (YouPinSaleReminderPageModel.TryParseNotificationMode(combo.SelectedValue, out var mode))
                {
                    Set(settingKey, mode);
                    ConfigureService();
                }
            };
            RegisterRefresh(() =>
            {
                var mode = YouPinSaleReminderPageModel.NormalizeNotificationMode(
                    Get(settingKey, YouPinSaleReminderNotificationMode.Bubble));
                combo.SelectValue(((int)mode).ToString());
            });
            RegisterSave(() =>
            {
                if (YouPinSaleReminderPageModel.TryParseNotificationMode(combo.SelectedValue, out var mode))
                    Set(settingKey, mode);
            });
            return combo;
        }

        private AntdSwitch CreateBoundSwitch(string settingKey, bool fallback, Action<bool>? afterChanged)
        {
            var sw = new AntdSwitch
            {
                Checked = Get(settingKey, fallback),
                Size = UIUtils.S(new Size(46, 24))
            };
            sw.CheckedChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;
                Set(settingKey, sw.Checked);
                afterChanged?.Invoke(sw.Checked);
            };
            RegisterRefresh(() => sw.Checked = Get(settingKey, fallback));
            RegisterSave(() => Set(settingKey, sw.Checked));
            return sw;
        }

        private LiteNumberInput CreateBoundInterval(string settingKey, int fallback, Action<int>? afterChanged)
        {
            var input = new LiteNumberInput(Get(settingKey, fallback).ToString(), "秒", "", 76)
            {
                Padding = UIUtils.S(new Padding(0, 5, 0, 1))
            };
            input.Inner.TextChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                if (int.TryParse(input.Inner.Text, out int value))
                {
                    int normalized = Math.Max(30, value);
                    Set(settingKey, normalized);
                    afterChanged?.Invoke(normalized);
                }
            };
            RegisterRefresh(() => input.Inner.Text = Math.Max(30, Get(settingKey, fallback)).ToString());
            RegisterSave(() =>
            {
                if (int.TryParse(input.Inner.Text, out int value))
                    Set(settingKey, Math.Max(30, value));
            });
            return input;
        }

        private async Task RunCheckAsync(bool useMock)
        {
            if (Config == null)
                return;

            SetButtonsEnabled(false);
            SetCheckStatus(useMock ? "正在发送测试提醒…" : "正在检查待发报价…", ok: true);
            try
            {
                ConfigureService();
                await _presenter.CheckTodoNowAsync(useMock: useMock, notify: true);
                SetCheckStatus(useMock ? "测试提醒已发送" : "已检查", ok: true);
            }
            catch (Exception ex)
            {
                SetCheckStatus("失败：" + ex.Message, ok: false);
            }
            finally
            {
                SetButtonsEnabled(true);
            }
        }

        private void SetButtonsEnabled(bool enabled)
        {
            if (_checkButton != null)
                _checkButton.Enabled = enabled;
            if (_testButton != null)
                _testButton.Enabled = enabled;
        }

        private void SetCheckStatus(string text, bool ok)
        {
            if (_checkStatusLabel == null)
                return;
            _checkStatusLabel.Text = text;
            _checkStatusLabel.ForeColor = ok ? UIColors.TextSub : UIColors.TextWarn;
        }
    }
}
