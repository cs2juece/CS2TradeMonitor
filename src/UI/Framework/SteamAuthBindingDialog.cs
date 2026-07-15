using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Steam;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.Application.Steam.Auth;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.Domain.Steam;

namespace CS2TradeMonitor.src.UI.Framework.SteamOffers
{
    public sealed class SteamAuthBindingDialog : Form
    {
        private readonly Panel _content;
        private readonly Label _summaryLabel;
        private readonly Label _toolsStepLabel;
        private readonly Panel _toolsPanel;
        private readonly Label _currentCodeTitle;
        private readonly Label _currentCodeLabel;
        private readonly Panel _currentCodeCountdownBar;
        private readonly LiteButton _currentCodeVisibilityButton;
        private readonly Label _connectionStatusLabel;
        private readonly LiteButton _detectConnectionButton;
        private readonly LiteButton _proxySettingsButton;
        private readonly System.Windows.Forms.Timer _currentCodeTimer;
        private readonly Label _tokenStepLabel;
        private readonly Label _loginStepLabel;
        private readonly Label _tokenStatusLabel;
        private readonly Label _loginStatusLabel;
        private readonly Label _advancedHint;
        private readonly ToolTip _toolTip;
        private readonly List<AuthRow> _tokenRows = new();
        private readonly List<AuthRow> _loginRows = new();
        private readonly LiteUnderlineInput _sharedInput;
        private readonly LiteUnderlineInput _identityInput;
        private readonly LiteUnderlineInput _accountInput;
        private readonly LiteUnderlineInput _passwordInput;
        private readonly LiteButton _saveTokenButton;
        private readonly LiteButton _verifyTokenButton;
        private readonly LiteButton _clearTokenButton;
        private readonly LiteButton _mainWebLoginButton;
        private readonly LiteButton _loginButton;
        private readonly LiteButton _clearLoginButton;
        private readonly LiteButton _closeButton;
        private readonly LiteButton _advancedToggle;
        private readonly Panel _advancedPanel;
        private readonly TextBox _importTextBox;
        private readonly LiteButton _chooseButton;
        private readonly LiteButton _importButton;
        private readonly LiteButton _webLoginButton;
        private readonly LiteButton _tokenRestoreButton;
        private readonly ISteamOfferService _steamOffers;
        private readonly ISteamAuthStore _steamAuthStore;
        private readonly SteamOfferPagePresenter _presenter;
        private string _selectedImportPath = "";
        private bool _advancedExpanded;
        private bool _busy;
        private bool _codeVisible;
        private int _currentCodeSecondsLeft;

        public event EventHandler? OpenWebLoginRequested;
        public event EventHandler? StatusChanged;
        public event EventHandler? AuthChanged;

        public SteamAuthBindingDialog()
            : this(SteamOfferPageRuntimeServices.Resolve())
        {
        }

        internal SteamAuthBindingDialog(SteamOfferPageRuntimeServices runtimeServices, SteamOfferPagePresenter? presenter = null)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);

            _steamOffers = runtimeServices.SteamOffers;
            _steamAuthStore = runtimeServices.SteamAuthStore;
            _presenter = presenter ?? new SteamOfferPagePresenter(runtimeServices);

            Text = "Steam令牌绑定";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            MinimumSize = UIUtils.S(new Size(900, 680));
            ClientSize = UIUtils.S(new Size(900, 740));
            BackColor = UIColors.MainBg;
            Font = new Font("Microsoft YaHei UI", 9F);

