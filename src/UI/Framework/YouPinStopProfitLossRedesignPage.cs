using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using static CS2TradeMonitor.src.UI.Framework.YouPinStopProfitLossRedesignUiFactory;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal enum ItemListFilterMode
    {
        All,
        NearTrigger,
        Triggered
    }

    public sealed class YouPinStopProfitLossRedesignPage : FrameworkSettingsPageBase
    {
        private readonly IYouPinInventoryService _inventoryService;
        private readonly UiDeferredActionScheduler _itemRowRefreshScheduler;
        private Label? _enabledStateLabel;
        private Label? _runStateLabel;
        private Label? _lastScanLabel;
        private Label? _recentAlertLabel;
        private Label? _scopeHintLabel;
        private Label? _profitThresholdTitleLabel;
        private Label? _lossThresholdTitleLabel;
        private Label? _monitorCountLabel;
        private Label? _costTotalLabel;
        private Label? _floatingPnlLabel;
        private Label? _nearTriggerLabel;
        private Label? _missingCostLabel;
        private Control? _excludedItemsRow;
        private Panel? _summaryRowPanel;
        private Control? _monitorScopeCard;
        private Panel? _monitorScopeBody;
        private Panel? _specifiedItemsPanel;
        private TableLayoutPanel? _itemRowsHost;
        private Label? _itemRowsEmptyLabel;
        private ScopeSegmentControl? _scopeSegment;
        private LiteButton? _addSpecifiedButton;
        private LiteButton? _showAllInventoryButton;
        private LiteButton? _loadMoreRowsButton;
        private LiteUnderlineInput? _itemSearchInput;
        private readonly List<LiteButton> _itemFilterButtons = new();
        private readonly Dictionary<string, int> _itemRowIndexByKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Control> _itemRowCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _itemRowSignatureByKey = new(StringComparer.OrdinalIgnoreCase);
        private LiteButton? _mockButton;
        private ToggleSwitch? _enabledSwitch;
        private LiteNumberInput? _profitThresholdInput;
        private LiteNumberInput? _lossThresholdInput;
        private ItemListFilterMode _itemFilterMode = ItemListFilterMode.All;
        private bool _showAllInventoryItems = true;
        private int _displayRowsLimit = InitialDisplayRowsLimit;
        private bool _busy;
        private TableLayoutPanel? _root;
        private bool _widthSyncQueued;
        private bool _pageBuilt;
        private bool _pageBuildQueued;
        private System.Windows.Forms.Timer? _pageBuildTimer;
        private UiAsyncRefreshController<string>? _itemSearchRefreshController;
        private const int InitialDisplayRowsLimit = 6;
        private const int DisplayRowsBatchSize = 6;
        private const int InventoryPickerDisplayLimit = 500;

        private int ContentWidth
        {
            get { return ContentBounds.Width; }
        }

        private Rectangle ContentBounds
        {
            get { return GetVisibleContentBounds(FrameworkSettingsPageLayoutHelper.StandardContentMinimumWidth); }
        }

        public YouPinStopProfitLossRedesignPage()
            : this(YouPinPageRuntimeServices.Resolve())
        {
        }

        internal YouPinStopProfitLossRedesignPage(YouPinPageRuntimeServices runtimeServices)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);
            _inventoryService = runtimeServices.Inventory;
            _itemRowRefreshScheduler = new UiDeferredActionScheduler(() => !IsDisposed && !Disposing);
            Container.SizeChanged += (_, __) => QueueDeferredWidthSync();
        }

        protected override void OnStoreAttached()
        {
            ShowLoadingPlaceholder();
            QueuePageBuild();
        }

        public override void Activate()
        {
            base.Activate();
            if (!_pageBuilt)
            {
                QueuePageBuild();
                return;
            }

            ConfigureInventoryService();
            RefreshRuntimeView();
        }

        public override void Save()
        {
            base.Save();
            RunIfSettingsChanged(ConfigureInventoryService);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _pageBuildTimer?.Stop();
                _pageBuildTimer?.Dispose();
                _pageBuildTimer = null;
                _itemRowRefreshScheduler.Dispose();
                foreach (Control row in _itemRowCache.Values.ToList())
                    row.Dispose();
                _itemRowCache.Clear();
                _itemRowSignatureByKey.Clear();
            }

            base.Dispose(disposing);
        }

        private void BuildPage()
        {
            _pageBuilt = true;
            ClearPage();
            Rectangle bounds = ContentBounds;
            _root = new TableLayoutPanel
            {
                Dock = DockStyle.None,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 0,
                BackColor = UIColors.MainBg,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            Container.Controls.Add(_root);

            AddRootRow(CreatePageHeader());
            AddRootRow(CreateStatusDashboardCard());
            AddRootRow(CreateScopeIntroRow());
            AddRootRow(CreateMonitorScopeDashboardCard());
            AddRootRow(CreateNotificationDashboardCard());
            AddRootRow(CreateRecentAlertsDashboardCard());
            AddRootRow(CreateHelpDashboardCard());
            RefreshRuntimeView();
        }

        private void ShowLoadingPlaceholder()
        {
            _pageBuilt = false;
            ClearPage();
            Rectangle bounds = ContentBounds;
            _root = new TableLayoutPanel
            {
                Dock = DockStyle.None,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                BackColor = UIColors.MainBg,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var shell = new YouPinCcRoundedPanel
            {
                Height = UIUtils.S(156),
                Radius = UIUtils.S(6),
                FillOverride = UIColors.CardBg,
                BackColor = Color.Transparent,
                Padding = UIUtils.S(new Padding(22, 20, 22, 20))
            };
            var title = CreateLabel("库存止损/盈", strong: true);
            var hint = CreateSubLabel("正在加载监控规则、库存快照和页面布局...");
            shell.Controls.Add(title);
            shell.Controls.Add(hint);
            shell.Layout += (_, __) =>
            {
                title.SetBounds(shell.Padding.Left, shell.Padding.Top, Math.Max(1, shell.Width - shell.Padding.Horizontal), UIUtils.S(30));
                hint.SetBounds(shell.Padding.Left, title.Bottom + UIUtils.S(12), Math.Max(1, shell.Width - shell.Padding.Horizontal), UIUtils.S(24));
            };

            Container.Controls.Add(_root);
            AddRootRow(shell);
        }

        private void QueuePageBuild()
        {
            if (_pageBuilt || _pageBuildQueued || IsDisposed)
                return;

            _pageBuildQueued = true;
            if (!IsHandleCreated)
            {
                EventHandler? handler = null;
                handler = (_, __) =>
                {
                    HandleCreated -= handler;
                    StartPageBuildTimer();
                };
                HandleCreated += handler;
                return;
            }

            StartPageBuildTimer();
        }

        private void StartPageBuildTimer()
        {
            if (IsDisposed || _pageBuilt)
            {
                _pageBuildQueued = false;
                return;
            }

            _pageBuildTimer ??= new System.Windows.Forms.Timer();
            _pageBuildTimer.Stop();
            _pageBuildTimer.Interval = 220;
            _pageBuildTimer.Tick -= PageBuildTimerOnTick;
            _pageBuildTimer.Tick += PageBuildTimerOnTick;
            _pageBuildTimer.Start();
        }

        private void PageBuildTimerOnTick(object? sender, EventArgs e)
        {
            _pageBuildTimer?.Stop();
            _pageBuildQueued = false;
            if (IsDisposed || _pageBuilt)
                return;

            BuildPage();
            ConfigureInventoryService();
            RefreshRuntimeView();
        }

        private void QueueDeferredWidthSync()
        {
            if (_widthSyncQueued || !IsHandleCreated || IsDisposed)
                return;

            _widthSyncQueued = true;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    _widthSyncQueued = false;
                    if (_root == null || IsDisposed)
                        return;

                    Rectangle bounds = ContentBounds;
                    int width = bounds.Width;
                    if (_root.Left != bounds.Left || _root.Top != bounds.Top || _root.Width != width)
                    {
                        _root.SetBounds(bounds.Left, bounds.Top, width, _root.Height);
                        foreach (Control child in _root.Controls)
                            child.Width = width;
                        _root.PerformLayout();
                        HideHorizontalScroll(Container);
                    }
                }));
            }
            catch
            {
                _widthSyncQueued = false;
            }
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
        }

        private Control CreatePageHeader()
        {
            var panel = new Panel
            {
                Height = UIUtils.S(50),
                Dock = DockStyle.Top,
                BackColor = UIColors.MainBg,
                Margin = new Padding(0, 0, 0, UIUtils.S(8))
            };
            var title = CreateLabel("库存止损/盈", strong: true);
            title.Font = UIFonts.Bold(16F);
            var desc = CreateHeaderDescription("读取悠悠库存的购入价(成本)，相对购入价涨到止盈 / 跌到止损即报警。只读价、不交易");
            panel.Controls.Add(title);
            panel.Controls.Add(desc);
            panel.Layout += (_, __) =>
            {
                title.SetBounds(0, UIUtils.S(4), UIUtils.S(150), UIUtils.S(34));
                desc.SetBounds(title.Right + UIUtils.S(10), UIUtils.S(8), Math.Max(1, panel.Width - title.Right - UIUtils.S(10)), UIUtils.S(28));
            };
            return panel;
        }

        private Control CreateScopeIntroRow()
        {
            var panel = new Panel
            {
                Height = UIUtils.S(34),
                Dock = DockStyle.Top,
                BackColor = UIColors.MainBg,
                Margin = new Padding(0, 0, 0, UIUtils.S(6))
            };
            var label = CreateHintLabel("监控范围二选 —— 一个入口切换整库与指定单品，阈值与通知规则共用。");
            label.Dock = DockStyle.Fill;
            panel.Controls.Add(label);
            return panel;
        }

        private Control CreateStatusDashboardCard()
        {
            return CreateDashboardCard(
                "止盈 / 损监控",
                "数据来源：悠悠有品 · 库存涨跌（购入价）",
                CreateStatusPanel(),
                UIUtils.S(166));
        }

        private Control CreateMonitorScopeDashboardCard()
        {
            _monitorScopeCard = CreateDashboardCard(
                "监控范围与阈值",
                "整库与指定单品共用阈值；指定单品可逐项覆盖",
                CreateMonitorScopeBody(),
                UIUtils.S(1140),
                accentBorder: true);
            return _monitorScopeCard;
        }

        private Control CreateNotificationDashboardCard()
        {
            return CreateDashboardCard(
                "通知与节流",
                "两种模式共用 · 触发后按冷却防重复",
                CreateNotificationBody(),
                UIUtils.S(136));
        }

        private Control CreateRecentAlertsDashboardCard()
        {
            return CreateDashboardCard(
                "最近止盈 / 损报警",
                "仅保留最近 20 条",
                CreateRecentAlertsPanel(),
                UIUtils.S(150));
        }

        private Control CreateHelpDashboardCard()
        {
            return CreateDashboardCard(
                "测试与说明",
                "模拟测试只检查提醒样式与规则",
                CreateHelpBody(),
                UIUtils.S(174));
        }

        private Control CreateDashboardCard(string titleText, string rightText, Control body, int height, bool accentBorder = false)
        {
            bool autoHeight = height <= 0;
            var card = new FlatPanel
            {
                Height = autoHeight ? UIUtils.S(360) : height,
                AutoSize = autoHeight,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = UIUtils.S(new Padding(20, 14, 20, 14)),
                Margin = new Padding(0, 0, 0, UIUtils.S(12)),
                BorderColorOverride = accentBorder ? Color.FromArgb(210, UIColors.Primary) : null
            };
            var title = CreateLabel(titleText, strong: true);
            title.Font = UIFonts.Bold(11F);
            var right = CreateHeaderDescription(rightText);
            right.TextAlign = ContentAlignment.MiddleRight;
            card.Controls.Add(title);
            card.Controls.Add(right);
            card.Controls.Add(body);
            card.Layout += (_, __) =>
            {
                int headerHeight = UIUtils.S(30);
                int left = card.Padding.Left;
                int top = card.Padding.Top;
                int innerWidth = Math.Max(1, card.Width - card.Padding.Horizontal);
                title.SetBounds(left, top, UIUtils.S(240), headerHeight);
                right.SetBounds(title.Right + UIUtils.S(12), top, Math.Max(1, left + innerWidth - title.Right - UIUtils.S(12)), headerHeight);
                int bodyTop = top + headerHeight + UIUtils.S(10);
                int bodyHeight = autoHeight
                    ? Math.Max(1, body.Height)
                    : Math.Max(1, card.Height - card.Padding.Vertical - headerHeight - UIUtils.S(10));
                body.SetBounds(left, bodyTop, innerWidth, bodyHeight);
                if (autoHeight)
                {
                    int desiredHeight = bodyTop + body.Height + card.Padding.Bottom;
                    if (Math.Abs(card.Height - desiredHeight) > UIUtils.S(2))
                        card.Height = desiredHeight;
                }
            };
            return card;
        }

        private LiteSettingsGroup CreateStatusGroup()
        {
            var group = new LiteSettingsGroup("止盈 / 损监控");
            group.AddHeaderInlineAction(CreateHeaderDescription("数据来源：悠悠有品 · 库存涨跌（购入价）。只读价、不交易。"));
            group.AddFullItem(CreateStatusPanel());
            return group;
        }

        private Control CreateStatusPanel()
        {
            var panel = new Panel
            {
                Height = UIUtils.S(112),
                Padding = UIUtils.S(new Padding(4, 4, 4, 4)),
                BackColor = Color.Transparent
            };

            _enabledSwitch = new ToggleSwitch
            {
                Checked = Get(nameof(Settings.YouPinStopProfitLossEnabled), false)
            };
            _enabledSwitch.CheckedChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                Set(nameof(Settings.YouPinStopProfitLossEnabled), _enabledSwitch.Checked);
                ConfigureInventoryService();
                RefreshRuntimeView();
            };
            RegisterRefresh(() => _enabledSwitch.Checked = Get(nameof(Settings.YouPinStopProfitLossEnabled), false));
            RegisterSave(() => Set(nameof(Settings.YouPinStopProfitLossEnabled), _enabledSwitch.Checked));

            _enabledStateLabel = CreateValueLabel("");
            _runStateLabel = CreateValueLabel("");
            _lastScanLabel = CreateValueLabel("");
            _recentAlertLabel = CreateValueLabel("");

            var enabledTile = CreateStatusTile("实时监控", _enabledStateLabel, _enabledSwitch);
            var stateTile = CreateStatusTile("运行状态", _runStateLabel);
            var scanTile = CreateStatusTile("最近扫描", _lastScanLabel);
            var alertTile = CreateStatusTile("最近报警", _recentAlertLabel);

            var scanButton = new LiteButton("立即扫描", false)
            {
                Width = UIUtils.S(118),
                Height = UIUtils.S(32)
            };
            scanButton.Click += async (_, __) => await RunScanAsync(scanButton, useMock: false);

            panel.Controls.Add(enabledTile);
            panel.Controls.Add(stateTile);
            panel.Controls.Add(scanTile);
            panel.Controls.Add(alertTile);
            panel.Controls.Add(scanButton);
            panel.Layout += (_, __) =>
            {
                int gap = UIUtils.S(12);
                int buttonWidth = UIUtils.S(128);
                int tileWidth = Math.Max(UIUtils.S(150), (panel.ClientSize.Width - panel.Padding.Horizontal - buttonWidth - gap * 4) / 4);
                int top = panel.Padding.Top;
                int height = panel.ClientSize.Height - panel.Padding.Vertical;
                int x = panel.Padding.Left;
                enabledTile.SetBounds(x, top, tileWidth, height);
                x += tileWidth + gap;
                stateTile.SetBounds(x, top, tileWidth, height);
                x += tileWidth + gap;
                scanTile.SetBounds(x, top, tileWidth, height);
                x += tileWidth + gap;
                alertTile.SetBounds(x, top, tileWidth, height);
                scanButton.SetBounds(
                    panel.ClientSize.Width - panel.Padding.Right - buttonWidth,
                    top + (height - scanButton.Height) / 2,
                    buttonWidth,
                    scanButton.Height);
            };

            return panel;
        }

        private LiteSettingsGroup CreateMonitorScopeGroup()
        {
            var group = new LiteSettingsGroup("监控范围与阈值");
            group.AddHeaderInlineAction(CreateHeaderDescription("整库与指定单品共用阈值；指定单品可逐项覆盖。"));

            var card = new FlatPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = UIUtils.S(new Padding(14, 12, 14, 12))
            };
            var layout = CreateOneColumnLayout();
            AddLayoutRow(layout, CreateScopeRow());
            AddLayoutRow(layout, CreateThresholdRow());
            AddLayoutRow(layout, CreateSummaryRow());
            AddLayoutRow(layout, CreateDivider());
            AddLayoutRow(layout, CreateExcludedItemsRow());
            AddLayoutRow(layout, CreateSpecifiedItemsBlock());
            AddLayoutRow(layout, CreateHintLabel("止盈 / 损按购入价百分比计算；缺成本的饰品不会触发，补填成本后才生效。"));
            card.Controls.Add(layout);
            group.AddFullItem(card);
            return group;
        }

        private Control CreateMonitorScopeBody()
        {
            var panel = new Panel
            {
                Height = UIUtils.S(360),
                AutoSize = false,
                Dock = DockStyle.Top,
                BackColor = Color.Transparent,
                Margin = Padding.Empty
            };
            _monitorScopeBody = panel;
            var scope = CreateScopeRow();
            var threshold = CreateThresholdRow();
            var summary = CreateSummaryRow();
            var divider1 = CreateDivider();
            var excluded = CreateExcludedItemsRow();
            var divider2 = CreateDivider();
            var specified = CreateSpecifiedItemsBlock();
            var hint = CreateHintLabel("止盈 / 损按购入价百分比计算；右侧为触发目标价；缺成本的填写后才生效。");

            panel.Controls.Add(scope);
            panel.Controls.Add(threshold);
            panel.Controls.Add(summary);
            panel.Controls.Add(divider1);
            panel.Controls.Add(excluded);
            panel.Controls.Add(divider2);
            panel.Controls.Add(specified);
            panel.Controls.Add(hint);
            panel.Layout += (_, __) =>
            {
                bool showExcluded = excluded.Visible;
                int y = UIUtils.S(24);
                LayoutBodyRow(scope, y, panel.Width);
                y += scope.Height;
                LayoutBodyRow(threshold, y, panel.Width);
                y += threshold.Height;
                LayoutBodyRow(summary, y, panel.Width);
                y += summary.Height + UIUtils.S(2);
                LayoutBodyRow(divider1, y, panel.Width);
                y += divider1.Height;
                if (showExcluded)
                {
                    LayoutBodyRow(excluded, y, panel.Width);
                    y += excluded.Height;
                    LayoutBodyRow(divider2, y, panel.Width);
                    y += divider2.Height;
                }
                else
                {
                    excluded.SetBounds(0, y, panel.Width, 0);
                    divider2.SetBounds(0, y, panel.Width, 0);
                }
                LayoutBodyRow(specified, y, panel.Width);
                y += specified.Height;
                LayoutBodyRow(hint, y, panel.Width);
            };
            return panel;
        }

        private static void LayoutBodyRow(Control control, int y, int width)
        {
            control.Dock = DockStyle.None;
            control.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            control.SetBounds(0, y, Math.Max(1, width), control.Height);
        }

        private Control CreateScopeRow()
        {
            var row = new Panel
            {
                Height = UIUtils.S(54),
                BackColor = UIColors.IsDark ? Color.FromArgb(18, 25, 34) : UIColors.CardBg
            };

            _scopeSegment = new ScopeSegmentControl
            {
                Width = UIUtils.S(260),
                Height = UIUtils.S(36)
            };
            _scopeSegment.ScopeChanged += onlySpecified => SetScope(onlySpecified);

            _scopeHintLabel = CreateSubLabel("");
            _scopeHintLabel.TextAlign = ContentAlignment.MiddleRight;

            RegisterRefresh(RefreshScopeButtons);

            row.Controls.Add(_scopeSegment);
            row.Controls.Add(_scopeHintLabel);
            row.Layout += (_, __) =>
            {
                int mid = row.Height / 2;
                _scopeSegment.SetBounds(0, mid - _scopeSegment.Height / 2, _scopeSegment.Width, _scopeSegment.Height);
                int hintLeft = _scopeSegment.Right + UIUtils.S(16);
                _scopeHintLabel.SetBounds(hintLeft, 0, Math.Max(1, row.Width - hintLeft), row.Height);
            };
            return row;
        }

        private Control CreateThresholdRow()
        {
            var row = new Panel { Height = UIUtils.S(70), BackColor = Color.Transparent };
            var profitTitle = CreateLabel("↗ 止盈阈值", strong: true);
            profitTitle.ForeColor = ProfitColor;
            var lossTitle = CreateLabel("↘ 止损阈值", strong: true);
            lossTitle.ForeColor = LossColor;
            _profitThresholdTitleLabel = profitTitle;
            _lossThresholdTitleLabel = lossTitle;

            _profitThresholdInput = CreateThresholdInput(
                Get(nameof(Settings.YouPinStopProfitPercentThreshold), 30.0),
                ProfitColor,
                () => CommitThresholdInput(_profitThresholdInput, nameof(Settings.YouPinStopProfitPercentThreshold), 30.0));
            _lossThresholdInput = CreateThresholdInput(
                Get(nameof(Settings.YouPinStopLossPercentThreshold), 30.0),
                LossColor,
                () => CommitThresholdInput(_lossThresholdInput, nameof(Settings.YouPinStopLossPercentThreshold), 30.0));
            RegisterRefresh(() =>
            {
                RefreshThresholdInputText(_profitThresholdInput, Get(nameof(Settings.YouPinStopProfitPercentThreshold), 30.0));
                RefreshThresholdInputText(_lossThresholdInput, Get(nameof(Settings.YouPinStopLossPercentThreshold), 30.0));
            });
            RegisterSave(() =>
            {
                CommitThresholdInput(_profitThresholdInput, nameof(Settings.YouPinStopProfitPercentThreshold), 30.0, refresh: false);
                CommitThresholdInput(_lossThresholdInput, nameof(Settings.YouPinStopLossPercentThreshold), 30.0, refresh: false);
            });

            _addSpecifiedButton = new LiteButton("+ 从库存添加单品", false)
            {
                Width = UIUtils.S(150),
                Height = UIUtils.S(32)
            };
            _addSpecifiedButton.Click += (_, __) => PromptAddInventoryItems();

            row.Controls.Add(profitTitle);
            row.Controls.Add(_profitThresholdInput);
            row.Controls.Add(lossTitle);
            row.Controls.Add(_lossThresholdInput);
            row.Controls.Add(_addSpecifiedButton);
            row.Layout += (_, __) =>
            {
                int buttonArea = _addSpecifiedButton.Width + UIUtils.S(18);
                int availableWidth = Math.Max(UIUtils.S(700), row.Width - buttonArea);
                int half = Math.Max(UIUtils.S(320), (availableWidth - UIUtils.S(28)) / 2);
                LayoutThresholdSide(profitTitle, _profitThresholdInput, 0, half);
                LayoutThresholdSide(lossTitle, _lossThresholdInput, half + UIUtils.S(28), availableWidth - half - UIUtils.S(28));
                _addSpecifiedButton.SetBounds(
                    row.Width - _addSpecifiedButton.Width,
                    UIUtils.S(19),
                    _addSpecifiedButton.Width,
                    _addSpecifiedButton.Height);
            };
            return row;
        }

        private LiteNumberInput CreateThresholdInput(double value, Color color, Action commit)
        {
            var input = new LiteNumberInput(FormatThresholdInput(value), "%", "", 92, color, maxLength: 5)
            {
                Height = UIUtils.S(30)
            };
            input.Inner.Font = UIFonts.Bold(10F);
            input.Inner.Leave += (_, __) => commit();
            input.Inner.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter)
                    return;

                e.SuppressKeyPress = true;
                commit();
            };
            return input;
        }

        private void CommitThresholdInput(LiteNumberInput? input, string settingKey, double fallback, bool refresh = true)
        {
            if (input == null || IsUpdatingControls)
                return;

            double current = Get(settingKey, fallback);
            double value = TryParsePercentInput(input.Inner.Text, out double parsed)
                ? Math.Clamp(parsed, 1.0, 100.0)
                : current;
            Set(settingKey, value);
            RefreshThresholdInputText(input, value, force: true);
            if (refresh)
                RefreshRuntimeView();
        }

        private static bool TryParsePercentInput(string text, out double value)
        {
            string normalized = text.Replace("%", "", StringComparison.Ordinal).Trim();
            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                return true;
            normalized = normalized.Replace(',', '.');
            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string FormatThresholdInput(double value)
        {
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private static void RefreshThresholdInputText(LiteNumberInput? input, double value, bool force = false)
        {
            if (input == null || (!force && input.Inner.Focused))
                return;

            string text = FormatThresholdInput(Math.Clamp(value, 1.0, 100.0));
            if (!string.Equals(input.Inner.Text, text, StringComparison.Ordinal))
                input.Inner.Text = text;
        }

        private static void LayoutThresholdSide(Label title, LiteNumberInput input, int left, int width)
        {
            title.SetBounds(left, UIUtils.S(6), UIUtils.S(126), UIUtils.S(28));
            int inputLeft = title.Right + UIUtils.S(10);
            int inputWidth = Math.Min(UIUtils.S(116), Math.Max(UIUtils.S(86), left + width - inputLeft));
            input.SetBounds(inputLeft, UIUtils.S(5), inputWidth, UIUtils.S(30));
        }

        private Control CreateSummaryRow()
        {
            var row = new SummaryTilesPanel
            {
                Height = UIUtils.S(88),
                Dock = DockStyle.Top,
                BackColor = Color.Transparent,
                Margin = Padding.Empty
            };

            _monitorCountLabel = CreateKpiValueLabel("");
            _costTotalLabel = CreateKpiValueLabel("");
            _floatingPnlLabel = CreateKpiValueLabel("");
            _nearTriggerLabel = CreateKpiValueLabel("");
            _missingCostLabel = CreateKpiValueLabel("");

            _summaryRowPanel = row;
            row.PaintTiles = DrawSummaryTiles;
            return row;
        }

        private void DrawSummaryTiles(Graphics graphics, Size size)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var values = new[]
            {
                ("监控件数", _monitorCountLabel?.Text ?? "", UIColors.TextMain, false),
                ("成本合计 · 均价", _costTotalLabel?.Text ?? "", UIColors.TextMain, false),
                ("当前浮盈亏", _floatingPnlLabel?.Text ?? "", _floatingPnlLabel?.ForeColor ?? UIColors.TextMain, false),
                ("接近触发", _nearTriggerLabel?.Text ?? "", UIColors.TextWarn, false),
                ("缺购入价", _missingCostLabel?.Text ?? "", UIColors.TextWarn, true)
            };

            int gap = UIUtils.S(12);
            int tileWidth = Math.Max(UIUtils.S(120), (size.Width - gap * (values.Length - 1)) / values.Length);
            int x = 0;
            for (int i = 0; i < values.Length; i++)
            {
                int width = i == values.Length - 1 ? Math.Max(1, size.Width - x) : tileWidth;
                var rect = new Rectangle(x, UIUtils.S(6), width, UIUtils.S(70));
                using var path = UIUtils.RoundRect(new Rectangle(rect.X, rect.Y, rect.Width - 1, rect.Height - 1), UIUtils.S(6));
                using var fill = new SolidBrush(UIColors.IsDark ? Color.FromArgb(18, 25, 34) : UIColors.CardBg);
                using var border = new Pen(values[i].Item4 ? Color.FromArgb(150, 190, 125, 15) : UIColors.Border);
                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);

                var titleRect = new Rectangle(rect.X + UIUtils.S(12), rect.Y + UIUtils.S(8), rect.Width - UIUtils.S(24), UIUtils.S(20));
                var valueRect = new Rectangle(rect.X + UIUtils.S(12), rect.Y + UIUtils.S(32), rect.Width - UIUtils.S(24), UIUtils.S(32));
                TextRenderer.DrawText(graphics, values[i].Item1, UIFonts.Regular(8.5F), titleRect, UIColors.TextSub,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(graphics, values[i].Item2, UIFonts.Bold(14F), valueRect, values[i].Item3,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                x += width + gap;
            }
        }

        private Control CreateExcludedItemsRow()
        {
            var row = new Panel { Height = UIUtils.S(78), BackColor = Color.Transparent };
            _excludedItemsRow = row;
            var title = CreateLabel("整库排除名单", strong: true);
            var subtitle = CreateSubLabel("这些饰品不参与整库报警");
            var chips = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                AutoScroll = true
            };
            var addButton = new LiteButton("+ 添加排除", false)
            {
                Width = UIUtils.S(112),
                Height = UIUtils.S(32)
            };
            addButton.Click += (_, __) => PromptAddKeyword(
                nameof(Settings.YouPinStopProfitLossExcludedItems),
                "添加整库排除",
                "输入要排除的饰品名称或关键词：");
            row.Controls.Add(title);
            row.Controls.Add(subtitle);
            row.Controls.Add(chips);
            row.Controls.Add(addButton);
            row.Layout += (_, __) =>
            {
                title.SetBounds(0, UIUtils.S(8), UIUtils.S(110), UIUtils.S(24));
                subtitle.SetBounds(title.Right + UIUtils.S(8), title.Top, UIUtils.S(260), UIUtils.S(24));
                addButton.SetBounds(row.Width - addButton.Width, UIUtils.S(25), addButton.Width, addButton.Height);
                chips.SetBounds(0, UIUtils.S(38), Math.Max(1, addButton.Left - UIUtils.S(12)), UIUtils.S(34));
            };
            RegisterRefresh(() => RenderExclusionChips(chips));
            RegisterSave(() => Set(nameof(Settings.YouPinStopProfitLossExcludedItems),
                YouPinStopProfitLossPageModel.NormalizeKeywordInput(Get(nameof(Settings.YouPinStopProfitLossExcludedItems), string.Empty))));
            RenderExclusionChips(chips);
            return row;
        }

        private Control CreateSpecifiedItemsBlock()
        {
            var panel = new Panel
            {
                Height = UIUtils.S(260),
                BackColor = Color.Transparent
            };
            _specifiedItemsPanel = panel;
            var title = CreateLabel("指定单品覆盖", strong: true);
            var subtitle = CreateSubLabel("勾选单品后可覆盖默认止盈 / 止损");
            _itemSearchInput = new LiteUnderlineInput("", "", "", 220) { Placeholder = "搜索饰品名 / 模板ID" };
            _itemSearchInput.Inner.TextChanged += (_, __) => ScheduleItemSearchRefresh();
            _itemSearchInput.Inner.Leave += (_, __) => ApplyItemSearchRefresh();
            _itemSearchInput.Inner.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter)
                    return;

                e.SuppressKeyPress = true;
                ApplyItemSearchRefresh();
            };
            var filter = CreateItemFilterButtons();
            _showAllInventoryButton = new LiteButton("查看全部", false)
            {
                Width = UIUtils.S(86),
                Height = UIUtils.S(28)
            };
            _showAllInventoryButton.Click += (_, __) =>
            {
                _showAllInventoryItems = !_showAllInventoryItems;
                _itemFilterMode = ItemListFilterMode.All;
                _displayRowsLimit = InitialDisplayRowsLimit;
                RefreshItemFilterButtons();
                RefreshScopeButtons();
                RefreshItemRowsPreservingScroll();
            };
            _loadMoreRowsButton = new LiteButton("继续显示更多", false)
            {
                Width = UIUtils.S(132),
                Height = UIUtils.S(30),
                Visible = false
            };
            _loadMoreRowsButton.Click += (_, __) =>
            {
                AppendMoreItemRowsPreservingScroll();
            };
            _itemRowsEmptyLabel = CreateSubLabel("开启监控后显示库存止损/盈监控项；未启用时不展示饰品。");
            _itemRowsEmptyLabel.TextAlign = ContentAlignment.MiddleCenter;
            _itemRowsHost = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 1,
                BackColor = Color.Transparent,
                AutoScroll = false,
                AutoSize = false,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            _itemRowsHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.Controls.Add(title);
            panel.Controls.Add(subtitle);
            panel.Controls.Add(_itemSearchInput);
            panel.Controls.Add(filter);
            panel.Controls.Add(_showAllInventoryButton);
            panel.Controls.Add(_itemRowsEmptyLabel);
            panel.Controls.Add(_itemRowsHost);
            panel.Controls.Add(_loadMoreRowsButton);
            panel.Layout += (_, __) =>
            {
                title.SetBounds(0, UIUtils.S(8), UIUtils.S(120), UIUtils.S(24));
                int right = panel.Width;
                _showAllInventoryButton.SetBounds(right - _showAllInventoryButton.Width, UIUtils.S(6), _showAllInventoryButton.Width, _showAllInventoryButton.Height);
                right = _showAllInventoryButton.Left - UIUtils.S(10);
                filter.SetBounds(right - filter.Width, UIUtils.S(5), filter.Width, filter.Height);
                right = filter.Left - UIUtils.S(10);
                int searchWidth = Math.Min(UIUtils.S(240), Math.Max(UIUtils.S(150), right - title.Right - UIUtils.S(170)));
                _itemSearchInput.SetBounds(right - searchWidth, UIUtils.S(5), searchWidth, UIUtils.S(30));
                subtitle.SetBounds(title.Right + UIUtils.S(8), title.Top, Math.Max(1, _itemSearchInput.Left - title.Right - UIUtils.S(16)), UIUtils.S(24));
                int top = UIUtils.S(42);
                int rowsHeight = _itemRowsHost.Visible
                    ? Math.Max(_itemRowsHost.Height, UIUtils.S(66))
                    : UIUtils.S(72);
                _itemRowsHost.SetBounds(0, top, panel.Width, rowsHeight);
                _itemRowsEmptyLabel.SetBounds(0, top, panel.Width, rowsHeight);
                int desiredHeight = top + rowsHeight + UIUtils.S(4);
                if (_loadMoreRowsButton.Visible)
                {
                    _loadMoreRowsButton.SetBounds((panel.Width - _loadMoreRowsButton.Width) / 2, desiredHeight + UIUtils.S(4), _loadMoreRowsButton.Width, _loadMoreRowsButton.Height);
                    desiredHeight = _loadMoreRowsButton.Bottom + UIUtils.S(4);
                }
            };
            return panel;
        }

        private Control CreateItemFilterButtons()
        {
            var panel = new Panel
            {
                Width = UIUtils.S(240),
                Height = UIUtils.S(30),
                BackColor = Color.Transparent
            };
            _itemFilterButtons.Clear();
            var options = new[]
            {
                ("全部", ItemListFilterMode.All),
                ("接近触发", ItemListFilterMode.NearTrigger),
                ("已超阈值", ItemListFilterMode.Triggered)
            };
            foreach (var option in options)
            {
                var button = new LiteButton(option.Item1, false)
                {
                    Height = UIUtils.S(28),
                    Tag = option.Item2
                };
                button.Click += (_, __) =>
                {
                    _itemFilterMode = option.Item2;
                    if (_itemFilterMode == ItemListFilterMode.All
                        && !Get(nameof(Settings.YouPinStopProfitLossOnlySpecifiedItems), false))
                    {
                        _showAllInventoryItems = true;
                    }

                    _displayRowsLimit = InitialDisplayRowsLimit;
                    RefreshItemFilterButtons();
                    RefreshScopeButtons();
                    RefreshItemRowsPreservingScroll();
                };
                panel.Controls.Add(button);
                _itemFilterButtons.Add(button);
            }

            panel.Layout += (_, __) =>
            {
                int width = Math.Max(UIUtils.S(72), panel.Width / Math.Max(1, _itemFilterButtons.Count));
                for (int i = 0; i < _itemFilterButtons.Count; i++)
                {
                    int x = i * width;
                    _itemFilterButtons[i].SetBounds(x, UIUtils.S(1), i == _itemFilterButtons.Count - 1 ? panel.Width - x : width + 1, UIUtils.S(28));
                }
            };
            RefreshItemFilterButtons();
            return panel;
        }

        private void RefreshItemFilterButtons()
        {
            foreach (var button in _itemFilterButtons)
                button.IsActive = button.Tag is ItemListFilterMode mode && mode == _itemFilterMode;
        }

        private void ScheduleItemSearchRefresh()
        {
            if (IsDisposed || Disposing)
                return;

            _itemSearchRefreshController ??= CreateAsyncRefreshController<string>(
                GetType().Name + ".ItemSearch",
                static (reason, _) => Task.FromResult(reason.ToString()),
                _ => ApplyItemSearchRefresh(),
                new UiRefreshOptions
                {
                    Name = "YouPinStopProfitLoss.ItemSearch",
                    DebounceMs = 300
                });
            _itemSearchRefreshController.Request(UiRefreshReason.Deferred("ItemSearch", GetType().Name));
        }

        private void ApplyItemSearchRefresh()
        {
            if (!_pageBuilt || IsDisposed || Disposing)
                return;

            _displayRowsLimit = InitialDisplayRowsLimit;
            RefreshItemRowsPreservingScroll();
        }

        private LiteSettingsGroup CreateNotificationGroup()
        {
            var group = new LiteSettingsGroup("通知与节流");
            group.AddHeaderInlineAction(CreateHeaderDescription("两种模式共用；触发后按冷却防重复。"));
            group.AddFullItem(CreateNotificationBody());
            return group;
        }

        private Control CreateNotificationBody()
        {
            var panel = new Panel
            {
                Height = UIUtils.S(112),
                Padding = UIUtils.S(new Padding(0, 0, 0, 0)),
                BackColor = Color.Transparent
            };
            var title = CreateLabel("提醒方式", strong: true);
            var modes = CreateModeButtons();
            var window = CreateNumberEditor("观察时间", "小时", () =>
                Math.Max(1, (int)Math.Round(Get(nameof(Settings.YouPinStopProfitLossWindowMinutes), 180) / 60.0)),
                value => Set(nameof(Settings.YouPinStopProfitLossWindowMinutes), Math.Clamp(value <= 0 ? 3 : value, 1, 168) * 60));
            var cooldown = CreateNumberEditor("提醒冷却", "分钟", () =>
                Get(nameof(Settings.YouPinStopProfitLossCooldownMinutes), 30),
                value => Set(nameof(Settings.YouPinStopProfitLossCooldownMinutes), Math.Clamp(value <= 0 ? 30 : value, 1, 1440)));
            var hint = CreateSubLabel("同一单品触发后，提醒冷却时间内不重复弹出；冷却过后若仍满足条件会再次提醒。");

            panel.Controls.Add(title);
            panel.Controls.Add(modes);
            panel.Controls.Add(window);
            panel.Controls.Add(cooldown);
            panel.Controls.Add(hint);
            panel.Layout += (_, __) =>
            {
                int midTop = UIUtils.S(20);
                title.SetBounds(0, midTop, UIUtils.S(84), UIUtils.S(34));
                modes.SetBounds(title.Right + UIUtils.S(10), UIUtils.S(18), Math.Min(UIUtils.S(430), panel.Width / 2), UIUtils.S(38));
                cooldown.SetBounds(panel.Width - cooldown.Width, UIUtils.S(18), cooldown.Width, cooldown.Height);
                window.SetBounds(cooldown.Left - window.Width - UIUtils.S(12), UIUtils.S(18), window.Width, window.Height);
                hint.SetBounds(0, UIUtils.S(66), panel.Width, UIUtils.S(24));
            };
            return panel;
        }

        private LiteSettingsGroup CreateRecentAlertsGroup()
        {
            var group = new LiteSettingsGroup("最近止盈 / 损报警");
            group.AddHeaderInlineAction(CreateHeaderDescription("仅保留最近 20 条。"));
            group.AddFullItem(CreateRecentAlertsPanel());
            return group;
        }

        private Control CreateRecentAlertsPanel()
        {
            var panel = new Panel
            {
                Height = UIUtils.S(80),
                Padding = Padding.Empty,
                BackColor = Color.Transparent
            };
            var alerts = _inventoryService.GetStopProfitLossState().RecentAlerts
                .OrderByDescending(alert => alert.Time)
                .Take(20)
                .ToList();

            if (alerts.Count == 0)
            {
                var empty = new FlatPanel
                {
                    Height = UIUtils.S(48),
                    Padding = UIUtils.S(new Padding(12, 8, 12, 8))
                };
                var label = CreateHintLabel("暂无报警。触发提醒不代表自动交易，只是本地通知。");
                empty.Controls.Add(label);
                empty.Layout += (_, __) => label.SetBounds(UIUtils.S(10), UIUtils.S(8), Math.Max(1, empty.Width - UIUtils.S(20)), UIUtils.S(28));
                panel.Controls.Add(empty);
                panel.Layout += (_, __) => empty.SetBounds(0, UIUtils.S(8), panel.Width, UIUtils.S(48));
            }
            else
            {
                var rows = alerts.Select(CreateRecentAlertRow).ToArray();
                for (int i = 0; i < alerts.Count; i++)
                    panel.Controls.Add(rows[i]);
                panel.Layout += (_, __) =>
                {
                    int y = UIUtils.S(4);
                    foreach (Control row in rows)
                    {
                        row.SetBounds(0, y, panel.Width, UIUtils.S(42));
                        y += UIUtils.S(46);
                    }
                };
            }

            return panel;
        }

        private LiteSettingsGroup CreateHelpGroup()
        {
            var group = new LiteSettingsGroup("测试与说明");
            group.AddHeaderInlineAction(CreateHeaderDescription("模拟测试只检查提醒样式与规则，不改变真实监控状态。"));
            group.AddFullItem(CreateHelpBody());
            return group;
        }

        private Control CreateHelpBody()
        {
            var panel = new Panel
            {
                Height = UIUtils.S(122),
                Padding = Padding.Empty,
                BackColor = Color.Transparent
            };
            _mockButton = new LiteButton("模拟测试", false)
            {
                Width = UIUtils.S(108),
                Height = UIUtils.S(32)
            };
            _mockButton.Click += async (_, __) => await RunScanAsync(_mockButton, useMock: true);
            var help = CreateSubLabel("• 阈值相对悠悠购入价计算，同款多件按均价合并。\r\n• 整库模式可用排除名单；指定单品模式只监控勾选项。\r\n• 缺成本价必须补填后才生效；提醒不会自动交易、上架或卖出。");
            help.AutoEllipsis = false;
            panel.Controls.Add(_mockButton);
            panel.Controls.Add(help);
            panel.Layout += (_, __) =>
            {
                _mockButton.SetBounds(0, UIUtils.S(28), _mockButton.Width, _mockButton.Height);
                int left = _mockButton.Right + UIUtils.S(22);
                help.SetBounds(left, UIUtils.S(10), Math.Max(1, panel.Width - left), UIUtils.S(92));
            };
            return panel;
        }

        private Control CreateModeButtons()
        {
            var panel = new Panel
            {
                Width = UIUtils.S(430),
                Height = UIUtils.S(38),
                BackColor = Color.Transparent
            };

            var options = new[]
            {
                ("气泡", YouPinSaleReminderNotificationMode.Bubble),
                ("气泡 + 提示音", YouPinSaleReminderNotificationMode.BubbleAndSound),
                ("仅提示音", YouPinSaleReminderNotificationMode.Sound),
                ("静默", YouPinSaleReminderNotificationMode.Silent)
            };

            var buttons = options
                .Select(option =>
                {
                    var button = new LiteButton(option.Item1, false)
                    {
                        Height = UIUtils.S(34)
                    };
                    button.Click += (_, __) =>
                    {
                        Set(nameof(Settings.YouPinStopProfitLossNotificationMode), option.Item2);
                        ConfigureInventoryService();
                        RefreshModeButtons(panel, buttons: null);
                    };
                    button.Tag = option.Item2;
                    panel.Controls.Add(button);
                    return button;
                })
                .ToArray();

            panel.Layout += (_, __) =>
            {
                int width = Math.Max(UIUtils.S(82), panel.Width / buttons.Length);
                for (int i = 0; i < buttons.Length; i++)
                    buttons[i].SetBounds(i * width, UIUtils.S(2), i == buttons.Length - 1 ? panel.Width - i * width : width + 1, UIUtils.S(34));
            };
            RegisterRefresh(() => RefreshModeButtons(panel, buttons));
            RegisterSave(() => Set(nameof(Settings.YouPinStopProfitLossNotificationMode),
                Get(nameof(Settings.YouPinStopProfitLossNotificationMode), YouPinSaleReminderNotificationMode.BubbleAndSound)));
            RefreshModeButtons(panel, buttons);
            return panel;
        }

        private void RefreshModeButtons(Control panel, LiteButton[]? buttons)
        {
            buttons ??= panel.Controls.OfType<LiteButton>().ToArray();
            var mode = Get(nameof(Settings.YouPinStopProfitLossNotificationMode), YouPinSaleReminderNotificationMode.BubbleAndSound);
            foreach (var button in buttons)
                button.IsActive = button.Tag is YouPinSaleReminderNotificationMode option && option == mode;
        }

        private Control CreateNumberEditor(string title, string unit, Func<int> get, Action<int> set)
        {
            var panel = new Panel
            {
                Width = UIUtils.S(152),
                Height = UIUtils.S(36),
                BackColor = Color.Transparent
            };
            var label = CreateSubLabel(title);
            var input = new LiteNumberInput(get().ToString(CultureInfo.InvariantCulture), unit, "", 70)
            {
                Width = UIUtils.S(86)
            };
            input.Inner.TextChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                if (int.TryParse(input.Inner.Text, out int value))
                    set(value);
            };
            RegisterRefresh(() => input.Inner.Text = get().ToString(CultureInfo.InvariantCulture));
            RegisterSave(() =>
            {
                if (int.TryParse(input.Inner.Text, out int value))
                    set(value);
            });
            panel.Controls.Add(label);
            panel.Controls.Add(input);
            panel.Layout += (_, __) =>
            {
                label.SetBounds(0, 0, UIUtils.S(62), panel.Height);
                input.SetBounds(label.Right + UIUtils.S(6), UIUtils.S(2), input.Width, input.Height);
            };
            return panel;
        }

        private void SetScope(bool onlySpecified)
        {
            Set(nameof(Settings.YouPinStopProfitLossOnlySpecifiedItems), onlySpecified);
            _showAllInventoryItems = !onlySpecified;
            _displayRowsLimit = InitialDisplayRowsLimit;
            ConfigureInventoryService();
            RefreshRuntimeView(preserveScroll: true);
        }

        private void RefreshRuntimeView(bool refreshRows = true, bool preserveScroll = false)
        {
            RefreshScopeButtons();
            RefreshStatusLabels();
            RefreshThresholdLabels();
            RefreshSummaryLabels();
            if (refreshRows)
            {
                if (preserveScroll)
                    RefreshItemRowsPreservingScroll();
                else
                    RefreshItemRows();
            }
            Invalidate(true);
        }

        private void RefreshScopeButtons()
        {
            bool onlySpecified = Get(nameof(Settings.YouPinStopProfitLossOnlySpecifiedItems), false);
            if (_scopeSegment != null)
                _scopeSegment.OnlySpecified = onlySpecified;
            if (_excludedItemsRow != null)
            {
                _excludedItemsRow.Visible = !onlySpecified;
                _excludedItemsRow.Parent?.PerformLayout();
                ResizeMonitorScopeContainers();
            }
            if (_scopeHintLabel != null)
            {
                _scopeHintLabel.Text = onlySpecified
                    ? "模式 B · 只监控勾选单品，逐项设置止盈 / 止损"
                    : "模式 A · 库存内所有有成本价饰品按统一阈值监控";
            }
            if (_showAllInventoryButton != null)
            {
                _showAllInventoryButton.Visible = !onlySpecified;
                _showAllInventoryButton.Text = _showAllInventoryItems ? "只看重点" : "查看全部";
            }
        }

        private void RefreshStatusLabels()
        {
            var state = _inventoryService.GetStopProfitLossState();
            var trendState = _inventoryService.GetTrendState();
            var manualCosts = LoadManualCosts();
            bool enabled = Get(nameof(Settings.YouPinStopProfitLossEnabled), false);
            int monitored = enabled ? BuildEffectiveGroups(trendState).Sum(group => group.Quantity) : 0;
            int missing = CountMissingCosts(trendState, manualCosts);

            if (_enabledStateLabel != null)
            {
                _enabledStateLabel.Text = enabled ? "已启用" : "已关闭";
                _enabledStateLabel.ForeColor = enabled ? UIColors.TextMain : UIColors.TextDisabled;
            }
            if (_runStateLabel != null)
            {
                _runStateLabel.Text = enabled
                    ? monitored > 0 ? "监控中" : missing > 0 ? "待补成本" : "等待库存"
                    : "未运行";
                _runStateLabel.ForeColor = enabled && monitored > 0
                    ? UIColors.Positive
                    : enabled && missing > 0 ? UIColors.TextWarn : UIColors.TextSub;
            }
            if (_lastScanLabel != null)
            {
                DateTime lastFetch = state.LastFetch == DateTime.MinValue ? trendState.LastFetch : state.LastFetch;
                _lastScanLabel.Text = lastFetch == DateTime.MinValue
                    ? "暂无"
                    : FormatRelativeTime(lastFetch);
            }
            if (_recentAlertLabel != null)
            {
                _recentAlertLabel.Text = state.RecentAlerts.Count == 0
                    ? "暂无报警"
                    : $"{state.RecentAlerts.Count} 条";
                _recentAlertLabel.ForeColor = state.RecentAlerts.Count == 0 ? UIColors.TextMain : UIColors.TextWarn;
            }
        }

        private void RefreshThresholdLabels()
        {
            if (_profitThresholdTitleLabel != null)
                _profitThresholdTitleLabel.Text = "↗ 止盈阈值";
            if (_lossThresholdTitleLabel != null)
                _lossThresholdTitleLabel.Text = "↘ 止损阈值";
            RefreshThresholdInputText(_profitThresholdInput, Get(nameof(Settings.YouPinStopProfitPercentThreshold), 30.0));
            RefreshThresholdInputText(_lossThresholdInput, Get(nameof(Settings.YouPinStopLossPercentThreshold), 30.0));
        }

        private void RefreshSummaryLabels()
        {
            bool enabled = Get(nameof(Settings.YouPinStopProfitLossEnabled), false);
            var trendState = _inventoryService.GetTrendState();
            var manualCosts = LoadManualCosts();
            var groups = enabled ? BuildEffectiveGroups(trendState) : new List<YouPinStopProfitLossMonitorGroup>();
            double costTotal = groups.Sum(group => group.CostUnitPrice * group.Quantity);
            double currentTotal = groups.Sum(group => group.CurrentUnitPrice * group.Quantity);
            double pnl = currentTotal - costTotal;
            int count = groups.Sum(group => group.Quantity);
            int missing = enabled
                ? CountMissingCosts(trendState, manualCosts)
                : 0;
            int nearTrigger = groups.Count(IsNearTrigger);

            if (_monitorCountLabel != null)
                _monitorCountLabel.Text = count.ToString(CultureInfo.InvariantCulture);
            if (_costTotalLabel != null)
                _costTotalLabel.Text = costTotal > 0 ? "¥" + costTotal.ToString("N0", CultureInfo.InvariantCulture) : "—";
            if (_floatingPnlLabel != null)
            {
                _floatingPnlLabel.Text = pnl == 0 ? "—" : (pnl > 0 ? "+¥" : "-¥") + Math.Abs(pnl).ToString("N0", CultureInfo.InvariantCulture);
                _floatingPnlLabel.ForeColor = pnl >= 0 ? ProfitColor : LossColor;
            }
            if (_nearTriggerLabel != null)
                _nearTriggerLabel.Text = nearTrigger + " 件";
            if (_missingCostLabel != null)
                _missingCostLabel.Text = missing + " 件";
            _summaryRowPanel?.Invalidate();
        }

        private void RefreshItemRows()
        {
            if (_itemRowsHost == null || _itemRowsEmptyLabel == null)
                return;

            bool enabled = Get(nameof(Settings.YouPinStopProfitLossEnabled), false);
            _itemRowIndexByKey.Clear();
            _itemRowsHost.SuspendLayout();
            try
            {
                _itemRowsHost.Controls.Clear();
                _itemRowsHost.RowStyles.Clear();
                _itemRowsHost.RowCount = 0;

                if (!enabled)
                {
                    PruneItemRuleRowCache(Array.Empty<string>());
                    if (_loadMoreRowsButton != null)
                        _loadMoreRowsButton.Visible = false;
                    _itemRowsHost.Visible = false;
                    _itemRowsEmptyLabel.Visible = true;
                    _itemRowsEmptyLabel.Text = "开启监控后显示库存止损/盈监控项；未启用时不展示饰品。";
                    SetRowsHostHeight(0);
                    return;
                }

                var rows = ApplyDisplayFilters(BuildDisplayRows());
                int totalRows = rows.Count;
                var visibleRows = rows
                    .Take(Math.Max(1, _displayRowsLimit))
                    .ToList();
                if (_loadMoreRowsButton != null)
                {
                    _loadMoreRowsButton.Visible = totalRows > visibleRows.Count;
                    _loadMoreRowsButton.Text = $"显示更多（{visibleRows.Count}/{totalRows}）";
                }
                if (visibleRows.Count == 0)
                {
                    PruneItemRuleRowCache(Array.Empty<string>());
                    _itemRowsHost.Visible = false;
                    _itemRowsEmptyLabel.Visible = true;
                    _itemRowsEmptyLabel.Text = BuildItemRowsEmptyText();
                    SetRowsHostHeight(0);
                    return;
                }

                _itemRowsHost.Visible = true;
                _itemRowsEmptyLabel.Visible = false;
                var visibleKeys = visibleRows
                    .Select(BuildDisplayRowKey)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                PruneItemRuleRowCache(visibleKeys);
                _itemRowsHost.RowCount = visibleRows.Count;
                for (int i = 0; i < visibleRows.Count; i++)
                {
                    _itemRowsHost.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(68)));
                    var itemRow = GetOrCreateItemRuleRow(visibleRows[i]);
                    itemRow.Dock = DockStyle.Fill;
                    _itemRowsHost.Controls.Add(itemRow, 0, i);
                    _itemRowIndexByKey[BuildDisplayRowKey(visibleRows[i])] = i;
                }
                SetRowsHostHeight(visibleRows.Count);
            }
            finally
            {
                _itemRowsHost.ResumeLayout(performLayout: true);
            }
        }

        private void RefreshItemRowsPreservingScroll()
        {
            Point oldScroll = Container.AutoScrollPosition;
            Control? oldActive = FindForm()?.ActiveControl;
            RefreshItemRows();
            RestoreScrollAndFocus(oldScroll, oldActive);
        }

        private void AppendMoreItemRowsPreservingScroll()
        {
            if (_itemRowsHost == null || _itemRowsEmptyLabel == null)
            {
                _displayRowsLimit += DisplayRowsBatchSize;
                return;
            }

            Point oldScroll = Container.AutoScrollPosition;
            Control? oldActive = FindForm()?.ActiveControl;
            int oldCount = _itemRowsHost.Visible ? _itemRowsHost.RowCount : 0;
            _displayRowsLimit += DisplayRowsBatchSize;

            var rows = ApplyDisplayFilters(BuildDisplayRows());
            int newCount = Math.Min(rows.Count, Math.Max(1, _displayRowsLimit));
            if (oldCount <= 0 || oldCount > newCount || _itemRowsHost.Controls.Count != oldCount)
            {
                RefreshItemRows();
                RestoreScrollAndFocus(oldScroll, oldActive);
                return;
            }

            _itemRowsHost.SuspendLayout();
            try
            {
                _itemRowsHost.RowCount = newCount;
                for (int i = oldCount; i < newCount; i++)
                {
                    _itemRowsHost.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(68)));
                    var itemRow = GetOrCreateItemRuleRow(rows[i]);
                    itemRow.Dock = DockStyle.Fill;
                    _itemRowsHost.Controls.Add(itemRow, 0, i);
                    _itemRowIndexByKey[BuildDisplayRowKey(rows[i])] = i;
                }

                if (_loadMoreRowsButton != null)
                {
                    _loadMoreRowsButton.Visible = rows.Count > newCount;
                    _loadMoreRowsButton.Text = $"显示更多（{newCount}/{rows.Count}）";
                }

                _itemRowsHost.Visible = true;
                _itemRowsEmptyLabel.Visible = false;
                SetRowsHostHeight(newCount);
            }
            finally
            {
                _itemRowsHost.ResumeLayout(performLayout: true);
            }

            RestoreScrollAndFocus(oldScroll, oldActive);
        }

        private void RefreshSingleItemRow(StopProfitLossDisplayRow row)
        {
            if (_itemRowsHost == null)
                return;

            string key = BuildDisplayRowKey(row);
            var updated = ApplyDisplayFilters(BuildDisplayRows())
                .FirstOrDefault(item => string.Equals(BuildDisplayRowKey(item), key, StringComparison.OrdinalIgnoreCase));
            if (updated == null || !_itemRowIndexByKey.TryGetValue(key, out int index))
            {
                RefreshItemRowsPreservingScroll();
                return;
            }

            Control? old = _itemRowsHost.GetControlFromPosition(0, index);
            string signature = BuildDisplayRowSignature(updated);
            if (old != null
                && _itemRowSignatureByKey.TryGetValue(key, out string? previousSignature)
                && string.Equals(previousSignature, signature, StringComparison.Ordinal))
            {
                old.PerformLayout();
                return;
            }

            if (old != null)
                _itemRowsHost.Controls.Remove(old);

            RemoveCachedItemRuleRow(key);
            var replacement = GetOrCreateItemRuleRow(updated);
            replacement.Dock = DockStyle.Fill;
            _itemRowsHost.Controls.Add(replacement, 0, index);
            _itemRowsHost.PerformLayout();
        }

        private void QueueSingleItemRowRefresh(StopProfitLossDisplayRow row)
        {
            string key = "YouPinStopProfitLoss.ItemRow." + BuildDisplayRowKey(row);
            _itemRowRefreshScheduler.Schedule(key, 1, () => RefreshSingleItemRow(row));
        }

        private Control GetOrCreateItemRuleRow(StopProfitLossDisplayRow row)
        {
            string key = BuildDisplayRowKey(row);
            string signature = BuildDisplayRowSignature(row);
            if (_itemRowCache.TryGetValue(key, out Control? cached)
                && _itemRowSignatureByKey.TryGetValue(key, out string? previousSignature)
                && string.Equals(previousSignature, signature, StringComparison.Ordinal)
                && !cached.IsDisposed)
            {
                return cached;
            }

            RemoveCachedItemRuleRow(key);
            Control itemRow = CreateItemRuleRow(row);
            _itemRowCache[key] = itemRow;
            _itemRowSignatureByKey[key] = signature;
            return itemRow;
        }

        private void PruneItemRuleRowCache(IEnumerable<string> visibleKeys)
        {
            var visible = visibleKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var staleKeys = _itemRowCache.Keys
                .Where(key => !visible.Contains(key))
                .ToList();
            foreach (string key in staleKeys)
                RemoveCachedItemRuleRow(key);
        }

        private void RemoveCachedItemRuleRow(string key)
        {
            if (_itemRowCache.TryGetValue(key, out Control? row))
            {
                if (ReferenceEquals(row.Parent, _itemRowsHost))
                    _itemRowsHost?.Controls.Remove(row);
                row.Dispose();
                _itemRowCache.Remove(key);
            }

            _itemRowSignatureByKey.Remove(key);
        }

        private static string BuildDisplayRowSignature(StopProfitLossDisplayRow row)
        {
            return string.Join(
                "\u001f",
                row.Name,
                row.TemplateId,
                row.Enabled,
                row.MissingCost,
                row.IsPreview,
                row.Badge,
                row.SecondaryBadge,
                row.Detail,
                row.ProfitText,
                row.LossText,
                row.ProfitTargetText,
                row.LossTargetText,
                row.MissingWarning,
                row.Source,
                row.Percent.ToString("R", CultureInfo.InvariantCulture),
                row.IsNearTrigger,
                row.IsTriggered,
                row.ManualCost,
                row.BadgeTone,
                row.SecondaryBadgeTone);
        }

        private void SetRowsHostHeight(int rowCount)
        {
            if (_itemRowsHost == null)
                return;

            _itemRowsHost.Height = rowCount > 0
                ? UIUtils.S(68) * rowCount
                : UIUtils.S(120);
            ResizeMonitorScopeContainers();
        }

        private void ResizeMonitorScopeContainers()
        {
            int rowsHeight = _itemRowsHost?.Visible == true
                ? _itemRowsHost.Height
                : UIUtils.S(72);
            int specifiedHeight = UIUtils.S(42) + rowsHeight + UIUtils.S(8);
            if (_loadMoreRowsButton?.Visible == true)
                specifiedHeight += _loadMoreRowsButton.Height + UIUtils.S(10);

            if (_specifiedItemsPanel != null && Math.Abs(_specifiedItemsPanel.Height - specifiedHeight) > UIUtils.S(2))
                _specifiedItemsPanel.Height = specifiedHeight;

            bool showExcluded = _excludedItemsRow?.Visible != false;
            int bodyHeight = UIUtils.S(24 + 54 + 54 + 90 + 10 + 28 + 8)
                + specifiedHeight
                + (showExcluded ? UIUtils.S(90) : 0);
            if (_monitorScopeBody != null && Math.Abs(_monitorScopeBody.Height - bodyHeight) > UIUtils.S(2))
                _monitorScopeBody.Height = bodyHeight;

            int cardHeight = bodyHeight + UIUtils.S(68);
            if (_monitorScopeCard != null && Math.Abs(_monitorScopeCard.Height - cardHeight) > UIUtils.S(2))
                _monitorScopeCard.Height = cardHeight;

            _root?.PerformLayout();
        }

        private void RestoreScrollAndFocus(Point oldScroll, Control? oldActive)
        {
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed)
                        return;

                    Container.AutoScrollPosition = new Point(-oldScroll.X, -oldScroll.Y);
                    if (oldActive != null && !oldActive.IsDisposed && oldActive.CanFocus)
                        oldActive.Focus();
                }));
            }
            catch
            {
                // 搜索输入控件可能已销毁，焦点恢复失败不影响列表刷新。
            }
        }

        private List<StopProfitLossDisplayRow> ApplyDisplayFilters(List<StopProfitLossDisplayRow> rows)
        {
            bool onlySpecified = Get(nameof(Settings.YouPinStopProfitLossOnlySpecifiedItems), false);
            string search = _itemSearchInput?.Inner.Text?.Trim() ?? string.Empty;
            bool hasSearch = search.Length > 0;
            IEnumerable<StopProfitLossDisplayRow> query = rows;

            if (hasSearch)
            {
                query = query.Where(row =>
                    row.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || row.TemplateId.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            if (_itemFilterMode == ItemListFilterMode.NearTrigger)
                query = query.Where(row => row.IsNearTrigger);
            else if (_itemFilterMode == ItemListFilterMode.Triggered)
                query = query.Where(row => row.IsTriggered);

            if (!onlySpecified && !hasSearch && !_showAllInventoryItems && _itemFilterMode == ItemListFilterMode.All)
                query = query.Where(row => row.IsNearTrigger || row.IsTriggered);

            return query.ToList();
        }

        private string BuildItemRowsEmptyText()
        {
            bool onlySpecified = Get(nameof(Settings.YouPinStopProfitLossOnlySpecifiedItems), false);
            if (!onlySpecified && !_showAllInventoryItems && _itemFilterMode == ItemListFilterMode.All)
                return "当前为只看重点项：仅显示接近触发或已超阈值的饰品；可搜索或点“查看全部”。";
            return "暂无匹配项：请调整搜索、筛选，或先完成库存扫描/补填成本。";
        }

        private static string BuildDisplayRowKey(StopProfitLossDisplayRow row)
        {
            return !string.IsNullOrWhiteSpace(row.TemplateId)
                ? "T:" + row.TemplateId
                : "N:" + row.Name;
        }

        private List<StopProfitLossDisplayRow> BuildDisplayRows()
        {
            var trendState = _inventoryService.GetTrendState();
            var rules = YouPinStopProfitLossRuleStore.LoadRules(
                Get(nameof(Settings.YouPinStopProfitLossItemRulesJson), string.Empty));
            var manualCosts = LoadManualCosts();
            double globalProfit = Get(nameof(Settings.YouPinStopProfitPercentThreshold), 30.0);
            double globalLoss = Get(nameof(Settings.YouPinStopLossPercentThreshold), 30.0);
            var costRows = BuildDisplayGroups(trendState, rules, manualCosts)
                .Select(group => StopProfitLossDisplayRow.FromGroup(
                    group,
                    YouPinStopProfitLossRuleStore.FindAnyRule(group, rules),
                    globalProfit,
                    globalLoss,
                    IsManualCostGroup(group, manualCosts)))
                .ToList();
            var missingRows = BuildMissingCostRows(trendState, globalProfit, globalLoss, manualCosts);
            return costRows
                .Concat(missingRows)
                .ToList();
        }

        private List<YouPinStopProfitLossMonitorGroup> BuildDisplayGroups(
            YouPinInventoryTrendState trendState,
            IReadOnlyList<YouPinStopProfitLossItemRule> rules,
            IReadOnlyList<YouPinStopProfitLossManualCostEntry> manualCosts)
        {
            var items = BuildTrendItemsWithManualCosts(trendState, manualCosts);
            var groups = YouPinStopProfitLossRuleStore.BuildCostBasisGroups(items);
            bool onlySpecified = Get(nameof(Settings.YouPinStopProfitLossOnlySpecifiedItems), false);
            var excluded = YouPinStopProfitLossPageModel.SplitSpecifiedItems(
                Get(nameof(Settings.YouPinStopProfitLossExcludedItems), string.Empty)).ToList();
            var specified = YouPinStopProfitLossPageModel.SplitSpecifiedItems(
                Get(nameof(Settings.YouPinStopProfitLossSpecifiedItems), string.Empty)).ToList();

            return groups
                .Where(group =>
                {
                    var rule = YouPinStopProfitLossRuleStore.FindAnyRule(group, rules);
                    if (onlySpecified)
                        return rule != null || YouPinStopProfitLossRuleStore.MatchesKeywords(group, specified);

                    return !YouPinStopProfitLossRuleStore.MatchesKeywords(group, excluded);
                })
                .OrderByDescending(group => Math.Abs(group.CurrentUnitPrice - group.CostUnitPrice))
                .ToList();
        }

        private List<StopProfitLossDisplayRow> BuildMissingCostRows(
            YouPinInventoryTrendState trendState,
            double globalProfit,
            double globalLoss,
            IReadOnlyList<YouPinStopProfitLossManualCostEntry> manualCosts)
        {
            bool onlySpecified = Get(nameof(Settings.YouPinStopProfitLossOnlySpecifiedItems), false);
            var excluded = YouPinStopProfitLossPageModel.SplitSpecifiedItems(
                Get(nameof(Settings.YouPinStopProfitLossExcludedItems), string.Empty)).ToList();
            var specified = YouPinStopProfitLossPageModel.SplitSpecifiedItems(
                Get(nameof(Settings.YouPinStopProfitLossSpecifiedItems), string.Empty)).ToList();
            var rules = YouPinStopProfitLossRuleStore.LoadRules(
                Get(nameof(Settings.YouPinStopProfitLossItemRulesJson), string.Empty));

            return trendState.Rows
                .Where(row => !string.IsNullOrWhiteSpace(row.Name) || !string.IsNullOrWhiteSpace(row.TemplateId))
                .Where(row => row.CurrentPrice > 0)
                .Where(row => !row.HasPurchasePrice || row.MissingPurchaseCount > 0)
                .Where(row => !YouPinStopProfitLossManualCostStore.TryFindCost(manualCosts, row.Name, row.TemplateId, out _))
                .Where(row =>
                {
                    bool hasRule = rules.Any(rule => RuleMatchesTrendRow(rule, row));
                    bool matchesSpecified = MatchesTrendRowKeywords(row, specified);
                    if (onlySpecified)
                        return hasRule || matchesSpecified;

                    return !MatchesTrendRowKeywords(row, excluded);
                })
                .OrderByDescending(row => row.CurrentPrice)
                .ThenBy(row => row.Name)
                .Select(row => StopProfitLossDisplayRow.FromMissingTrendRow(row, globalProfit, globalLoss))
                .ToList();
        }

        private static bool RuleMatchesTrendRow(YouPinStopProfitLossItemRule rule, YouPinInventoryTrendRow row)
        {
            if (!rule.Enabled)
                return false;
            if (!string.IsNullOrWhiteSpace(rule.TemplateId)
                && string.Equals(rule.TemplateId, row.TemplateId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(rule.Name)
                && MatchesTrendRowKeyword(row, rule.Name);
        }

        private static bool MatchesTrendRowKeywords(YouPinInventoryTrendRow row, IReadOnlyList<string> keywords)
        {
            return keywords != null && keywords.Any(keyword => MatchesTrendRowKeyword(row, keyword));
        }

        private static bool MatchesTrendRowKeyword(YouPinInventoryTrendRow row, string keyword)
        {
            keyword = (keyword ?? string.Empty).Trim();
            return keyword.Length > 0
                && (row.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || row.TemplateId.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        private List<YouPinStopProfitLossManualCostEntry> LoadManualCosts()
        {
            return YouPinStopProfitLossManualCostStore.LoadCosts(
                Get(nameof(Settings.YouPinStopProfitLossManualCostJson), string.Empty));
        }

        private static List<YouPinInventoryItem> BuildTrendItemsWithManualCosts(
            YouPinInventoryTrendState trendState,
            IReadOnlyList<YouPinStopProfitLossManualCostEntry> manualCosts)
        {
            var items = YouPinInventoryComputationHelper.BuildItemsFromTrendRows(trendState);
            return YouPinStopProfitLossManualCostStore.ApplyManualCostsToItems(items, manualCosts);
        }

        private static bool IsManualCostGroup(
            YouPinStopProfitLossMonitorGroup group,
            IReadOnlyList<YouPinStopProfitLossManualCostEntry> manualCosts)
        {
            return YouPinStopProfitLossManualCostStore.TryFindCost(manualCosts, group.Name, group.TemplateId, out _);
        }

        private static int CountMissingCosts(
            YouPinInventoryTrendState trendState,
            IReadOnlyList<YouPinStopProfitLossManualCostEntry> manualCosts)
        {
            return trendState.Rows
                .Where(row => !string.IsNullOrWhiteSpace(row.Name) || !string.IsNullOrWhiteSpace(row.TemplateId))
                .Where(row => !row.HasPurchasePrice || row.MissingPurchaseCount > 0)
                .Where(row => !YouPinStopProfitLossManualCostStore.TryFindCost(manualCosts, row.Name, row.TemplateId, out _))
                .Sum(row => row.MissingPurchaseCount > 0 ? row.MissingPurchaseCount : Math.Max(1, row.Quantity));
        }

        private List<YouPinStopProfitLossMonitorGroup> BuildEffectiveGroups(YouPinInventoryTrendState trendState)
        {
            var manualCosts = LoadManualCosts();
            var items = BuildTrendItemsWithManualCosts(trendState, manualCosts);
            var groups = YouPinStopProfitLossRuleStore.BuildCostBasisGroups(items);
            bool onlySpecified = Get(nameof(Settings.YouPinStopProfitLossOnlySpecifiedItems), false);
            var excluded = YouPinStopProfitLossPageModel.SplitSpecifiedItems(
                Get(nameof(Settings.YouPinStopProfitLossExcludedItems), string.Empty)).ToList();
            var specified = YouPinStopProfitLossPageModel.SplitSpecifiedItems(
                Get(nameof(Settings.YouPinStopProfitLossSpecifiedItems), string.Empty)).ToList();
            var rules = YouPinStopProfitLossRuleStore.LoadRules(
                Get(nameof(Settings.YouPinStopProfitLossItemRulesJson), string.Empty));

            return groups
                .Where(group =>
                {
                    var rule = YouPinStopProfitLossRuleStore.FindAnyRule(group, rules);
                    if (rule?.Enabled == false)
                        return false;

                    if (onlySpecified)
                    {
                        return rule != null || YouPinStopProfitLossRuleStore.MatchesKeywords(group, specified);
                    }

                    return !YouPinStopProfitLossRuleStore.MatchesKeywords(group, excluded);
                })
                .OrderByDescending(group => Math.Abs(group.CurrentUnitPrice - group.CostUnitPrice))
                .ToList();
        }

        private bool IsNearTrigger(YouPinStopProfitLossMonitorGroup group)
        {
            double profitThreshold = Get(nameof(Settings.YouPinStopProfitPercentThreshold), 30.0);
            double lossThreshold = Get(nameof(Settings.YouPinStopLossPercentThreshold), 30.0);
            double percent = (group.CurrentUnitPrice - group.CostUnitPrice) / group.CostUnitPrice * 100.0;
            return percent < profitThreshold && percent >= profitThreshold - 5
                || percent > -lossThreshold && percent <= -lossThreshold + 5;
        }

        private Control CreateItemRuleRow(StopProfitLossDisplayRow row)
        {
            var panel = new FlatPanel
            {
                Height = UIUtils.S(66),
                Padding = UIUtils.S(new Padding(10, 6, 10, 6)),
                BorderColorOverride = row.MissingCost ? Color.FromArgb(180, 190, 125, 15) : null
            };
            var check = new LiteCheck(row.Enabled, "")
            {
                Width = UIUtils.S(24),
                Enabled = !row.IsPreview && !row.MissingCost
            };
            check.CheckedChanged += (_, __) =>
            {
                if (!check.Enabled || IsUpdatingControls)
                    return;
                UpdateRule(row, rule => rule.Enabled = check.Checked);
            };
            var name = CreateLabel(row.Name, strong: true);
            var badge = CreateBadge(row.Badge, row.BadgeTone);
            var secondaryBadge = string.IsNullOrWhiteSpace(row.SecondaryBadge)
                ? null
                : CreateBadge(row.SecondaryBadge, row.SecondaryBadgeTone);
            var detail = CreateSubLabel(row.Detail);
            detail.TextAlign = ContentAlignment.MiddleRight;
            detail.ForeColor = row.MissingCost
                ? UIColors.TextSub
                : row.Detail.Contains(" -", StringComparison.Ordinal) ? LossColor : ProfitColor;
            var profitLabel = CreateSubLabel("止盈");
            profitLabel.ForeColor = UIColors.TextMain;
            var lossLabel = CreateSubLabel("止损");
            lossLabel.ForeColor = UIColors.TextMain;
            var profit = CreateItemPercentInput(row.ProfitText, ProfitColor, row.Enabled && !row.MissingCost && !row.IsPreview);
            var loss = CreateItemPercentInput(row.LossText, LossColor, row.Enabled && !row.MissingCost && !row.IsPreview);
            var profitToggle = CreateDirectionToggle(row.ProfitText, row.Enabled && !row.MissingCost && !row.IsPreview);
            var lossToggle = CreateDirectionToggle(row.LossText, row.Enabled && !row.MissingCost && !row.IsPreview);
            var profitTarget = CreateSubLabel(row.ProfitTargetText);
            var lossTarget = CreateSubLabel(row.LossTargetText);
            var warning = CreateSubLabel("⚠ " + row.MissingWarning);
            warning.ForeColor = UIColors.TextWarn;
            warning.Visible = row.MissingCost;
            profit.Inner.Leave += (_, __) => CommitItemPercentInput(row, profit, isProfit: true);
            profit.Inner.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter)
                    return;

                e.SuppressKeyPress = true;
                CommitItemPercentInput(row, profit, isProfit: true);
            };
            loss.Inner.Leave += (_, __) => CommitItemPercentInput(row, loss, isProfit: false);
            loss.Inner.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter)
                    return;

                e.SuppressKeyPress = true;
                CommitItemPercentInput(row, loss, isProfit: false);
            };
            profitToggle.Click += (_, __) => ToggleRuleDirection(row, isProfit: true);
            lossToggle.Click += (_, __) => ToggleRuleDirection(row, isProfit: false);
            var manualCostInput = new LiteNumberInput("", "", "¥", 92, UIColors.TextWarn, maxLength: 10)
            {
                Width = UIUtils.S(96),
                Visible = row.MissingCost,
                Enabled = row.MissingCost,
                Placeholder = "成本价"
            };
            var manualCostButton = new LiteButton("填写", false)
            {
                Width = UIUtils.S(54),
                Height = UIUtils.S(28),
                Visible = row.MissingCost,
                Enabled = row.MissingCost
            };
            manualCostButton.Click += (_, __) => CommitManualCostInput(row, manualCostInput);
            manualCostInput.Inner.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter)
                    return;

                e.SuppressKeyPress = true;
                CommitManualCostInput(row, manualCostInput);
            };
            bool onlySpecifiedMode = Get(nameof(Settings.YouPinStopProfitLossOnlySpecifiedItems), false);
            var remove = new LiteButton(onlySpecifiedMode ? "移除" : "重置", false)
            {
                Width = UIUtils.S(52),
                Height = UIUtils.S(28),
                Enabled = !row.IsPreview
            };
            remove.Click += (_, __) => RemoveRuleOrKeyword(row);
            panel.Controls.Add(check);
            panel.Controls.Add(name);
            panel.Controls.Add(badge);
            if (secondaryBadge != null)
                panel.Controls.Add(secondaryBadge);
            panel.Controls.Add(detail);
            panel.Controls.Add(profitLabel);
            panel.Controls.Add(profit);
            panel.Controls.Add(profitToggle);
            panel.Controls.Add(profitTarget);
            panel.Controls.Add(lossLabel);
            panel.Controls.Add(loss);
            panel.Controls.Add(lossToggle);
            panel.Controls.Add(lossTarget);
            panel.Controls.Add(warning);
            panel.Controls.Add(manualCostInput);
            panel.Controls.Add(manualCostButton);
            panel.Controls.Add(remove);
            panel.Layout += (_, __) =>
            {
                check.SetBounds(UIUtils.S(4), UIUtils.S(20), check.Width, check.Height);
                int nameLeft = check.Right + UIUtils.S(10);
                int removeLeft = panel.Width - panel.Padding.Right - remove.Width;
                remove.SetBounds(removeLeft, UIUtils.S(18), remove.Width, remove.Height);
                detail.SetBounds(Math.Max(UIUtils.S(470), remove.Left - UIUtils.S(350)), UIUtils.S(5), Math.Max(1, remove.Left - Math.Max(UIUtils.S(470), remove.Left - UIUtils.S(350)) - UIUtils.S(8)), UIUtils.S(22));
                name.SetBounds(nameLeft, UIUtils.S(5), Math.Max(UIUtils.S(170), Math.Min(UIUtils.S(360), detail.Left - nameLeft - UIUtils.S(120))), UIUtils.S(22));
                badge.SetBounds(name.Right + UIUtils.S(8), UIUtils.S(7), badge.Width, badge.Height);
                if (secondaryBadge != null)
                    secondaryBadge.SetBounds(badge.Right + UIUtils.S(6), badge.Top, secondaryBadge.Width, secondaryBadge.Height);

                int controlTop = UIUtils.S(36);
                if (row.MissingCost)
                {
                    warning.SetBounds(nameLeft, controlTop, UIUtils.S(320), UIUtils.S(22));
                    int inputLeft = Math.Max(warning.Right + UIUtils.S(12), panel.Width / 2 - UIUtils.S(40));
                    manualCostInput.SetBounds(inputLeft, UIUtils.S(32), UIUtils.S(96), UIUtils.S(28));
                    manualCostButton.SetBounds(manualCostInput.Right + UIUtils.S(8), UIUtils.S(32), UIUtils.S(54), UIUtils.S(28));
                    profit.SetBounds(manualCostButton.Right + UIUtils.S(16), UIUtils.S(32), UIUtils.S(86), UIUtils.S(28));
                    profitToggle.SetBounds(profit.Right + UIUtils.S(6), UIUtils.S(32), UIUtils.S(44), UIUtils.S(28));
                    loss.SetBounds(profitToggle.Right + UIUtils.S(10), UIUtils.S(32), UIUtils.S(86), UIUtils.S(28));
                    lossToggle.SetBounds(loss.Right + UIUtils.S(6), UIUtils.S(32), UIUtils.S(44), UIUtils.S(28));
                    profitLabel.Visible = false;
                    lossLabel.Visible = false;
                    profitTarget.Visible = false;
                    lossTarget.Visible = false;
                    profitToggle.Visible = false;
                    lossToggle.Visible = false;
                    return;
                }

                profitLabel.Visible = true;
                lossLabel.Visible = true;
                profitTarget.Visible = true;
                lossTarget.Visible = true;
                profitToggle.Visible = true;
                lossToggle.Visible = true;
                profitLabel.SetBounds(nameLeft, controlTop, UIUtils.S(42), UIUtils.S(24));
                profit.SetBounds(profitLabel.Right + UIUtils.S(4), UIUtils.S(32), UIUtils.S(82), UIUtils.S(28));
                profitToggle.SetBounds(profit.Right + UIUtils.S(6), UIUtils.S(32), UIUtils.S(44), UIUtils.S(28));
                profitTarget.SetBounds(profitToggle.Right + UIUtils.S(8), controlTop, UIUtils.S(86), UIUtils.S(24));
                lossLabel.SetBounds(profitTarget.Right + UIUtils.S(26), controlTop, UIUtils.S(42), UIUtils.S(24));
                loss.SetBounds(lossLabel.Right + UIUtils.S(4), UIUtils.S(32), UIUtils.S(82), UIUtils.S(28));
                lossToggle.SetBounds(loss.Right + UIUtils.S(6), UIUtils.S(32), UIUtils.S(44), UIUtils.S(28));
                lossTarget.SetBounds(lossToggle.Right + UIUtils.S(8), controlTop, Math.Max(1, remove.Left - lossToggle.Right - UIUtils.S(16)), UIUtils.S(24));
            };
            return panel;
        }

        private static LiteNumberInput CreateItemPercentInput(string text, Color color, bool enabled)
        {
            var input = new LiteNumberInput(
                ExtractPercentText(text),
                "%",
                "",
                72,
                color,
                maxLength: 6,
                allowNegative: false)
            {
                Enabled = enabled && !IsDirectionClosed(text)
            };
            input.Inner.Font = UIFonts.Bold(9F);
            return input;
        }

        private static LiteButton CreateDirectionToggle(string text, bool enabled)
        {
            var toggle = new LiteButton(IsDirectionClosed(text) ? "关" : "开", false)
            {
                Width = UIUtils.S(44),
                Height = UIUtils.S(28),
                Enabled = enabled
            };
            toggle.IsActive = !IsDirectionClosed(text);
            return toggle;
        }

        private void CommitManualCostInput(StopProfitLossDisplayRow row, LiteNumberInput input)
        {
            if (!row.MissingCost || !TryParsePercentInput(input.Inner.Text, out double value))
            {
                input.Inner.Focus();
                return;
            }

            value = Math.Round(Math.Clamp(value, 0.01, 10_000_000), 2);
            var costs = YouPinStopProfitLossManualCostStore.UpsertCost(
                Get(nameof(Settings.YouPinStopProfitLossManualCostJson), string.Empty),
                row.Name,
                row.TemplateId,
                value);
            Set(nameof(Settings.YouPinStopProfitLossManualCostJson),
                YouPinStopProfitLossManualCostStore.SaveCosts(costs));
            ConfigureInventoryService();
            RefreshStatusLabels();
            RefreshSummaryLabels();
            QueueSingleItemRowRefresh(row);
            Invalidate(true);
        }

        private void CommitItemPercentInput(StopProfitLossDisplayRow row, LiteNumberInput input, bool isProfit)
        {
            if (!input.Enabled
                || !TryParsePercentInput(input.Inner.Text, out double value)
                || value <= 0
                || value > 1000)
            {
                input.Inner.Text = ExtractPercentText(isProfit ? row.ProfitText : row.LossText);
                return;
            }

            UpdateRule(row, rule =>
            {
                if (isProfit)
                {
                    rule.ProfitEnabled = true;
                    rule.ProfitPercent = value;
                }
                else
                {
                    rule.LossEnabled = true;
                    rule.LossPercent = value;
                }
            });
        }

        private static bool IsDirectionClosed(string text)
        {
            return string.Equals((text ?? string.Empty).Trim(), "关", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractPercentText(string text)
        {
            string value = (text ?? string.Empty).Trim();
            if (IsDirectionClosed(value))
                return "";
            return value
                .Replace("+", "", StringComparison.Ordinal)
                .Replace("-", "", StringComparison.Ordinal)
                .Replace("%", "", StringComparison.Ordinal)
                .Trim();
        }

        private void UpdateRule(StopProfitLossDisplayRow row, Action<YouPinStopProfitLossItemRule> update)
        {
            var rules = YouPinStopProfitLossRuleStore.LoadRules(
                Get(nameof(Settings.YouPinStopProfitLossItemRulesJson), string.Empty));
            var rule = rules.FirstOrDefault(item =>
                (!string.IsNullOrWhiteSpace(row.TemplateId)
                    && string.Equals(item.TemplateId, row.TemplateId, StringComparison.OrdinalIgnoreCase))
                || string.Equals(item.Name, row.Name, StringComparison.OrdinalIgnoreCase));
            if (rule == null)
            {
                rule = new YouPinStopProfitLossItemRule { Name = row.Name, TemplateId = row.TemplateId };
                rules.Add(rule);
            }

            update(rule);
            Set(nameof(Settings.YouPinStopProfitLossItemRulesJson), YouPinStopProfitLossRuleStore.SaveRules(rules));
            ConfigureInventoryService();
            RefreshRuntimeView(refreshRows: false);
            QueueSingleItemRowRefresh(row);
        }

        private void RemoveRuleOrKeyword(StopProfitLossDisplayRow row)
        {
            var rules = YouPinStopProfitLossRuleStore.LoadRules(
                Get(nameof(Settings.YouPinStopProfitLossItemRulesJson), string.Empty));
            rules = rules
                .Where(rule => !RuleMatchesDisplayRow(rule, row))
                .ToList();

            var specified = YouPinStopProfitLossPageModel.SplitSpecifiedItems(
                    Get(nameof(Settings.YouPinStopProfitLossSpecifiedItems), string.Empty))
                .Where(keyword => !row.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    && !row.TemplateId.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();

            Set(nameof(Settings.YouPinStopProfitLossItemRulesJson), YouPinStopProfitLossRuleStore.SaveRules(rules));
            Set(nameof(Settings.YouPinStopProfitLossSpecifiedItems), string.Join(", ", specified));
            ConfigureInventoryService();
            RefreshRuntimeView(preserveScroll: true);
        }

        private static bool RuleMatchesDisplayRow(YouPinStopProfitLossItemRule rule, StopProfitLossDisplayRow row)
        {
            if (!string.IsNullOrWhiteSpace(rule.TemplateId)
                && string.Equals(rule.TemplateId, row.TemplateId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(rule.Name)
                && row.Name.Contains(rule.Name, StringComparison.OrdinalIgnoreCase);
        }

        private void ToggleRuleDirection(StopProfitLossDisplayRow row, bool isProfit)
        {
            UpdateRule(row, rule =>
            {
                if (isProfit)
                {
                    rule.ProfitEnabled = !rule.ProfitEnabled;
                    if (!rule.ProfitEnabled)
                        rule.ProfitPercent = null;
                }
                else
                {
                    rule.LossEnabled = !rule.LossEnabled;
                    if (!rule.LossEnabled)
                        rule.LossPercent = null;
                }
            });
        }

        private void RenderExclusionChips(FlowLayoutPanel chips)
        {
            chips.Controls.Clear();
            var items = YouPinStopProfitLossPageModel.SplitSpecifiedItems(
                Get(nameof(Settings.YouPinStopProfitLossExcludedItems), string.Empty)).ToList();
            if (items.Count == 0)
            {
                chips.Controls.Add(CreateSubLabel("暂无排除项"));
                return;
            }

            foreach (string item in items)
            {
                var chip = CreateChip(item + "  ×");
                chip.Click += (_, __) =>
                {
                    var kept = YouPinStopProfitLossPageModel.SplitSpecifiedItems(
                            Get(nameof(Settings.YouPinStopProfitLossExcludedItems), string.Empty))
                        .Where(value => !string.Equals(value, item, StringComparison.OrdinalIgnoreCase));
                    Set(nameof(Settings.YouPinStopProfitLossExcludedItems), string.Join(", ", kept));
                    RenderExclusionChips(chips);
                    RefreshRuntimeView();
                };
                chips.Controls.Add(chip);
            }
        }

        private void PromptAddKeyword(string settingKey, string title, string prompt)
        {
            string value = Interaction.InputBox(prompt, title, "");
            value = YouPinStopProfitLossPageModel.CleanCandidateName(value);
            if (string.IsNullOrWhiteSpace(value))
                return;

            var items = YouPinStopProfitLossPageModel.SplitSpecifiedItems(Get(settingKey, string.Empty)).ToList();
            if (!items.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)))
                items.Add(value);

            Set(settingKey, string.Join(", ", items));
            ConfigureInventoryService();
            RefreshRuntimeView(preserveScroll: true);
        }

        private void PromptAddInventoryItems()
        {
            var candidates = BuildInventoryPickerCandidates();
            if (candidates.Count == 0)
            {
                if (_runStateLabel != null)
                {
                    _runStateLabel.Text = "库存为空，先完成扫描";
                    _runStateLabel.ForeColor = UIColors.TextWarn;
                }
                return;
            }

            var existing = YouPinStopProfitLossPageModel.SplitSpecifiedItems(
                Get(nameof(Settings.YouPinStopProfitLossSpecifiedItems), string.Empty)).ToList();

            using var dialog = new Form
            {
                Text = "从库存添加单品",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                ClientSize = UIUtils.S(new Size(520, 420)),
                BackColor = UIColors.MainBg,
                ForeColor = UIColors.TextMain,
                Font = UIFonts.Regular(9F)
            };
            var search = new LiteUnderlineInput("", "", "", 280)
            {
                Placeholder = "搜索库存饰品名"
            };
            var status = CreateSubLabel("");
            var list = new CheckedListBox
            {
                CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = UIColors.ControlBg,
                ForeColor = UIColors.TextMain,
                IntegralHeight = false
            };

            var checkedItems = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
            bool updatingList = false;

            void renderCandidates()
            {
                string keyword = search.Inner.Text.Trim();
                List<string> filtered = string.IsNullOrWhiteSpace(keyword)
                    ? candidates
                    : candidates
                        .Where(candidate => candidate.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                List<string> visible = filtered.Take(InventoryPickerDisplayLimit).ToList();

                updatingList = true;
                list.BeginUpdate();
                try
                {
                    list.Items.Clear();
                    foreach (string candidate in visible)
                        list.Items.Add(candidate, checkedItems.Contains(candidate));
                }
                finally
                {
                    list.EndUpdate();
                    updatingList = false;
                }

                status.Text = filtered.Count == candidates.Count
                    ? $"共 {candidates.Count} 个候选，当前显示 {visible.Count} 个。"
                    : $"共 {candidates.Count} 个候选，筛选命中 {filtered.Count} 个，当前显示 {visible.Count} 个。";
                status.ForeColor = filtered.Count > visible.Count ? UIColors.TextWarn : UIColors.TextSub;
            }

            list.ItemCheck += (_, e) =>
            {
                if (updatingList || e.Index < 0 || e.Index >= list.Items.Count)
                    return;

                string? item = list.Items[e.Index] as string;
                if (string.IsNullOrWhiteSpace(item))
                    return;

                if (e.NewValue == CheckState.Checked)
                    checkedItems.Add(item);
                else
                    checkedItems.Remove(item);
            };
            search.CommittedTextChanged += (_, __) => renderCandidates();
            renderCandidates();

            var ok = new Button { Text = "添加", DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel };
            dialog.AcceptButton = ok;
            dialog.CancelButton = cancel;
            dialog.Controls.Add(search);
            dialog.Controls.Add(status);
            dialog.Controls.Add(list);
            dialog.Controls.Add(ok);
            dialog.Controls.Add(cancel);
            dialog.Layout += (_, __) =>
            {
                int margin = UIUtils.S(14);
                int buttonWidth = UIUtils.S(86);
                int buttonHeight = UIUtils.S(32);
                int top = margin;
                search.SetBounds(margin, top, dialog.ClientSize.Width - margin * 2, UIUtils.S(30));
                status.SetBounds(margin, search.Bottom + UIUtils.S(8), dialog.ClientSize.Width - margin * 2, UIUtils.S(22));
                list.SetBounds(
                    margin,
                    status.Bottom + UIUtils.S(8),
                    dialog.ClientSize.Width - margin * 2,
                    Math.Max(UIUtils.S(80), dialog.ClientSize.Height - status.Bottom - margin * 2 - buttonHeight));
                cancel.SetBounds(dialog.ClientSize.Width - margin - buttonWidth, dialog.ClientSize.Height - margin - buttonHeight, buttonWidth, buttonHeight);
                ok.SetBounds(cancel.Left - UIUtils.S(10) - buttonWidth, cancel.Top, buttonWidth, buttonHeight);
            };

            if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
                return;

            foreach (string selected in checkedItems)
            {
                string value = YouPinStopProfitLossPageModel.CleanCandidateName(selected);
                if (!string.IsNullOrWhiteSpace(value)
                    && !existing.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)))
                {
                    existing.Add(value);
                }
            }

            Set(nameof(Settings.YouPinStopProfitLossSpecifiedItems), string.Join(", ", existing));
            ConfigureInventoryService();
            RefreshRuntimeView(preserveScroll: true);
        }

        private List<string> BuildInventoryPickerCandidates()
        {
            var trendState = _inventoryService.GetTrendState();
            var grouped = YouPinStopProfitLossRuleStore.BuildCostBasisGroups(
                    YouPinInventoryComputationHelper.BuildItemsFromTrendRows(trendState))
                .Select(group => group.Name);
            var missing = trendState.Rows
                .Select(row => row.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name));

            return grouped
                .Concat(missing!)
                .Select(YouPinStopProfitLossPageModel.CleanCandidateName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task RunScanAsync(LiteButton button, bool useMock)
        {
            if (_busy || Config == null)
                return;

            Save();
            _busy = true;
            string oldText = button.Text;
            button.Enabled = false;
            button.Text = "扫描中...";
            try
            {
                _inventoryService.Configure(Config);
                await _inventoryService.FetchNowAsync(useMock);
                RefreshRuntimeView(preserveScroll: true);
            }
            finally
            {
                if (!button.IsDisposed)
                {
                    button.Text = oldText;
                    button.Enabled = true;
                }

                _busy = false;
            }
        }

        private void ConfigureInventoryService()
        {
            if (Config != null)
                _inventoryService.Configure(Config);
        }

        private static TableLayoutPanel CreateOneColumnLayout()
        {
            return new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 0,
                Dock = DockStyle.Top,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
        }

        private static void AddLayoutRow(TableLayoutPanel layout, Control control)
        {
            int row = layout.RowCount;
            layout.RowCount = row + 1;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Dock = DockStyle.Top;
            layout.Controls.Add(control, 0, row);
        }

        private static string FormatRelativeTime(DateTime time)
        {
            var span = DateTime.Now - time;
            if (span.TotalMinutes < 1)
                return "刚刚";
            if (span.TotalMinutes < 60)
                return $"{Math.Max(1, (int)Math.Round(span.TotalMinutes))} 分钟前";
            if (span.TotalHours < 24)
                return $"{Math.Max(1, (int)Math.Round(span.TotalHours))} 小时前";
            return time.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        private static readonly Color ProfitColor = Color.FromArgb(255, 82, 105);
        private static readonly Color LossColor = Color.FromArgb(48, 198, 158);
    }

}
