using System.Drawing;
using System.Windows.Forms;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Market;
using CS2TradeMonitor.Domain.InventoryMonitoring;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.Helpers;

namespace CS2TradeMonitor.src.UI.Framework
{
    public sealed class LocalInventoryMonitorHostPage : FrameworkSettingsHostPage<LocalInventoryMonitorPage>
    {
        public LocalInventoryMonitorHostPage()
            : base(new LocalInventoryMonitorPage())
        {
        }
    }

    public sealed class LocalInventoryMonitorPage : FrameworkSettingsPageBase
    {
        private readonly ILocalInventoryMonitorService _monitor;
        private readonly Label _statusLabel;
        private readonly Label _statusDetailLabel;
        private readonly LiteTextBox _targetsText;
        private readonly LiteTextBox _eventsText;
        private readonly LiteTextBox _watchListInput;
        private readonly LiteButton _refreshButton;
        private readonly Panel _statusPanel;
        private bool _refreshing;

        public LocalInventoryMonitorPage()
            : this(LocalInventoryMonitorPageRuntimeServices.Resolve())
        {
        }

        internal LocalInventoryMonitorPage(LocalInventoryMonitorPageRuntimeServices runtimeServices)
        {
            _monitor = (runtimeServices ?? throw new ArgumentNullException(nameof(runtimeServices))).Monitor;

            var configurationGroup = new LiteSettingsGroup("本机后台监控");
            AddToggle(
                configurationGroup,
                "启用库存监控",
                nameof(Settings.LocalInventoryMonitorEnabled),
                false);
            AddInt(
                configurationGroup,
                "检查间隔",
                nameof(Settings.LocalInventoryRefreshMinutes),
                5,
                "分钟",
                80,
                LocalInventoryMonitorPageModel.NormalizeRefreshMinutes);
            AddInt(
                configurationGroup,
                "最小异动数量",
                nameof(Settings.LocalInventoryMinimumChangeCount),
                1,
                "件",
                80,
                LocalInventoryMonitorPageModel.NormalizeMinimumChangeCount);
            AddToggle(
                configurationGroup,
                "同时发送手机提醒",
                nameof(Settings.LocalInventoryPhoneAlertEnabled),
                false);
            AddHint(configurationGroup, "监控由本机程序后台执行，依赖“大盘数据源”中已保存的 CSQAQ Token；无需浏览器常驻，也不会读取 Cookie。");
            AddHint(configurationGroup, "CSQAQ 官方接口限制为单 IP 每秒 1 次；本功能统一串行限流，最多保存 20 个观察对象。首次读取只建立基线。 ");

            var statusGroup = new LiteSettingsGroup("运行状态");
            _statusPanel = new Panel
            {
                Height = UIUtils.S(152),
                BackColor = UIColors.CardBg,
                Margin = Padding.Empty
            };
            _statusLabel = CreateLabel("未启动", strong: true);
            _statusLabel.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            _statusDetailLabel = CreateLabel("启用后，软件将在本机后台定时检查。 ");
            _statusDetailLabel.ForeColor = UIColors.TextSub;

            _refreshButton = new LiteButton("立即刷新", true)
            {
                Width = UIUtils.S(104),
                Height = UIUtils.S(34)
            };
            var openCsqaqButton = new LiteButton("打开 CSQAQ", false)
            {
                Width = UIUtils.S(116),
                Height = UIUtils.S(34)
            };
            var openSteamDtButton = new LiteButton("打开 SteamDT 库存", false)
            {
                Width = UIUtils.S(146),
                Height = UIUtils.S(34)
            };
            _refreshButton.Click += async (_, __) => await SaveAndRefreshAsync();
            openCsqaqButton.Click += (_, __) => SystemActions.OpenUrl(CsqaqUrls.WebBase + "/home");
            openSteamDtButton.Click += (_, __) => SystemActions.OpenUrl("https://www.steamdt.com/inventory");
            _statusPanel.Controls.AddRange(new Control[]
            {
                _statusLabel,
                _statusDetailLabel,
                _refreshButton,
                openCsqaqButton,
                openSteamDtButton
            });
            _statusPanel.Layout += (_, __) => LayoutStatusPanel(openCsqaqButton, openSteamDtButton);
            statusGroup.AddFullItem(_statusPanel);

            var watchGroup = new LiteSettingsGroup("大商观察名单");
            var watchPanel = new Panel
            {
                Height = UIUtils.S(174),
                BackColor = Color.Transparent,
                Margin = Padding.Empty
            };
            _watchListInput = CreateMultilineTextBox(readOnly: false);
            _watchListInput.PlaceholderText = "每行一个 SteamID，可写备注，例如：7656119xxxxxxxxxx | 大商A";
            var saveListButton = new LiteButton("保存名单并刷新", true)
            {
                Width = UIUtils.S(144),
                Height = UIUtils.S(34)
            };
            var listHint = CreateLabel("可直接粘贴包含 17 位 SteamID 的文本或链接；重复与无效行会自动清理。 ");
            listHint.ForeColor = UIColors.TextSub;
            saveListButton.Click += async (_, __) => await SaveAndRefreshAsync();
            watchPanel.Controls.AddRange(new Control[] { _watchListInput, saveListButton, listHint });
            watchPanel.Layout += (_, __) =>
            {
                int gap = UIUtils.S(10);
                _watchListInput.SetBounds(0, 0, watchPanel.ClientSize.Width, UIUtils.S(104));
                saveListButton.SetBounds(0, _watchListInput.Bottom + gap, saveListButton.Width, saveListButton.Height);
                listHint.SetBounds(saveListButton.Right + gap, saveListButton.Top, Math.Max(100, watchPanel.ClientSize.Width - saveListButton.Right - gap), saveListButton.Height);
            };
            watchGroup.AddFullItem(watchPanel);
            RegisterRefresh(() => _watchListInput.Text = Get(nameof(Settings.LocalInventoryWatchList), string.Empty));
            RegisterSave(() => Set(
                nameof(Settings.LocalInventoryWatchList),
                LocalInventoryWatchListParser.Normalize(_watchListInput.Text)));

            var targetsGroup = new LiteSettingsGroup("观察对象");
            _targetsText = CreateMultilineTextBox(readOnly: true);
            _targetsText.Height = UIUtils.S(142);
            targetsGroup.AddFullItem(_targetsText);

            var eventsGroup = new LiteSettingsGroup("最近库存异动");
            _eventsText = CreateMultilineTextBox(readOnly: true);
            _eventsText.Height = UIUtils.S(246);
            eventsGroup.AddFullItem(_eventsText);
            AddHint(eventsGroup, "“取出/恢复”和“卖出/存入”是数据源无法继续区分的组合状态，本机将保留原文，不推测成确定买卖。 ");

            AddGroupToPage(eventsGroup);
            AddGroupToPage(targetsGroup);
            AddGroupToPage(watchGroup);
            AddGroupToPage(statusGroup);
            AddGroupToPage(configurationGroup);

            _monitor.DataUpdated += OnMonitorDataUpdated;
            ApplySnapshot(_monitor.GetSnapshot());
        }

