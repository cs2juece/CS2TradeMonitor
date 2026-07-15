using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Market;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.Core.Actions;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.Helpers;
using CS2TradeMonitor.src.UI.SettingsPage;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    public sealed class DataRedesignHostPage : FrameworkSettingsHostPage<DataRedesignPage>
    {
        public DataRedesignHostPage()
            : base(new DataRedesignPage())
        {
        }
    }

    public sealed class DataRedesignPage : FrameworkSettingsPageBase
    {
        private const string SteamDtHomeUrl = SteamDtUrls.WebBase;
        private const string CsqaqHomeUrl = CsqaqUrls.WebBase;

        private static readonly int[] SegmentFormatValues = { 0, 3, 5 };

        private readonly ISteamDtService _steamDtService;
        private readonly ICsqaqService _csqaqService;

        private TableLayoutPanel? _root;
        private Label? _healthValue;
        private Label? _lastRefreshValue;
        private Label? _formatSummaryValue;
        private Label? _steamDtConfigValue;
        private Label? _csqaqConfigValue;
        private Label? _steamDtPreview;
        private Label? _csqaqPreview;
        private LiteCheck? _showPercentCheck;
        private RedesignSegmentedControl? _formatSegments;
        private LiteUnderlineInput? _steamDtApiInput;
        private LiteUnderlineInput? _csqaqApiInput;
        private LiteNumberInput? _steamDtIntervalInput;
        private LiteNumberInput? _csqaqIntervalInput;
        private Label? _steamDtResultLabel;
        private Label? _csqaqResultLabel;
        private DataRedesignSourceCardBinding? _steamDtCard;
        private DataRedesignSourceCardBinding? _csqaqCard;
        private bool _marketRefreshBusy;
        private bool _steamDtTestBusy;
        private bool _csqaqTestBusy;
        private bool _updatingIntervalInputs;
        private bool _widthSyncQueued;
        private bool _refreshAllQueued;
        private EventHandler? _refreshAllHandleCreatedHandler;
        private bool _disposed;

        private Rectangle ContentBounds => GetVisibleContentBounds(FrameworkSettingsPageLayoutHelper.WideContentMinimumWidth);
        private int ContentWidth => ContentBounds.Width;

        public DataRedesignPage()
            : this(DataPageRuntimeServices.Resolve())
        {
        }

        internal DataRedesignPage(DataPageRuntimeServices runtimeServices)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);

            _steamDtService = runtimeServices.SteamDtService;
            _csqaqService = runtimeServices.CsqaqService;
            Container.SizeChanged += (_, __) => QueueDeferredContentWidthSync();
            MarketDataSourceManager.DataUpdated += OnMarketDataUpdated;
        }

        protected override void OnStoreAttached()
        {
            EnsureMarketRefreshDefaults();
            BuildPage();
        }

        public override void Activate()
        {
            EnsureMarketRefreshDefaults();
            base.Activate();
            QueueDeferredRefreshAll();
            QueueDeferredContentWidthSync();
        }

        public override void Save()
        {
            base.Save();
            RunIfSettingsChanged(() =>
            {
                CommitMarketIntervalInput(_steamDtIntervalInput, SettingsKeys.SteamDtRefreshSec);
                CommitMarketIntervalInput(_csqaqIntervalInput, SettingsKeys.CsqaqRefreshSec);
                SaveSteamDtCredentials(_steamDtApiInput?.Inner.Text ?? Get(SettingsKeys.SteamDtApiKey, ""));
                SaveCsqaqCredentials(_csqaqApiInput?.Inner.Text ?? Get(SettingsKeys.CsqaqApiToken, ""));
            });
        }

        public override void ApplySystemTheme()
        {
            base.ApplySystemTheme();
            RefreshAll();
            _root?.Invalidate(true);
        }

        protected override int GetTopLevelContentWidth()
        {
            return ContentWidth;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                if (_refreshAllHandleCreatedHandler != null)
                {
                    HandleCreated -= _refreshAllHandleCreatedHandler;
                    _refreshAllHandleCreatedHandler = null;
                }

                MarketDataSourceManager.DataUpdated -= OnMarketDataUpdated;
            }

            base.Dispose(disposing);
        }

        private void BuildPage()
        {
            ClearPage();
            Rectangle bounds = ContentBounds;

            _root = new TableLayoutPanel
            {
                Dock = DockStyle.None,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                AutoSize = false,
                ColumnCount = 1,
                RowCount = 0,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = UIColors.MainBg
            };
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            Container.Controls.Add(_root);

            AddRootRow(CreateHeaderPanel());
            AddRootRow(CreateSummaryCard());
            AddRootRow(CreateSourcesHost());
            AddRootRow(CreateFormatCard());
            AddRootRow(CreateInterfaceConfigCard());
            AddRootRow(CreateCreditsCard());

            RegisterRefresh(RefreshInputsFromStore);
            RefreshFromStore();
            QueueDeferredRefreshAll();
            QueueDeferredContentWidthSync();
        }

        private Control CreateHeaderPanel()
        {
            var panel = new Panel
            {
                Height = UIUtils.S(66),
                BackColor = UIColors.MainBg,
                Margin = new Padding(0, 0, 0, UIUtils.S(8))
            };
            var title = CreateTextLabel("大盘数据源", 16F, FontStyle.Bold, UIColors.TextMain);
            var subtitle = CreateTextLabel("监控 SteamDT / QAQ 指数状态、刷新间隔与接口凭据。", 9F, FontStyle.Regular, UIColors.TextSub);
            var refresh = new LiteButton("立即刷新", true) { Width = UIUtils.S(128), Height = UIUtils.S(40) };
            var log = new LiteButton("打开日志", false) { Width = UIUtils.S(120), Height = UIUtils.S(40) };

            refresh.Click += async (_, __) => await RefreshMarketIndexesAsync(new Control[] { refresh, log });
            log.Click += (_, __) => DiagnosticsLogFileOpener.OpenLogFile();

            panel.Controls.AddRange(new Control[] { title, subtitle, refresh, log });
            panel.Layout += (_, __) =>
            {
                int top = UIUtils.S(8);
                int gap = UIUtils.S(12);
                log.SetBounds(panel.Width - log.Width, top + UIUtils.S(2), log.Width, log.Height);
                refresh.SetBounds(log.Left - gap - refresh.Width, log.Top, refresh.Width, refresh.Height);
                title.SetBounds(0, top, Math.Max(1, refresh.Left - gap), UIUtils.S(30));
                subtitle.SetBounds(0, title.Bottom + UIUtils.S(2), Math.Max(1, refresh.Left - gap), UIUtils.S(24));
            };
            return panel;
        }

        private Control CreateSummaryCard()
        {
            var card = CreateCard(UIUtils.S(126));
            var healthTile = CreateSummaryTile("数据源健康", out _healthValue, 15F, 11F);
            var lastRefreshTile = CreateSummaryTile("最近刷新", out _lastRefreshValue, 10.5F, 9F);
            var formatTile = CreateSummaryTile("当前显示格式", out _formatSummaryValue, 13F, 10.5F);
            var divider = new Panel { BackColor = UIColors.Border };
            var bulb = CreateTextLabel("💡", 15F, FontStyle.Regular, UIColors.TextWarn, ContentAlignment.TopCenter);
            var hint1 = CreateTextLabel(DataRedesignPageModel.ApiHintLine1, 9F, FontStyle.Regular, UIColors.TextMain);
            var hint2 = CreateTextLabel(DataRedesignPageModel.ApiHintLine2, 9F, FontStyle.Regular, UIColors.TextSub);
            _steamDtConfigValue = CreateTextLabel("", 8.5F, FontStyle.Regular, UIColors.TextSub);
            _csqaqConfigValue = CreateTextLabel("", 8.5F, FontStyle.Regular, UIColors.TextSub);

            card.Controls.AddRange(new Control[] { healthTile, lastRefreshTile, formatTile, divider, bulb, hint1, hint2, _steamDtConfigValue, _csqaqConfigValue });
            card.Layout += (_, __) =>
            {
                int pad = UIUtils.S(22);
                int gap = UIUtils.S(20);
                bool stacked = card.Width < UIUtils.S(940);
                card.Height = stacked ? UIUtils.S(198) : UIUtils.S(126);

                int tileTop = UIUtils.S(22);
                int tileHeight = UIUtils.S(78);
                int hintLeft = stacked ? pad : Math.Max(UIUtils.S(640), card.Width - UIUtils.S(420));
                int tileAreaWidth = stacked
                    ? card.Width - pad * 2
                    : Math.Max(UIUtils.S(450), hintLeft - pad - gap);
                int tileWidth = Math.Max(UIUtils.S(146), (tileAreaWidth - gap * 2) / 3);
                healthTile.SetBounds(pad, tileTop, tileWidth, tileHeight);
                lastRefreshTile.SetBounds(healthTile.Right + gap, tileTop, tileWidth, tileHeight);
                formatTile.SetBounds(lastRefreshTile.Right + gap, tileTop, Math.Max(UIUtils.S(146), tileAreaWidth - tileWidth * 2 - gap * 2), tileHeight);

                if (stacked)
                {
                    divider.SetBounds(pad, tileTop + tileHeight + UIUtils.S(18), 1, UIUtils.S(44));
                    bulb.SetBounds(pad + UIUtils.S(18), divider.Top, UIUtils.S(30), UIUtils.S(30));
                    int textLeft = bulb.Right + UIUtils.S(12);
                    int textWidth = Math.Max(1, card.Width - textLeft - pad);
                    hint1.SetBounds(textLeft, divider.Top, textWidth, UIUtils.S(22));
                    hint2.SetBounds(textLeft, hint1.Bottom, textWidth, UIUtils.S(22));
                    _steamDtConfigValue.SetBounds(textLeft, hint2.Bottom, textWidth / 2, UIUtils.S(22));
                    _csqaqConfigValue.SetBounds(textLeft + textWidth / 2, hint2.Bottom, textWidth / 2, UIUtils.S(22));
                    return;
                }

                divider.SetBounds(hintLeft - UIUtils.S(24), UIUtils.S(30), 1, UIUtils.S(70));
                bulb.SetBounds(hintLeft, UIUtils.S(36), UIUtils.S(30), UIUtils.S(28));
                int hintTextLeft = bulb.Right + UIUtils.S(12);
                int hintWidth = Math.Max(1, card.Width - hintTextLeft - pad);
                hint1.SetBounds(hintTextLeft, UIUtils.S(35), hintWidth, UIUtils.S(22));
                hint2.SetBounds(hintTextLeft, hint1.Bottom + UIUtils.S(2), hintWidth, UIUtils.S(22));
                _steamDtConfigValue.SetBounds(hintTextLeft, hint2.Bottom + UIUtils.S(2), hintWidth / 2, UIUtils.S(20));
                _csqaqConfigValue.SetBounds(hintTextLeft + hintWidth / 2, hint2.Bottom + UIUtils.S(2), hintWidth / 2, UIUtils.S(20));
            };
            return card;
        }

        private Control CreateSourcesHost()
        {
            var host = new Panel
            {
                Height = UIUtils.S(224),
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, UIUtils.S(14))
            };
            Control steamDt = CreateSourceCard(isSteamDt: true);
            Control csqaq = CreateSourceCard(isSteamDt: false);
            host.Controls.Add(steamDt);
            host.Controls.Add(csqaq);
            host.Layout += (_, __) =>
            {
                DataRedesignSourceCardsLayout layout = DataRedesignPageModel.BuildSourceCardsLayout(host.Width);
                host.Height = layout.HostHeight;
                steamDt.Bounds = layout.SteamDtBounds;
                csqaq.Bounds = layout.CsqaqBounds;
            };
            return host;
        }

        private Control CreateSourceCard(bool isSteamDt)
        {
            var card = CreateCard(UIUtils.S(224), bottomMargin: 0);
            var title = CreateTextLabel(isSteamDt ? "SteamDT 市场监控" : "QAQ 市场监控", 11F, FontStyle.Bold, UIColors.TextMain);
            var statusPill = new DataRedesignPillLabel();
            var sourcePill = new DataRedesignPillLabel { AccentColor = UIColors.TextSub };
            var indexTitle = CreateTextLabel("指数", 9F, FontStyle.Regular, UIColors.TextSub);
            var index = CreateTextLabel("--", 16F, FontStyle.Bold, UIColors.TextMain);
            var change = CreateTextLabel("--", 10F, FontStyle.Regular, UIColors.TextSub);
            var sparkline = new DataRedesignSparkline();
            var intervalTitle = CreateTextLabel("间隔", 9F, FontStyle.Regular, UIColors.TextSub);
            var interval = CreateMarketRefreshInput(isSteamDt ? SettingsKeys.SteamDtRefreshSec : SettingsKeys.CsqaqRefreshSec);
            var lastTitle = CreateTextLabel("上次刷新", 9F, FontStyle.Regular, UIColors.TextSub);
            var lastRefresh = CreateTextLabel("--", 9F, FontStyle.Regular, UIColors.TextMain);
            var divider = new Panel { BackColor = UIColors.Border };
            var detail = CreateTextLabel("", 9F, FontStyle.Regular, UIColors.TextSub);

            card.Controls.AddRange(new Control[]
            {
                title, statusPill, sourcePill, indexTitle, index, change, sparkline,
                intervalTitle, interval, lastTitle, lastRefresh, divider, detail
            });

            var binding = new DataRedesignSourceCardBinding(statusPill, sourcePill, index, change, sparkline, interval, lastRefresh, detail);
            if (isSteamDt)
            {
                _steamDtIntervalInput = interval;
                _steamDtCard = binding;
            }
            else
            {
                _csqaqIntervalInput = interval;
                _csqaqCard = binding;
            }

            card.Layout += (_, __) =>
            {
                DataRedesignSourceCardLayout layout = DataRedesignPageModel.BuildSourceCardLayout(card.Width);
                title.Bounds = layout.TitleBounds;
                statusPill.Bounds = layout.StatusPillBounds;
                sourcePill.Bounds = layout.SourcePillBounds;
                indexTitle.Bounds = layout.IndexTitleBounds;
                index.Bounds = layout.IndexBounds;
                change.Bounds = layout.ChangeBounds;
                sparkline.Bounds = layout.SparklineBounds;
                intervalTitle.Bounds = layout.IntervalTitleBounds;
                interval.Bounds = layout.IntervalBounds;
                lastTitle.Bounds = layout.LastTitleBounds;
                lastRefresh.Bounds = layout.LastRefreshBounds;
                divider.Bounds = layout.DividerBounds;
                detail.Bounds = layout.DetailBounds;
                FitLabelFont(change, 10F, 8F);
                FitLabelFont(lastRefresh, 9F, 7.5F);
            };

            return card;
        }

        private Control CreateFormatCard()
        {
            var card = CreateCard(UIUtils.S(98));
            var title = CreateTextLabel("大盘数据显示格式", 11F, FontStyle.Bold, UIColors.TextMain);
            _showPercentCheck = new LiteCheck(Get(SettingsKeys.SteamDtShowPercent, true), "显示百分比变化") { Width = UIUtils.S(170) };
            _formatSegments = new RedesignSegmentedControl("价格 + 涨跌幅", "仅价格", "极客代号") { Width = UIUtils.S(360), Height = UIUtils.S(36) };
            var previewTitle = CreateTextLabel("实时预览:", 9F, FontStyle.Bold, UIColors.TextSub);
            _steamDtPreview = CreatePreviewPill();
            _csqaqPreview = CreatePreviewPill();

            _showPercentCheck.CheckedChanged += (_, __) =>
            {
                if (!IsUpdatingControls)
                    Set(SettingsKeys.SteamDtShowPercent, _showPercentCheck.Checked);
            };
            _formatSegments.SelectedIndexChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                int index = Math.Clamp(_formatSegments.SelectedIndex, 0, SegmentFormatValues.Length - 1);
                Set(SettingsKeys.MarketFormat, SegmentFormatValues[index]);
                RefreshAll();
            };

            card.Controls.AddRange(new Control[] { title, _showPercentCheck, _formatSegments, previewTitle, _steamDtPreview, _csqaqPreview });
            card.Layout += (_, __) =>
            {
                int pad = UIUtils.S(24);
                title.SetBounds(pad, UIUtils.S(14), UIUtils.S(240), UIUtils.S(26));
                int bodyTop = UIUtils.S(54);
                _showPercentCheck.SetBounds(pad, bodyTop + UIUtils.S(6), UIUtils.S(170), UIUtils.S(24));
                int segmentLeft = _showPercentCheck.Right + UIUtils.S(40);
                bool stacked = card.Width < UIUtils.S(1040);
                if (stacked)
                {
                    card.Height = UIUtils.S(148);
                    _formatSegments.SetBounds(pad, _showPercentCheck.Bottom + UIUtils.S(14), Math.Min(UIUtils.S(400), card.Width - pad * 2), UIUtils.S(36));
                    previewTitle.SetBounds(_formatSegments.Right + UIUtils.S(18), _formatSegments.Top, UIUtils.S(96), UIUtils.S(36));
                    _steamDtPreview.SetBounds(previewTitle.Right + UIUtils.S(8), _formatSegments.Top, UIUtils.S(168), UIUtils.S(36));
                    _csqaqPreview.SetBounds(_steamDtPreview.Right + UIUtils.S(10), _formatSegments.Top, UIUtils.S(168), UIUtils.S(36));
                    if (_csqaqPreview.Right > card.Width - pad)
                    {
                        previewTitle.SetBounds(pad, _formatSegments.Bottom + UIUtils.S(10), UIUtils.S(96), UIUtils.S(34));
                        _steamDtPreview.SetBounds(previewTitle.Right + UIUtils.S(8), previewTitle.Top, UIUtils.S(168), UIUtils.S(34));
                        _csqaqPreview.SetBounds(_steamDtPreview.Right + UIUtils.S(10), previewTitle.Top, Math.Max(UIUtils.S(138), card.Width - _steamDtPreview.Right - UIUtils.S(10) - pad), UIUtils.S(34));
                    }
                    return;
                }

                card.Height = UIUtils.S(98);
                _formatSegments.SetBounds(segmentLeft, bodyTop, UIUtils.S(360), UIUtils.S(36));
                previewTitle.SetBounds(_formatSegments.Right + UIUtils.S(54), bodyTop, UIUtils.S(96), UIUtils.S(36));
                _steamDtPreview.SetBounds(previewTitle.Right + UIUtils.S(8), bodyTop, UIUtils.S(168), UIUtils.S(36));
                _csqaqPreview.SetBounds(_steamDtPreview.Right + UIUtils.S(10), bodyTop, UIUtils.S(168), UIUtils.S(36));
            };
            return card;
        }

        private Control CreateInterfaceConfigCard()
        {
            var card = CreateCard(UIUtils.S(228));
            var title = CreateTextLabel("接口配置", 11F, FontStyle.Bold, UIColors.TextMain);
            var warn = CreateTextLabel("⚠  强烈建议添加 SteamDT API 数据源，最好 SteamDT 和 QAQ 两个都添加，数据更稳定。", 8.8F, FontStyle.Bold, UIColors.TextWarn);
            _steamDtApiInput = CreateApiInput(SettingsKeys.SteamDtApiKey);
            _csqaqApiInput = CreateApiInput(SettingsKeys.CsqaqApiToken);
            _steamDtResultLabel = CreateTextLabel(DataRedesignPageModel.ApiHelpText, 8.5F, FontStyle.Regular, UIColors.TextSub);
            _csqaqResultLabel = CreateTextLabel(DataRedesignPageModel.ApiHelpText, 8.5F, FontStyle.Regular, UIColors.TextSub);

            var steamLabel = CreateTextLabel("SteamDT API（选填）", 9F, FontStyle.Regular, UIColors.TextMain);
            var qaqLabel = CreateTextLabel("QAQ API（选填）", 9F, FontStyle.Regular, UIColors.TextMain);
            var steamTest = new LiteButton("测试连接", false) { Width = UIUtils.S(110), Height = UIUtils.S(34) };
            var steamHelp = new LiteButton("填写说明", false) { Width = UIUtils.S(110), Height = UIUtils.S(34) };
            var qaqTest = new LiteButton("测试连接", false) { Width = UIUtils.S(110), Height = UIUtils.S(34) };
            var qaqHelp = new LiteButton("填写说明", false) { Width = UIUtils.S(110), Height = UIUtils.S(34) };

            steamHelp.Click += (_, __) => OpenSteamDtApiHelp(steamHelp.FindForm());
            qaqHelp.Click += (_, __) => OpenCsqaqApiHelp(qaqHelp.FindForm());
            steamTest.Click += async (_, __) => await RunSteamDtTestAsync(new Control[] { steamTest, steamHelp }, _steamDtApiInput.Inner.Text);
            qaqTest.Click += async (_, __) => await RunCsqaqTestAsync(new Control[] { qaqTest, qaqHelp }, _csqaqApiInput.Inner.Text);

            card.Controls.AddRange(new Control[]
            {
                title, warn,
                steamLabel, _steamDtApiInput, steamTest, steamHelp, _steamDtResultLabel,
                qaqLabel, _csqaqApiInput, qaqTest, qaqHelp, _csqaqResultLabel
            });

            card.Layout += (_, __) =>
            {
                int pad = UIUtils.S(24);
                int labelWidth = UIUtils.S(190);
                int buttonGap = UIUtils.S(12);
                int buttonTotal = steamTest.Width + steamHelp.Width + buttonGap;
                title.SetBounds(pad, UIUtils.S(18), UIUtils.S(110), UIUtils.S(24));
                warn.SetBounds(title.Right + UIUtils.S(18), title.Top, Math.Max(1, card.Width - title.Right - pad), UIUtils.S(24));

                LayoutCredentialRow(
                    card,
                    steamLabel,
                    _steamDtApiInput,
                    steamTest,
                    steamHelp,
                    _steamDtResultLabel,
                    UIUtils.S(70),
                    pad,
                    labelWidth,
                    buttonTotal,
                    buttonGap);
                LayoutCredentialRow(
                    card,
                    qaqLabel,
                    _csqaqApiInput,
                    qaqTest,
                    qaqHelp,
                    _csqaqResultLabel,
                    UIUtils.S(146),
                    pad,
                    labelWidth,
                    buttonTotal,
                    buttonGap);
            };

            return card;
        }

        private Control CreateCreditsCard()
        {
            var card = CreateCard(UIUtils.S(78), bottomMargin: 0);
            var title = CreateTextLabel("鸣谢", 11F, FontStyle.Bold, UIColors.TextMain);
            var thanks = CreateTextLabel("感谢 DT 站长星辰、QAQ 站长棒棒糖提供数据生态支持。", 9F, FontStyle.Regular, UIColors.TextSub);
            var steamDt = new LiteButton("SteamDT 主页", false) { Width = UIUtils.S(126), Height = UIUtils.S(34) };
            var qaq = new LiteButton("QAQ 主页", false) { Width = UIUtils.S(106), Height = UIUtils.S(34) };
            steamDt.Click += (_, __) => SystemActions.OpenUrl(SteamDtHomeUrl);
            qaq.Click += (_, __) => SystemActions.OpenUrl(CsqaqHomeUrl);

            card.Controls.AddRange(new Control[] { title, thanks, steamDt, qaq });
            card.Layout += (_, __) =>
            {
                int pad = UIUtils.S(24);
                title.SetBounds(pad, UIUtils.S(14), UIUtils.S(120), UIUtils.S(24));
                qaq.SetBounds(card.Width - pad - qaq.Width, UIUtils.S(28), qaq.Width, qaq.Height);
                steamDt.SetBounds(qaq.Left - UIUtils.S(12) - steamDt.Width, qaq.Top, steamDt.Width, steamDt.Height);
                thanks.SetBounds(pad, UIUtils.S(42), Math.Max(1, steamDt.Left - pad - UIUtils.S(16)), UIUtils.S(24));
            };
            return card;
        }

        private void RefreshInputsFromStore()
        {
            if (_steamDtApiInput != null)
                _steamDtApiInput.Inner.Text = Get(SettingsKeys.SteamDtApiKey, "");
            if (_csqaqApiInput != null)
                _csqaqApiInput.Inner.Text = Get(SettingsKeys.CsqaqApiToken, "");
            SetIntervalInputText(_steamDtIntervalInput, DataPageModel.NormalizeMarketRefreshValue(Get(SettingsKeys.SteamDtRefreshSec, Settings.DefaultMarketRefreshSec)));
            SetIntervalInputText(_csqaqIntervalInput, DataPageModel.NormalizeMarketRefreshValue(Get(SettingsKeys.CsqaqRefreshSec, Settings.DefaultMarketRefreshSec)));
            if (_showPercentCheck != null)
                _showPercentCheck.Checked = Get(SettingsKeys.SteamDtShowPercent, true);
            if (_formatSegments != null)
                _formatSegments.SelectedIndex = MarketFormatToSegment(Get(SettingsKeys.MarketFormat, 0));
        }

        private void RefreshAll()
        {
            Settings settings = BuildCurrentSettingsSnapshot();
            DataRedesignPageView view = DataRedesignPageModel.Build(
                settings,
                MarketDataSourceManager.GetSourceStates(settings));

            if (_healthValue != null)
            {
                _healthValue.Text = view.Summary.HealthText;
                _healthValue.ForeColor = view.Summary.HealthColor;
            }
            SetLabel(_lastRefreshValue, view.Summary.LastRefreshText, UIColors.TextMain);
            FitLabelFont(_lastRefreshValue, 10.5F, 9F);
            SetLabel(_formatSummaryValue, view.Summary.FormatText, UIColors.TextMain);
            SetLabel(_steamDtConfigValue, view.Summary.SteamDtConfigText, view.SteamDtApiConfigured ? UIColors.Positive : UIColors.TextSub);
            SetLabel(_csqaqConfigValue, view.Summary.CsqaqConfigText, view.CsqaqApiConfigured ? UIColors.Positive : UIColors.TextSub);
            SetLabel(_steamDtPreview, view.SteamDtPreviewText, view.SteamDtPreviewColor);
            SetLabel(_csqaqPreview, view.CsqaqPreviewText, view.CsqaqPreviewColor);
            UpdateSourceCard(_steamDtCard, view.SteamDt);
            UpdateSourceCard(_csqaqCard, view.Csqaq);

            if (_steamDtIntervalInput?.Inner.Focused != true)
                SetIntervalInputText(_steamDtIntervalInput, DataPageModel.NormalizeMarketRefreshValue(Get(SettingsKeys.SteamDtRefreshSec, Settings.DefaultMarketRefreshSec)));
            if (_csqaqIntervalInput?.Inner.Focused != true)
                SetIntervalInputText(_csqaqIntervalInput, DataPageModel.NormalizeMarketRefreshValue(Get(SettingsKeys.CsqaqRefreshSec, Settings.DefaultMarketRefreshSec)));
        }

        private Settings BuildCurrentSettingsSnapshot()
        {
            return new Settings
            {
                SteamDtApiKey = (_steamDtApiInput?.Inner.Text ?? Get(SettingsKeys.SteamDtApiKey, "")).Trim(),
                CsqaqApiToken = (_csqaqApiInput?.Inner.Text ?? Get(SettingsKeys.CsqaqApiToken, "")).Trim(),
                SteamDtRefreshSec = DataPageModel.NormalizeMarketRefreshValue(Get(SettingsKeys.SteamDtRefreshSec, Settings.DefaultMarketRefreshSec)),
                CsqaqRefreshSec = DataPageModel.NormalizeMarketRefreshValue(Get(SettingsKeys.CsqaqRefreshSec, Settings.DefaultMarketRefreshSec)),
                MarketFormat = Get(SettingsKeys.MarketFormat, 0),
                SteamDtShowPercent = Get(SettingsKeys.SteamDtShowPercent, true)
            };
        }

        private void UpdateSourceCard(DataRedesignSourceCardBinding? binding, DataRedesignSourceView view)
        {
            if (binding == null)
                return;

            binding.StatusPill.Text = view.StatusText;
            binding.StatusPill.AccentColor = view.StatusColor;
            binding.SourcePill.Text = view.SourceText;
            binding.Index.Text = view.IndexText;
            binding.Change.Text = view.ChangeText;
            binding.Change.ForeColor = view.ChangeColor;
            binding.Sparkline.LineColor = view.ChangeColor;
            binding.Sparkline.Positive = view.TrendPositive;
            binding.Sparkline.Invalidate();
            binding.LastRefresh.Text = view.LastRefreshText;
            binding.Detail.Text = view.DetailText;
            FitLabelFont(binding.Change, 10F, 8F);
            FitLabelFont(binding.LastRefresh, 9F, 7.5F);
            binding.StatusPill.Invalidate();
            binding.SourcePill.Invalidate();
        }

        private async Task RefreshMarketIndexesAsync(IReadOnlyList<Control> buttons)
        {
            if (_marketRefreshBusy)
                return;

            _marketRefreshBusy = true;
            SetControlsEnabled(buttons, false);
            try
            {
                SaveSteamDtCredentials(_steamDtApiInput?.Inner.Text ?? "");
                SaveCsqaqCredentials(_csqaqApiInput?.Inner.Text ?? "");
                await MarketDataSourceManager.RefreshMarketIndexesAsync("手动刷新", waitForSteamDtLock: true);
                RefreshAll();
            }
            finally
            {
                SetControlsEnabled(buttons, true);
                _marketRefreshBusy = false;
            }
        }

        private async Task RunSteamDtTestAsync(IReadOnlyList<Control> buttons, string apiKey)
        {
            if (_steamDtTestBusy)
                return;

            _steamDtTestBusy = true;
            SetControlsEnabled(buttons, false);
            try
            {
                SaveSteamDtCredentials(apiKey);
                await TestSteamDtConnection(apiKey);
            }
            finally
            {
                SetControlsEnabled(buttons, true);
                _steamDtTestBusy = false;
            }
        }

        private async Task RunCsqaqTestAsync(IReadOnlyList<Control> buttons, string token)
        {
            if (_csqaqTestBusy)
                return;

            _csqaqTestBusy = true;
            SetControlsEnabled(buttons, false);
            try
            {
                SaveCsqaqCredentials(token);
                await TestCsqaqConnection(token);
            }
            finally
            {
                SetControlsEnabled(buttons, true);
                _csqaqTestBusy = false;
            }
        }

        private async Task TestSteamDtConnection(string apiKey)
        {
            if (_steamDtResultLabel == null)
                return;

            SetLabel(_steamDtResultLabel, "正在测试...", UIColors.TextSub);
            try
            {
                int refreshSec = DataPageModel.NormalizeMarketRefreshValue(Get(SettingsKeys.SteamDtRefreshSec, Settings.DefaultMarketRefreshSec));
                await _steamDtService.TestAndUpdateAsync(apiKey, refreshSec);
                var data = _steamDtService.Latest;
                if (data != null && !data.IsStale)
                {
                    SetLabel(_steamDtResultLabel, $"连接成功  来源: {DataPageModel.NormalizeSourceText(data.Source, "公开接口")}  指数: {data.FormatIndex()}  {data.FormatRatio()}", UIColors.Positive);
                }
                else if (data != null)
                {
                    string reason = AppActions.SanitizeError(_steamDtService.LastError);
                    SetLabel(_steamDtResultLabel, "刷新失败，显示上次缓存  来源: " + DataPageModel.NormalizeSourceText(data.Source, "公开接口")
                        + (string.IsNullOrWhiteSpace(reason) ? "" : $"  原因: {reason}"), UIColors.TextWarn);
                }
                else
                {
                    SetLabel(_steamDtResultLabel, "尚未刷新成功：点击顶部“立即刷新”或检查 API", UIColors.TextCrit);
                }
            }
            catch (Exception ex)
            {
                SetLabel(_steamDtResultLabel, $"连接失败: {AppActions.SanitizeError(ex.Message)}", UIColors.TextCrit);
            }
            finally
            {
                RefreshAll();
            }
        }

        private async Task TestCsqaqConnection(string token)
        {
            if (_csqaqResultLabel == null)
                return;

            SetLabel(_csqaqResultLabel, "正在测试...", UIColors.TextSub);
            try
            {
                int refreshSec = DataPageModel.NormalizeMarketRefreshValue(Get(SettingsKeys.CsqaqRefreshSec, Settings.DefaultMarketRefreshSec));
                await _csqaqService.TestAndUpdateAsync(token, refreshSec);
                var data = _csqaqService.Latest;
                if (data != null && !data.IsStale)
                {
                    SetLabel(_csqaqResultLabel, $"连接成功  来源: {DataPageModel.NormalizeSourceText(data.Source, "公开接口")}  指数: {data.FormatIndex()}  {data.FormatRate()}", UIColors.Positive);
                }
                else if (data != null)
                {
                    string reason = AppActions.SanitizeError(_csqaqService.LastError);
                    SetLabel(_csqaqResultLabel, "刷新失败，显示上次缓存  来源: " + DataPageModel.NormalizeSourceText(data.Source, "公开接口")
                        + (string.IsNullOrWhiteSpace(reason) ? "" : $"  原因: {reason}"), UIColors.TextWarn);
                }
                else
                {
                    SetLabel(_csqaqResultLabel, "尚未刷新成功：点击顶部“立即刷新”或检查 API", UIColors.TextCrit);
                }
            }
            catch (Exception ex)
            {
                SetLabel(_csqaqResultLabel, $"连接失败: {AppActions.SanitizeError(ex.Message)}", UIColors.TextCrit);
            }
            finally
            {
                RefreshAll();
            }
        }

        private LiteNumberInput CreateMarketRefreshInput(string settingKey)
        {
            int current = DataPageModel.NormalizeMarketRefreshValue(Get(settingKey, Settings.DefaultMarketRefreshSec));
            var input = new LiteNumberInput(current.ToString(), "秒", "", 110, null, 5)
            {
                Padding = UIUtils.S(new Padding(0, 4, 0, 1))
            };

            input.Inner.TextChanged += (_, __) =>
            {
                if (_updatingIntervalInputs || IsUpdatingControls)
                    return;

                if (int.TryParse(input.Inner.Text, out int value))
                    ApplyMarketRefreshInterval(settingKey, DataPageModel.NormalizeMarketRefreshValue(value), updateText: false);
            };
            input.Inner.Leave += (_, __) => CommitMarketIntervalInput(input, settingKey);
            return input;
        }

        private LiteUnderlineInput CreateApiInput(string settingKey)
        {
            var input = new LiteUnderlineInput(Get(settingKey, ""), "", "", 320, null, HorizontalAlignment.Left)
            {
                Height = UIUtils.S(34)
            };
            input.Inner.UseSystemPasswordChar = true;
            input.Inner.TextChanged += (_, __) =>
            {
                if (!IsUpdatingControls)
                    Set(settingKey, input.Inner.Text);
            };
            return input;
        }

        private void EnsureMarketRefreshDefaults()
        {
            NormalizeMarketRefreshSetting(SettingsKeys.SteamDtRefreshSec);
            NormalizeMarketRefreshSetting(SettingsKeys.CsqaqRefreshSec);
        }

        private void NormalizeMarketRefreshSetting(string key)
        {
            int current = Get(key, Settings.DefaultMarketRefreshSec);
            int normalized = DataPageModel.NormalizeMarketRefreshValue(current);
            if (current != normalized)
                Set(key, normalized);
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
            Set(settingKey, seconds);
            int steamDtSec = DataPageModel.NormalizeMarketRefreshValue(Get(SettingsKeys.SteamDtRefreshSec, Settings.DefaultMarketRefreshSec));
            int csqaqSec = DataPageModel.NormalizeMarketRefreshValue(Get(SettingsKeys.CsqaqRefreshSec, Settings.DefaultMarketRefreshSec));
            _steamDtService.Configure((_steamDtApiInput?.Inner.Text ?? Get(SettingsKeys.SteamDtApiKey, "")).Trim(), steamDtSec);
            _csqaqService.Configure((_csqaqApiInput?.Inner.Text ?? Get(SettingsKeys.CsqaqApiToken, "")).Trim());
            MarketDataSourceManager.UpdateMarketRefreshIntervals(steamDtSec, csqaqSec);

            if (updateText)
            {
                if (settingKey == SettingsKeys.SteamDtRefreshSec)
                    SetIntervalInputText(_steamDtIntervalInput, seconds);
                else if (settingKey == SettingsKeys.CsqaqRefreshSec)
                    SetIntervalInputText(_csqaqIntervalInput, seconds);
            }

            RefreshAll();
        }

        private void SaveSteamDtCredentials(string rawKey)
        {
            string normalizedKey = rawKey?.Trim() ?? "";
            int refreshSec = DataPageModel.NormalizeMarketRefreshValue(Get(SettingsKeys.SteamDtRefreshSec, Settings.DefaultMarketRefreshSec));
            Set(SettingsKeys.SteamDtApiKey, normalizedKey);
            Set(SettingsKeys.SteamDtRefreshSec, refreshSec);
            _steamDtService.Configure(normalizedKey, refreshSec);
            MarketDataSourceManager.UpdateMarketRefreshIntervals(
                refreshSec,
                DataPageModel.NormalizeMarketRefreshValue(Get(SettingsKeys.CsqaqRefreshSec, Settings.DefaultMarketRefreshSec)));
        }

        private void SaveCsqaqCredentials(string token)
        {
            string normalizedToken = token?.Trim() ?? "";
            int refreshSec = DataPageModel.NormalizeMarketRefreshValue(Get(SettingsKeys.CsqaqRefreshSec, Settings.DefaultMarketRefreshSec));
            Set(SettingsKeys.CsqaqApiToken, normalizedToken);
            Set(SettingsKeys.CsqaqRefreshSec, refreshSec);
            _csqaqService.Configure(normalizedToken);
            MarketDataSourceManager.UpdateMarketRefreshIntervals(
                DataPageModel.NormalizeMarketRefreshValue(Get(SettingsKeys.SteamDtRefreshSec, Settings.DefaultMarketRefreshSec)),
                refreshSec);
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

        private void OnMarketDataUpdated()
        {
            if (_disposed || IsDisposed)
                return;

            void Refresh()
            {
                if (!_disposed && !IsDisposed)
                    RefreshAll();
            }

            try
            {
                if (IsHandleCreated)
                    BeginInvoke((MethodInvoker)Refresh);
                else
                    Refresh();
            }
            catch
            {
                // 页面隐藏或销毁期间的延迟刷新可以安全丢弃。
            }
        }

        private void QueueDeferredRefreshAll()
        {
            if (_refreshAllQueued || _disposed || IsDisposed)
                return;

            _refreshAllQueued = true;

            try
            {
                if (IsHandleCreated)
                {
                    BeginInvoke((MethodInvoker)RunDeferredRefreshAll);
                }
                else
                {
                    _refreshAllHandleCreatedHandler ??= (_, __) =>
                    {
                        if (_refreshAllHandleCreatedHandler != null)
                        {
                            HandleCreated -= _refreshAllHandleCreatedHandler;
                            _refreshAllHandleCreatedHandler = null;
                        }

                        if (!_refreshAllQueued || _disposed || IsDisposed)
                            return;

                        try
                        {
                            BeginInvoke((MethodInvoker)RunDeferredRefreshAll);
                        }
                        catch
                        {
                            _refreshAllQueued = false;
                        }
                    };
                    HandleCreated += _refreshAllHandleCreatedHandler;
                }
            }
            catch
            {
                _refreshAllQueued = false;
            }
        }

        private void RunDeferredRefreshAll()
        {
            _refreshAllQueued = false;
            if (_disposed || IsDisposed || !IsHandleCreated || _root == null || _root.IsDisposed || Container.IsDisposed)
                return;

            RefreshAll();
        }

        private void AddRootRow(Control control)
        {
            if (_root == null)
                return;

            int row = _root.RowCount;
            _root.RowCount = row + 1;
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Dock = DockStyle.Top;
            control.Width = ContentWidth;
            control.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _root.Controls.Add(control, 0, row);
            SyncContentWidth();
        }

        private void SyncContentWidth()
        {
            if (_root == null || _root.IsDisposed)
                return;

            Rectangle bounds = ContentBounds;
            int width = bounds.Width;
            bool changed = false;
            if (_root.Left != bounds.Left || _root.Top != bounds.Top || _root.Width != width)
            {
                _root.SetBounds(bounds.Left, bounds.Top, width, _root.Height);
                changed = true;
            }

            foreach (Control child in _root.Controls)
            {
                if (child.Width == width)
                    continue;

                child.Width = width;
                changed = true;
            }

            int height = Math.Max(UIUtils.S(1), _root.GetPreferredSize(new Size(width, 0)).Height);
            if (_root.Height != height)
            {
                _root.Height = height;
                changed = true;
            }

            if (changed)
                _root.PerformLayout();
            HideHorizontalScroll(Container);
        }

        private void QueueDeferredContentWidthSync()
        {
            if (_widthSyncQueued || IsDisposed)
                return;

            _widthSyncQueued = true;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    _widthSyncQueued = false;
                    SyncContentWidth();
                }));
            }
            catch
            {
                _widthSyncQueued = false;
                SyncContentWidth();
            }
        }

        private static YouPinCcRoundedPanel CreateCard(int height, int bottomMargin = 14)
        {
            return new YouPinCcRoundedPanel
            {
                Height = height,
                Radius = UIUtils.S(6),
                FillOverride = UIColors.CardBg,
                Margin = new Padding(0, 0, 0, UIUtils.S(bottomMargin))
            };
        }

        private static Control CreateSummaryTile(string titleText, out Label value, float valueFontSize, float minValueFontSize)
        {
            var tile = new FlatPanel();
            var title = CreateTextLabel(titleText, 9F, FontStyle.Regular, UIColors.TextSub);
            Label valueLabel = CreateTextLabel("", valueFontSize, FontStyle.Bold, UIColors.TextMain);
            value = valueLabel;
            tile.Controls.Add(title);
            tile.Controls.Add(valueLabel);
            tile.Layout += (_, __) =>
            {
                title.SetBounds(UIUtils.S(16), UIUtils.S(10), Math.Max(1, tile.Width - UIUtils.S(32)), UIUtils.S(24));
                valueLabel.SetBounds(UIUtils.S(16), UIUtils.S(39), Math.Max(1, tile.Width - UIUtils.S(32)), UIUtils.S(30));
                FitLabelFont(valueLabel, valueFontSize, minValueFontSize);
            };
            return tile;
        }

        private static Label CreatePreviewPill()
        {
            return new PillLabel
            {
                AutoSize = false,
                Font = UIFonts.Regular(9F),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
        }

        private static Label CreateTextLabel(string text, float size, FontStyle style, Color color, ContentAlignment align = ContentAlignment.MiddleLeft)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", size, style),
                ForeColor = color,
                TextAlign = align
            };
        }

        private static void LayoutCredentialRow(
            Control card,
            Label label,
            LiteUnderlineInput input,
            LiteButton test,
            LiteButton help,
            Label result,
            int top,
            int pad,
            int labelWidth,
            int buttonTotal,
            int buttonGap)
        {
            int labelTop = top + UIUtils.S(2);
            int inputLeft = pad + labelWidth;
            int helpLeft = card.Width - pad - help.Width;
            int testLeft = helpLeft - buttonGap - test.Width;
            int inputWidth = Math.Max(UIUtils.S(160), testLeft - inputLeft - UIUtils.S(18));

            label.SetBounds(pad, labelTop, labelWidth - UIUtils.S(10), UIUtils.S(30));
            input.SetBounds(inputLeft, top, inputWidth, UIUtils.S(34));
            test.SetBounds(testLeft, top, test.Width, test.Height);
            help.SetBounds(helpLeft, top, help.Width, help.Height);
            result.SetBounds(inputLeft, top + UIUtils.S(40), Math.Max(1, card.Width - inputLeft - pad), UIUtils.S(24));
        }

        private static int MarketFormatToSegment(int value)
        {
            int normalized = DataPageModel.NormalizeMarketFormatIndex(value);
            for (int i = 0; i < SegmentFormatValues.Length; i++)
            {
                if (SegmentFormatValues[i] == normalized)
                    return i;
            }

            return 0;
        }

        private static void SetLabel(Label? label, string text, Color color)
        {
            if (label == null)
                return;

            label.Text = text;
            label.ForeColor = color;
        }

        private static void FitLabelFont(Label? label, float defaultSize, float minSize)
        {
            if (label == null || label.Width <= 0 || string.IsNullOrWhiteSpace(label.Text))
                return;

            float targetSize = defaultSize;
            for (float size = defaultSize; size >= minSize; size -= 0.5F)
            {
                using var candidateFont = new Font(label.Font.FontFamily, size, label.Font.Style);
                Size textSize = TextRenderer.MeasureText(label.Text, candidateFont, Size.Empty, TextFormatFlags.NoPadding);
                if (textSize.Width <= label.Width)
                {
                    targetSize = size;
                    break;
                }

                targetSize = minSize;
            }

            if (Math.Abs(label.Font.Size - targetSize) <= 0.05F)
                return;

            Font oldFont = label.Font;
            label.Font = new Font(oldFont.FontFamily, targetSize, oldFont.Style);
            oldFont.Dispose();
        }

        private static void OpenSteamDtApiHelp(IWin32Window? owner)
        {
            using var dialog = new SteamDtApiHelpForm();
            if (owner != null)
                dialog.ShowDialog(owner);
            else
                dialog.ShowDialog();
        }

        private static void OpenCsqaqApiHelp(IWin32Window? owner)
        {
            using var dialog = new CsqaqApiHelpForm();
            if (owner != null)
                dialog.ShowDialog(owner);
            else
                dialog.ShowDialog();
        }

        private sealed record DataRedesignSourceCardBinding(
            DataRedesignPillLabel StatusPill,
            DataRedesignPillLabel SourcePill,
            Label Index,
            Label Change,
            DataRedesignSparkline Sparkline,
            LiteNumberInput IntervalInput,
            Label LastRefresh,
            Label Detail);
    }

    internal sealed class DataRedesignPillLabel : Label
    {
        public DataRedesignPillLabel()
        {
            AutoSize = false;
            BackColor = Color.Transparent;
            Font = UIFonts.Bold(8.5F);
            ForeColor = UIColors.TextMain;
            TextAlign = ContentAlignment.MiddleCenter;
        }

        public Color AccentColor { get; set; } = UIColors.Primary;

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color accent = Enabled ? AccentColor : UIColors.TextDisabled;
            Color fill = Color.FromArgb(UIColors.IsDark ? 42 : 28, accent);
            Color border = Color.FromArgb(UIColors.IsDark ? 170 : 130, accent);
            var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (GraphicsPath path = UIUtils.RoundRect(rect, UIUtils.S(6)))
            using (var fillBrush = new SolidBrush(fill))
            using (var borderPen = new Pen(border))
            {
                e.Graphics.FillPath(fillBrush, path);
                e.Graphics.DrawPath(borderPen, path);
            }

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                ClientRectangle,
                accent,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class DataRedesignSparkline : Control
    {
        public DataRedesignSparkline()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public Color LineColor { get; set; } = UIColors.Primary;

        public bool Positive { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = ClientRectangle;
            if (rect.Width <= 2 || rect.Height <= 2)
                return;

            int mid = rect.Top + rect.Height / 2;
            using (var baseline = new Pen(Color.FromArgb(90, UIColors.Border)) { DashStyle = DashStyle.Dot })
                e.Graphics.DrawLine(baseline, rect.Left, mid, rect.Right, mid);

            int count = 36;
            PointF[] points = new PointF[count];
            float step = rect.Width / (float)(count - 1);
            float trend = Positive ? -rect.Height * 0.16F : rect.Height * 0.16F;
            for (int i = 0; i < count; i++)
            {
                float x = rect.Left + i * step;
                double wave = Math.Sin(i * 0.63) * rect.Height * 0.16
                    + Math.Sin(i * 1.37) * rect.Height * 0.07;
                float y = (float)(mid + wave + trend * (i / (float)(count - 1) - 0.5F));
                y = Math.Clamp(y, rect.Top + 2, rect.Bottom - 2);
                points[i] = new PointF(x, y);
            }

            using var pen = new Pen(LineColor, Math.Max(1.4F, UIUtils.ScaleFactor * 1.4F));
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            e.Graphics.DrawLines(pen, points);
        }
    }
}
