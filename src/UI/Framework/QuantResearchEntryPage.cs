using System;
using System.Drawing;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.Helpers;

namespace CS2TradeMonitor.src.UI.Framework
{
    public sealed class QuantResearchEntryHostPage : FrameworkSettingsHostPage<QuantResearchEntryPage>
    {
        public QuantResearchEntryHostPage()
            : base(new QuantResearchEntryPage())
        {
        }
    }

    public sealed class QuantResearchEntryPage : FrameworkSettingsPageBase
    {
        private const string AspNetCoreRuntimeDownloadUrl =
            "https://aka.ms/dotnet/10.0/aspnetcore-runtime-win-x64.exe";
        private readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(2.5)
        };
        private readonly Uri _serviceUrl;
        private readonly Label _statusLabel;
        private readonly Label _statusDetailLabel;
        private readonly Label _addressLabel;
        private readonly Panel _heroPanel;
        private readonly LiteButton _startButton;
        private readonly QuantResearchServiceLauncher _serviceLauncher;
        private readonly bool _canStartLocalService;
        private QuantResearchServiceState _lastServiceState = QuantResearchServiceState.Offline;
        private bool _startingService;

        public QuantResearchEntryPage()
        {
            _serviceUrl = QuantResearchEntryPageModel.ResolveUrl();
            _canStartLocalService = QuantResearchEntryPageModel.CanStartLocalService(_serviceUrl);

            var entryGroup = new LiteSettingsGroup("量化研究网页");
            _heroPanel = new Panel
            {
                Height = UIUtils.S(206),
                BackColor = UIColors.CardBg,
                Margin = Padding.Empty
            };

            var eyebrow = CreateLabel("独立网页模块", strong: true);
            eyebrow.ForeColor = UIColors.Primary;
            eyebrow.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);