        public override void Activate()
        {
            base.Activate();
            ApplySnapshot(_monitor.GetSnapshot());
            _statusPanel.PerformLayout();
        }

        public override void Save()
        {
            base.Save();
            SaveSettingsStoreToDisk();
            if (Config != null)
                _monitor.Configure(Config);
        }

        public override void ApplySystemTheme()
        {
            base.ApplySystemTheme();
            _statusPanel.BackColor = UIColors.CardBg;
            ApplyTextBoxTheme(_watchListInput);
            ApplyTextBoxTheme(_targetsText);
            ApplyTextBoxTheme(_eventsText);
            ApplyStatusColor(_monitor.GetSnapshot());
        }

        private async Task SaveAndRefreshAsync()
        {
            if (_refreshing)
                return;

            _refreshing = true;
            _refreshButton.Enabled = false;
            try
            {
                Save();
                _watchListInput.Text = Get(nameof(Settings.LocalInventoryWatchList), string.Empty);
                await _monitor.RefreshAsync(force: true, PageToken);
                if (!IsDisposed)
                    ApplySnapshot(_monitor.GetSnapshot());
            }
            catch (OperationCanceledException)
            {
                // Page deactivation cancelled a manual refresh.
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    GlobalPromptService.Show(
                        FindForm(),
                        "刷新失败：" + ex.Message,
                        "本机库存监控",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            finally
            {
                _refreshing = false;
                if (!IsDisposed)
                    _refreshButton.Enabled = true;
            }
        }

        private void OnMonitorDataUpdated(object? sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke(new Action(() => ApplySnapshot(_monitor.GetSnapshot())));
            }
            catch
            {
                // The settings window may be closing.
            }
        }

        private void ApplySnapshot(LocalInventoryMonitorSnapshot snapshot)
        {
            if (IsDisposed)
                return;

            _statusLabel.Text = snapshot.Status;
            _statusDetailLabel.Text = LocalInventoryMonitorPageModel.BuildStatusDetail(snapshot);
            _targetsText.Text = LocalInventoryMonitorPageModel.BuildTargetsText(snapshot);
            _eventsText.Text = LocalInventoryMonitorPageModel.BuildEventsText(snapshot);
            ApplyStatusColor(snapshot);
        }

        private void ApplyStatusColor(LocalInventoryMonitorSnapshot snapshot)
        {
            Color color = !string.IsNullOrWhiteSpace(snapshot.Error)
                ? UIColors.TextWarn
                : snapshot.Enabled && string.Equals(snapshot.Status, "监控正常", StringComparison.Ordinal)
                    ? UIColors.Positive
                    : UIColors.TextSub;
            _statusLabel.ForeColor = color;
        }

        private void LayoutStatusPanel(Control openCsqaqButton, Control openSteamDtButton)
        {
            int gap = UIUtils.S(10);
            _statusLabel.SetBounds(0, 0, _statusPanel.ClientSize.Width, UIUtils.S(36));
            _statusDetailLabel.SetBounds(0, _statusLabel.Bottom, _statusPanel.ClientSize.Width, UIUtils.S(44));
            int buttonTop = _statusDetailLabel.Bottom + gap;
            _refreshButton.SetBounds(0, buttonTop, _refreshButton.Width, _refreshButton.Height);
            openCsqaqButton.SetBounds(_refreshButton.Right + gap, buttonTop, openCsqaqButton.Width, openCsqaqButton.Height);
            openSteamDtButton.SetBounds(openCsqaqButton.Right + gap, buttonTop, openSteamDtButton.Width, openSteamDtButton.Height);
        }

        private static LiteTextBox CreateMultilineTextBox(bool readOnly)
        {
            var textBox = new LiteTextBox
            {
                Multiline = true,
                ReadOnly = readOnly,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                Font = new Font(readOnly ? "Microsoft YaHei UI" : "Consolas", 9F, FontStyle.Regular),
                WordWrap = true,
                AcceptsReturn = !readOnly,
                AcceptsTab = false
            };
            ApplyTextBoxTheme(textBox);
            return textBox;
        }

        private static void ApplyTextBoxTheme(LiteTextBox textBox)
        {
            textBox.BackColor = UIColors.InputBg;
            textBox.ForeColor = UIColors.TextMain;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _monitor.DataUpdated -= OnMonitorDataUpdated;
            base.Dispose(disposing);
        }
    }
}
