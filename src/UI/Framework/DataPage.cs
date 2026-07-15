using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Market;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.Helpers;

namespace CS2TradeMonitor.src.UI.Framework
{
    /// <summary>
    /// 大盘数据源设置页 - 迁移自 SystemHardwarPage(showMarketSettings:true)
    /// 实现 IUiPage，通过 SettingsStore 管理设置，通过 AutoSaveCoordinator 落盘
    /// </summary>
    public sealed class DataPage : UserControl, IUiPage
    {
        private readonly BufferedPanel _container;
        private readonly List<Action> _refreshActions = new List<Action>();
        private readonly List<LiteSettingsGroup> _groups = new List<LiteSettingsGroup>();
        private readonly ISteamDtService _steamDtService;
        private readonly ICsqaqService _csqaqService;
        private SettingsStore? _settingsStore;

        // SteamDT 控件引用
        private LiteUnderlineInput? _steamDtApiKeyInput;
        private Label? _lblSteamDtTestResult;
        private Label? _lblSteamDtLastRefresh;
        private bool _steamDtTestBusy;

        // QAQ 控件引用
        private LiteUnderlineInput? _csqaqTokenInput;
        private Label? _lblCsqaqTestResult;
        private Label? _lblCsqaqLastRefresh;
        private bool _csqaqTestBusy;

        private Label? _lblSteamDtCardStatus;
        private Label? _lblSteamDtSource;
        private Label? _lblSteamDtInterval;
        private LiteNumberInput? _steamDtIntervalInput;
        private Label? _lblCsqaqCardStatus;
        private Label? _lblCsqaqSource;
        private Label? _lblCsqaqInterval;
        private LiteNumberInput? _csqaqIntervalInput;
        private bool _marketRefreshBusy;
        private bool _updatingIntervalInputs;

        private const string SteamDtHomeUrl = SteamDtUrls.WebBase;
        private const string CsqaqHomeUrl = CsqaqUrls.WebBase;

        // 格式控件
        private LiteComboBox? _marketFormatCombo;

        public DataPage()
            : this(DataPageRuntimeServices.Resolve())
        {
        }

        internal DataPage(DataPageRuntimeServices runtimeServices)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);

            BackColor = UIColors.MainBg;
            Dock = DockStyle.Fill;
            Padding = new Padding(0);
            _steamDtService = runtimeServices.SteamDtService;
            _csqaqService = runtimeServices.CsqaqService;

