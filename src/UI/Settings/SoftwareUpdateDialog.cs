using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.Framework;
using CS2TradeMonitor.src.UI.Helpers;

namespace CS2TradeMonitor.src.UI.SettingsPage
{
    public sealed class SoftwareUpdateDialog : Form
    {
        private readonly SoftwareUpdateCheckResult _update;
        private CancellationTokenSource _cts = new();
        private readonly ISoftwareUpdateService _softwareUpdates;
        private readonly Label _statusLabel;
        private readonly ProgressBar _progress;
        private readonly Label _trustLabel;
        private readonly LiteButton _downloadButton;
        private readonly LiteButton _cancelButton;
        private bool _downloading;

        public SoftwareUpdateDialog(SoftwareUpdateCheckResult update)
            : this(update, UIFrameworkRuntimeServices.ResolveSoftwareUpdates())
        {
        }

        internal SoftwareUpdateDialog(SoftwareUpdateCheckResult update, ISoftwareUpdateService softwareUpdates)
        {
            _update = update ?? throw new ArgumentNullException(nameof(update));
            _softwareUpdates = softwareUpdates ?? throw new ArgumentNullException(nameof(softwareUpdates));

            Text = "CS2 交易监控 · 软件更新";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = UIColors.MainBg;
            ForeColor = UIColors.TextMain;
            ClientSize = UIUtils.S(new Size(700, 500));
            Font = new Font("Microsoft YaHei UI", 9F);

            UIColors.ApplyNativeTheme(this);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = UIUtils.S(new Padding(24, 18, 24, 18)),
                BackColor = UIColors.MainBg,
                RowCount = 8,
                ColumnCount = 1
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(54)));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(66)));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(36)));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(24)));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(34)));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(30)));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(46)));
            Controls.Add(root);

            var title = new Label
            {
                Text = $"发现新版本 v{_update.Manifest?.Version}",
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
                ForeColor = UIColors.TextMain,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };
            root.Controls.Add(title, 0, 0);

            var meta = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 3,
                RowCount = 1,
                Margin = Padding.Empty
            };
            meta.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            meta.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            meta.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            meta.Controls.Add(CreateInfoBlock("当前版本", $"v{_update.CurrentVersion}", new Padding(0, 0, 6, 0)), 0, 0);
            meta.Controls.Add(CreateInfoBlock(
                "发布时间",
                SoftwareUpdateDialogPresentation.FormatReleaseTime(_update.Manifest?.ReleaseDate),
                new Padding(3, 0, 3, 0)), 1, 0);
            meta.Controls.Add(CreateInfoBlock("安装包大小", FormatSize(_update.Asset?.SizeBytes ?? 0), new Padding(6, 0, 0, 0)), 2, 0);
            root.Controls.Add(meta, 0, 1);

            var changelogTitle = new Label
            {
                Text = "更新内容",
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                ForeColor = UIColors.TextMain,
                TextAlign = ContentAlignment.BottomLeft,
                BackColor = Color.Transparent,
                Padding = UIUtils.S(new Padding(0, 0, 0, 6))
            };
            root.Controls.Add(changelogTitle, 0, 2);

            var changelog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = UIColors.CardBg,
                ForeColor = UIColors.TextMain,
                Font = new Font("Microsoft YaHei UI", 9F),
                Text = SoftwareUpdateDialogPresentation.FormatChangelog(
                    _update.Manifest?.Changelog,
                    _update.Manifest?.Version),
                DetectUrls = false,
                TabStop = false
            };
            root.Controls.Add(changelog, 0, 3);

            _progress = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            root.Controls.Add(_progress, 0, 4);

            var trustPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UIColors.CardBg,
                ColumnCount = 2,
                RowCount = 1,
                Margin = UIUtils.S(new Padding(0, 6, 0, 0)),
                Padding = UIUtils.S(new Padding(10, 0, 10, 0))
            };
            trustPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            trustPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));

            var sourceLabel = new Label
            {
                Text = $"更新来源：{SoftwareUpdateDialogPresentation.FormatSourceName(_update.ManifestSourceName)}",
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = UIColors.TextSub,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
            _trustLabel = new Label
            {
                Text = SoftwareUpdateDialogPresentation.BuildTrustMessage(_update.Asset?.Sha256),
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = UIColors.Positive,
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
            trustPanel.Controls.Add(sourceLabel, 0, 0);
            trustPanel.Controls.Add(_trustLabel, 1, 0);
            root.Controls.Add(trustPanel, 0, 5);

            _statusLabel = new Label
            {
                Text = "准备就绪。点击“立即更新”后将下载并校验安装包。",
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextSub,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
            root.Controls.Add(_statusLabel, 0, 6);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Color.Transparent
            };
            root.Controls.Add(buttons, 0, 7);

            _downloadButton = new LiteButton("立即更新", true)
            {
                Width = UIUtils.S(108),
                Height = UIUtils.S(32),
                Margin = UIUtils.S(new Padding(8, 8, 0, 0))
            };
            _cancelButton = new LiteButton("取消", false)
            {
                Width = UIUtils.S(86),
                Height = UIUtils.S(32),
                Margin = UIUtils.S(new Padding(8, 8, 0, 0))
            };
            var openButton = new LiteButton("打开发布页", false)
            {
                Width = UIUtils.S(104),
                Height = UIUtils.S(32),
                Margin = UIUtils.S(new Padding(8, 8, 0, 0))
            };
            var copyButton = new LiteButton("复制下载链接", false)
            {
                Width = UIUtils.S(116),
                Height = UIUtils.S(32),
                Margin = UIUtils.S(new Padding(8, 8, 0, 0))
            };
            var qqUpdateButton = new LiteButton("官方 QQ 群更新", false)
            {
                Width = UIUtils.S(140),
                Height = UIUtils.S(32),
                Margin = UIUtils.S(new Padding(8, 8, 0, 0))
            };

            _downloadButton.Click += async (_, __) => await DownloadAndInstallAsync();
            _cancelButton.Click += (_, __) =>
            {
                if (_downloading)
                    _cts.Cancel();
                else
                    Close();
            };
            openButton.Click += (_, __) => SystemActions.OpenUrl(SupportInfo.GetReleaseDownloadPage());
            copyButton.Click += (_, __) => CopyDownloadLink();
            qqUpdateButton.Click += (_, __) => OpenOfficialQqGroup();

            buttons.Controls.Add(_downloadButton);
            buttons.Controls.Add(_cancelButton);
            buttons.Controls.Add(qqUpdateButton);
            buttons.Controls.Add(copyButton);
            buttons.Controls.Add(openButton);

            AcceptButton = _downloadButton;
            CancelButton = _cancelButton;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_downloading && !_cts.IsCancellationRequested)
            {
                var confirm = GlobalPromptService.Show(this, "更新包正在下载，确定要取消吗？", "取消更新",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }

                _cts.Cancel();
            }

            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _cts.Dispose();
            base.Dispose(disposing);
        }

        private async Task DownloadAndInstallAsync()
        {
            if (_downloading) return;

            if (_cts.IsCancellationRequested)
            {
                _cts.Dispose();
                _cts = new CancellationTokenSource();
            }

            _downloading = true;
            _downloadButton.Enabled = false;
            _cancelButton.Text = "取消下载";
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.MarqueeAnimationSpeed = 30;
            _progress.Value = 0;
            _statusLabel.ForeColor = UIColors.TextSub;
            _statusLabel.Text = "正在连接官方 Release；网络较慢时可使用“官方 QQ 群更新”。";

            try
            {
                var progress = new Progress<SoftwareUpdateProgress>(p =>
                {
                    if (p.TotalBytes.HasValue && p.TotalBytes.Value > 0)
                    {
                        _progress.Style = ProgressBarStyle.Blocks;
                        _progress.Value = Math.Clamp(p.Percent, 0, 100);
                    }
                    else if (_progress.Style != ProgressBarStyle.Marquee)
                        _progress.Style = ProgressBarStyle.Marquee;

                    string total = p.TotalBytes.HasValue ? SoftwareUpdateService.FormatBytes(p.TotalBytes.Value) : "未知大小";
                    string speed = p.BytesPerSecond > 0 ? $"{SoftwareUpdateService.FormatBytes((long)p.BytesPerSecond)}/s" : "校验中";
                    string source = SoftwareUpdateDialogPresentation.FormatSourceName(p.SourceName);
                    _statusLabel.Text = $"正在从 {source} 下载：{SoftwareUpdateService.FormatBytes(p.BytesReceived)} / {total}    {speed}";
                });

                var downloaded = await _softwareUpdates.DownloadAsync(_update, progress, _cts.Token);
                _progress.Style = ProgressBarStyle.Blocks;
                _progress.Value = 100;
                _statusLabel.Text = "下载完成，SHA-256 校验通过。";
                _statusLabel.ForeColor = UIColors.Positive;
                _trustLabel.Text = "官方发布 · SHA-256 校验通过";

                var confirm = GlobalPromptService.Show(this,
                    "更新包已下载并通过校验。\n\n现在将关闭软件并覆盖安装，更新完成后会自动重启。",
                    "安装更新",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Information);
                if (confirm != DialogResult.OK)
                {
                    _statusLabel.Text = "已下载，用户取消安装。";
                    return;
                }

                _softwareUpdates.LaunchUpdater(downloaded);
            }
            catch (OperationCanceledException)
            {
                _statusLabel.Text = "已取消下载。";
                _progress.Style = ProgressBarStyle.Blocks;
                _progress.Value = 0;
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("SoftwareUpdate", "Downloading or launching update failed.", ex);
                _statusLabel.ForeColor = UIColors.TextCrit;
                string error = SoftwareUpdateService.GetFriendlyError(ex);
                _statusLabel.Text = "更新失败：" + error;
                if (error.Contains("长时间无响应", StringComparison.Ordinal))
                    _statusLabel.Text += "。请重试或使用官方 QQ 群更新。";
            }
            finally
            {
                _downloading = false;
                _downloadButton.Enabled = true;
                _cancelButton.Text = "关闭";
            }
        }

        private static string FormatSize(long sizeBytes)
        {
            return sizeBytes > 0 ? SoftwareUpdateService.FormatBytes(sizeBytes) : "未知";
        }

        private static Control CreateInfoBlock(string caption, string value, Padding margin)
        {
            var block = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UIColors.CardBg,
                ColumnCount = 1,
                RowCount = 2,
                Margin = UIUtils.S(margin),
                Padding = UIUtils.S(new Padding(12, 7, 12, 7))
            };
            block.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            block.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            block.Controls.Add(new Label
            {
                Text = caption,
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 8F),
                ForeColor = UIColors.TextSub,
                TextAlign = ContentAlignment.BottomLeft,
                BackColor = Color.Transparent
            }, 0, 0);
            block.Controls.Add(new Label
            {
                Text = value,
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                ForeColor = UIColors.TextMain,
                TextAlign = ContentAlignment.TopLeft,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            }, 0, 1);
            return block;
        }

        private void CopyDownloadLink()
        {
            string url = _update.DownloadUrl;
            if (string.IsNullOrWhiteSpace(url) && _update.Asset != null)
            {
                var urls = _update.Asset.GetUrls();
                if (urls.Count > 0)
                    url = urls[0];
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                _statusLabel.ForeColor = UIColors.TextWarn;
                _statusLabel.Text = "当前更新没有可复制的下载链接，可打开官方发布页获取新版。";
                return;
            }

            try
            {
                Clipboard.SetText(url);
                _statusLabel.ForeColor = UIColors.Positive;
                _statusLabel.Text = "下载链接已复制。";
            }
            catch (Exception ex)
            {
                _statusLabel.ForeColor = UIColors.TextWarn;
                _statusLabel.Text = "复制失败：" + ex.Message;
            }
        }

        private void OpenOfficialQqGroup()
        {
            try
            {
                SupportInfo.OpenQqGroup();
                _statusLabel.ForeColor = UIColors.Positive;
                _statusLabel.Text = "已打开官方 QQ 群加群页面，可在群内获取更新帮助。";
            }
            catch (Exception ex)
            {
                SupportInfo.ShowOpenFailure(ex, this);
            }
        }

    }
}