            _toolTip = new ToolTip { AutomaticDelay = 250, ReshowDelay = 100, ShowAlways = false };
            _content = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UIColors.MainBg, Padding = UIUtils.S(new Padding(18)) };
            Controls.Add(_content);

            _summaryLabel = new Label
            {
                AutoSize = false,
                ForeColor = UIColors.TextSub,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _toolsStepLabel = MakeSectionLabel("验证码与连接");
            _toolsPanel = new Panel
            {
                BackColor = UIColors.CardBg,
                BorderStyle = BorderStyle.FixedSingle
            };
            _currentCodeTitle = MakeLabel("验证码");
            _currentCodeLabel = MakeCodeLabel();
            _currentCodeCountdownBar = new Panel
            {
                AccessibleName = "Steam 验证码倒计时",
                BackColor = UIColors.ControlBg,
                Visible = false
            };
            _currentCodeCountdownBar.Paint += (_, e) =>
                SteamOfferPageControls.PaintTotpCountdownBar(e.Graphics, _currentCodeCountdownBar.ClientRectangle, _currentCodeSecondsLeft);
            _currentCodeVisibilityButton = new LiteButton("显示", false) { Width = UIUtils.S(72), Height = UIUtils.S(32) };
            _connectionStatusLabel = MakeLabel("Steam 网络：正在检测连接");
            _detectConnectionButton = new LiteButton("检测连接", false) { Width = UIUtils.S(96), Height = UIUtils.S(32) };
            _proxySettingsButton = new LiteButton("代理设置", false) { Width = UIUtils.S(96), Height = UIUtils.S(32) };
            _currentCodeTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _currentCodeTimer.Tick += (_, __) => RefreshTokenTools();
            _toolsPanel.Controls.AddRange(new Control[]
            {
                _currentCodeTitle,
                _currentCodeLabel,
                _currentCodeCountdownBar,
                _currentCodeVisibilityButton,
                _connectionStatusLabel,
                _detectConnectionButton,
                _proxySettingsButton
            });
            _tokenStepLabel = MakeSectionLabel("第一步：保存令牌密钥");
            _loginStepLabel = MakeSectionLabel("第二步：保存 Steam 登录状态（账号密码优先，网页登录备用）");
            _tokenStatusLabel = new Label
            {
                AutoSize = false,
                ForeColor = UIColors.TextSub,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _loginStatusLabel = new Label
            {
                AutoSize = false,
                ForeColor = UIColors.TextSub,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _sharedInput = AddMainRow("shared_secret *", "来自 SDA maFile 或手机令牌数据", true, _tokenRows,
                "用于生成 Steam Guard 验证码。不会在窗口重开时回填。");
            _identityInput = AddMainRow("identity_secret *", "来自 SDA maFile 或手机令牌数据", true, _tokenRows,
                "用于移动确认和纯收货报价处理。不会在窗口重开时回填。");
            _accountInput = AddMainRow("Steam 账号名 *", "Steam 登录账号名，不是昵称", false, _loginRows,
                "推荐主路径。登录成功后会保存 Steam 登录状态，后续失效时可自动恢复。");
            _passwordInput = AddMainRow("Steam 密码 *", "仅加密保存，不回填", true, _loginRows,
                "推荐主路径。登录成功后才会加密保存；失败不会保存错误密码。");

            _saveTokenButton = new LiteButton("保存令牌密钥", false) { Width = UIUtils.S(150), Height = UIUtils.S(34) };
            _verifyTokenButton = new LiteButton("验证令牌", false) { Width = UIUtils.S(110), Height = UIUtils.S(34) };
            _clearTokenButton = new LiteButton("清空令牌", false) { Width = UIUtils.S(100), Height = UIUtils.S(34) };
            _loginButton = new LiteButton("账号密码登录并保存", true) { Width = UIUtils.S(170), Height = UIUtils.S(36) };
            _mainWebLoginButton = new LiteButton("网页登录备用", false) { Width = UIUtils.S(140), Height = UIUtils.S(36) };
            _clearLoginButton = new LiteButton("清空登录状态", false) { Width = UIUtils.S(126), Height = UIUtils.S(36) };
            _closeButton = new LiteButton("关闭", false) { Width = UIUtils.S(90), Height = UIUtils.S(34) };
            _advancedToggle = new LiteButton("其他方式 ▸", false) { Width = UIUtils.S(128), Height = UIUtils.S(34) };
            _advancedHint = new Label
            {
                AutoSize = false,
                ForeColor = UIColors.TextSub,
                Text = "不愿保存 Steam 密码时再用 maFile/SDA JSON、网页登录或 Token 恢复；网页登录状态可能过期。",
                TextAlign = ContentAlignment.MiddleLeft
            };

            _advancedPanel = new Panel
            {
                BackColor = UIColors.CardBg,
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };
            _chooseButton = new LiteButton("选择 maFile/SDA JSON", false) { Width = UIUtils.S(170), Height = UIUtils.S(32) };
            _importButton = new LiteButton("导入并加密保存", true) { Width = UIUtils.S(150), Height = UIUtils.S(32) };
            _webLoginButton = new LiteButton("打开 Steam 网页登录", false) { Width = UIUtils.S(170), Height = UIUtils.S(32) };
            _tokenRestoreButton = new LiteButton("用 Token 恢复登录", false) { Width = UIUtils.S(160), Height = UIUtils.S(32) };
            _importTextBox = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = UIColors.ControlBg,
                ForeColor = UIColors.TextMain,
                Font = new Font("Consolas", 9F),
                WordWrap = false
            };
            _advancedPanel.Controls.AddRange(new Control[] { _chooseButton, _importButton, _webLoginButton, _tokenRestoreButton, _importTextBox });

            _content.Controls.AddRange(new Control[]
            {
                _summaryLabel,
                _toolsStepLabel,
                _toolsPanel,
                _tokenStepLabel,
                _loginStepLabel,
                _tokenStatusLabel,
                _loginStatusLabel,
                _saveTokenButton,
                _verifyTokenButton,
                _clearTokenButton,
                _mainWebLoginButton,
                _loginButton,
                _clearLoginButton,
                _advancedToggle,
                _advancedHint,
                _advancedPanel,
                _closeButton
            });

            Layout += (_, __) => LayoutChildren();
            _currentCodeVisibilityButton.Click += (_, __) =>
            {
                _codeVisible = !_codeVisible;
                RefreshTokenTools();
            };
            _detectConnectionButton.Click += async (_, __) => await DetectConnectionAsync();
            _proxySettingsButton.Click += (_, __) => ShowProxySettings();
            _saveTokenButton.Click += (_, __) => SaveTokenSecrets();
            _verifyTokenButton.Click += (_, __) => VerifyTokenCode();
            _clearTokenButton.Click += (_, __) => ClearTokenSecrets();
            _mainWebLoginButton.Click += (_, __) => OpenWebLoginRequested?.Invoke(this, EventArgs.Empty);
            _loginButton.Click += async (_, __) => await LoginAndConfigureAsync();
            _clearLoginButton.Click += (_, __) => ClearLoginState();
            _advancedToggle.Click += (_, __) =>
            {
                _advancedExpanded = !_advancedExpanded;
                _advancedPanel.Visible = _advancedExpanded;
                _advancedToggle.Text = _advancedExpanded ? "其他方式 ▾" : "其他方式 ▸";
                LayoutChildren();
            };
            _chooseButton.Click += (_, __) => ChooseImportFile();
            _importButton.Click += (_, __) => ImportCurrentText();
            _webLoginButton.Click += (_, __) => OpenWebLoginRequested?.Invoke(this, EventArgs.Empty);
            _tokenRestoreButton.Click += async (_, __) => await RestoreLoginStateFromTokenTextAsync();
            _closeButton.Click += (_, __) => Close();
            Shown += async (_, __) =>
            {
                _currentCodeTimer.Start();
                await SyncSteamTimeOffsetForVerificationAsync();
                RefreshTokenTools();
            };
            FormClosed += (_, __) => _currentCodeTimer.Stop();
            Disposed += (_, __) =>
            {
                _currentCodeTimer.Stop();
                _currentCodeTimer.Dispose();
            };

            RefreshStatus();
        }

        public void RefreshStatus(string? message = null, bool ok = true)
        {
            _summaryLabel.Text = BuildSummary();
            RefreshTokenTools();
            if (!string.IsNullOrWhiteSpace(message))
            {
                _loginStatusLabel.ForeColor = ok ? Color.FromArgb(0, 170, 90) : UIColors.TextWarn;
                _loginStatusLabel.Text = message.Trim();
                return;
            }

            _tokenStatusLabel.ForeColor = UIColors.TextSub;
            var status = _steamOffers.GetState().AuthStatus;
            _tokenStatusLabel.Text = status.HasSecrets
                ? "令牌密钥：已保存。identity_secret 用于报价/移动确认，不用于直接批准网页登录。"
                : "令牌密钥：未保存。先保存 shared_secret / identity_secret；已有 maFile 建议从“其他方式”导入。";
            _loginStatusLabel.ForeColor = UIColors.TextSub;
            _loginStatusLabel.Text = BuildLoginStateLine(status);
        }

        private void RefreshTokenTools()
        {
            SteamTokenBarSnapshot snapshot = _presenter.GetTokenBarSnapshot();
            SteamTokenEntry? selected = snapshot.VisibleTokens.FirstOrDefault(token =>
                string.Equals(token.Id, snapshot.DefaultTokenId, StringComparison.Ordinal))
                ?? snapshot.VisibleTokens.FirstOrDefault();
            SteamTokenCodeDisplayViewModel display = _presenter.GetTokenCodeDisplay(selected, _codeVisible);
            _currentCodeLabel.Text = display.CodeText;
            _currentCodeLabel.ForeColor = TokenSessionToneColor(display.SessionTone);
            _currentCodeVisibilityButton.Text = display.VisibilityButtonText;
            _currentCodeVisibilityButton.Enabled = display.CanToggleCodeVisibility;
            _toolTip.SetToolTip(_currentCodeLabel, display.SessionText);
            _currentCodeSecondsLeft = display.SecondsLeft;
            bool showCountdown = display.CanToggleCodeVisibility && display.SecondsLeft > 0;
            _currentCodeCountdownBar.Visible = showCountdown;
            _currentCodeCountdownBar.AccessibleDescription = showCountdown
                ? $"验证码剩余 {display.SecondsLeft} 秒"
                : "验证码倒计时不可用";
            _toolTip.SetToolTip(_currentCodeCountdownBar, _currentCodeCountdownBar.AccessibleDescription);
            _currentCodeCountdownBar.Invalidate();

            SteamConnectionStatusViewModel connection = _presenter.GetConnectionStatus();
            _connectionStatusLabel.Text = connection.LabelText;
            _connectionStatusLabel.ForeColor = ConnectionToneColor(connection.Tone);
            _toolTip.SetToolTip(
                _connectionStatusLabel,
                string.IsNullOrWhiteSpace(connection.TooltipText) ? connection.DetailText : connection.TooltipText);
        }

        private async Task DetectConnectionAsync()
        {
            if (_busy)
                return;

            _busy = true;
            _detectConnectionButton.Enabled = false;
            _connectionStatusLabel.ForeColor = UIColors.TextSub;
            _connectionStatusLabel.Text = "Steam 网络：正在检测连接";
            try
            {
                SteamConnectionProfile profile = await _presenter.ResolveConnectionAsync(force: true, CancellationToken.None);
                SteamConnectionStatusViewModel connection = SteamOfferPagePresenter.BuildConnectionStatus(profile, DateTime.Now);
                _connectionStatusLabel.Text = connection.LabelText;
                _connectionStatusLabel.ForeColor = ConnectionToneColor(connection.Tone);
                _toolTip.SetToolTip(
                    _connectionStatusLabel,
                    string.IsNullOrWhiteSpace(connection.TooltipText) ? connection.DetailText : connection.TooltipText);
                StatusChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _connectionStatusLabel.ForeColor = UIColors.TextWarn;
                _connectionStatusLabel.Text = "Steam 网络：检测失败";
                _toolTip.SetToolTip(_connectionStatusLabel, ex.Message);
            }
            finally
            {
                _detectConnectionButton.Enabled = true;
                _busy = false;
            }
        }

        private void ShowProxySettings()
        {
            if (_busy)
                return;

            using var dialog = new SteamProxySettingsDialog(_presenter, () =>
            {
                RefreshTokenTools();
                StatusChanged?.Invoke(this, EventArgs.Empty);
            });
            dialog.ShowDialog(this);
            RefreshTokenTools();
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        private LiteUnderlineInput AddMainRow(string labelText, string placeholder, bool secret, List<AuthRow> rows, string tooltip)
        {
            var label = MakeLabel(labelText);
            var input = new LiteUnderlineInput("", "", "", 360) { Height = UIUtils.S(28), Placeholder = placeholder };
            LiteButton? toggle = null;
            if (secret)
            {
                input.Inner.UseSystemPasswordChar = true;
                toggle = new LiteButton("显示", false) { Width = UIUtils.S(58), Height = UIUtils.S(28) };
                toggle.Click += (_, __) =>
                {
                    bool hidden = input.Inner.UseSystemPasswordChar;
                    input.Inner.UseSystemPasswordChar = !hidden;
                    toggle.Text = hidden ? "隐藏" : "显示";
                };
            }

            _toolTip.SetToolTip(label, tooltip);
            _toolTip.SetToolTip(input, tooltip);
            _toolTip.SetToolTip(input.Inner, tooltip);
            if (toggle != null) _toolTip.SetToolTip(toggle, "显示或隐藏本输入框内容。");

            _content.Controls.Add(label);
            _content.Controls.Add(input);
            if (toggle != null) _content.Controls.Add(toggle);
            rows.Add(new AuthRow(label, input, toggle));
            return input;
        }

        private void SaveTokenSecrets()
        {
            if (_busy) return;

            var result = _steamOffers.SaveManualTokenSecrets(_sharedInput.Inner.Text, _identityInput.Inner.Text);
            _summaryLabel.Text = BuildSummary();
            _tokenStatusLabel.ForeColor = result.Ok ? Color.FromArgb(0, 170, 90) : UIColors.TextWarn;
            _tokenStatusLabel.Text = result.Message;
            if (result.Ok)
            {
                _sharedInput.Inner.Clear();
                _identityInput.Inner.Clear();
                RefreshTokenTools();
                AuthChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void VerifyTokenCode()
        {
            if (_busy) return;

            var credential = _steamAuthStore.Load();
            string sharedSecret = FirstText(_sharedInput.Inner.Text, credential?.SharedSecret ?? "");
            if (string.IsNullOrWhiteSpace(sharedSecret))
            {
                _tokenStatusLabel.ForeColor = UIColors.TextWarn;
                _tokenStatusLabel.Text = "无法验证：请先填写或保存 shared_secret。";
                return;
            }
            if (!SteamCryptoHelper.TryValidateSteamGuardSharedSecret(sharedSecret, out string validationMessage))
            {
                _tokenStatusLabel.ForeColor = UIColors.TextWarn;
                _tokenStatusLabel.Text = "无法验证：" + validationMessage;
                return;
            }

            long now = _steamOffers.GetCorrectedSteamTimeSeconds();
            int secondsLeft = 30 - (int)(now % 30);
            string code = SteamCryptoHelper.GenerateSteamGuardCode(sharedSecret, now);
            var status = _steamOffers.GetState().AuthStatus;
            string account = FirstText(status.PersonaName, _accountInput.Inner.Text, status.AccountName, "当前令牌");
            string steamId = string.IsNullOrWhiteSpace(status.SteamId) ? "SteamID 未记录" : "SteamID " + status.SteamId;

            _tokenStatusLabel.ForeColor = Color.FromArgb(0, 170, 90);
            _tokenStatusLabel.Text = $"当前验证码：{code}，剩余 {secondsLeft} 秒。请和手机 Steam Guard 的“{account}”对比；一致则令牌匹配（{steamId}），不一致请先检查 shared_secret 是否来自同一账号。";
        }

        private void ClearTokenSecrets()
        {
            if (_busy) return;
            if (GlobalPromptService.Show(
                    this,
                    "确认只清空 shared_secret / identity_secret 吗？\n\nSteam 登录状态会保留，但验证码和移动确认需要重新保存令牌密钥。",
                    "清空令牌密钥",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            var result = _steamOffers.ClearTokenSecrets();
            _sharedInput.Inner.Clear();
            _identityInput.Inner.Clear();
            _tokenStatusLabel.ForeColor = result.Ok ? Color.FromArgb(0, 170, 90) : UIColors.TextWarn;
            _tokenStatusLabel.Text = result.Message;
            _summaryLabel.Text = BuildSummary();
            RefreshTokenTools();
            AuthChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ClearLoginState()
        {
            if (_busy) return;
            if (GlobalPromptService.Show(
                    this,
                    "确认只清空 Steam 登录状态吗？\n\n令牌密钥会保留；报价列表会清空，需要时可重新网页登录或用 Token 恢复。",
                    "清空 Steam 登录状态",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            var result = _steamOffers.ClearLoginState();
            _passwordInput.Inner.Clear();
            _loginStatusLabel.ForeColor = result.Ok ? Color.FromArgb(0, 170, 90) : UIColors.TextWarn;
            _loginStatusLabel.Text = result.Message;
            _summaryLabel.Text = BuildSummary();
            RefreshTokenTools();
            AuthChanged?.Invoke(this, EventArgs.Empty);
        }

        private async Task SyncSteamTimeOffsetForVerificationAsync()
        {
            try
            {
                await _steamOffers.SyncSteamTimeOffsetAsync();
            }
            catch
            {
                // Best-effort sync; local UTC remains the fallback when Steam is unreachable.
            }
        }

        private async Task LoginAndConfigureAsync()
        {
            if (_busy) return;

            var authStatus = _steamOffers.GetState().AuthStatus;
            if (authStatus.AutoReloginCooldownUntil > DateTime.Now)
            {
                RefreshStatus(
                    $"账号密码登录冷却中，{authStatus.AutoReloginCooldownUntil:HH:mm:ss} 后再试；不愿等待时可使用“网页登录备用”。",
                    ok: false);
                return;
            }

            var missing = new List<string>();
            if (!authStatus.HasSecrets) missing.Add("先保存令牌密钥");
            if (string.IsNullOrWhiteSpace(_accountInput.Inner.Text)) missing.Add("Steam 账号名");
            if (string.IsNullOrWhiteSpace(_passwordInput.Inner.Text)) missing.Add("Steam 密码");
            if (missing.Count > 0)
            {
                RefreshStatus("缺少必填字段：" + string.Join("、", missing) + "。", ok: false);
                return;
            }

            _busy = true;
            _loginButton.Enabled = false;
            RefreshStatus("登录中：正在连接 Steam。", ok: true);
            try
            {
                var result = await _steamOffers.LoginAndConfigureAsync(new SteamAutoLoginRequest
                {
                    AccountName = _accountInput.Inner.Text,
                    Password = _passwordInput.Inner.Text
                });
                RefreshStatus(WithLoginFailureHint(result.Message, result.Code, _accountInput.Inner.Text), result.Ok);
                if (result.Ok)
                {
                    ClearSensitiveInputs();
                    RefreshTokenTools();
                    AuthChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            finally
            {
                _loginButton.Enabled = true;
                _busy = false;
            }
        }

        private void ChooseImportFile()
        {
            using var ofd = new OpenFileDialog
            {
                Title = "选择 Steam maFile / SDA JSON",
                Filter = "Steam Auth 文件|*.maFile;*.json;*.txt|所有文件|*.*"
            };
            if (ofd.ShowDialog(this) != DialogResult.OK)
                return;

            var loaded = _steamOffers.LoadMaFileImportFile(ofd.FileName);
            if (loaded.RequiresSelection)
            {
                var selected = ChooseSteamAuthImportCandidate(loaded.Candidates);
                if (selected == null)
                {
                    _selectedImportPath = "";
                    _importTextBox.Text = loaded.Text;
                    RefreshStatus(loaded.Message, ok: false);
                    return;
                }

                loaded = _steamOffers.LoadMaFileImportFile(selected.Path);
            }

            _selectedImportPath = loaded.SourcePath;
            _importTextBox.Text = loaded.Text;
            RefreshStatus(loaded.Message, loaded.Ok);
        }

        private void ImportCurrentText()
        {
            var result = _steamOffers.ImportMaFileText(_importTextBox.Text, _selectedImportPath);
            RefreshStatus(result.Message, result.Ok);
            if (!result.Ok)
                return;

            AuthChanged?.Invoke(this, EventArgs.Empty);
            RefreshTokenTools();
            if (IsPlainTextRiskPath(_selectedImportPath))
                AskRecyclePlainTextFile(_selectedImportPath);
        }

        private async Task RestoreLoginStateFromTokenTextAsync()
        {
            if (_busy) return;

            if (string.IsNullOrWhiteSpace(_importTextBox.Text))
            {
                RefreshStatus("请先在文本框粘贴 AccessToken、RefreshToken 或 steamLoginSecure Cookie。", ok: false);
                return;
            }

            _busy = true;
            _tokenRestoreButton.Enabled = false;
            RefreshStatus("正在用 Token 恢复 Steam 登录状态。", ok: true);
            try
            {
                var result = await _steamOffers.RestoreLoginStateFromTokenTextAsync(_importTextBox.Text);
                RefreshStatus(result.Message, result.Ok);
                if (result.Ok)
                {
                    _importTextBox.Clear();
                    RefreshTokenTools();
                    AuthChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            finally
            {
                _tokenRestoreButton.Enabled = true;
                _busy = false;
            }
        }

        private SteamOfferImportCandidate? ChooseSteamAuthImportCandidate(IReadOnlyList<SteamOfferImportCandidate> candidates)
        {
            if (candidates.Count == 0) return null;
            if (candidates.Count == 1) return candidates[0];

            using var dialog = new Form
            {
                Text = "选择 Steam 账户",
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                ClientSize = UIUtils.S(new Size(520, 320)),
                BackColor = UIColors.MainBg,
                Font = new Font("Microsoft YaHei UI", 9F)
            };

            var title = MakeLabel("manifest.json 中找到多个未加密 maFile，请选择要导入的账户。");
            var list = new ListBox
            {
                BackColor = UIColors.ControlBg,
                ForeColor = UIColors.TextMain,
                BorderStyle = BorderStyle.FixedSingle,
                DisplayMember = nameof(SteamOfferImportCandidate.DisplayName),
                DataSource = candidates.ToList()
            };
            var btnOk = new LiteButton("导入所选", true) { Width = UIUtils.S(110), Height = UIUtils.S(32) };
            var btnCancel = new LiteButton("取消", false) { Width = UIUtils.S(90), Height = UIUtils.S(32) };
            dialog.Controls.AddRange(new Control[] { title, list, btnOk, btnCancel });

            SteamOfferImportCandidate? selected = null;
            btnOk.Click += (_, __) =>
            {
                selected = list.SelectedItem as SteamOfferImportCandidate;
                dialog.DialogResult = selected == null ? DialogResult.Cancel : DialogResult.OK;
                dialog.Close();
            };
            btnCancel.Click += (_, __) =>
            {
                dialog.DialogResult = DialogResult.Cancel;
                dialog.Close();
            };
            list.DoubleClick += (_, __) => btnOk.PerformClick();
            dialog.Layout += (_, __) =>
            {
                int pad = UIUtils.S(18);
                int width = Math.Max(1, dialog.ClientSize.Width - pad * 2);
                title.SetBounds(pad, pad, width, UIUtils.S(32));
                list.SetBounds(pad, title.Bottom + UIUtils.S(10), width, UIUtils.S(190));
                btnCancel.SetBounds(dialog.ClientSize.Width - pad - btnCancel.Width, list.Bottom + UIUtils.S(18), btnCancel.Width, btnCancel.Height);
                btnOk.SetBounds(btnCancel.Left - UIUtils.S(10) - btnOk.Width, btnCancel.Top, btnOk.Width, btnOk.Height);
            };

            return dialog.ShowDialog(this) == DialogResult.OK ? selected : null;
        }

        private void LayoutChildren()
        {
            int pad = _content.Padding.Left;
            int width = Math.Max(1, _content.ClientSize.Width - _content.Padding.Left - _content.Padding.Right);
            int y = _content.Padding.Top;

            _summaryLabel.SetBounds(pad, y, width, UIUtils.S(46));
            y = _summaryLabel.Bottom + UIUtils.S(8);

            _toolsStepLabel.SetBounds(pad, y, width, UIUtils.S(28));
            y = _toolsStepLabel.Bottom + UIUtils.S(6);
            _toolsPanel.SetBounds(pad, y, width, UIUtils.S(94));
            LayoutTokenToolsPanel();
            y = _toolsPanel.Bottom + UIUtils.S(14);

            int labelWidth = UIUtils.S(170);
            int toggleWidth = UIUtils.S(58);
            int rowHeight = UIUtils.S(42);

            _tokenStepLabel.SetBounds(pad, y, width, UIUtils.S(28));
            y = _tokenStepLabel.Bottom + UIUtils.S(6);
            LayoutRows(_tokenRows, ref y, pad, width, labelWidth, toggleWidth, rowHeight);
            _saveTokenButton.SetBounds(pad + labelWidth, y, _saveTokenButton.Width, _saveTokenButton.Height);
            _verifyTokenButton.SetBounds(_saveTokenButton.Right + UIUtils.S(10), y, _verifyTokenButton.Width, _verifyTokenButton.Height);
            _clearTokenButton.SetBounds(_verifyTokenButton.Right + UIUtils.S(10), y, _clearTokenButton.Width, _clearTokenButton.Height);
            int tokenStatusX = _clearTokenButton.Right + UIUtils.S(14);
            if (tokenStatusX + UIUtils.S(240) > pad + width)
            {
                _tokenStatusLabel.SetBounds(pad + labelWidth, _saveTokenButton.Bottom + UIUtils.S(8), Math.Max(1, width - labelWidth), UIUtils.S(42));
                y = _tokenStatusLabel.Bottom + UIUtils.S(18);
            }
            else
            {
                _tokenStatusLabel.SetBounds(tokenStatusX, y, Math.Max(1, pad + width - tokenStatusX), _saveTokenButton.Height);
                y = _saveTokenButton.Bottom + UIUtils.S(18);
            }

            _loginStepLabel.SetBounds(pad, y, width, UIUtils.S(28));
            y = _loginStepLabel.Bottom + UIUtils.S(6);
            LayoutRows(_loginRows, ref y, pad, width, labelWidth, toggleWidth, rowHeight);

            int actionX = pad + labelWidth;
            _loginButton.SetBounds(actionX, y, _loginButton.Width, _loginButton.Height);
            _mainWebLoginButton.SetBounds(_loginButton.Right + UIUtils.S(10), y, _mainWebLoginButton.Width, _mainWebLoginButton.Height);
            _clearLoginButton.SetBounds(_mainWebLoginButton.Right + UIUtils.S(10), y, _clearLoginButton.Width, _clearLoginButton.Height);
            int statusX = _clearLoginButton.Right + UIUtils.S(14);
            if (statusX + UIUtils.S(220) > pad + width)
            {
                _loginStatusLabel.SetBounds(actionX, _loginButton.Bottom + UIUtils.S(8), Math.Max(1, width - labelWidth), UIUtils.S(36));
                y = _loginStatusLabel.Bottom + UIUtils.S(12);
            }
            else
            {
                _loginStatusLabel.SetBounds(statusX, y, Math.Max(1, pad + width - statusX), _loginButton.Height);
                y = _loginButton.Bottom + UIUtils.S(18);
            }

            void LayoutRows(List<AuthRow> rows, ref int currentY, int left, int availableWidth, int labelW, int toggleW, int rowH)
            {
                foreach (var row in rows)
                {
                    row.Label.SetBounds(left, currentY + UIUtils.S(2), labelW, UIUtils.S(28));
                    int inputX = left + labelW;
                    int toggleSpace = row.Toggle == null ? 0 : toggleW + UIUtils.S(8);
                    int inputWidth = Math.Max(UIUtils.S(260), availableWidth - labelW - toggleSpace);
                    row.Input.SetBounds(inputX, currentY, inputWidth, UIUtils.S(28));
                    row.Toggle?.SetBounds(row.Input.Right + UIUtils.S(8), currentY, toggleW, UIUtils.S(28));
                    currentY += rowH;
                }
            }

            _advancedToggle.SetBounds(pad, y, _advancedToggle.Width, _advancedToggle.Height);
            _advancedHint.SetBounds(_advancedToggle.Right + UIUtils.S(12), y, Math.Max(1, width - _advancedToggle.Width - UIUtils.S(12)), _advancedToggle.Height);
            y = _advancedToggle.Bottom + UIUtils.S(8);

            if (_advancedExpanded)
            {
                int advancedHeight = Math.Max(UIUtils.S(210), _content.ClientSize.Height - y - UIUtils.S(62));
                _advancedPanel.SetBounds(pad, y, width, advancedHeight);
                LayoutAdvancedPanel();
                y = _advancedPanel.Bottom + UIUtils.S(10);
            }

            _closeButton.SetBounds(pad + width - _closeButton.Width, Math.Max(y, _content.ClientSize.Height - _content.Padding.Bottom - _closeButton.Height), _closeButton.Width, _closeButton.Height);
            _content.AutoScrollMinSize = new Size(0, _closeButton.Bottom + _content.Padding.Bottom);
        }

        private void LayoutTokenToolsPanel()
        {
            int pad = UIUtils.S(12);
            int gap = UIUtils.S(10);
            int width = Math.Max(1, _toolsPanel.ClientSize.Width - pad * 2);
            int rowHeight = UIUtils.S(32);
            int row1Y = UIUtils.S(12);
            int codeTitleWidth = UIUtils.S(54);
            int codeWidth = UIUtils.S(112);

            _currentCodeTitle.SetBounds(pad, row1Y, codeTitleWidth, rowHeight);
            _currentCodeLabel.SetBounds(_currentCodeTitle.Right + gap, row1Y, codeWidth, rowHeight);
            _currentCodeVisibilityButton.SetBounds(_currentCodeLabel.Right + gap, row1Y, _currentCodeVisibilityButton.Width, _currentCodeVisibilityButton.Height);
            _currentCodeCountdownBar.SetBounds(_currentCodeLabel.Left, _currentCodeLabel.Bottom + UIUtils.S(3), _currentCodeLabel.Width, UIUtils.S(8));

            int row2Y = row1Y + rowHeight + UIUtils.S(12);
            int actionWidth = _detectConnectionButton.Width + _proxySettingsButton.Width + gap;
            _proxySettingsButton.SetBounds(_toolsPanel.ClientSize.Width - pad - _proxySettingsButton.Width, row2Y, _proxySettingsButton.Width, _proxySettingsButton.Height);
            _detectConnectionButton.SetBounds(_proxySettingsButton.Left - gap - _detectConnectionButton.Width, row2Y, _detectConnectionButton.Width, _detectConnectionButton.Height);
            _connectionStatusLabel.SetBounds(pad, row2Y, Math.Max(1, width - actionWidth - gap), rowHeight);
        }

        private void LayoutAdvancedPanel()
        {
            int pad = UIUtils.S(12);
            int gap = UIUtils.S(10);
            int width = Math.Max(1, _advancedPanel.ClientSize.Width - pad * 2);
            int y = pad;
            _chooseButton.SetBounds(pad, y, _chooseButton.Width, _chooseButton.Height);
            _importButton.SetBounds(_chooseButton.Right + gap, y, _importButton.Width, _importButton.Height);
            _webLoginButton.SetBounds(_importButton.Right + gap, y, _webLoginButton.Width, _webLoginButton.Height);
            if (_webLoginButton.Right + gap + _tokenRestoreButton.Width <= _advancedPanel.ClientSize.Width - pad)
            {
                _tokenRestoreButton.SetBounds(_webLoginButton.Right + gap, y, _tokenRestoreButton.Width, _tokenRestoreButton.Height);
                y = _chooseButton.Bottom + gap;
            }
            else
            {
                y = _chooseButton.Bottom + gap;
                _tokenRestoreButton.SetBounds(pad, y, _tokenRestoreButton.Width, _tokenRestoreButton.Height);
                y = _tokenRestoreButton.Bottom + gap;
            }
            _importTextBox.SetBounds(pad, y, width, Math.Max(UIUtils.S(110), _advancedPanel.ClientSize.Height - y - pad));
        }

        private void ClearSensitiveInputs()
        {
            _sharedInput.Inner.Clear();
            _identityInput.Inner.Clear();
            _passwordInput.Inner.Clear();
        }

        private string WithLoginFailureHint(string message, string code, string accountName)
        {
            if (!string.Equals(code, "InvalidTwoFactor", StringComparison.OrdinalIgnoreCase))
                return message;

            var status = _steamOffers.GetState().AuthStatus;
            string boundAccount = status.AccountName.Trim();
            string inputAccount = (accountName ?? "").Trim();
            if (!status.HasCredential
                || string.IsNullOrWhiteSpace(boundAccount)
                || string.Equals(boundAccount, "未命名令牌", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(inputAccount)
                || string.Equals(boundAccount, inputAccount, StringComparison.OrdinalIgnoreCase))
            {
                return message;
            }

            return message + $" 当前已绑定令牌账户是“{boundAccount}”，输入账号名是“{inputAccount}”；如果 shared_secret/identity_secret 来自当前令牌，请把账号名改成一致后再试。";
        }

        private static Label MakeLabel(string text)
        {
            return new Label
            {
                AutoSize = false,
                Text = text,
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static Label MakeCodeLabel()
        {
            return new Label
            {
                AutoSize = false,
                Text = "•••••",
                ForeColor = UIColors.TextMain,
                BackColor = UIColors.ControlBg,
                Font = new Font("Consolas", 10F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };
        }

        private static Label MakeSectionLabel(string text)
        {
            return new Label
            {
                AutoSize = false,
                Text = text,
                ForeColor = UIColors.TextMain,
                BackColor = UIColors.CardBg,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = UIUtils.S(new Padding(8, 0, 0, 0))
            };
        }

        private static Color TokenSessionToneColor(SteamTokenSessionStatusTone tone)
        {
            return tone switch
            {
                SteamTokenSessionStatusTone.Success => Color.FromArgb(0, 170, 90),
                SteamTokenSessionStatusTone.Warning => UIColors.TextWarn,
                _ => UIColors.TextMain
            };
        }

        private static Color ConnectionToneColor(SteamConnectionStatusTone tone)
        {
            return tone switch
            {
                SteamConnectionStatusTone.Success => Color.FromArgb(0, 170, 90),
                SteamConnectionStatusTone.Warning => UIColors.TextWarn,
                _ => UIColors.TextSub
            };
        }

        private string BuildSummary()
        {
            var status = _steamOffers.GetState().AuthStatus;
            if (!status.HasCredential)
                return "未绑定 Steam 令牌。主路径：填写 4 项并登录；已有 maFile 可展开“其他方式”导入。";

            string login = status.HasSession ? "Steam 登录状态：已保存" : "Steam 登录状态：未登录";
            string autoLogin = status.HasAutoLogin ? "自动登录：已保存" : "自动登录：未保存";
            string api = string.IsNullOrWhiteSpace(status.Message) ? "" : status.Message;
            string cooldown = status.AutoReloginCooldownUntil > DateTime.Now
                ? $"，冷却至：{status.AutoReloginCooldownUntil:HH:mm:ss}"
                : "";
            return $"令牌：{BuildStatusDisplayName(status)} / {status.SteamId}，{login}，{autoLogin}{cooldown}。{api}";
        }

        private static string BuildLoginStateLine(SteamAuthStoreStatus status)
        {
            string secrets = status.HasSecrets ? "令牌密钥：已保存" : "令牌密钥：未保存";
            string login = status.HasSession ? "Steam 登录状态：已保存" : "Steam 登录状态：未登录";
            string access = status.HasAccessToken ? "AccessToken：可用" : "AccessToken：不可用";
            string refresh = status.HasRefreshToken ? "RefreshToken：可用" : "RefreshToken：不可用";
            string auto = status.HasAutoLogin ? "账号密码自动恢复：已保存" : "账号密码自动恢复：未保存";
            string cooldown = status.AutoReloginCooldownUntil > DateTime.Now
                ? $"冷却：至 {status.AutoReloginCooldownUntil:HH:mm:ss}"
                : "冷却：无";
            return $"{secrets}；{login}；{access}；{refresh}；{auto}；{cooldown}。";
        }

        private static string FirstText(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return "";
        }

        private static string BuildStatusDisplayName(SteamAuthStoreStatus status)
        {
            return FirstText(status.PersonaName, status.AccountName, "未命名令牌");
        }

        private static bool IsPlainTextRiskPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string full = Path.GetFullPath(path);
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            return full.StartsWith(desktop, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(downloads, StringComparison.OrdinalIgnoreCase);
        }

        private void AskRecyclePlainTextFile(string path)
        {
            if (GlobalPromptService.Show(
                    this,
                    "导入成功。原始 maFile/SDA JSON 仍是明文文件，放在桌面或下载目录会有泄漏风险。\n\n是否把源文件移动到回收站？",
                    "明文令牌文件风险",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            catch (Exception ex)
            {
                GlobalPromptService.Show(this, "移动到回收站失败：" + ex.Message, "Steam令牌绑定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private sealed record AuthRow(Label Label, LiteUnderlineInput Input, LiteButton? Toggle);
    }
}