            _container = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(18, 18, 18, 72),
                BackColor = UIColors.MainBg
            };
            Controls.Add(_container);
            InitializeUI();

            MarketDataSourceManager.DataUpdated -= OnMarketDataUpdated;
            MarketDataSourceManager.DataUpdated += OnMarketDataUpdated;
        }

        public void Initialize(SettingsStore settingsStore)
        {
            _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
            EnsureMarketRefreshDefaults();
            RefreshFromStore();
            UpdateMarketStatusLabels();
        }

        public void Activate()
        {
            EnsureMarketRefreshDefaults();
            RefreshFromStore();
            UpdateMarketStatusLabels();
        }

        public void Deactivate()
        {
        }

        public void Save()
        {
            CommitMarketIntervalInput(_steamDtIntervalInput, SettingsKeys.SteamDtRefreshSec);
            CommitMarketIntervalInput(_csqaqIntervalInput, SettingsKeys.CsqaqRefreshSec);
        }

        private void OnMarketDataUpdated()
        {
            if (IsDisposed || Disposing)
                return;

            void RefreshLabels()
            {
                if (IsDisposed || Disposing)
                    return;

                UpdateMarketStatusLabels();
            }

            try
            {
                if (IsHandleCreated)
                    BeginInvoke((MethodInvoker)RefreshLabels);
                else
                    RefreshLabels();
            }
            catch
            {
                // The page may be closing while a background refresh completes.
            }
        }

        private void InitializeUI()
        {
            var statusWrapper = CreateUnifiedStatusCard();
            var interfaceWrapper = CreateInterfaceConfigCard();
            var formatWrapper = CreateFormatCard();
            var creditsWrapper = CreateCreditsCard();

            // Z-order: index 0 bottom → index N top.
            _container.Controls.SetChildIndex(creditsWrapper, 0);
            _container.Controls.SetChildIndex(interfaceWrapper, 1);
            _container.Controls.SetChildIndex(formatWrapper, 2);
            _container.Controls.SetChildIndex(statusWrapper, 3);
        }

        private Panel CreateUnifiedStatusCard()
        {
            var group = new LiteSettingsGroup("大盘数据源");
            var btnRefresh = new LiteButton("立即刷新", true) { Width = UIUtils.S(110), Height = UIUtils.S(30) };
            btnRefresh.Click += async (_, __) => await RefreshMarketIndexesAsync(new Control[] { btnRefresh });
            group.AddHeaderAction(btnRefresh);

            _lblSteamDtCardStatus = DataPageInterfaceControls.CreateValueLabel();
            _lblSteamDtSource = DataPageInterfaceControls.CreateValueLabel();
            _lblSteamDtInterval = DataPageInterfaceControls.CreateValueLabel();
            _steamDtIntervalInput = CreateMarketRefreshInput(SettingsKeys.SteamDtRefreshSec);
            _lblSteamDtLastRefresh = DataPageInterfaceControls.CreateHeaderRefreshLabel();
            _lblCsqaqCardStatus = DataPageInterfaceControls.CreateValueLabel();
            _lblCsqaqSource = DataPageInterfaceControls.CreateValueLabel();
            _lblCsqaqInterval = DataPageInterfaceControls.CreateValueLabel();
            _csqaqIntervalInput = CreateMarketRefreshInput(SettingsKeys.CsqaqRefreshSec);
            _lblCsqaqLastRefresh = DataPageInterfaceControls.CreateHeaderRefreshLabel();

            var steamDtRow = DataPageSourceCompactRowFactory.Create("SteamDT 市场监控", _lblSteamDtCardStatus, _lblSteamDtSource, _steamDtIntervalInput!, _lblSteamDtLastRefresh);
            var qaqRow = DataPageSourceCompactRowFactory.Create("QAQ 市场监控", _lblCsqaqCardStatus, _lblCsqaqSource, _csqaqIntervalInput!, _lblCsqaqLastRefresh);
            Control body = DataPageUnifiedStatusBodyFactory.Create(steamDtRow, qaqRow);
            group.AddFullItem(body);
            return AddGroupToPage(group);
        }

        private Panel CreateInterfaceConfigCard()
        {
            var group = new LiteSettingsGroup("接口配置");
            group.AddHeaderInlineAction(DataPageInterfaceControls.CreateApiRecommendationHeaderNotice());

            var steamDtRow = CreateSteamDtCredentialRow();
            var qaqRow = CreateCsqaqCredentialRow();
            _lblSteamDtTestResult = DataPageInterfaceControls.CreateResultLabel("选填，无 API Key 时走公开接口；填写后优先使用官方 API。");
            _lblCsqaqTestResult = DataPageInterfaceControls.CreateResultLabel("选填，无 Token 时走公开接口；填写后请求会带 ApiToken。");

            Control body = DataPageInterfaceControls.CreateCredentialBody(
                steamDtRow,
                _lblSteamDtTestResult,
                qaqRow,
                _lblCsqaqTestResult);
            group.AddFullItem(body);

            return AddGroupToPage(group);
        }

        private Control CreateSteamDtCredentialRow()
        {
            var apiKeyInput = new LiteUnderlineInput(
                GetSetting(SettingsKeys.SteamDtApiKey, ""), "", "", 240, null, HorizontalAlignment.Left);
            apiKeyInput.Inner.UseSystemPasswordChar = true;
            apiKeyInput.Inner.TextChanged += (_, __) =>
                _settingsStore?.Set(SettingsKeys.SteamDtApiKey, apiKeyInput.Inner.Text);
            _refreshActions.Add(() => apiKeyInput.Inner.Text = GetSetting(SettingsKeys.SteamDtApiKey, ""));
            _steamDtApiKeyInput = apiKeyInput;

            var btnTest = new LiteButton("测试连接", false) { Width = UIUtils.S(96), Height = UIUtils.S(30) };
            var btnHelp = new LiteButton("填写说明", false) { Width = UIUtils.S(96), Height = UIUtils.S(30) };
            btnHelp.Click += (_, __) => OpenSteamDtApiHelp(btnHelp.FindForm());
            btnTest.Click += async (_, __) =>
            {
                if (_steamDtApiKeyInput != null)
                    await RunSteamDtTestAsync(new Control[] { btnTest, btnHelp }, _steamDtApiKeyInput.Inner.Text);
            };

            return DataPageInterfaceControls.CreateCredentialRow("SteamDT API Key（选填）", apiKeyInput, btnTest, btnHelp);
        }

        private Control CreateCsqaqCredentialRow()
        {
            var tokenInput = new LiteUnderlineInput(
                GetSetting(SettingsKeys.CsqaqApiToken, ""), "", "", 240, null, HorizontalAlignment.Left);
            tokenInput.Inner.UseSystemPasswordChar = true;
            tokenInput.Inner.TextChanged += (_, __) =>
                _settingsStore?.Set(SettingsKeys.CsqaqApiToken, tokenInput.Inner.Text);
            _refreshActions.Add(() => tokenInput.Inner.Text = GetSetting(SettingsKeys.CsqaqApiToken, ""));
            _csqaqTokenInput = tokenInput;

            var btnTest = new LiteButton("测试连接", false) { Width = UIUtils.S(96), Height = UIUtils.S(30) };
            var btnHelp = new LiteButton("填写说明", false) { Width = UIUtils.S(96), Height = UIUtils.S(30) };
            btnHelp.Click += (_, __) => OpenCsqaqApiHelp(btnHelp.FindForm());
            btnTest.Click += async (_, __) =>
            {
                if (_csqaqTokenInput != null)
                    await RunCsqaqTestAsync(new Control[] { btnTest, btnHelp }, _csqaqTokenInput.Inner.Text);
            };

            return DataPageInterfaceControls.CreateCredentialRow("QAQ API Token（选填）", tokenInput, btnTest, btnHelp);
        }

        // ═══════════════════════════════════════════
        //  大盘数据显示格式卡片
        // ═══════════════════════════════════════════
        private Panel CreateFormatCard()
        {
            var formatGroup = new LiteSettingsGroup("大盘数据显示格式");

            // 显示百分比开关
            var chkPercent = new LiteCheck(GetSetting(SettingsKeys.SteamDtShowPercent, true), "显示百分比变化");
            chkPercent.CheckedChanged += (s, e) =>
            {
                _settingsStore?.Set(SettingsKeys.SteamDtShowPercent, chkPercent.Checked);
            };
            _refreshActions.Add(() => chkPercent.Checked = GetSetting(SettingsKeys.SteamDtShowPercent, true));

            _marketFormatCombo = new LiteComboBox();
            foreach (DataPageMarketFormatOption option in DataPageModel.MarketFormatOptions)
                _marketFormatCombo.Items.Add(option.Text);
            _marketFormatCombo.SelectedIndex = DataPageModel.NormalizeMarketFormatIndex(GetSetting(SettingsKeys.MarketFormat, 0));
            _marketFormatCombo.Inner.SelectedIndexChanged += (s, e) =>
            {
                int idx = _marketFormatCombo.SelectedIndex;
                if (idx >= 0 && idx < DataPageModel.MarketFormatOptions.Count)
                    _settingsStore?.Set(SettingsKeys.MarketFormat, idx);
            };
            _refreshActions.Add(() =>
            {
                _marketFormatCombo.SelectedIndex = DataPageModel.NormalizeMarketFormatIndex(GetSetting(SettingsKeys.MarketFormat, 0));
            });
            _marketFormatCombo.Width = UIUtils.S(220);
            formatGroup.AddItem(new LiteSettingsItem("显示百分比", chkPercent));
            formatGroup.AddItem(new LiteSettingsItem("显示格式", _marketFormatCombo));

            return AddGroupToPage(formatGroup);
        }

        private Panel CreateCreditsCard()
        {
            var group = new LiteSettingsGroup("鸣谢");
            var body = new Panel
            {
                Height = UIUtils.S(58),
                BackColor = Color.Transparent,
                Padding = UIUtils.S(new Padding(20, 8, 20, 8))
            };
            var thanks = new Label
            {
                Text = "感谢 DT 站长星辰、QAQ 站长棒棒糖提供数据生态支持。",
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            var steamDtButton = new LiteButton("SteamDT 主页", false) { Width = UIUtils.S(116), Height = UIUtils.S(30) };
            var qaqButton = new LiteButton("QAQ 主页", false) { Width = UIUtils.S(96), Height = UIUtils.S(30) };
            steamDtButton.Click += (_, __) => SystemActions.OpenUrl(SteamDtHomeUrl);
            qaqButton.Click += (_, __) => SystemActions.OpenUrl(CsqaqHomeUrl);

            body.Controls.AddRange(new Control[] { thanks, steamDtButton, qaqButton });
            body.Layout += (_, __) =>
            {
                int gap = UIUtils.S(10);
                int mid = body.Height / 2;
                qaqButton.SetBounds(body.Width - body.Padding.Right - qaqButton.Width, mid - qaqButton.Height / 2, qaqButton.Width, qaqButton.Height);
                steamDtButton.SetBounds(qaqButton.Left - gap - steamDtButton.Width, mid - steamDtButton.Height / 2, steamDtButton.Width, steamDtButton.Height);
                thanks.SetBounds(body.Padding.Left, body.Padding.Top, Math.Max(1, steamDtButton.Left - body.Padding.Left - gap), body.Height - body.Padding.Vertical);
            };
            group.AddFullItem(body);
            return AddGroupToPage(group);
        }

        private LiteNumberInput CreateMarketRefreshInput(string settingKey)
        {
            int current = DataPageModel.NormalizeMarketRefreshValue(GetSetting(settingKey, Settings.DefaultMarketRefreshSec));
            var input = new LiteNumberInput(current.ToString(), "秒", "", 88, null, 5)
            {
                Padding = UIUtils.S(new Padding(0, 3, 0, 1))
            };

            input.Inner.TextChanged += (_, __) =>
            {
                if (_updatingIntervalInputs)
                    return;

                if (int.TryParse(input.Inner.Text, out int value))
                    ApplyMarketRefreshInterval(settingKey, DataPageModel.NormalizeMarketRefreshValue(value), updateText: false);
            };
            input.Inner.Leave += (_, __) => CommitMarketIntervalInput(input, settingKey);
            _refreshActions.Add(() => SetIntervalInputText(input, DataPageModel.NormalizeMarketRefreshValue(GetSetting(settingKey, Settings.DefaultMarketRefreshSec))));
            return input;
        }

        private void EnsureMarketRefreshDefaults()
        {
            bool changed = false;
            changed |= NormalizeMarketRefreshSetting(SettingsKeys.SteamDtRefreshSec);
            changed |= NormalizeMarketRefreshSetting(SettingsKeys.CsqaqRefreshSec);
            if (changed)
                _settingsStore?.Save();
        }

        private bool NormalizeMarketRefreshSetting(string key)
        {
            int current = GetSetting(key, Settings.DefaultMarketRefreshSec);
            int normalized = DataPageModel.NormalizeMarketRefreshValue(current);
            if (current != normalized)
            {
                _settingsStore?.Set(key, normalized);
                return true;
            }

            return false;
        }

        private void CommitMarketIntervalInput(LiteNumberInput? input, string settingKey)
        {
            if (input == null)
                return;

            int normalized = DataPageModel.NormalizeMarketRefreshValue(input.ValueInt);
            ApplyMarketRefreshInterval(settingKey, normalized, updateText: true);
        }

        private void ApplyMarketRefreshInterval(string settingKey, int seconds, bool updateText)
        {
            _settingsStore?.Set(settingKey, seconds);
            _settingsStore?.Save();

            int steamDtSec = DataPageModel.NormalizeMarketRefreshValue(GetSetting(SettingsKeys.SteamDtRefreshSec, Settings.DefaultMarketRefreshSec));
            int csqaqSec = DataPageModel.NormalizeMarketRefreshValue(GetSetting(SettingsKeys.CsqaqRefreshSec, Settings.DefaultMarketRefreshSec));
            _steamDtService.Configure(GetCurrentSteamDtApiKey(), steamDtSec);
            _csqaqService.Configure(GetCurrentCsqaqApiToken());
            MarketDataSourceManager.UpdateMarketRefreshIntervals(steamDtSec, csqaqSec);

            if (updateText)
            {
                if (settingKey == SettingsKeys.SteamDtRefreshSec)
                    SetIntervalInputText(_steamDtIntervalInput, seconds);
                else if (settingKey == SettingsKeys.CsqaqRefreshSec)
                    SetIntervalInputText(_csqaqIntervalInput, seconds);
            }

            UpdateMarketStatusLabels();
        }

        private void SetIntervalInputText(LiteNumberInput? input, int seconds)
        {
            if (input == null)
                return;

            string text = seconds.ToString();
            if (input.Inner.Text == text)
                return;

            _updatingIntervalInputs = true;
            try
            {
                input.Inner.Text = text;
            }
            finally
            {
                _updatingIntervalInputs = false;
            }
        }

        private async System.Threading.Tasks.Task RefreshMarketIndexesAsync(IReadOnlyList<Control> buttons)
        {
            if (_marketRefreshBusy) return;
            _marketRefreshBusy = true;
            SetBusyControls(buttons, false);
            try
            {
                if (_steamDtApiKeyInput != null)
                    SaveSteamDtCredentials(_steamDtApiKeyInput.Inner.Text);
                if (_csqaqTokenInput != null)
                    SaveCsqaqCredentials(_csqaqTokenInput.Inner.Text);

                await MarketDataSourceManager.RefreshMarketIndexesAsync("手动刷新", waitForSteamDtLock: true);
                UpdateMarketStatusLabels();
            }
            finally
            {
                SetBusyControls(buttons, true);
                _marketRefreshBusy = false;
            }
        }

        private void UpdateMarketStatusLabels()
        {
            UpdateSteamDtStatusLabels();
            UpdateCsqaqStatusLabels();
        }

        private void UpdateSteamDtStatusLabels()
        {
            int interval = DataPageModel.NormalizeMarketRefreshValue(GetSetting(SettingsKeys.SteamDtRefreshSec, Settings.DefaultMarketRefreshSec));
            MarketSourceStateSnapshot snapshot = MarketDataSourceManager.GetSourceState(
                MarketDataSourceManager.SteamDtId,
                !string.IsNullOrWhiteSpace(GetSetting(SettingsKeys.SteamDtApiKey, "")),
                interval);
            if (_steamDtIntervalInput?.Inner.Focused != true)
                SetIntervalInputText(_steamDtIntervalInput, interval);

            DataPageStatusRenderer.ApplyDataSourceStatus(
                DataPageModel.BuildSourceStatus(snapshot),
                _lblSteamDtCardStatus,
                _lblSteamDtLastRefresh,
                _lblSteamDtSource,
                _lblSteamDtInterval);
        }

        private void UpdateCsqaqStatusLabels()
        {
            int interval = DataPageModel.NormalizeMarketRefreshValue(GetSetting(SettingsKeys.CsqaqRefreshSec, Settings.DefaultMarketRefreshSec));
            MarketSourceStateSnapshot snapshot = MarketDataSourceManager.GetSourceState(
                MarketDataSourceManager.QaqId,
                !string.IsNullOrWhiteSpace(GetSetting(SettingsKeys.CsqaqApiToken, "")),
                interval);
            if (_csqaqIntervalInput?.Inner.Focused != true)
                SetIntervalInputText(_csqaqIntervalInput, interval);

            DataPageStatusRenderer.ApplyDataSourceStatus(
                DataPageModel.BuildSourceStatus(snapshot),
                _lblCsqaqCardStatus,
                _lblCsqaqLastRefresh,
                _lblCsqaqSource,
                _lblCsqaqInterval);
        }

        // ═══════════════════════════════════════════
        //  SettingsStore 便捷方法
        // ═══════════════════════════════════════════
        private T GetSetting<T>(string key, T fallback)
        {
            return _settingsStore != null ? _settingsStore.Get(key, fallback) : fallback;
        }

        private void RefreshFromStore()
        {
            foreach (var action in _refreshActions)
                action();
        }

        // ═══════════════════════════════════════════
        //  SteamDT 测试与刷新
        // ═══════════════════════════════════════════
        private async System.Threading.Tasks.Task RunSteamDtTestAsync(IReadOnlyList<Control> buttons, string apiKey)
        {
            if (_steamDtTestBusy) return;
            _steamDtTestBusy = true;
            SetBusyControls(buttons, false);
            try
            {
                SaveSteamDtCredentials(apiKey);
                await TestSteamDtConnection(apiKey);
            }
            finally
            {
                SetBusyControls(buttons, true);
                _steamDtTestBusy = false;
            }
        }

        private async System.Threading.Tasks.Task TestSteamDtConnection(string apiKey)
        {
            if (_lblSteamDtTestResult == null) return;
            _lblSteamDtTestResult.Text = "正在测试...";
            _lblSteamDtTestResult.ForeColor = UIColors.TextSub;
            try
            {
                int refreshSec = DataPageModel.NormalizeMarketRefreshValue(GetSetting(SettingsKeys.SteamDtRefreshSec, Settings.DefaultMarketRefreshSec));
                await _steamDtService.TestAndUpdateAsync(apiKey, refreshSec);
                var d = _steamDtService.Latest;
                if (d != null && !d.IsStale)
                {
                    _lblSteamDtTestResult.Text = $"✓ 连接成功  来源: {DataPageModel.NormalizeSourceText(d.Source, "公开接口")}  指数: {d.FormatIndex()}  {d.FormatRatio()}";
                    _lblSteamDtTestResult.ForeColor = Color.FromArgb(0, 180, 80);
                    UpdateSteamDtLastRefreshLabel();
                }
                else if (d != null)
                {
                    string reason = CS2TradeMonitor.src.Core.Actions.AppActions.SanitizeError(_steamDtService.LastError);
                    _lblSteamDtTestResult.Text = $"⚠ 刷新失败，显示上次缓存  来源: {DataPageModel.NormalizeSourceText(d.Source, "公开接口")}"
                        + (string.IsNullOrWhiteSpace(reason) ? "" : $"  原因: {reason}");
                    _lblSteamDtTestResult.ForeColor = UIColors.TextWarn;
                    UpdateSteamDtLastRefreshLabel();
                }
                else
                {
                    _lblSteamDtTestResult.Text = "✗ 尚未刷新成功：点击顶部“立即刷新”或检查 API Key";
                    _lblSteamDtTestResult.ForeColor = UIColors.TextCrit;
                    UpdateMarketStatusLabels();
                }
            }
            catch (Exception ex)
            {
                _lblSteamDtTestResult.Text = $"✗ 连接失败: {CS2TradeMonitor.src.Core.Actions.AppActions.SanitizeError(ex.Message)}";
                _lblSteamDtTestResult.ForeColor = UIColors.TextCrit;
                UpdateMarketStatusLabels();
            }
        }

        private void SaveSteamDtCredentials(string rawKey)
        {
            string normalizedKey = rawKey?.Trim() ?? "";
            int refreshSec = DataPageModel.NormalizeMarketRefreshValue(GetSetting(SettingsKeys.SteamDtRefreshSec, Settings.DefaultMarketRefreshSec));
            _settingsStore?.Set(SettingsKeys.SteamDtApiKey, normalizedKey);
            _settingsStore?.Set(SettingsKeys.SteamDtRefreshSec, refreshSec);
            _settingsStore?.Save();

            _steamDtService.Configure(normalizedKey, refreshSec);
            MarketDataSourceManager.UpdateMarketRefreshIntervals(
                refreshSec,
                DataPageModel.NormalizeMarketRefreshValue(GetSetting(SettingsKeys.CsqaqRefreshSec, Settings.DefaultMarketRefreshSec)));
        }

        private void UpdateSteamDtLastRefreshLabel()
        {
            UpdateSteamDtStatusLabels();
        }

        // ═══════════════════════════════════════════
        //  QAQ 测试与刷新
        // ═══════════════════════════════════════════
        private async System.Threading.Tasks.Task RunCsqaqTestAsync(IReadOnlyList<Control> buttons, string token)
        {
            if (_csqaqTestBusy) return;
            _csqaqTestBusy = true;
            SetBusyControls(buttons, false);
            try
            {
                SaveCsqaqCredentials(token);
                await TestCsqaqConnection(token);
            }
            finally
            {
                SetBusyControls(buttons, true);
                _csqaqTestBusy = false;
            }
        }

        private async System.Threading.Tasks.Task TestCsqaqConnection(string token)
        {
            if (_lblCsqaqTestResult == null) return;
            _lblCsqaqTestResult.Text = "正在测试...";
            _lblCsqaqTestResult.ForeColor = UIColors.TextSub;
            try
            {
                int refreshSec = DataPageModel.NormalizeMarketRefreshValue(GetSetting(SettingsKeys.CsqaqRefreshSec, Settings.DefaultMarketRefreshSec));
                await _csqaqService.TestAndUpdateAsync(token, refreshSec);
                var d = _csqaqService.Latest;
                if (d != null && !d.IsStale)
                {
                    _lblCsqaqTestResult.Text = $"✓ 连接成功  来源: {DataPageModel.NormalizeSourceText(d.Source, "公开接口")}  指数: {d.FormatIndex()}  {d.FormatRate()}";
                    _lblCsqaqTestResult.ForeColor = Color.FromArgb(0, 180, 80);
                    UpdateCsqaqLastRefreshLabel();
                }
                else if (d != null)
                {
                    string reason = CS2TradeMonitor.src.Core.Actions.AppActions.SanitizeError(_csqaqService.LastError);
                    _lblCsqaqTestResult.Text = $"⚠ 刷新失败，显示上次缓存  来源: {DataPageModel.NormalizeSourceText(d.Source, "公开接口")}"
                        + (string.IsNullOrWhiteSpace(reason) ? "" : $"  原因: {reason}");
                    _lblCsqaqTestResult.ForeColor = UIColors.TextWarn;
                    UpdateCsqaqLastRefreshLabel();
                }
                else
                {
                    _lblCsqaqTestResult.Text = "✗ 尚未刷新成功：点击顶部“立即刷新”或检查 API Token";
                    _lblCsqaqTestResult.ForeColor = UIColors.TextCrit;
                    UpdateMarketStatusLabels();
                }
            }
            catch (Exception ex)
            {
                _lblCsqaqTestResult.Text = $"✗ 连接失败: {CS2TradeMonitor.src.Core.Actions.AppActions.SanitizeError(ex.Message)}";
                _lblCsqaqTestResult.ForeColor = UIColors.TextCrit;
                UpdateMarketStatusLabels();
            }
        }

        private void SaveCsqaqCredentials(string token)
        {
            string normalizedToken = token?.Trim() ?? "";
            int refreshSec = DataPageModel.NormalizeMarketRefreshValue(GetSetting(SettingsKeys.CsqaqRefreshSec, Settings.DefaultMarketRefreshSec));
            _settingsStore?.Set(SettingsKeys.CsqaqApiToken, normalizedToken);
            _settingsStore?.Set(SettingsKeys.CsqaqRefreshSec, refreshSec);
            _settingsStore?.Save();

            _csqaqService.Configure(normalizedToken);
            MarketDataSourceManager.UpdateMarketRefreshIntervals(
                DataPageModel.NormalizeMarketRefreshValue(GetSetting(SettingsKeys.SteamDtRefreshSec, Settings.DefaultMarketRefreshSec)),
                refreshSec);
        }

        private void UpdateCsqaqLastRefreshLabel()
        {
            UpdateCsqaqStatusLabels();
        }

        private string GetCurrentSteamDtApiKey()
        {
            return (_steamDtApiKeyInput?.Inner.Text ?? GetSetting(SettingsKeys.SteamDtApiKey, "")).Trim();
        }

        private string GetCurrentCsqaqApiToken()
        {
            return (_csqaqTokenInput?.Inner.Text ?? GetSetting(SettingsKeys.CsqaqApiToken, "")).Trim();
        }

        // ═══════════════════════════════════════════
        //  帮助对话框
        // ═══════════════════════════════════════════
        private static void OpenSteamDtApiHelp(IWin32Window? owner)
        {
            using var dialog = new CS2TradeMonitor.src.UI.SettingsPage.SteamDtApiHelpForm();
            if (owner != null)
                dialog.ShowDialog(owner);
            else
                dialog.ShowDialog();
        }

        private static void OpenCsqaqApiHelp(IWin32Window? owner)
        {
            using var dialog = new CS2TradeMonitor.src.UI.SettingsPage.CsqaqApiHelpForm();
            if (owner != null)
                dialog.ShowDialog(owner);
            else
                dialog.ShowDialog();
        }

        // ═══════════════════════════════════════════
        //  通用工具方法
        // ═══════════════════════════════════════════
        private static void SetBusyControls(IEnumerable<Control> controls, bool enabled)
        {
            foreach (var control in controls)
                control.Enabled = enabled;
        }

        // ═══════════════════════════════════════════
        //  页面容器布局
        // ═══════════════════════════════════════════
        private Panel AddGroupToPage(LiteSettingsGroup group)
        {
            _groups.Add(group);
            var wrapper = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(0, 0, 0, UIUtils.S(16))
            };
            wrapper.Resize += (s, e) => ClampGroupWidth(wrapper, group);
            wrapper.Layout += (s, e) => ClampGroupWidth(wrapper, group);
            wrapper.Controls.Add(group);
            _container.Controls.Add(wrapper);
            _container.Controls.SetChildIndex(wrapper, 0);
            return wrapper;
        }

        private static void ClampGroupWidth(Control wrapper, Control group)
        {
            int width = Math.Max(1, wrapper.ClientSize.Width);
            group.MaximumSize = new Size(width, 0);
            group.Width = width;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                MarketDataSourceManager.DataUpdated -= OnMarketDataUpdated;
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// SettingsStore 使用的 key 常量 — 对应 Settings 类的属性名。
    /// 与 SettingsTransaction 的 draft 映射 key 命名保持一致。
    /// </summary>
    internal static class SettingsKeys
    {
        public const string SteamDtApiKey = nameof(Settings.SteamDtApiKey);
        public const string SteamDtRefreshSec = nameof(Settings.SteamDtRefreshSec);
        public const string SteamDtShowPercent = nameof(Settings.SteamDtShowPercent);
        public const string CsqaqApiToken = nameof(Settings.CsqaqApiToken);
        public const string CsqaqRefreshSec = nameof(Settings.CsqaqRefreshSec);
        public const string MarketFormat = nameof(Settings.MarketFormat);
    }
}
