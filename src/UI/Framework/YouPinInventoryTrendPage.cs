using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.SettingsPage;

namespace CS2TradeMonitor.src.UI.Framework
{
    public class YouPinInventoryTrendHostPage : FrameworkSettingsHostPage<YouPinInventoryTrendPage>
    {
        public YouPinInventoryTrendHostPage()
            : base(new YouPinInventoryTrendPage(YouPinPageRuntimeServices.Resolve()))
        {
        }
    }

    public sealed class YouPinInventoryTrendPage : FrameworkSettingsPageBase
    {
        private static readonly Color DefaultRiseColor = Color.FromArgb(220, 70, 90);
        private static readonly Color DefaultFallColor = Color.FromArgb(80, 160, 135);

        private readonly Action _dataUpdatedHandler;
        private readonly IYouPinInventoryService _inventoryService;
        private readonly IYouPinAuthService _authService;
        private readonly bool _showAuthControls;
        private readonly YouPinInventoryTrendGridScrollCoordinator _gridScrollCoordinator;
        private readonly YouPinInventoryTrendRefreshCoordinator _refreshCoordinator;

        private Label? _authLabel;
        private Label? _lastFetchLabel;
        private Label? _marketValueLabel;
        private Label? _marketSubLabel;
        private Label? _deltaValueLabel;
        private Label? _deltaSubLabel;
        private Label? _purchaseValueLabel;
        private Label? _purchaseSubLabel;
        private YouPinTrendCurveControl? _profitCurveChart;
        private Label? _emptyLabel;
        private LiteUnderlineInput? _searchInput;
        private LiteComboBox? _filterCombo;
        private Panel? _gridHost;
        private readonly YouPinTrendGridViewAdapter _gridView;
        private DataGridView? _grid;
        private ThemedVerticalScrollBar? _gridScrollBar;
        private List<TrendGridRow> _gridRows = new();
        private string _gridSortColumn = "Price";
        private bool _gridSortDescending = true;
        private System.Windows.Forms.Timer? _filterDebounceTimer;
        private System.Windows.Forms.Timer? _gridWarmupTimer;
        private System.Windows.Forms.Timer? _gridBindTimer;
        private bool _disposed;
        private List<TrendGridRow>? _pendingGridPreparedRows;
        private int _pendingGridRowIndex;
        private string _pendingGridSignature = "";
        private int _gridPrepareVersion;
        private string _lastGridSignature = "";

        public YouPinInventoryTrendPage()
            : this(YouPinPageRuntimeServices.Resolve())
        {
        }

        internal YouPinInventoryTrendPage(YouPinPageRuntimeServices runtimeServices, bool showAuthControls = true)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);

            _inventoryService = runtimeServices.Inventory;
            _authService = runtimeServices.Auth;
            _showAuthControls = showAuthControls;
            _gridView = new YouPinTrendGridViewAdapter(
                () => TrendFontSize,
                () => TrendTextColor,
                () => TrendSubTextColor,
                GetTrendColor);
            _gridScrollCoordinator = new YouPinInventoryTrendGridScrollCoordinator(
                () => _grid,
                () => _gridScrollBar,
                () => _disposed || IsDisposed,
                () => IsHandleCreated,
                action => BeginInvoke(action));
            _refreshCoordinator = new YouPinInventoryTrendRefreshCoordinator(
                () => _disposed || IsDisposed,
                () => Visible,
                () => IsHandleCreated,
                action => BeginInvoke(action),
                RefreshData,
                force => PopulateGrid(_inventoryService.GetTrendState(), force),
                ConfigureInventoryService,
                token => _inventoryService.FetchNowAsync(cancellationToken: token),
                message => DiagnosticsLogger.Info("YouPinInventoryTrend", message));
            _dataUpdatedHandler = _refreshCoordinator.HandleDataUpdated;

