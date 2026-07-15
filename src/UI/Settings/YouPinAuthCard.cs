using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.YouPin;
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.SettingsPage
{
    public sealed class YouPinAuthCard : LiteSettingsGroup
    {
        private readonly Func<Settings?> _getConfig;
        private readonly Action? _credentialChanged;
        private readonly Action? _manualCredentialRequested;
        private readonly IYouPinAuthService _authService;
        private readonly LiteUnderlineInput _phoneInput;
        private readonly LiteUnderlineInput _codeInput;
        private readonly LiteButton _btnSend;
        private readonly LiteButton _btnLogin;
        private readonly LiteButton _btnSmsUpLogin;
        private readonly LiteButton _btnValidate;
        private readonly LiteButton _btnClear;
        private readonly LiteButton? _btnManualCredential;
        private readonly Label _stateLabel;
        private readonly Label _statusLabel;
        private string _sessionId = "";
        private string _phone = "";

        public YouPinAuthCard(Func<Settings?> getConfig, Action? credentialChanged = null, Action? manualCredentialRequested = null)
            : this(getConfig, YouPinAuthRuntimeServices.Resolve(), credentialChanged, manualCredentialRequested)
        {
        }

        internal YouPinAuthCard(
            Func<Settings?> getConfig,
            YouPinAuthRuntimeServices runtimeServices,
            Action? credentialChanged = null,
            Action? manualCredentialRequested = null)
            : base("悠悠有品登录")
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);

            _getConfig = getConfig;
            _credentialChanged = credentialChanged;
            _manualCredentialRequested = manualCredentialRequested;
            _authService = runtimeServices.Auth;

            this.AddHint("使用手机号短信登录后会自动加密保存登录凭据。不会保存账号密码，库存读取和待办提醒会共用该登录态。");

            _stateLabel = new Label
            {
                AutoSize = false,
                Height = UIUtils.S(38),
                Dock = DockStyle.Top,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextSub,
                TextAlign = ContentAlignment.MiddleLeft
            };
            AddFullItem(_stateLabel);

            var phoneRow = new Panel { Height = UIUtils.S(44), Dock = DockStyle.Top, BackColor = Color.Transparent };
            var phoneLabel = CreateRowLabel("手机号");
            _phoneInput = new LiteUnderlineInput("", "", "", 220, null, HorizontalAlignment.Left);
            _phoneInput.Placeholder = "仅支持 +86 手机号";
            _btnSend = new LiteButton("发送验证码", true) { Width = UIUtils.S(110), Height = UIUtils.S(30) };
            _btnSend.Click += async (_, __) => await SendCodeAsync();
            phoneRow.Controls.Add(phoneLabel);
            phoneRow.Controls.Add(_phoneInput);
            phoneRow.Controls.Add(_btnSend);
            phoneRow.Layout += (_, __) =>
            {
                int mid = phoneRow.Height / 2;
                phoneLabel.Location = new Point(0, mid - phoneLabel.Height / 2);
                _phoneInput.Location = new Point(UIUtils.S(150), mid - _phoneInput.Height / 2);
                _phoneInput.Width = Math.Max(UIUtils.S(220), Math.Min(UIUtils.S(360), phoneRow.Width - UIUtils.S(300)));
                _btnSend.Location = new Point(_phoneInput.Right + UIUtils.S(12), mid - _btnSend.Height / 2);
            };
            phoneRow.Paint += PaintBottomLine;
            AddFullItem(phoneRow);

            var codeRow = new Panel { Height = UIUtils.S(44), Dock = DockStyle.Top, BackColor = Color.Transparent };
            var codeLabel = CreateRowLabel("验证码");
            _codeInput = new LiteUnderlineInput("", "", "", 120, null, HorizontalAlignment.Left);
            _codeInput.Placeholder = "短信验证码";
            _btnLogin = new LiteButton("登录并保存", true) { Width = UIUtils.S(110), Height = UIUtils.S(30) };
            _btnSmsUpLogin = new LiteButton("短信验证已完成", false) { Width = UIUtils.S(128), Height = UIUtils.S(30), Enabled = false };
            _btnLogin.Click += async (_, __) => await LoginAsync(useSmsUp: false);
            _btnSmsUpLogin.Click += async (_, __) => await LoginAsync(useSmsUp: true);
            codeRow.Controls.Add(codeLabel);
            codeRow.Controls.Add(_codeInput);
            codeRow.Controls.Add(_btnLogin);
            codeRow.Controls.Add(_btnSmsUpLogin);
            codeRow.Layout += (_, __) =>
            {
                int mid = codeRow.Height / 2;
                codeLabel.Location = new Point(0, mid - codeLabel.Height / 2);
                _codeInput.Location = new Point(UIUtils.S(150), mid - _codeInput.Height / 2);
                _btnLogin.Location = new Point(_codeInput.Right + UIUtils.S(12), mid - _btnLogin.Height / 2);
                _btnSmsUpLogin.Location = new Point(_btnLogin.Right + UIUtils.S(10), mid - _btnSmsUpLogin.Height / 2);
            };
            codeRow.Paint += PaintBottomLine;
            AddFullItem(codeRow);

            var actionRow = new Panel { Height = UIUtils.S(44), Dock = DockStyle.Top, BackColor = Color.Transparent };
            _btnValidate = new LiteButton("校验当前登录", false) { Width = UIUtils.S(118), Height = UIUtils.S(30) };
            _btnClear = new LiteButton("清除凭据", false) { Width = UIUtils.S(96), Height = UIUtils.S(30) };
            _btnValidate.Click += async (_, __) => await ValidateAsync();
            _btnClear.Click += (_, __) => ClearCredential();
            actionRow.Controls.Add(_btnValidate);
            actionRow.Controls.Add(_btnClear);
            actionRow.Layout += (_, __) =>
            {
                int mid = actionRow.Height / 2;
                _btnValidate.Location = new Point(0, mid - _btnValidate.Height / 2);
                _btnClear.Location = new Point(_btnValidate.Right + UIUtils.S(10), mid - _btnClear.Height / 2);
            };
            actionRow.Paint += PaintBottomLine;
            AddFullItem(actionRow);

            var statusRow = new Panel { Height = UIUtils.S(54), Dock = DockStyle.Top, BackColor = Color.Transparent };
            _statusLabel = new Label
            {
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = UIColors.TextSub,
                TextAlign = ContentAlignment.MiddleLeft
            };
            statusRow.Controls.Add(_statusLabel);

            if (_manualCredentialRequested != null)
            {
                _btnManualCredential = new LiteButton("高级备用入口", false) { Width = UIUtils.S(118), Height = UIUtils.S(30) };
                _btnManualCredential.Click += (_, __) => _manualCredentialRequested?.Invoke();
                statusRow.Controls.Add(_btnManualCredential);
            }

            statusRow.Layout += (_, __) =>
            {
                int gap = UIUtils.S(12);
                int right = UIUtils.S(4);
                int buttonWidth = _btnManualCredential?.Width ?? 0;
                if (_btnManualCredential != null)
                {
                    _btnManualCredential.Location = new Point(
                        Math.Max(0, statusRow.Width - buttonWidth - right),
                        Math.Max(0, statusRow.Height - _btnManualCredential.Height - UIUtils.S(5)));
                }

                int statusWidth = _btnManualCredential == null
                    ? statusRow.Width
                    : Math.Max(1, statusRow.Width - buttonWidth - gap - right);
                _statusLabel.SetBounds(0, 0, statusWidth, statusRow.Height);
            };
            AddFullItem(statusRow);
            RefreshState();
        }

        public void RefreshState()
        {
            var state = _authService.GetState(_getConfig());
            if (state.HasCredential)
            {
                string saved = state.SavedAt == default ? "" : "  保存时间：" + state.SavedAt.ToString("yyyy-MM-dd HH:mm:ss");
                string stateText = string.IsNullOrWhiteSpace(state.Error) ? "已登录" : "异常";
                _stateLabel.Text = $"登录状态：{stateText}  登录用户：{state.NickName}  来源：{state.Source}{saved}";
                _stateLabel.ForeColor = string.IsNullOrWhiteSpace(state.Error) ? Color.FromArgb(0, 150, 80) : UIColors.TextWarn;
                _btnValidate.Enabled = true;
                return;
            }

            _stateLabel.Text = "登录状态：未登录。请使用短信登录，或点击右下角“高级备用入口”手动填写凭据。";
            _stateLabel.ForeColor = UIColors.TextSub;
            _btnValidate.Enabled = false;
        }

        private async Task SendCodeAsync()
        {
            string phone = _phoneInput.Inner.Text.Trim();
            SetBusy(true);
            SetStatus("正在发送验证码...", warn: false);
            try
            {
                var result = await _authService.SendSmsCodeAsync(phone, _getConfig());
                _sessionId = result.SessionId;
                _phone = phone;
                _btnSmsUpLogin.Enabled = result.Ok && result.NeedSmsUp;

                if (result.Ok && result.NeedSmsUp)
                {
                    SetStatus($"需要短信验证：请编辑短信“{result.SmsUpContent}”发送到 {result.SmsUpNumber}，发送后点击“短信验证已完成”。", warn: false);
                }
                else
                {
                    SetStatus(result.Message, warn: !result.Ok);
                }
            }
            finally
            {
                SetBusy(false);
                RefreshState();
            }
        }

        private async Task LoginAsync(bool useSmsUp)
        {
            string phone = string.IsNullOrWhiteSpace(_phone) ? _phoneInput.Inner.Text.Trim() : _phone;
            string code = useSmsUp ? "" : _codeInput.Inner.Text.Trim();
            if (!useSmsUp && string.IsNullOrWhiteSpace(code))
            {
                SetStatus("请输入短信验证码。", warn: true);
                return;
            }

            SetBusy(true);
            SetStatus(useSmsUp ? "正在确认短信验证..." : "正在登录并保存凭据...", warn: false);
            try
            {
                var result = await _authService.CompleteSmsLoginAsync(phone, code, _sessionId, _getConfig());
                SetStatus(result.Message, warn: !result.Ok);
                if (result.Ok)
                {
                    _codeInput.Inner.Text = "";
                    _btnSmsUpLogin.Enabled = false;
                    _credentialChanged?.Invoke();
                }
            }
            finally
            {
                SetBusy(false);
                RefreshState();
            }
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
            if (GlobalPromptService.Show("确认清除本机保存的悠悠有品登录凭据？", "悠悠有品登录", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _authService.ClearCredential(_getConfig());
            _codeInput.Inner.Text = "";
            _btnSmsUpLogin.Enabled = false;
            SetStatus("已清除本机保存的悠悠有品登录凭据。", warn: false);
            _credentialChanged?.Invoke();
            RefreshState();
        }

        private void SetBusy(bool busy)
        {
            _btnSend.Enabled = !busy;
            _btnLogin.Enabled = !busy;
            _btnValidate.Enabled = !busy && _authService.GetState(_getConfig()).HasCredential;
            _btnClear.Enabled = !busy;
            if (_btnManualCredential != null) _btnManualCredential.Enabled = !busy;
            _phoneInput.Inner.Enabled = !busy;
            _codeInput.Inner.Enabled = !busy;
        }

        private void SetStatus(string text, bool warn)
        {
            _statusLabel.Text = text;
            _statusLabel.ForeColor = warn ? UIColors.TextWarn : UIColors.TextSub;
        }

        private static Label CreateRowLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextMain,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static void PaintBottomLine(object? sender, PaintEventArgs e)
        {
            if (sender is not Control control) return;
            using var pen = new Pen(UIColors.Border);
            e.Graphics.DrawLine(pen, 0, control.Height - 1, control.Width, control.Height - 1);
        }
    }
}