            var title = CreateLabel("CS2 量化研究台", strong: true);
            title.Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold);

            var description = CreateLabel(
                "集中查看 K 线、均线、MACD、精简缠论和价格信号。网页独立运行，不读取账户或执行交易。 ");
            description.ForeColor = UIColors.TextSub;

            _statusLabel = CreateLabel("正在检查服务", strong: true);
            _statusLabel.TextAlign = ContentAlignment.MiddleCenter;
            _statusLabel.Padding = UIUtils.S(new Padding(10, 0, 10, 0));

            _statusDetailLabel = CreateLabel("正在连接本机量化服务…");
            _statusDetailLabel.ForeColor = UIColors.TextSub;

            _addressLabel = CreateLabel(_serviceUrl.AbsoluteUri);
            _addressLabel.Font = new Font("Consolas", 8.5F, FontStyle.Regular);
            _addressLabel.ForeColor = UIColors.Link;

            _serviceLauncher = new QuantResearchServiceLauncher(
                QuantResearchServiceProcessHost.Instance,
                cancellationToken => QuantResearchEntryPageModel.CheckAvailabilityAsync(
                    _serviceUrl,
                    _httpClient,
                    cancellationToken),
                Task.Delay,
                startupPollAttempts: 20,
                startupPollInterval: TimeSpan.FromMilliseconds(250),
                serviceUrl: _serviceUrl);

            _startButton = new LiteButton(_canStartLocalService ? "启动服务" : "远程服务", true)
            {
                Width = UIUtils.S(104),
                Height = UIUtils.S(36),
                Enabled = _canStartLocalService
            };
            var openButton = new LiteButton("打开量化网页", false)
            {
                Width = UIUtils.S(132),
                Height = UIUtils.S(36)
            };
            var refreshButton = new LiteButton("刷新状态", false)
            {
                Width = UIUtils.S(104),
                Height = UIUtils.S(36)
            };
            var copyButton = new LiteButton("复制地址", false)
            {
                Width = UIUtils.S(104),
                Height = UIUtils.S(36)
            };

            _startButton.Click += async (_, __) => await StartServiceAsync();
            openButton.Click += (_, __) => SystemActions.OpenUrl(_serviceUrl.AbsoluteUri);
            refreshButton.Click += async (_, __) => await RefreshAvailabilityAsync();
            copyButton.Click += (_, __) => CopyAddress();

            _heroPanel.Controls.AddRange(new Control[]
            {
                eyebrow,
                title,
                description,
                _statusLabel,
                _statusDetailLabel,
                _addressLabel,
                _startButton,
                openButton,
                refreshButton,
                copyButton
            });
            _heroPanel.Layout += (_, __) => LayoutHero(
                eyebrow,
                title,
                description,
                _startButton,
                openButton,
                refreshButton,
                copyButton);
            entryGroup.AddFullItem(_heroPanel);

            var boundaryGroup = new LiteSettingsGroup("功能边界");
            AddHint(boundaryGroup, "包含：日 K、MA5/10/20、MACD、分型/笔/线段/中枢、候选背驰、趋势突破、恐慌底、高位风险、轻量回看和 CSV 导出。 ");
            AddHint(boundaryGroup, "不包含：模拟仓、账户、持仓、订单、自动交易、Python/vn.py 运行时、参数搜索和机器学习。 ");
            AddHint(boundaryGroup, "量化服务读取“大盘数据源”中保存的 SteamDT API 与 QAQ Token，不读取 Windows 全局密钥。 ");
            AddGroupToPage(boundaryGroup);
            AddGroupToPage(entryGroup);
        }

        public override void Activate()
        {
            base.Activate();
            _heroPanel.PerformLayout();
            _ = RefreshAvailabilityAsync();
        }

        public override void RequestViewportRelayout()
        {
            base.RequestViewportRelayout();
            _heroPanel.PerformLayout();
        }

        public override void ApplySystemTheme()
        {
            base.ApplySystemTheme();
            _heroPanel.BackColor = UIColors.CardBg;
            _addressLabel.ForeColor = UIColors.Link;
            ApplyStatusColors(_lastServiceState);
        }

        private void LayoutHero(
            Control eyebrow,
            Control title,
            Control description,
            Control startButton,
            Control openButton,
            Control refreshButton,
            Control copyButton)
        {
            QuantResearchEntryHeroLayout layout = QuantResearchEntryHeroLayoutModel.Build(
                _heroPanel.ClientSize,
                FrameworkSettingsPageLayoutHelper.CalculateVisibleWidthWithinForm(_heroPanel),
                startButton.Size,
                openButton.Size,
                refreshButton.Size,
                copyButton.Size,
                UIUtils.ScaleFactor);
            eyebrow.Bounds = layout.Eyebrow;
            title.Bounds = layout.Title;
            description.Bounds = layout.Description;
            _statusLabel.Bounds = layout.Status;
            _statusDetailLabel.Bounds = layout.StatusDetail;
            _addressLabel.Bounds = layout.Address;
            startButton.Bounds = layout.StartButton;
            openButton.Bounds = layout.OpenButton;
            refreshButton.Bounds = layout.RefreshButton;
            copyButton.Bounds = layout.CopyButton;
            if (_heroPanel.Height != layout.RequiredHeight)
                _heroPanel.Height = layout.RequiredHeight;
        }

        private async Task RefreshAvailabilityAsync()
        {
            _statusLabel.Text = "正在检查";
            _statusDetailLabel.Text = "正在连接本机量化服务…";
            _lastServiceState = QuantResearchServiceState.Offline;
            ApplyStatusColors(QuantResearchServiceState.Offline);
            QuantResearchServiceStatus status;
            try
            {
                status = await QuantResearchEntryPageModel.CheckAvailabilityAsync(
                    _serviceUrl,
                    _httpClient,
                    PageToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (IsDisposed || PageToken.IsCancellationRequested)
                return;
            _statusLabel.Text = status.Text;
            _statusDetailLabel.Text = status.Detail;
            _lastServiceState = status.State;
            ApplyStatusColors(status.State);
        }

        private async Task StartServiceAsync()
        {
            if (_startingService || !_canStartLocalService)
                return;

            _startingService = true;
            _startButton.Enabled = false;
            _statusLabel.Text = "正在启动";
            _statusDetailLabel.Text = "正在检查运行环境并启动本机量化服务…";
            _lastServiceState = QuantResearchServiceState.Offline;
            ApplyStatusColors(_lastServiceState);
            try
            {
                QuantResearchServiceLaunchResult result = await _serviceLauncher.StartAsync(PageToken);
                if (IsDisposed || PageToken.IsCancellationRequested)
                    return;

                switch (result.State)
                {
                    case QuantResearchServiceLaunchState.AlreadyRunning:
                    case QuantResearchServiceLaunchState.Started:
                        _statusLabel.Text = "本地服务已就绪";
                        _statusDetailLabel.Text = result.Detail;
                        _lastServiceState = QuantResearchServiceState.Online;
                        ApplyStatusColors(_lastServiceState);
                        break;
                    case QuantResearchServiceLaunchState.MissingMarketDataSourceCredential:
                        ShowMissingMarketDataSourcePrompt(result.Detail);
                        break;
                    case QuantResearchServiceLaunchState.MissingRuntime:
                        ShowMissingRuntimePrompt();
                        break;
                    case QuantResearchServiceLaunchState.MissingExecutable:
                    case QuantResearchServiceLaunchState.Failed:
                        _statusLabel.Text = "启动失败";
                        _statusDetailLabel.Text = result.Detail;
                        GlobalPromptService.Show(
                            FindForm(),
                            result.Detail,
                            "量化研究服务",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // Page deactivation or application shutdown cancelled the pending start.
            }
            finally
            {
                _startingService = false;
                if (!IsDisposed)
                    _startButton.Enabled = _canStartLocalService;
            }
        }

        private void ShowMissingMarketDataSourcePrompt(string detail)
        {
            _statusLabel.Text = "未配置大盘数据源";
            _statusDetailLabel.Text = detail;
            _lastServiceState = QuantResearchServiceState.Offline;
            ApplyStatusColors(_lastServiceState);
            GlobalPromptService.Show(
                FindForm(),
                detail + "\n\n请打开设置中的“大盘数据源”，填写并保存 SteamDT API 后重试。",
                "需要配置大盘数据源",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private void ShowMissingRuntimePrompt()
        {
            _statusLabel.Text = "缺少运行环境";
            _statusDetailLabel.Text = "请安装 Microsoft ASP.NET Core Runtime 10（x64）后重试。";
            if (GlobalPromptService.Show(
                    FindForm(),
                    "量化研究服务需要 Microsoft ASP.NET Core Runtime 10（x64）微软官方组件。\n\n" +
                    "安装完成后，请返回本页面再次点击“启动服务”。\n\n" +
                    "是否立即打开微软官方下载地址？",
                    "需要安装运行环境",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                SystemActions.OpenUrl(AspNetCoreRuntimeDownloadUrl);
            }
        }

        private void ApplyStatusColors(QuantResearchServiceState state)
        {
            Color color = state switch
            {
                QuantResearchServiceState.Online => UIColors.Positive,
                QuantResearchServiceState.InvalidAddress => UIColors.TextCrit,
                _ => UIColors.TextWarn
            };
            _statusLabel.ForeColor = color;
            _statusLabel.BackColor = Color.FromArgb(UIColors.IsDark ? 38 : 22, color);
        }

        private void CopyAddress()
        {
            try
            {
                Clipboard.SetText(_serviceUrl.AbsoluteUri);
                _statusDetailLabel.Text = "地址已复制到剪贴板。 ";
            }
            catch
            {
                _statusDetailLabel.Text = "复制失败，请手动复制页面中的地址。 ";
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _httpClient.Dispose();
            base.Dispose(disposing);
        }
    }
}
