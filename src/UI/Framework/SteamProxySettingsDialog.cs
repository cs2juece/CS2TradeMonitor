using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework.SteamOffers
{
    internal sealed class SteamProxySettingsDialog : Form
    {
        private readonly SteamOfferPagePresenter _presenter;
        private readonly Action _refreshConnectionStatus;
        private readonly LiteUnderlineInput _input;
        private readonly Label _savedLabel;
        private readonly Label _resultLabel;
        private readonly LiteButton _btnSaveTest;
        private readonly LiteButton _btnClear;

        public SteamProxySettingsDialog(SteamOfferPagePresenter presenter, Action refreshConnectionStatus)
        {
            _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
            _refreshConnectionStatus = refreshConnectionStatus ?? throw new ArgumentNullException(nameof(refreshConnectionStatus));

            Text = "Steam 网络设置";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            BackColor = UIColors.MainBg;
            ForeColor = UIColors.TextMain;
            ClientSize = UIUtils.S(new Size(720, 430));

            var title = new Label
            {
                Text = "Steam 专用网络",
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            var intro = new Label
            {
                Text = "只影响 Steam 登录、报价、确认和 API Key 获取；悠悠、SteamDT、QAQ 等国内接口不走这里。",
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8.8F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            _savedLabel = new Label
            {
                Text = "当前：" + _presenter.GetManualProxyDisplay(),
                AutoSize = false,
                Padding = UIUtils.S(new Padding(12, 0, 12, 0)),
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 170, 90),
                BackColor = UIColors.CardBg,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            var usageTitle = MakeLabel("什么时候需要填写");
            usageTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            var usageText = new Label
            {
                Text = "一般不用填：程序会先尝试已缓存路径、系统代理、常见本地代理端口和直连。\r\n需要填写：Steam 登录/报价/确认超时，或代理软件已开但未被自动识别。\r\n填写后：Steam 请求优先走这里；清空后恢复自动探测。",
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8.8F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            var exampleTitle = MakeLabel("填写示例");
            exampleTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            var exampleText = new Label
            {
                Text = "Clash / Mihomo / Verge 常见：http://127.0.0.1:7890\r\nv2rayN 常见：http://127.0.0.1:10809 或 socks5://127.0.0.1:10808\r\n只填本机监听地址和端口，不要填写网页地址。",
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8.8F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            var inputLabel = MakeLabel("代理地址");
            _input = new LiteUnderlineInput("", "", "", 420)
            {
                Placeholder = "http://127.0.0.1:7890 或 socks5://127.0.0.1:10808"
            };
            _resultLabel = new Label
            {
                AutoSize = false,
                Text = "代理地址会加密保存；界面和日志只显示脱敏路径。",
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            _btnSaveTest = new LiteButton("保存并测试", true) { Width = UIUtils.S(112), Height = UIUtils.S(32) };
            _btnClear = new LiteButton("清空", false) { Width = UIUtils.S(82), Height = UIUtils.S(32) };
            var btnClose = new LiteButton("关闭", false) { Width = UIUtils.S(82), Height = UIUtils.S(32) };
            btnClose.Click += (_, __) => Close();
            _btnSaveTest.Click += async (_, __) => await SaveAndTestAsync();
            _btnClear.Click += async (_, __) => await ClearAndDetectAsync();

            Controls.AddRange(new Control[]
            {
                title,
                intro,
                _savedLabel,
                usageTitle,
                usageText,
                exampleTitle,
                exampleText,
                inputLabel,
                _input,
                _resultLabel,
                _btnSaveTest,
                _btnClear,
                btnClose
            });
            Layout += (_, __) =>
            {
                int pad = UIUtils.S(22);
                int gap = UIUtils.S(12);
                title.SetBounds(pad, UIUtils.S(16), ClientSize.Width - pad * 2, UIUtils.S(28));
                intro.SetBounds(pad, title.Bottom + UIUtils.S(4), ClientSize.Width - pad * 2, UIUtils.S(24));
                _savedLabel.SetBounds(pad, intro.Bottom + UIUtils.S(10), ClientSize.Width - pad * 2, UIUtils.S(34));

                int halfGap = UIUtils.S(16);
                int columnWidth = (ClientSize.Width - pad * 2 - halfGap) / 2;
                int guideTop = _savedLabel.Bottom + UIUtils.S(18);
                usageTitle.SetBounds(pad, guideTop, columnWidth, UIUtils.S(24));
                usageText.SetBounds(pad, usageTitle.Bottom + UIUtils.S(4), columnWidth, UIUtils.S(86));
                exampleTitle.SetBounds(pad + columnWidth + halfGap, guideTop, columnWidth, UIUtils.S(24));
                exampleText.SetBounds(exampleTitle.Left, exampleTitle.Bottom + UIUtils.S(4), columnWidth, UIUtils.S(86));

                int inputTop = usageText.Bottom + UIUtils.S(20);
                inputLabel.SetBounds(pad, inputTop, UIUtils.S(80), UIUtils.S(28));
                _input.SetBounds(inputLabel.Right + gap, inputTop, Math.Max(UIUtils.S(360), ClientSize.Width - inputLabel.Right - pad - gap), UIUtils.S(28));
                _resultLabel.SetBounds(pad, _input.Bottom + UIUtils.S(12), ClientSize.Width - pad * 2, UIUtils.S(24));
                btnClose.SetBounds(ClientSize.Width - pad - btnClose.Width, ClientSize.Height - pad - btnClose.Height, btnClose.Width, btnClose.Height);
                _btnClear.SetBounds(btnClose.Left - UIUtils.S(10) - _btnClear.Width, btnClose.Top, _btnClear.Width, _btnClear.Height);
                _btnSaveTest.SetBounds(_btnClear.Left - UIUtils.S(10) - _btnSaveTest.Width, btnClose.Top, _btnSaveTest.Width, _btnSaveTest.Height);
            };
        }

        private async System.Threading.Tasks.Task SaveAndTestAsync()
        {
            string value = _input.Inner.Text.Trim();
            if (!_presenter.SaveManualProxy(value, out string message))
            {
                _resultLabel.Text = message;
                _resultLabel.ForeColor = UIColors.TextWarn;
                return;
            }

            _savedLabel.Text = "当前：" + _presenter.GetManualProxyDisplay();
            _resultLabel.Text = message + " 正在测试...";
            _resultLabel.ForeColor = UIColors.TextSub;
            _btnSaveTest.Enabled = false;
            _btnClear.Enabled = false;
            try
            {
                var profile = await _presenter.ResolveConnectionAsync(force: true, CancellationToken.None);
                _resultLabel.Text = profile.IsUsable
                    ? "测试成功：" + _presenter.FormatConnectionRoute(profile)
                    : "测试失败：" + profile.FailureReason;
                _resultLabel.ForeColor = profile.IsUsable ? Color.FromArgb(0, 170, 90) : UIColors.TextWarn;
                _refreshConnectionStatus();
            }
            finally
            {
                if (!_btnSaveTest.IsDisposed)
                    _btnSaveTest.Enabled = true;
                if (!_btnClear.IsDisposed)
                    _btnClear.Enabled = true;
            }
        }

        private async System.Threading.Tasks.Task ClearAndDetectAsync()
        {
            _presenter.SaveManualProxy("", out string message);
            _input.Inner.Text = "";
            _savedLabel.Text = "当前：未设置";
            _resultLabel.Text = message + " 正在重新检测...";
            _resultLabel.ForeColor = UIColors.TextSub;
            var profile = await _presenter.ResolveConnectionAsync(force: true, CancellationToken.None);
            _resultLabel.Text = profile.IsUsable
                ? "已切换：" + _presenter.FormatConnectionRoute(profile)
                : "检测失败：" + profile.FailureReason;
            _resultLabel.ForeColor = profile.IsUsable ? Color.FromArgb(0, 170, 90) : UIColors.TextWarn;
            _refreshConnectionStatus();
        }

        private static Label MakeLabel(string text) => new Label
        {
            Text = text,
            AutoSize = false,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = UIColors.TextMain,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }
}