            _inventoryService.DataUpdated += _dataUpdatedHandler;
            if (!_showAuthControls)
                Container.Padding = FrameworkSettingsPageLayoutHelper.CreateDefaultPagePadding();
            BuildPage();
        }

        private float TrendFontSize
        {
            get
            {
                float size = Get(nameof(Settings.YouPinTrendFontSize), Config?.YouPinTrendFontSize ?? 9f);
                return Math.Clamp(size <= 0 ? 9f : size, 7f, 16f);
            }
        }

        private Color TrendTextColor => ParseThemeAwareConfiguredColor(
            Get(nameof(Settings.YouPinTrendTextColor), Config?.YouPinTrendTextColor ?? "#202020"),
            "#202020",
            UIColors.TextMain);

        private Color TrendSubTextColor => ParseThemeAwareConfiguredColor(
            Get(nameof(Settings.YouPinTrendSubTextColor), Config?.YouPinTrendSubTextColor ?? "#5A5A5A"),
            "#5A5A5A",
            UIColors.TextSub);

        private Color TrendRiseColor => ParseConfiguredColor(
            Get(nameof(Settings.YouPinTrendRiseColor), Config?.YouPinTrendRiseColor ?? "#DC465A"),
            DefaultRiseColor);

        private Color TrendFallColor => ParseConfiguredColor(
            Get(nameof(Settings.YouPinTrendFallColor), Config?.YouPinTrendFallColor ?? "#50A087"),
            DefaultFallColor);

        private Color TrendCurveColor => ParseConfiguredColor(
            Get(nameof(Settings.YouPinTrendCurveColor), Config?.YouPinTrendCurveColor ?? "#0078D7"),
            UIColors.Primary);

        public override void Activate()
        {
            base.Activate();
            ConfigureInventoryService();
            _refreshCoordinator.QueueRefreshData();
        }

        public override void Deactivate()
        {
            _refreshCoordinator.StopDeferredRefresh();
            base.Deactivate();
        }

        public override void Save()
        {
            base.Save();
            RunIfSettingsChanged(ConfigureInventoryService);
        }

        public override void ApplySystemTheme()
        {
            base.ApplySystemTheme();
            ApplyGridTheme();
            _refreshCoordinator.QueueRefreshData(force: true);
        }

        protected override void OnStoreAttached()
        {
            _refreshCoordinator.QueueRefreshData(force: true);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible)
                _refreshCoordinator.QueueRefreshData(force: true);
        }

        private void BuildPage()
        {
            using (UiJankProfiler.Measure("YouPinInventoryTrend.BuildPage", thresholdMs: 1))
            {
                Container.SuspendLayout();
                try
                {
                    ClearPage();
                    CreateListCard();
                    QueueGridWarmupIfNeeded();
                    CreateProfitCurveCard();
                    CreateSummaryCard();
                    _refreshCoordinator.QueueRefreshData(force: true, delayMs: 140);
                }
                finally
                {
                    Container.ResumeLayout(false);
                }
            }
        }

        private void CreateSummaryCard()
        {
            if (!_showAuthControls)
            {
                CreateCcSummaryCard();
                return;
            }

            var card = YouPinInventoryTrendSummaryCardFactory.Create(
                () => _refreshCoordinator.RefreshNowAsync(PageToken),
                OpenRefreshSettingsDialog,
                OpenYouPinAuthDialog,
                _showAuthControls);
            _lastFetchLabel = card.LastFetchLabel;
            _authLabel = card.AuthLabel;
            _marketValueLabel = card.MarketValueLabel;
            _marketSubLabel = card.MarketSubLabel;
            _deltaValueLabel = card.DeltaValueLabel;
            _deltaSubLabel = card.DeltaSubLabel;
            _purchaseValueLabel = card.PurchaseValueLabel;
            _purchaseSubLabel = card.PurchaseSubLabel;
            AddGroupToPage(card.Group);
        }

        private void CreateProfitCurveCard()
        {
            if (!_showAuthControls)
            {
                CreateCcProfitCurveCard();
                return;
            }

            var group = new LiteSettingsGroup("收益曲线图");
            _profitCurveChart = new YouPinTrendCurveControl(new List<YouPinDailyPnl>(), TrendCurveColor, TrendRiseColor, TrendFallColor, TrendTextColor, TrendSubTextColor, TrendFontSize)
            {
                Height = UIUtils.S(220),
                Dock = DockStyle.Top
            };

            group.AddFullItem(_profitCurveChart);
            AddGroupToPage(group);
        }

        private void CreateCcSummaryCard()
        {
            var card = new Panel
            {
                Height = UIUtils.S(188),
                BackColor = Color.Transparent
            };
            var lastFetch = YouPinCcUi.Label("上次刷新 暂无", 9.2F, FontStyle.Regular, UIColors.TextSub);
            var refreshButton = YouPinInventoryTrendUiFactory.CreateHeaderButton("立即刷新");
            refreshButton.Width = UIUtils.S(96);
            refreshButton.Height = UIUtils.S(34);
            refreshButton.Click += async (_, __) => await _refreshCoordinator.RefreshNowAsync(PageToken);

            var settingsButton = YouPinInventoryTrendUiFactory.CreateHeaderButton("刷新设置");
            settingsButton.Width = UIUtils.S(96);
            settingsButton.Height = UIUtils.S(34);
            settingsButton.Click += (_, __) => OpenRefreshSettingsDialog();

            Panel market = CreateCcStatCard("市场价", out _marketValueLabel, out _marketSubLabel);
            Panel delta = CreateCcStatCard("今日涨跌", out _deltaValueLabel, out _deltaSubLabel);
            Panel purchase = CreateCcStatCard("购入成本", out _purchaseValueLabel, out _purchaseSubLabel);

            _lastFetchLabel = lastFetch;
            _authLabel = null;
            card.Controls.Add(lastFetch);
            card.Controls.Add(refreshButton);
            card.Controls.Add(settingsButton);
            card.Controls.Add(market);
            card.Controls.Add(delta);
            card.Controls.Add(purchase);
            card.Layout += (_, __) =>
            {
                int width = Math.Max(1, card.Width);
                int top = UIUtils.S(4);
                int gap = UIUtils.S(12);
                settingsButton.SetBounds(width - settingsButton.Width, top, settingsButton.Width, settingsButton.Height);
                refreshButton.SetBounds(settingsButton.Left - gap - refreshButton.Width, top, refreshButton.Width, refreshButton.Height);
                lastFetch.SetBounds(0, top + UIUtils.S(2), Math.Max(1, refreshButton.Left - gap), UIUtils.S(30));

                int cardTop = UIUtils.S(54);
                int colGap = UIUtils.S(14);
                int colW = Math.Max(1, (width - colGap * 2) / 3);
                int statH = UIUtils.S(110);
                market.SetBounds(0, cardTop, colW, statH);
                delta.SetBounds(colW + colGap, cardTop, colW, statH);
                purchase.SetBounds((colW + colGap) * 2, cardTop, Math.Max(1, width - (colW + colGap) * 2), statH);
            };

            YouPinCcUi.AddTopCard(Container, card, 14);
        }

        private void CreateCcProfitCurveCard()
        {
            var card = new YouPinCcRoundedPanel
            {
                Height = UIUtils.S(262),
                Padding = UIUtils.S(new Padding(22, 18, 22, 18))
            };
            var title = YouPinCcUi.Label("收益曲线", 10.5F, FontStyle.Bold);
            _profitCurveChart = new YouPinTrendCurveControl(new List<YouPinDailyPnl>(), TrendCurveColor, TrendRiseColor, TrendFallColor, TrendTextColor, TrendSubTextColor, TrendFontSize)
            {
                BackColor = Color.Transparent
            };
            card.Controls.Add(title);
            card.Controls.Add(_profitCurveChart);
            card.Layout += (_, __) =>
            {
                int pad = UIUtils.S(22);
                title.SetBounds(pad, UIUtils.S(14), Math.Max(1, card.Width - pad * 2), UIUtils.S(30));
                _profitCurveChart.SetBounds(pad, UIUtils.S(48), Math.Max(1, card.Width - pad * 2), Math.Max(1, card.Height - UIUtils.S(66)));
            };

            YouPinCcUi.AddTopCard(Container, card, 14);
        }

        private static Panel CreateCcStatCard(string title, out Label valueLabel, out Label subLabel)
        {
            var card = new YouPinCcRoundedPanel
            {
                DrawBorder = false,
                FillOverride = UIColors.ControlBg,
                Padding = UIUtils.S(new Padding(20, 14, 20, 12))
            };
            var titleLabel = YouPinCcUi.Label(title, 9F, FontStyle.Regular, UIColors.TextSub);
            Label value = YouPinCcUi.Label("—", 18F, FontStyle.Bold, UIColors.TextMain);
            Label sub = YouPinCcUi.Label("", 9F, FontStyle.Regular, UIColors.TextSub);
            valueLabel = value;
            subLabel = sub;

            card.Controls.Add(titleLabel);
            card.Controls.Add(value);
            card.Controls.Add(sub);
            card.Layout += (_, __) =>
            {
                int pad = UIUtils.S(20);
                int width = Math.Max(1, card.Width - pad * 2);
                titleLabel.SetBounds(pad, UIUtils.S(12), width, UIUtils.S(24));
                value.SetBounds(pad, UIUtils.S(44), width, UIUtils.S(36));
                sub.SetBounds(pad, UIUtils.S(78), width, UIUtils.S(24));
            };

            return card;
        }

        private void CreateListCard()
        {
            var card = YouPinInventoryTrendListCardFactory.Create(
                ScheduleFilterRefresh,
                _gridScrollCoordinator.ScrollToCustomBarValue,
                _gridScrollCoordinator.UpdateScrollBar);
            _emptyLabel = card.EmptyLabel;
            _searchInput = card.SearchInput;
            _filterCombo = card.FilterCombo;
            _gridHost = card.GridHost;
            _gridScrollBar = card.GridScrollBar;
            AddGroupToPage(card.Group);
        }

        private void ScheduleFilterRefresh()
        {
            if (_disposed || IsDisposed)
                return;

            _filterDebounceTimer ??= new System.Windows.Forms.Timer { Interval = 220 };
            _filterDebounceTimer.Tick -= OnFilterDebounceTick;
            _filterDebounceTimer.Tick += OnFilterDebounceTick;
            _filterDebounceTimer.Stop();
            _filterDebounceTimer.Start();
        }

        private void OnFilterDebounceTick(object? sender, EventArgs e)
        {
            _filterDebounceTimer?.Stop();
            if (!_disposed && !IsDisposed)
                _refreshCoordinator.QueueGridRefresh(force: true, delayMs: 1);
        }

        private DataGridView CreateTrendGrid()
        {
            return _gridView.CreateGrid(
                () => _gridRows,
                Grid_ColumnHeaderMouseClick,
                _gridScrollCoordinator.HandleMouseWheel,
                _gridScrollCoordinator.UpdateScrollBar);
        }

        private void RefreshData(bool force = false)
        {
            if (_refreshCoordinator.IsRefreshing && !force)
                return;

            var state = _inventoryService.GetTrendState();
            UpdateHeader(state);
            UpdateSummary(state);
            UpdateProfitCurve(state);
            _refreshCoordinator.QueueGridRefresh(force);
        }

        private void QueueGridWarmup(int delayMs = 45)
        {
            if (_disposed || IsDisposed)
                return;

            _gridWarmupTimer ??= CreateGridWarmupTimer();
            _gridWarmupTimer.Stop();
            _gridWarmupTimer.Interval = Math.Max(1, delayMs);
            _gridWarmupTimer.Start();
        }

        private void QueueGridWarmupIfNeeded(int delayMs = 45)
        {
            if (_inventoryService.GetTrendState().Rows.Count > 0)
                QueueGridWarmup(delayMs);
        }

        private System.Windows.Forms.Timer CreateGridWarmupTimer()
        {
            var timer = new System.Windows.Forms.Timer { Interval = 45 };
            timer.Tick += (_, __) =>
            {
                timer.Stop();
                if (_disposed || IsDisposed || !Visible)
                    return;

                using (UiJankProfiler.Measure("YouPinInventoryTrend.GridWarmup", thresholdMs: 1))
                {
                    EnsureGridCreated();
                }
            };
            return timer;
        }

        private void UpdateHeader(YouPinInventoryTrendState state)
        {
            var authState = _authService.GetState(Config);
            var header = YouPinInventoryTrendPageModel.BuildHeader(state, authState);
            SetText(_lastFetchLabel, _showAuthControls ? header.LastFetchText : FormatCcLastFetchText(header.LastFetchText));
            if (_authLabel != null)
            {
                _authLabel.Text = header.AuthText;
                _authLabel.ForeColor = header.AuthStatus switch
                {
                    YouPinInventoryTrendAuthStatus.SignedIn => Color.FromArgb(0, 150, 80),
                    YouPinInventoryTrendAuthStatus.Error => UIColors.TextWarn,
                    _ => UIColors.TextSub
                };
            }
        }

        private void UpdateSummary(YouPinInventoryTrendState state)
        {
            var summary = YouPinInventoryTrendPageModel.BuildSummary(state);
            SetValue(_marketValueLabel, summary.MarketValueText, TrendTextColor);
            SetText(_marketSubLabel, summary.MarketSubText);
            SetValue(_deltaValueLabel, summary.DeltaValueText, GetTrendColor(state.TotalDelta, summary.HasDeltaComparison));
            SetText(_deltaSubLabel, summary.DeltaSubText);
            SetValue(_purchaseValueLabel, _showAuthControls ? summary.PurchaseValueText : FormatCcPurchaseValueText(summary.PurchaseValueText), TrendTextColor);
            SetText(_purchaseSubLabel, summary.PurchaseSubText);
        }

        private void UpdateProfitCurve(YouPinInventoryTrendState state)
        {
            if (_profitCurveChart == null)
                return;

            var historyState = _inventoryService.GetState();
            var points = YouPinInventoryTrendCurveModel.BuildCurvePoints(historyState?.DailyPoints, state);
            _profitCurveChart.UpdateData(points, TrendCurveColor, TrendRiseColor, TrendFallColor, TrendTextColor, TrendSubTextColor, TrendFontSize);
        }

        private void PopulateGrid(YouPinInventoryTrendState state, bool force = false)
        {
            if (_emptyLabel == null)
                return;

            using (UiJankProfiler.Measure("YouPinInventoryTrend.PopulateGrid", $"SourceRows={state.Rows.Count}", thresholdMs: 1))
            {
                CancelGridRowBind();
                int version = Interlocked.Increment(ref _gridPrepareVersion);
                var sourceRows = state.Rows.Count == 0
                    ? new List<YouPinInventoryTrendRow>()
                    : state.Rows.ToList();

                if (sourceRows.Count == 0)
                {
                    bool hasDisplayedRows =
                        _gridRows.Count > 0
                        || (_grid != null && _grid.RowCount > 0)
                        || !string.IsNullOrEmpty(_lastGridSignature);
                    if (!hasDisplayedRows)
                    {
                        _gridRows = new List<TrendGridRow>();
                        if (_gridHost != null)
                            _gridHost.Visible = false;
                        _emptyLabel.Visible = true;
                        _emptyLabel.Text = "暂无库存快照。请先完成悠悠有品库存读取，或点击立即刷新。";
                        _lastGridSignature = "";
                        return;
                    }

                    bool alreadyEmpty =
                        _gridRows.Count == 0
                        && (_grid == null || _grid.RowCount == 0)
                        && (_gridHost == null || !_gridHost.Visible)
                        && _emptyLabel.Visible
                        && string.IsNullOrEmpty(_lastGridSignature);
                    if (alreadyEmpty)
                    {
                        _emptyLabel.Text = "暂无库存快照。请先完成悠悠有品库存读取，或点击立即刷新。";
                        return;
                    }

                    _gridRows = new List<TrendGridRow>();
                    if (_grid != null)
                    {
                        _grid.SuspendLayout();
                        try
                        {
                            _grid.RowCount = 0;
                            _grid.ClearSelection();
                            _grid.CurrentCell = null;
                            UpdateGridSortGlyphs();
                            _gridScrollCoordinator.QueueUpdate();
                            _grid.Invalidate();
                        }
                        finally
                        {
                            _grid.ResumeLayout();
                        }
                    }

                    if (_gridHost != null)
                        _gridHost.Visible = false;
                    _emptyLabel.Visible = true;
                    _emptyLabel.Text = "暂无库存快照。请先完成悠悠有品库存读取，或点击立即刷新。";
                    _lastGridSignature = "";
                    RequestRelayoutGroups();
                    return;
                }

                if (!EnsureGridCreated() || _grid == null)
                    return;

                if (_gridRows.Count == 0)
                    _gridRows = new List<TrendGridRow>(Math.Min(sourceRows.Count, 16));

                QueueGridPrepare(sourceRows, version, force);
            }
        }

        private void QueueGridPrepare(List<YouPinInventoryTrendRow> sourceRows, int version, bool force)
        {
            string keyword = _searchInput?.Inner.Text.Trim() ?? string.Empty;
            string filter = _filterCombo?.SelectedValue ?? "all";
            string sortColumn = _gridSortColumn;
            bool sortDescending = _gridSortDescending;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var prepared = PrepareGridRows(sourceRows, keyword, filter, sortColumn, sortDescending);
                    if (_disposed || IsDisposed || version != _gridPrepareVersion || !IsHandleCreated)
                        return;

                    try
                    {
                        BeginInvoke(new Action(() => ApplyPreparedTrendGridRows(prepared, version, force)));
                    }
                    catch
                    {
                        // The page may have been closed while background preparation completed.
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Info("YouPinInventoryTrend", "Grid prepare failed: " + ex.Message);
                }
            });
        }

        private void ApplyPreparedTrendGridRows(PreparedTrendGridRows prepared, int version, bool force)
        {
            if (_disposed || IsDisposed || !Visible || version != _gridPrepareVersion)
                return;

            if (!force && string.Equals(prepared.Signature, _lastGridSignature, StringComparison.Ordinal))
                return;

            if (prepared.Rows.Count == 0)
            {
                CancelGridRowBind();
                _gridRows = new List<TrendGridRow>();
                if (_grid != null)
                {
                    _grid.RowCount = 0;
                    _grid.ClearSelection();
                    _grid.CurrentCell = null;
                    UpdateGridSortGlyphs();
                    _gridScrollCoordinator.QueueUpdate();
                    _grid.Invalidate();
                }

                if (_gridHost != null)
                    _gridHost.Visible = false;
                if (_emptyLabel != null)
                {
                    _emptyLabel.Visible = true;
                    _emptyLabel.Text = "没有符合当前搜索或筛选条件的饰品。";
                }

                _lastGridSignature = prepared.Signature;
                RequestRelayoutGroups();
                return;
            }

            QueueGridRowBind(prepared.Rows, prepared.Signature);
        }

        private void QueueGridRowBind(List<TrendGridRow> rows, string signature)
        {
            _pendingGridPreparedRows = rows;
            _pendingGridRowIndex = 0;
            _pendingGridSignature = signature;
            _gridRows = new List<TrendGridRow>(Math.Min(rows.Count, 16));
            if (_grid != null)
            {
                _grid.RowCount = 0;
                _grid.Visible = true;
            }
            if (_gridHost != null && !_gridHost.Visible)
                _gridHost.Visible = true;
            if (_emptyLabel != null)
                _emptyLabel.Visible = false;
            RequestRelayoutGroups();

            _gridBindTimer ??= CreateGridBindTimer();
            _gridBindTimer.Stop();
            _gridBindTimer.Interval = 1;
            _gridBindTimer.Start();
        }

        private System.Windows.Forms.Timer CreateGridBindTimer()
        {
            var timer = new System.Windows.Forms.Timer { Interval = 1 };
            timer.Tick += (_, __) => BindNextGridRowBatch(timer);
            return timer;
        }

        private void BindNextGridRowBatch(System.Windows.Forms.Timer timer)
        {
            timer.Stop();
            if (_disposed || IsDisposed || !Visible || _grid == null || _pendingGridPreparedRows == null)
                return;

            using (UiJankProfiler.Measure("YouPinInventoryTrend.GridBindBatch", $"Start={_pendingGridRowIndex}; Total={_pendingGridPreparedRows.Count}", thresholdMs: 1))
            {
                int batchSize = Math.Min(12, Math.Max(4, _pendingGridPreparedRows.Count / 3));
                int end = Math.Min(_pendingGridPreparedRows.Count, _pendingGridRowIndex + batchSize);
                for (int i = _pendingGridRowIndex; i < end; i++)
                    _gridRows.Add(_pendingGridPreparedRows[i]);

                _pendingGridRowIndex = end;

                _grid.SuspendLayout();
                try
                {
                    _grid.RowCount = _gridRows.Count;
                    _grid.Invalidate();
                    _gridScrollCoordinator.QueueUpdate();
                }
                finally
                {
                    _grid.ResumeLayout();
                }

                if (_pendingGridRowIndex >= _pendingGridPreparedRows.Count)
                {
                    _lastGridSignature = _pendingGridSignature;
                    _pendingGridPreparedRows = null;
                    _pendingGridRowIndex = 0;
                    _pendingGridSignature = "";
                    return;
                }
            }

            timer.Interval = 1;
            timer.Start();
        }

        private void CancelGridRowBind()
        {
            _gridBindTimer?.Stop();
            _pendingGridPreparedRows = null;
            _pendingGridRowIndex = 0;
            _pendingGridSignature = "";
        }

        private bool EnsureGridCreated()
        {
            if (_grid != null)
                return true;
            if (_gridHost == null)
                return false;

            _grid = CreateTrendGrid();
            _grid.Dock = DockStyle.Fill;
            _gridHost.SuspendLayout();
            try
            {
                _gridHost.Controls.Add(_grid);
                _grid.BringToFront();
                _gridScrollBar?.BringToFront();
            }
            finally
            {
                _gridHost.ResumeLayout(false);
            }

            ApplyGridTheme();
            UpdateGridSortGlyphs();
            return true;
        }

        private void Grid_ColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
        {
            if (_grid == null || e.ColumnIndex < 0 || e.ColumnIndex >= _grid.Columns.Count)
                return;

            string columnName = _grid.Columns[e.ColumnIndex].Name;
            if (!YouPinTrendGridViewAdapter.IsSortableColumn(columnName))
                return;

            if (string.Equals(_gridSortColumn, columnName, StringComparison.OrdinalIgnoreCase))
                _gridSortDescending = !_gridSortDescending;
            else
            {
                _gridSortColumn = columnName;
                _gridSortDescending = true;
            }

            PopulateGrid(_inventoryService.GetTrendState(), force: true);
        }

        private static PreparedTrendGridRows PrepareGridRows(
            List<YouPinInventoryTrendRow> sourceRows,
            string keyword,
            string filter,
            string sortColumn,
            bool sortDescending)
        {
            using (UiJankProfiler.Measure("YouPinInventoryTrend.GridPrepare", $"SourceRows={sourceRows.Count}", thresholdMs: 1))
            {
                return YouPinInventoryTrendGridModel.Prepare(sourceRows, keyword, filter, sortColumn, sortDescending);
            }
        }

        private void UpdateGridSortGlyphs()
        {
            _gridView.UpdateSortGlyphs(_grid, _gridSortColumn, _gridSortDescending);
        }

        private void OpenYouPinAuthDialog()
        {
            using var dialog = new YouPinAuthDialog(() => Config, () =>
            {
                SyncAuthSettingsFromConfig();
                RefreshData(force: true);
            });
            dialog.ShowDialog(FindForm());
            SyncAuthSettingsFromConfig();
            RefreshData(force: true);
        }

        private void OpenRefreshSettingsDialog()
        {
            int currentInterval = Get(nameof(Settings.YouPinInventoryRefreshSec), Config?.YouPinInventoryRefreshSec ?? 1800);
            using var dialog = new RefreshIntervalDialog(currentInterval, value =>
            {
                Set(nameof(Settings.YouPinInventoryRefreshSec), value);
                ConfigureInventoryService();
            });
            dialog.ShowDialog(FindForm());
        }

        private void SyncAuthSettingsFromConfig()
        {
            if (Config is null)
                return;

            Set(nameof(Settings.YouPinInventoryToken), Config.YouPinInventoryToken ?? string.Empty);
            Set(nameof(Settings.YouPinInventoryDeviceToken), Config.YouPinInventoryDeviceToken ?? string.Empty);
            ConfigureInventoryService();
        }

        private void ConfigureInventoryService()
        {
            if (Config is not null)
                _inventoryService.Configure(Config);
        }

        private void ApplyGridTheme()
        {
            _gridView.ApplyTheme(_gridHost, _gridScrollBar, _grid);
        }

        private Color GetTrendColor(double value, bool hasComparison)
        {
            if (!hasComparison || Math.Abs(value) < 0.001)
                return TrendSubTextColor;

            return value > 0 ? TrendRiseColor : TrendFallColor;
        }

        private static void SetText(Label? label, string text)
        {
            if (label != null)
                label.Text = text;
        }

        private static string FormatCcLastFetchText(string text)
        {
            return string.IsNullOrWhiteSpace(text)
                ? "上次刷新 暂无"
                : text.Replace("上次刷新时间：", "上次刷新 ", StringComparison.Ordinal);
        }

        private static string FormatCcPurchaseValueText(string text)
        {
            return string.Equals(text, "暂无购入价", StringComparison.Ordinal) ? "—" : text;
        }

        private static void SetValue(Label? label, string text, Color color)
        {
            if (label == null)
                return;

            label.Text = text;
            label.ForeColor = color;
        }

        private static Color ParseConfiguredColor(string? hex, Color fallback)
        {
            try
            {
                return string.IsNullOrWhiteSpace(hex) ? fallback : ColorTranslator.FromHtml(hex);
            }
            catch
            {
                return fallback;
            }
        }

        private static Color ParseThemeAwareConfiguredColor(string? hex, string defaultHex, Color themeColor)
        {
            if (string.IsNullOrWhiteSpace(hex) || string.Equals(hex, defaultHex, StringComparison.OrdinalIgnoreCase))
                return themeColor;

            return ParseConfiguredColor(hex, themeColor);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _refreshCoordinator.Dispose();
                if (_gridWarmupTimer != null)
                {
                    _gridWarmupTimer.Stop();
                    _gridWarmupTimer.Dispose();
                    _gridWarmupTimer = null;
                }
                if (_gridBindTimer != null)
                {
                    _gridBindTimer.Stop();
                    _gridBindTimer.Dispose();
                    _gridBindTimer = null;
                }
                _pendingGridPreparedRows = null;
                if (_filterDebounceTimer != null)
                {
                    _filterDebounceTimer.Stop();
                    _filterDebounceTimer.Tick -= OnFilterDebounceTick;
                    _filterDebounceTimer.Dispose();
                    _filterDebounceTimer = null;
                }
                _gridView.Dispose();
                _inventoryService.DataUpdated -= _dataUpdatedHandler;
            }

            base.Dispose(disposing);
        }

    }
}
