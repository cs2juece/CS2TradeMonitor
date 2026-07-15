using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.Application.YouPin;
using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.SettingsPage
{
    public sealed class YouPinAuthStatusCard : LiteSettingsGroup
    {
        private readonly Func<Settings?> _getConfig;
        private readonly Action? _credentialChanged;
        private readonly YouPinAuthRuntimeServices _runtimeServices;
        private readonly IYouPinAuthService _authService;
        private readonly Label _stateLabel;
        private readonly Label _statusLabel;
        private readonly LiteButton _btnManage;
        private readonly LiteButton _btnAutomation;
        private readonly LiteButton _btnValidate;
        private readonly LiteButton _btnClear;

        public YouPinAuthStatusCard(Func<Settings?> getConfig, Action? credentialChanged = null, bool showAutomationButton = true)
            : this(getConfig, YouPinAuthRuntimeServices.Resolve(), credentialChanged, showAutomationButton)
        {
        }

        internal YouPinAuthStatusCard(
            Func<Settings?> getConfig,
            YouPinAuthRuntimeServices runtimeServices,
            Action? credentialChanged = null,
            bool showAutomationButton = true)
            : base("悠悠有品登录")
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);

            _getConfig = getConfig;
            _credentialChanged = credentialChanged;
            _runtimeServices = runtimeServices;
            _authService = runtimeServices.Auth;

            var row = new Panel { Height = UIUtils.S(48), Dock = DockStyle.Top, BackColor = Color.Transparent };
            _stateLabel = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextSub,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _btnManage = new LiteButton("登录/管理登录", true) { Width = UIUtils.S(118), Height = UIUtils.S(30) };
            _btnAutomation = new LiteButton("纯收货自动处理", false) { Width = UIUtils.S(146), Height = UIUtils.S(30) };
            _btnValidate = new LiteButton("校验登录", false) { Width = UIUtils.S(92), Height = UIUtils.S(30) };
            _btnClear = new LiteButton("清除凭据", false) { Width = UIUtils.S(92), Height = UIUtils.S(30) };

            _btnManage.Click += (_, __) => OpenDialog();
            _btnAutomation.Click += (_, __) =>
            {
                OpenAutomationDialog(FindForm(), _getConfig, _credentialChanged, _runtimeServices);
                RefreshState();
            };
            _btnValidate.Click += async (_, __) => await ValidateAsync();
            _btnClear.Click += (_, __) => ClearCredential();

            row.Controls.Add(_stateLabel);
            row.Controls.Add(_btnManage);
            row.Controls.Add(_btnAutomation);
            row.Controls.Add(_btnValidate);
            row.Controls.Add(_btnClear);
            row.Layout += (_, __) =>
            {
                int gap = UIUtils.S(10);
                int mid = row.Height / 2;
                int right = row.ClientSize.Width;
                _btnManage.SetBounds(Math.Max(0, right - _btnManage.Width), mid - _btnManage.Height / 2, _btnManage.Width, _btnManage.Height);

                int nextRight = _btnManage.Left - gap;
                bool showAutomation = showAutomationButton && nextRight >= _btnAutomation.Width + UIUtils.S(160);
                _btnAutomation.Visible = showAutomation;
                if (showAutomation)
                {
                    _btnAutomation.SetBounds(nextRight - _btnAutomation.Width, mid - _btnAutomation.Height / 2, _btnAutomation.Width, _btnAutomation.Height);
                    nextRight = _btnAutomation.Left - gap;
                }

                bool showValidate = nextRight >= _btnValidate.Width + UIUtils.S(120);
                _btnValidate.Visible = showValidate;
                if (showValidate)
                {
                    _btnValidate.SetBounds(nextRight - _btnValidate.Width, mid - _btnValidate.Height / 2, _btnValidate.Width, _btnValidate.Height);
                    nextRight = _btnValidate.Left - gap;
                }

                bool showClear = showValidate && nextRight >= _btnClear.Width + UIUtils.S(120);
                _btnClear.Visible = showClear;
                if (showClear)
                    _btnClear.SetBounds(nextRight - _btnClear.Width, mid - _btnClear.Height / 2, _btnClear.Width, _btnClear.Height);

                int stateRight = showClear ? _btnClear.Left : showValidate ? _btnValidate.Left : showAutomation ? _btnAutomation.Left : _btnManage.Left;
                _stateLabel.SetBounds(0, 0, Math.Max(1, stateRight - gap), row.Height);
            };
            row.Paint += PaintBottomLine;
            AddFullItem(row);

            _statusLabel = new Label
            {
                AutoSize = false,
                Height = UIUtils.S(24),
                Dock = DockStyle.Top,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = UIColors.TextSub,
                TextAlign = ContentAlignment.MiddleLeft
            };
            AddFullItem(_statusLabel);
            RefreshState();
        }

        internal static void OpenAutomationDialog(
            IWin32Window? owner,
            Func<Settings?> getConfig,
            Action? credentialChanged,
            YouPinAuthRuntimeServices runtimeServices)
        {
            using var dialog = new YouPinAutoDeliveryDialog(getConfig, () =>
            {
                credentialChanged?.Invoke();
            }, runtimeServices);
            dialog.ShowDialog(owner);
        }

        public void RefreshState()
        {
            var state = _authService.GetState(_getConfig());
            _btnValidate.Enabled = state.HasCredential;
            _btnClear.Enabled = state.HasCredential;

            if (state.HasCredential)
            {
                string saved = state.SavedAt == default ? "" : "  保存时间：" + state.SavedAt.ToString("yyyy-MM-dd HH:mm:ss");
                string stateText = string.IsNullOrWhiteSpace(state.Error) ? "已登录" : "异常";
                _stateLabel.Text = $"登录状态：{stateText}  登录用户：{state.NickName}  来源：{state.Source}{saved}";
                _stateLabel.ForeColor = string.IsNullOrWhiteSpace(state.Error) ? Color.FromArgb(0, 150, 80) : UIColors.TextWarn;
                return;
            }

            _stateLabel.Text = "登录状态：未登录。点击“登录/管理登录”使用短信登录或填写高级备用凭据。";
            _stateLabel.ForeColor = UIColors.TextSub;
        }

        private void OpenDialog()
        {
            using var dialog = new YouPinAuthDialog(_getConfig, _runtimeServices, () =>
            {
                _credentialChanged?.Invoke();
                RefreshState();
            });
            dialog.ShowDialog(FindForm());
            RefreshState();
        }

        private async Task ValidateAsync()
        {
            SetBusy(true);
            SetStatus("正在校验当前悠悠有品登录...", warn: false);
            try
            {
                var result = await _authService.ValidateCurrentAsync(_getConfig());
                SetStatus(result.Message, warn: !result.Ok);
            }
            finally
            {
                SetBusy(false);
                RefreshState();
            }
        }

        private void ClearCredential()
        {
            if (GlobalPromptService.Show(FindForm(), "确认清除本机保存的悠悠有品登录凭据？", "悠悠有品登录", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _authService.ClearCredential(_getConfig());
            SetStatus("已清除本机保存的悠悠有品登录凭据。", warn: false);
            _credentialChanged?.Invoke();
            RefreshState();
        }

        private void SetBusy(bool busy)
        {
            _btnManage.Enabled = !busy;
            _btnAutomation.Enabled = !busy;
            var hasCredential = _authService.GetState(_getConfig()).HasCredential;
            _btnValidate.Enabled = !busy && hasCredential;
            _btnClear.Enabled = !busy && hasCredential;
        }

        private void SetStatus(string text, bool warn)
        {
            _statusLabel.Text = text;
            _statusLabel.ForeColor = warn ? UIColors.TextWarn : UIColors.TextSub;
        }

        private static void PaintBottomLine(object? sender, PaintEventArgs e)
        {
            if (sender is not Control control) return;
            using var pen = new Pen(UIColors.Border);
            e.Graphics.DrawLine(pen, 0, control.Height - 1, control.Width, control.Height - 1);
        }

        private sealed class YouPinAutoDeliveryDialog : Form
        {
            private readonly Func<Settings?> _getConfig;
            private readonly Action? _settingsChanged;
            private readonly LiteCheck _autoCheck;
            private readonly LiteCheck _autoAccept;
            private readonly LiteCheck _allowYouPinVerified;
            private readonly LiteNumberInput _intervalInput;
            private readonly Label _statusLabel;
            private readonly Label _youPinStatusLabel;
            private readonly LiteButton _btnSaveStart;
            private readonly LiteButton _btnStop;
            private readonly LiteButton _btnAcceptNow;
            private readonly LiteButton _btnRefreshYouPin;
            private readonly ISteamOfferService _steamOffers;
            private readonly IYouPinSaleReminderService _youPinSaleReminders;

            public YouPinAutoDeliveryDialog(
                Func<Settings?> getConfig,
                Action? settingsChanged,
                YouPinAuthRuntimeServices runtimeServices)
            {
                ArgumentNullException.ThrowIfNull(runtimeServices);

                _getConfig = getConfig;
                _settingsChanged = settingsChanged;
                _steamOffers = runtimeServices.SteamOffers;
                _youPinSaleReminders = runtimeServices.YouPinSaleReminders;

                Text = "纯收货报价自动处理";
                StartPosition = FormStartPosition.CenterParent;
                Size = new Size(UIUtils.S(880), UIUtils.S(640));
                MinimumSize = new Size(UIUtils.S(820), UIUtils.S(580));
                BackColor = UIColors.MainBg;
                ForeColor = UIColors.TextMain;
                Font = new Font("Microsoft YaHei UI", 9F);

                var settings = LoadSettings();

                var root = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    BackColor = UIColors.MainBg,
                    Padding = UIUtils.S(new Padding(18)),
                    ColumnCount = 1,
                    RowCount = 5
                };
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(72)));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(244)));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(42)));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(44)));
                Controls.Add(root);

                var intro = new Label
                {
                    Dock = DockStyle.Fill,
                    AutoSize = false,
                    ForeColor = UIColors.TextSub,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Text = "用于 Steam 纯收货报价的后台检查和一键同意。只处理我方不转出任何物品的收货报价；悠悠平台的自动发货/出租开关仍以手机端为准，本软件不修改平台开关。"
                };
                root.Controls.Add(intro, 0, 0);

                var options = CreateCard("后台处理规则");
                root.Controls.Add(options, 0, 1);

                _autoCheck = new LiteCheck(settings.SteamOfferAutoCheck, "后台检查 Steam 报价");
                _autoAccept = new LiteCheck(settings.SteamOfferAutoAccept, "自动同意纯收货报价");
                _allowYouPinVerified = new LiteCheck(settings.SteamOfferAllowYouPinVerifiedAccept, "悠悠已校验报价需手动处理") { Enabled = false, Visible = false };
                _intervalInput = new LiteNumberInput(Math.Max(30, settings.SteamOfferAutoCheckSec).ToString(), "秒", "", 100);
                _btnSaveStart = new LiteButton("保存并应用", true) { Width = UIUtils.S(124), Height = UIUtils.S(32) };
                _btnStop = new LiteButton("停止后台", false) { Width = UIUtils.S(112), Height = UIUtils.S(32) };
                _btnAcceptNow = new LiteButton("立即处理纯收货报价", true) { Width = UIUtils.S(170), Height = UIUtils.S(32) };
                _btnRefreshYouPin = new LiteButton("读取悠悠状态", false) { Width = UIUtils.S(126), Height = UIUtils.S(32) };

                _btnSaveStart.Click += (_, __) => SaveAndStart();
                _btnStop.Click += (_, __) => StopAuto();
                _btnAcceptNow.Click += async (_, __) => await AcceptNowAsync();
                _btnRefreshYouPin.Click += async (_, __) => await RefreshYouPinAsync();

                var optionGrid = new TableLayoutPanel
                {
                    Dock = DockStyle.None,
                    BackColor = Color.Transparent,
                    Padding = UIUtils.S(new Padding(18, 10, 18, 8)),
                    ColumnCount = 2,
                    RowCount = 5
                };
                optionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                optionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                optionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(34)));
                optionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(34)));
                optionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(44)));
                optionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(38)));
                optionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(42)));
                options.Controls.Add(optionGrid);

                optionGrid.Controls.Add(_autoCheck, 0, 0);
                optionGrid.Controls.Add(_autoAccept, 1, 0);
                optionGrid.Controls.Add(_allowYouPinVerified, 0, 1);
                optionGrid.Controls.Add(BuildInline("间隔", _intervalInput), 1, 1);

                var buttonRow = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.Transparent,
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = false,
                    Padding = Padding.Empty,
                    Margin = Padding.Empty
                };
                buttonRow.Controls.Add(_btnSaveStart);
                buttonRow.Controls.Add(_btnStop);
                buttonRow.Controls.Add(_btnAcceptNow);
                buttonRow.Controls.Add(_btnRefreshYouPin);
                foreach (Control button in buttonRow.Controls)
                    button.Margin = UIUtils.S(new Padding(0, 4, 18, 4));
                optionGrid.SetColumnSpan(buttonRow, 2);
                optionGrid.Controls.Add(buttonRow, 0, 2);

                var note = new Label
                {
                    Dock = DockStyle.Fill,
                    AutoSize = false,
                    ForeColor = UIColors.TextSub,
                    Text = "开启“自动同意”时会自动启用后台检查。只处理纯收货报价；任何会转出库存的报价始终需要人工处理。",
                    TextAlign = ContentAlignment.MiddleLeft
                };
                optionGrid.SetColumnSpan(note, 2);
                optionGrid.Controls.Add(note, 0, 3);

                _youPinStatusLabel = new Label
                {
                    Dock = DockStyle.Fill,
                    AutoSize = false,
                    AutoEllipsis = true,
                    ForeColor = UIColors.TextSub,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                optionGrid.SetColumnSpan(_youPinStatusLabel, 2);
                optionGrid.Controls.Add(_youPinStatusLabel, 0, 4);
                RefreshYouPinStatus();

                var featureCard = CreateCard("功能边界");
                root.Controls.Add(featureCard, 0, 2);
                var featureList = new TableLayoutPanel
                {
                    Dock = DockStyle.None,
                    AutoSize = true,
                    BackColor = Color.Transparent,
                    Padding = UIUtils.S(new Padding(18, 12, 18, 8)),
                    ColumnCount = 2
                };
                featureList.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UIUtils.S(210)));
                featureList.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                featureCard.Controls.Add(featureList);
                AddFeatureRow(featureList, "纯收货报价", "可自动处理：只收货、不转出库存，风险最低。", Color.FromArgb(0, 170, 90));
                AddFeatureRow(featureList, "悠悠已校验报价", "只做展示和核对：涉及转出库存时仍需打开 Steam 手动处理。", UIColors.TextWarn);
                AddFeatureRow(featureList, "待发货/报价状态", "只读诊断：抓包接口可读取待办、待发货、报价状态，用于核对订单。", UIColors.TextSub);
                AddFeatureRow(featureList, "自动发货/出租开关", "不在本机修改：平台开关和真实发货流程仍以悠悠手机端为准。", UIColors.TextWarn);

                _statusLabel = new Label
                {
                    Dock = DockStyle.Fill,
                    AutoSize = false,
                    ForeColor = UIColors.TextSub,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Text = BuildCurrentStatus(settings)
                };
                root.Controls.Add(_statusLabel, 0, 3);

                var bottom = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
                var btnClose = new LiteButton("关闭", false) { Width = UIUtils.S(100), Height = UIUtils.S(32) };
                btnClose.Click += (_, __) => Close();
                bottom.Controls.Add(btnClose);
                bottom.Layout += (_, __) =>
                    btnClose.SetBounds(bottom.ClientSize.Width - btnClose.Width, UIUtils.S(6), btnClose.Width, btnClose.Height);
                root.Controls.Add(bottom, 0, 4);
            }

            private Settings LoadSettings()
            {
                // 设置页卡片优先使用当前草稿；独立使用该控件时才回退到持久化配置。
                return _getConfig() ?? Settings.Load();
            }

            private void SaveAndStart()
            {
                var settings = LoadSettings();
                int interval = Math.Clamp(_intervalInput.ValueInt <= 0 ? 180 : _intervalInput.ValueInt, 30, 3600);
                _intervalInput.Inner.Text = interval.ToString();

                if (_autoAccept.Checked && !_autoCheck.Checked)
                    _autoCheck.Checked = true;

                settings.SteamOfferAutoCheck = _autoCheck.Checked;
                settings.SteamOfferAutoAccept = _autoAccept.Checked;
                settings.SteamOfferAllowYouPinVerifiedAccept = _allowYouPinVerified.Checked;
                settings.SteamOfferAutoCheckSec = interval;
                settings.Save();

                if (settings.SteamOfferAutoCheck)
                    _steamOffers.StartAutoConfirm(interval, settings.SteamOfferAutoAccept, settings.SteamOfferAllowYouPinVerifiedAccept);
                else
                    _steamOffers.StopAutoConfirm();

                _settingsChanged?.Invoke();
                SetStatus(settings.SteamOfferAutoCheck ? "已保存并应用后台处理规则。只会处理纯收货报价。" : "已保存设置，后台检查未启用。", warn: false);
            }

            private void StopAuto()
            {
                var settings = LoadSettings();
                settings.SteamOfferAutoCheck = false;
                settings.SteamOfferAutoAccept = false;
                settings.Save();
                _autoCheck.Checked = false;
                _autoAccept.Checked = false;

                _steamOffers.StopAutoConfirm();
                _settingsChanged?.Invoke();
                SetStatus("已停止后台检查和自动同意。", warn: false);
            }

            private async Task RefreshYouPinAsync()
            {
                SetBusy(true);
                SetStatus("正在读取悠悠待办、待发货和报价状态...", warn: false);
                try
                {
                    var result = await _youPinSaleReminders.CheckTodoNowAsync(useMock: false, notify: false);
                    RefreshYouPinStatus();
                    SetStatus(result.Message, warn: !result.Ok && !result.Skipped);
                }
                finally
                {
                    SetBusy(false);
                }
            }

            private async Task AcceptNowAsync()
            {
                SetBusy(true);
                SetStatus("正在刷新 Steam 报价...", warn: false);
                try
                {
                    var load = await _steamOffers.LoadOffersAsync(useMock: false);
                    if (!load.Ok)
                    {
                        SetStatus(load.Message, warn: true);
                        return;
                    }

                    SetStatus("正在同意纯收货报价...", warn: false);
                    var result = await _steamOffers.AcceptSafeOffersAsync(_allowYouPinVerified.Checked);
                    SetStatus(result.Message, warn: !result.Ok);
                }
                finally
                {
                    SetBusy(false);
                }
            }

            private void SetBusy(bool busy)
            {
                _btnSaveStart.Enabled = !busy;
                _btnStop.Enabled = !busy;
                _btnAcceptNow.Enabled = !busy;
                _btnRefreshYouPin.Enabled = !busy;
            }

            private void SetStatus(string text, bool warn)
            {
                _statusLabel.Text = text;
                _statusLabel.ForeColor = warn ? UIColors.TextWarn : UIColors.TextSub;
            }

            private static string BuildCurrentStatus(Settings settings)
            {
                string run = settings.SteamOfferAutoCheck ? "后台检查：已启用" : "后台检查：未启用";
                string accept = settings.SteamOfferAutoAccept ? "自动同意：已启用" : "自动同意：未启用";
                return $"{run}，{accept}，转出库存：手动处理，间隔 {Math.Max(30, settings.SteamOfferAutoCheckSec)} 秒。";
            }

            private void RefreshYouPinStatus()
            {
                var state = _youPinSaleReminders.GetState();
                string status = string.IsNullOrWhiteSpace(state.LastAutoDeliveryStatus)
                    ? "未检查"
                    : state.LastAutoDeliveryStatus.Trim();

                if (state.LastAutoDeliveryCheck != default && state.LastAutoDeliveryCheck != DateTime.MinValue)
                    status += " 上次读取：" + state.LastAutoDeliveryCheck.ToString("MM-dd HH:mm:ss");
                if (!string.IsNullOrWhiteSpace(state.LastAutoDeliveryError))
                    status += "；错误：" + state.LastAutoDeliveryError.Trim();

                _youPinStatusLabel.Text = "悠悠状态：" + status;
                _youPinStatusLabel.ForeColor = string.IsNullOrWhiteSpace(state.LastAutoDeliveryError) ? UIColors.TextSub : UIColors.TextWarn;
            }

            private static Panel CreateCard(string title)
            {
                var panel = new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = UIColors.CardBg,
                    Padding = UIUtils.S(new Padding(1))
                };
                var header = new Label
                {
                    Name = "CardHeader",
                    AutoSize = false,
                    Height = UIUtils.S(36),
                    Padding = UIUtils.S(new Padding(14, 0, 14, 0)),
                    BackColor = UIColors.CardBg,
                    ForeColor = UIColors.TextMain,
                    Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                    Text = title,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                header.Paint += PaintBottomLine;
                panel.Controls.Add(header);
                panel.Layout += (_, __) =>
                {
                    int headerHeight = UIUtils.S(36);
                    header.SetBounds(0, 0, panel.ClientSize.Width, headerHeight);
                    foreach (Control child in panel.Controls.Cast<Control>().Where(control => control != header))
                    {
                        child.SetBounds(
                            UIUtils.S(1),
                            headerHeight + UIUtils.S(1),
                            Math.Max(1, panel.ClientSize.Width - UIUtils.S(2)),
                            Math.Max(1, panel.ClientSize.Height - headerHeight - UIUtils.S(2)));
                    }
                };
                return panel;
            }

            private static Panel BuildInline(string label, Control input)
            {
                var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
                var lbl = new Label
                {
                    AutoSize = false,
                    ForeColor = UIColors.TextSub,
                    Text = label,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                panel.Controls.Add(lbl);
                panel.Controls.Add(input);
                panel.Layout += (_, __) =>
                {
                    int labelWidth = UIUtils.S(42);
                    int gap = UIUtils.S(8);
                    int inputWidth = Math.Min(input.Width, Math.Max(UIUtils.S(64), panel.ClientSize.Width - labelWidth - gap));
                    lbl.SetBounds(0, 0, labelWidth, panel.Height);
                    input.SetBounds(panel.ClientSize.Width - inputWidth, UIUtils.S(2), inputWidth, input.Height);
                };
                return panel;
            }

            private static void AddFeatureRow(TableLayoutPanel grid, string name, string status, Color statusColor)
            {
                int row = grid.RowCount++;
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(34)));
                var nameLabel = new Label
                {
                    Dock = DockStyle.Fill,
                    AutoSize = false,
                    ForeColor = UIColors.TextMain,
                    Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                    Text = name,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                var statusLabel = new Label
                {
                    Dock = DockStyle.Fill,
                    AutoSize = false,
                    ForeColor = statusColor,
                    Text = status,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                grid.Controls.Add(nameLabel, 0, row);
                grid.Controls.Add(statusLabel, 1, row);
            }
        }
    }
}
