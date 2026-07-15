using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.SettingsPage;
using AntdTag = AntdUI.Tag;

namespace CS2TradeMonitor.src.UI.Framework
{
    // 「悠悠有品」新主入口专属紧凑登录条（对齐审核 Widget：YP 头像 + 悠悠有品 + 已登录胶囊 + 用户·来源 + 按钮）。
    // 复用同一套 IYouPinAuthService + YouPinAuthDialog；不改动共享的 YouPinAuthStatusCard，线上「悠悠有品」不受影响。
    internal sealed class YouPinCcAuthBar : Panel
    {
        private readonly Func<Settings?> _getConfig;
        private readonly Action? _credentialChanged;
        private readonly YouPinAuthRuntimeServices _runtimeServices;
        private readonly IYouPinAuthService _authService;
        private readonly Panel _avatar;
        private readonly Label _titleLabel;
        private readonly AntdTag _statePill;
        private readonly Label _metaLabel;
        private readonly LiteButton _btnValidate;
        private readonly LiteButton _btnClear;
        private readonly LiteButton _btnManage;

        public YouPinCcAuthBar(Func<Settings?> getConfig, Action? credentialChanged, YouPinAuthRuntimeServices runtimeServices)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);
            _getConfig = getConfig;
            _credentialChanged = credentialChanged;
            _runtimeServices = runtimeServices;
            _authService = runtimeServices.Auth;

            BackColor = Color.Transparent;
            Height = UIUtils.S(42);

            _avatar = new Panel { Size = UIUtils.S(new Size(40, 40)), BackColor = Color.Transparent };
            _avatar.Paint += PaintAvatar;

            _titleLabel = new Label
            {
                Text = "悠悠有品",
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent
            };
            _statePill = new AntdTag
            {
                Text = "未登录",
                AutoSize = false,
                Size = UIUtils.S(new Size(64, 24)),
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = UIColors.TextSub
            };
            _metaLabel = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _btnValidate = new LiteButton("校验登录", false) { Width = UIUtils.S(88), Height = UIUtils.S(38) };
            _btnClear = new LiteButton("清除凭据", false) { Width = UIUtils.S(96), Height = UIUtils.S(38) };
            _btnManage = new LiteButton("登录/管理", true) { Width = UIUtils.S(98), Height = UIUtils.S(38) };
            _btnManage.Click += (_, __) => OpenDialog();
            _btnValidate.Click += async (_, __) => await ValidateAsync();
            _btnClear.Click += (_, __) => ClearCredential();

            Controls.Add(_avatar);
            Controls.Add(_titleLabel);
            Controls.Add(_statePill);
            Controls.Add(_metaLabel);
            Controls.Add(_btnValidate);
            Controls.Add(_btnClear);
            Controls.Add(_btnManage);
            Layout += (_, __) => LayoutBar();
            RefreshState();
        }

        public void RefreshState()
        {
            YouPinAuthState state = _authService.GetState(_getConfig());
            if (state.HasCredential)
            {
                _statePill.Text = "已登录";
                _statePill.ForeColor = UIColors.Positive;
                string meta = string.IsNullOrWhiteSpace(state.NickName) ? "已保存登录凭据" : state.NickName;
                if (!string.IsNullOrWhiteSpace(state.Source))
                    meta += " · " + state.Source;
                _metaLabel.Text = meta;
            }
            else
            {
                _statePill.Text = "未登录";
                _statePill.ForeColor = UIColors.TextSub;
                _metaLabel.Text = "未登录，点击「登录/管理」使用短信登录或填写高级备用凭据。";
            }

            _btnValidate.Enabled = state.HasCredential;
            _btnClear.Enabled = state.HasCredential;
            _titleLabel.ForeColor = UIColors.TextMain;
            _avatar.Invalidate();
            LayoutBar();
        }

        private void LayoutBar()
        {
            int gap = UIUtils.S(10);
            int mid = Height / 2;
            int x = 0;
            _avatar.SetBounds(x, mid - _avatar.Height / 2, _avatar.Width, _avatar.Height);
            x = _avatar.Right + gap;
            _titleLabel.SetBounds(x, mid - _titleLabel.Height / 2, _titleLabel.Width, _titleLabel.Height);
            x = _titleLabel.Right + gap;
            _statePill.SetBounds(x, mid - _statePill.Height / 2, _statePill.Width, _statePill.Height);
            int metaLeft = _statePill.Right + gap;

            int right = ClientSize.Width;
            int requiredForAllButtons = _btnValidate.Width + _btnClear.Width + _btnManage.Width + gap * 2 + UIUtils.S(120);
            bool showClearButton = right - metaLeft >= requiredForAllButtons;
            _btnClear.Visible = showClearButton;

            _btnManage.SetBounds(Math.Max(0, right - _btnManage.Width), mid - _btnManage.Height / 2, _btnManage.Width, _btnManage.Height);
            int buttonLeft = _btnManage.Left - gap;
            if (showClearButton)
            {
                _btnClear.SetBounds(Math.Max(0, buttonLeft - _btnClear.Width), mid - _btnClear.Height / 2, _btnClear.Width, _btnClear.Height);
                buttonLeft = _btnClear.Left - gap;
            }
            else
            {
                _btnClear.SetBounds(0, mid - _btnClear.Height / 2, _btnClear.Width, _btnClear.Height);
            }

            _btnValidate.SetBounds(Math.Max(0, buttonLeft - _btnValidate.Width), mid - _btnValidate.Height / 2, _btnValidate.Width, _btnValidate.Height);

            int metaRight = _btnValidate.Left - gap;
            _metaLabel.SetBounds(metaLeft, 0, Math.Max(1, metaRight - metaLeft), Height);
        }

        private void PaintAvatar(object? sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, _avatar.Width - 1, _avatar.Height - 1);
            using var brush = new SolidBrush(UIColors.Primary);
            g.FillEllipse(brush, rect);
            TextRenderer.DrawText(
                g,
                "YP",
                new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                rect,
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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
            _btnValidate.Enabled = false;
            try
            {
                await _authService.ValidateCurrentAsync(_getConfig());
            }
            finally
            {
                RefreshState();
            }
        }

        private void ClearCredential()
        {
            if (GlobalPromptService.Show(FindForm(), "确认清除本机保存的悠悠有品登录凭据？", "悠悠有品登录", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _authService.ClearCredential(_getConfig());
            _credentialChanged?.Invoke();
            RefreshState();
        }
    }
}
