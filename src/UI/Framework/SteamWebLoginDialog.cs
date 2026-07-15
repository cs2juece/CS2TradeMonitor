using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework.SteamOffers
{
    public sealed class SteamLoginStateCapturedEventArgs : EventArgs
    {
        public SteamLoginStateCapturedEventArgs(
            string sessionId,
            string steamLoginSecure,
            string steamLogin,
            string accessToken,
            string refreshToken,
            string steamId)
        {
            SessionId = sessionId;
            SteamLoginSecure = steamLoginSecure;
            SteamLogin = steamLogin;
            AccessToken = accessToken;
            RefreshToken = refreshToken;
            SteamId = steamId;
        }

        public string SessionId { get; }
        public string SteamLoginSecure { get; }
        public string SteamLogin { get; }
        public string AccessToken { get; }
        public string RefreshToken { get; }
        public string SteamId { get; }
    }

    public sealed class SteamWebLoginDialog : Form
    {
        private readonly WebView2 _webView;
        private readonly Label _status;
        private readonly LiteButton _captureButton;
        private readonly LiteButton _externalButton;
        private readonly LiteButton _closeButton;
        private readonly string _profileDir;
        private bool _initialized;

        public string SessionId { get; private set; } = "";
        public string SteamLoginSecure { get; private set; } = "";
        public string SteamLogin { get; private set; } = "";
        public event EventHandler<SteamLoginStateCapturedEventArgs>? LoginStateCaptured;

        public SteamWebLoginDialog()
        {
            Text = "Steam 网页登录";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            BackColor = UIColors.MainBg;
            Font = new Font("Microsoft YaHei UI", 9F);
            MinimumSize = UIUtils.S(new Size(900, 600));
            ClientSize = UIUtils.S(new Size(980, 720));

            _profileDir = Path.Combine(Path.GetTempPath(), "CS2TradeMonitor-WebView2-" + Guid.NewGuid().ToString("N"));

            _status = new Label
            {
                AutoSize = false,
                ForeColor = UIColors.TextSub,
                BackColor = UIColors.CardBg,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "请在窗口内登录 Steam。登录完成后点击“读取并保存登录状态”。"
            };

            _captureButton = new LiteButton("读取并保存登录状态", true) { Width = UIUtils.S(160), Height = UIUtils.S(32) };
            _externalButton = new LiteButton("外部浏览器", false) { Width = UIUtils.S(100), Height = UIUtils.S(32) };
            _closeButton = new LiteButton("关闭", false) { Width = UIUtils.S(80), Height = UIUtils.S(32) };
            _webView = new WebView2
            {
                DefaultBackgroundColor = UIColors.MainBg
            };

            Controls.AddRange(new Control[] { _status, _webView, _captureButton, _externalButton, _closeButton });
            Layout += (_, __) => LayoutChildren();
            Load += async (_, __) => await InitializeWebViewAsync();
            FormClosed += (_, __) => CleanupProfileDir();

            _captureButton.Click += async (_, __) => await CaptureCookiesAsync();
            _externalButton.Click += (_, __) => OpenExternalLogin();
            _closeButton.Click += (_, __) => Close();
        }

        private void LayoutChildren()
        {
            int pad = UIUtils.S(10);
            int buttonGap = UIUtils.S(8);
            int topHeight = UIUtils.S(48);
            _status.SetBounds(pad, pad, Math.Max(1, ClientSize.Width - pad * 2 - UIUtils.S(370)), UIUtils.S(32));
            _closeButton.SetBounds(ClientSize.Width - pad - _closeButton.Width, pad, _closeButton.Width, _closeButton.Height);
            _externalButton.SetBounds(_closeButton.Left - buttonGap - _externalButton.Width, pad, _externalButton.Width, _externalButton.Height);
            _captureButton.SetBounds(_externalButton.Left - buttonGap - _captureButton.Width, pad, _captureButton.Width, _captureButton.Height);
            _webView.SetBounds(pad, topHeight, Math.Max(1, ClientSize.Width - pad * 2), Math.Max(1, ClientSize.Height - topHeight - pad));
        }

        private async Task InitializeWebViewAsync()
        {
            if (_initialized)
                return;

            try
            {
                Directory.CreateDirectory(_profileDir);
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _profileDir);
                await _webView.EnsureCoreWebView2Async(environment);
                _webView.CoreWebView2.NavigationCompleted += (_, args) =>
                {
                    _status.ForeColor = args.IsSuccess ? UIColors.TextSub : UIColors.TextWarn;
                    _status.Text = args.IsSuccess
                        ? "登录完成后点击“读取并保存登录状态”。本窗口不会记录 Cookie 明文。"
                        : $"页面加载失败：{args.WebErrorStatus}";
                };
                _webView.CoreWebView2.Navigate(SteamUrls.WebLoginHome);
                _initialized = true;
            }
            catch (Exception ex)
            {
                _status.ForeColor = UIColors.TextWarn;
                _status.Text = "WebView2 初始化失败，请安装/修复 WebView2 Runtime，或使用外部浏览器后手填登录状态。原因：" + ex.Message;
            }
        }

        private async Task CaptureCookiesAsync()
        {
            if (_webView.CoreWebView2 == null)
            {
                _status.ForeColor = UIColors.TextWarn;
                _status.Text = "WebView2 尚未就绪。";
                return;
            }

            try
            {
                var cookies = await ReadSteamCookiesAsync();
                cookies.TryGetValue("sessionid", out var sessionId);
                cookies.TryGetValue("steamLoginSecure", out var secure);
                cookies.TryGetValue("steamLogin", out var login);
                cookies.TryGetValue("steamRefresh_steam", out var refreshToken);

                if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(secure))
                {
                    _status.ForeColor = UIColors.TextWarn;
                    _status.Text = "未读取到 sessionid / steamLoginSecure。请确认已登录 Steam Community 后再读取。";
                    return;
                }

                SessionId = sessionId;
                SteamLoginSecure = secure;
                SteamLogin = login ?? "";
                LoginStateCaptured?.Invoke(this, new SteamLoginStateCapturedEventArgs(
                    SessionId,
                    SteamLoginSecure,
                    SteamLogin,
                    ExtractAccessTokenFromSteamLoginSecure(SteamLoginSecure),
                    refreshToken ?? "",
                    ExtractSteamIdFromSteamLoginSecure(SteamLoginSecure)));
                Close();
            }
            catch (Exception ex)
            {
                _status.ForeColor = UIColors.TextWarn;
                _status.Text = "读取登录状态失败：" + ex.Message;
            }
        }

        private async Task<Dictionary<string, string>> ReadSteamCookiesAsync()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] urls =
            {
                SteamUrls.StoreBase + "/",
                SteamUrls.CommunityBase + "/"
            };

            foreach (string url in urls)
            {
                var cookies = await _webView.CoreWebView2.CookieManager.GetCookiesAsync(url);
                foreach (var cookie in cookies.Where(c => !string.IsNullOrWhiteSpace(c.Name)))
                    result[cookie.Name] = cookie.Value;
            }

            return result;
        }

        private static void OpenExternalLogin()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = SteamUrls.WebLoginHome,
                UseShellExecute = true
            });
        }

        private static string ExtractSteamIdFromSteamLoginSecure(string value)
        {
            string text = Uri.UnescapeDataString((value ?? "").Trim());
            int separator = text.IndexOf("||", StringComparison.Ordinal);
            if (separator <= 0)
                return "";
            string steamId = text[..separator].Trim();
            return steamId.All(char.IsDigit) ? steamId : "";
        }

        private static string ExtractAccessTokenFromSteamLoginSecure(string value)
        {
            string text = Uri.UnescapeDataString((value ?? "").Trim());
            int separator = text.IndexOf("||", StringComparison.Ordinal);
            if (separator < 0 || separator + 2 >= text.Length)
                return "";
            string token = text[(separator + 2)..].Trim();
            return token.Count(c => c == '.') == 2 ? token : "";
        }

        private void CleanupProfileDir()
        {
            try
            {
                _webView.Dispose();
                if (Directory.Exists(_profileDir))
                    Directory.Delete(_profileDir, recursive: true);
            }
            catch
            {
                // WebView2 can keep files locked briefly after close; temporary folders are harmless if cleanup is retried by the OS later.
            }
        }
    }
}
