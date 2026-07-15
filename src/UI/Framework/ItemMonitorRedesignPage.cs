using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Market;
using CS2TradeMonitor.Domain.Market;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.SettingsPage;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    public sealed class ItemMonitorHostPage : SettingsPageBase
    {
        private readonly PageHost _pageHost;
        private readonly SettingsTransaction _settingsTransaction;
        private readonly ItemMonitorRedesignPage _page;
        private readonly ISteamDtItemService _steamDtItemService;
        private AutoSaveCoordinator? _autoSaveCoordinator;
        private bool _hosted;

        public ItemMonitorHostPage()
            : this(ItemMonitorPageRuntimeServices.Resolve())
        {
        }

        internal ItemMonitorHostPage(ItemMonitorPageRuntimeServices runtimeServices)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);

            _pageHost = new PageHost();
            _settingsTransaction = new SettingsTransaction(() => Config);
            _page = new ItemMonitorRedesignPage(runtimeServices);
            _steamDtItemService = runtimeServices.SteamDtItems;
            Controls.Add(_pageHost);
        }

        public override void OnShow()
        {
            base.OnShow();
            if (Config is null)
                return;

            _autoSaveCoordinator?.Dispose();
            _autoSaveCoordinator = null;

            _settingsTransaction.Rebase();
            _steamDtItemService.Configure(Config.SteamDtApiKey);

            if (!_hosted)
            {
                _pageHost.AttachSettings(_settingsTransaction.Draft);
                _pageHost.ShowPage(_page);
                _hosted = true;
            }
            else
            {
                _page.Activate();
            }

            _autoSaveCoordinator = new AutoSaveCoordinator(_settingsTransaction.Draft);
        }

        public override void OnThemeChanged()
        {
            base.OnThemeChanged();
            _page.ApplySystemTheme();
        }

        public override void RequestViewportRelayout()
        {
            base.RequestViewportRelayout();
            _pageHost.Bounds = ClientRectangle;
            _pageHost.PerformLayout();
            _pageHost.RequestCurrentPageRelayout();
        }

        public override void Save()
        {
            if (!_hosted || Config is null)
                return;

            _pageHost.SaveCurrentPage();
            _autoSaveCoordinator?.Flush();
            _settingsTransaction.Commit();
        }

        public override void OnHide()
        {
            base.OnHide();
            if (!_hosted || Config is null)
                return;

            _pageHost.SaveCurrentPage();
            _page.Deactivate();
            _autoSaveCoordinator?.Flush();
            _settingsTransaction.Commit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _autoSaveCoordinator?.Dispose();
                _autoSaveCoordinator = null;
            }

            base.Dispose(disposing);
        }

    }

    public sealed class ItemMonitorRedesignPage : UserControl, IUiPage
    {
        private readonly ISteamDtItemService _steamDtItemService;
        private readonly BufferedPanel _container;
        private readonly RedesignCardPanel _searchCard;
        private readonly RedesignCardPanel _listCard;
        private readonly LiteUnderlineInput _keywordInput;
        private readonly LiteButton _addButton;
        private readonly LiteButton _clearButton;
        private readonly Label _statusLabel;
        private readonly ListBox _candidateList;
        private readonly Label _listTitle;
        private readonly Label _itemRowsSummary;
        private readonly LiteButton _toggleItemRowsButton;
        private readonly Panel _listHeader;
        private readonly Panel _itemRowsHost;
        private readonly Label _emptyIcon;
        private readonly Label _emptyText;
        private readonly Label _emptyHint;
        private readonly HashSet<string> _expandedItemKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Panel> _itemRowCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _itemRowSignatureByKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<CandidateListItem> _candidateItems = new();
        private readonly System.Windows.Forms.Timer _searchDebounceTimer;
        private SettingsStore? _settingsStore;
        private CancellationTokenSource? _pageCts;
        private long _searchRequestVersion;
        private bool _disposed;
        private bool _refreshingPrice;
        private bool _showAllItemRows;

        public ItemMonitorRedesignPage()
            : this(ItemMonitorPageRuntimeServices.Resolve())
        {
        }

        internal ItemMonitorRedesignPage(ItemMonitorPageRuntimeServices runtimeServices)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);

            _steamDtItemService = runtimeServices.SteamDtItems;
            BackColor = UIColors.MainBg;
            Dock = DockStyle.Fill;
            Padding = Padding.Empty;

            _container = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = UIColors.MainBg,
                Padding = FrameworkSettingsPageLayoutHelper.CreateDefaultPagePadding()
            };
            _container.HandleCreated += (_, __) => FrameworkSettingsPageLayoutHelper.HideHorizontalScroll(_container);
            Controls.Add(_container);

            _searchCard = CreateShellCard(ItemMonitorRedesignPageModel.SearchCardHeight);
            _listCard = CreateShellCard(ItemMonitorRedesignPageModel.ListCardHeight);
            _keywordInput = new LiteUnderlineInput("", "", "", 420) { Placeholder = "输入饰品名（支持中文）" };
            _addButton = new LiteButton("添加", true) { Width = 96, Height = 46, Enabled = true };
            _statusLabel = CreateLabel("请先选择候选", UIFonts.Regular(9f), UIColors.Positive, ContentAlignment.MiddleLeft);
            _clearButton = new LiteButton("清空", false) { Width = UIUtils.S(96), Height = UIUtils.S(46) };
            _candidateList = CreateCandidateList();

            _listTitle = CreateLabel("已监控单品", UIFonts.Bold(12f), UIColors.TextMain, ContentAlignment.MiddleLeft);
            _itemRowsSummary = CreateLabel("", UIFonts.Regular(8.8f), UIColors.TextSub, ContentAlignment.MiddleLeft);
            _toggleItemRowsButton = new LiteButton("显示更多", false) { Width = UIUtils.S(104), Height = UIUtils.S(32), Visible = false };
            _listHeader = CreateListHeader();
            _itemRowsHost = new Panel { BackColor = Color.Transparent };
            _emptyIcon = CreateLabel("▭", UIFonts.Bold(28f), UIColors.TextSub, ContentAlignment.MiddleCenter);
            _emptyIcon.AutoEllipsis = false;
            _emptyText = CreateLabel("暂无监控单品", UIFonts.Bold(12f), UIColors.TextMain, ContentAlignment.MiddleLeft);
            _emptyHint = CreateLabel("在上方输入饰品名，选择候选后点击“添加”。", UIFonts.Regular(8.8f), UIColors.TextSub, ContentAlignment.MiddleLeft);
            _searchDebounceTimer = new System.Windows.Forms.Timer { Interval = 350 };
            _searchDebounceTimer.Tick += OnSearchDebounceTick;
            _toggleItemRowsButton.Click += (_, __) =>
            {
                _showAllItemRows = !_showAllItemRows;
                RefreshItemRows();
            };
            _itemRowsHost.Resize += (_, __) => ResizeItemRows();

            BuildSearchCard();
            BuildListCard();

            _container.Controls.Add(_listCard);
            _container.Controls.Add(_searchCard);
            _container.Resize += (_, __) => LayoutCards();
            LayoutCards();
        }

        public void Initialize(SettingsStore settingsStore)
        {
            _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
            EnsureConfig();
            RefreshFromStore();
        }

        public void Activate()
        {
            BeginPageWork();
            EnsureConfig();
            RefreshFromStore();
        }

        public void Deactivate()
        {
            CancelPageWork();
            _searchDebounceTimer.Stop();
            if (!_disposed && !IsDisposed)
                SetBusy(false);
        }

        public void Save()
        {
            if (_settingsStore is null)
                return;

            CommitItemConfigs();
            SetDefaultItemRefreshIntervalSec(GetDefaultItemRefreshIntervalSec());
        }

        public void ApplySystemTheme()
        {
            BackColor = UIColors.MainBg;
            _container.BackColor = UIColors.MainBg;
            RefreshTheme(this);
            Invalidate(true);
        }

        private CancellationToken PageToken => _pageCts?.Token ?? CancellationToken.None;

        private void BeginPageWork()
        {
            CancelPageWork();
            _pageCts = new CancellationTokenSource();
        }

        private void CancelPageWork()
        {
            CancellationTokenSource? cts = Interlocked.Exchange(ref _pageCts, null);
            if (cts is null)
                return;

            try
            {
                cts.Cancel();
            }
            catch
            {
                // Ignore cancellation races while switching settings pages.
            }
            finally
            {
                cts.Dispose();
            }
        }

        private void EnsureConfig()
        {
            if (_settingsStore is null)
                return;

            _ = ItemConfigs;
            int normalized = ItemMonitorListCardModel.NormalizeDefaultInterval(
                _settingsStore.Get(nameof(Settings.DefaultItemRefreshIntervalSec), 600));
            SetDefaultItemRefreshIntervalSec(normalized);
            CommitItemConfigs(ItemMonitorPageModel.NormalizeItemIndexes(ItemConfigs, normalized));
        }

        private List<ItemMonitorConfig> ItemConfigs
        {
            get
            {
                if (_settingsStore is null)
                    return new List<ItemMonitorConfig>();

                var items = _settingsStore.Get<List<ItemMonitorConfig>?>(nameof(Settings.ItemConfigs), null);
                if (items is not null)
                    return items;

                items = new List<ItemMonitorConfig>();
                _settingsStore.Set(nameof(Settings.ItemConfigs), items);
                return items;
            }
        }

        private string GetSteamDtApiKey() => _settingsStore?.Get(nameof(Settings.SteamDtApiKey), string.Empty) ?? string.Empty;

        private int GetDefaultItemRefreshIntervalSec()
        {
            int value = _settingsStore?.Get(nameof(Settings.DefaultItemRefreshIntervalSec), 600) ?? 600;
            return ItemMonitorListCardModel.NormalizeDefaultInterval(value);
        }

        private void SetDefaultItemRefreshIntervalSec(int value)
        {
            _settingsStore?.Set(nameof(Settings.DefaultItemRefreshIntervalSec), ItemMonitorListCardModel.NormalizeDefaultInterval(value));
        }

        private void CommitItemConfigs(List<ItemMonitorConfig>? items = null, bool forceChanged = false, bool flushDraft = false)
        {
            _settingsStore?.Set(nameof(Settings.ItemConfigs), items ?? ItemConfigs, forceChanged);
            if (flushDraft)
                _settingsStore?.Save();
        }

        private void BuildSearchCard()
        {
            _searchCard.Controls.Add(CreateLabel("搜索添加单品", UIFonts.Bold(12f), UIColors.TextMain, ContentAlignment.MiddleLeft));
            _searchCard.Controls.Add(CreateLabel(
                "选择候选后立即入列表，价格后台读取。",
                UIFonts.Regular(9f),
                UIColors.TextSub,
                ContentAlignment.MiddleLeft));

            var keywordLabel = CreateLabel("关键字", UIFonts.Bold(9.5f), UIColors.TextMain, ContentAlignment.MiddleLeft);
            _searchCard.Controls.Add(keywordLabel);
            _searchCard.Controls.Add(_keywordInput);
            _searchCard.Controls.Add(_addButton);
            _searchCard.Controls.Add(_statusLabel);
            _searchCard.Controls.Add(_clearButton);
            _searchCard.Controls.Add(_candidateList);

            _keywordInput.Inner.TextChanged += (_, __) => ScheduleCandidateSearch();
            _keywordInput.Inner.KeyDown += (_, e) =>
            {
                CandidateListKeyboardHelper.HandleKeyDown(
                    _candidateList,
                    e,
                    () => _ = AddSelectedCandidateAsync(),
                    () => ClearCandidateDropdown(clearText: false));
            };
            _addButton.Click += async (_, __) => await AddSelectedCandidateAsync();
            _clearButton.Click += (_, __) =>
            {
                ClearCandidateDropdown(clearText: true);
                SetStatus("请先选择候选", warn: false);
            };
            _candidateList.SelectedIndexChanged += (_, __) => UpdateAddButtonState();
            _candidateList.DoubleClick += async (_, __) => await AddSelectedCandidateAsync();

            _searchCard.Layout += (_, __) => LayoutSearchCard();
        }

        private void BuildListCard()
        {
            _listCard.Controls.Add(_listTitle);
            _listCard.Controls.Add(_itemRowsSummary);
            var refresh = new LiteButton("⟳ 刷新价格", false) { Width = 134, Height = 40 };
            refresh.Click += async (_, __) => await RefreshAllPricesAsync(refresh);
            _listCard.Controls.Add(refresh);

            _listCard.Controls.Add(_listHeader);
            _listCard.Controls.Add(_itemRowsHost);
            _listCard.Controls.Add(_toggleItemRowsButton);
            _listCard.Controls.Add(_emptyIcon);
            _listCard.Controls.Add(_emptyText);
            _listCard.Controls.Add(_emptyHint);

            _listCard.Layout += (_, __) =>
            {
                int width = Math.Max(1, _listCard.ClientSize.Width);
                _listTitle.SetBounds(22, 18, 220, 34);
                refresh.SetBounds(width - 22 - 134, 18, 134, 40);
                _itemRowsSummary.SetBounds(_listTitle.Right + 12, 21, Math.Max(1, refresh.Left - _listTitle.Right - 28), 28);
                _listHeader.SetBounds(22, 70, width - 44, 34);
                LayoutListHeader(_listHeader);
                int rowsTop = 112;
                int rowsHeight = _itemRowsHost.Visible ? Math.Max(UIUtils.S(58), _itemRowsHost.Height) : UIUtils.S(90);
                _itemRowsHost.SetBounds(22, rowsTop, width - 44, rowsHeight);
                _toggleItemRowsButton.SetBounds(width - 22 - _toggleItemRowsButton.Width, rowsTop + rowsHeight + UIUtils.S(8), _toggleItemRowsButton.Width, _toggleItemRowsButton.Height);
                _emptyIcon.SetBounds((width - 280) / 2, rowsTop + UIUtils.S(16), 64, 58);
                _emptyText.SetBounds(_emptyIcon.Right + 8, _emptyIcon.Top + 2, 260, 30);
                _emptyHint.SetBounds(_emptyIcon.Right + 8, _emptyText.Bottom + 2, 320, 24);
                int desiredHeight = rowsTop + rowsHeight + (_toggleItemRowsButton.Visible ? UIUtils.S(52) : UIUtils.S(20));
                if (_listCard.Height != desiredHeight)
                    _listCard.Height = desiredHeight;
            };
        }

        private Panel CreateListHeader()
        {
            var panel = new RedesignCardPanel(UIColors.InputBg, radius: 0)
            {
                Padding = Padding.Empty
            };
            panel.Controls.Add(CreateLabel("单品 / 价格", UIFonts.Regular(8.2f), UIColors.TextSub, ContentAlignment.MiddleLeft));
            panel.Controls.Add(CreateLabel("上次刷新", UIFonts.Regular(8.2f), UIColors.TextSub, ContentAlignment.MiddleLeft));
            panel.Controls.Add(CreateLabel("配置摘要", UIFonts.Regular(8.2f), UIColors.TextSub, ContentAlignment.MiddleLeft));
            panel.Controls.Add(CreateLabel("操作", UIFonts.Regular(8.2f), UIColors.TextSub, ContentAlignment.MiddleCenter));
            panel.Layout += (_, __) => LayoutListHeader(panel);
            return panel;
        }

        private static void LayoutListHeader(Control panel)
        {
            int width = Math.Max(1, panel.ClientSize.Width);
            int actionWidth = 220;
            int timeWidth = 120;
            int summaryWidth = 260;
            int priceWidth = Math.Max(280, width - actionWidth - timeWidth - summaryWidth - 36);
            int x = 20;
            panel.Controls[0].SetBounds(x, 0, priceWidth, panel.Height);
            x += priceWidth;
            panel.Controls[1].SetBounds(x, 0, timeWidth, panel.Height);
            x += timeWidth;
            panel.Controls[2].SetBounds(x, 0, summaryWidth, panel.Height);
            panel.Controls[3].SetBounds(width - actionWidth, 0, actionWidth - UIUtils.S(20), panel.Height);
        }

        private void LayoutCards()
        {
            Rectangle bounds = FrameworkSettingsPageLayoutHelper.CalculateDefaultContentBounds(
                _container,
                FrameworkSettingsPageLayoutHelper.ItemMonitorContentMinimumWidth);
            int width = bounds.Width;
            _searchCard.Width = width;
            _searchCard.Location = new Point(bounds.Left, bounds.Top);
            _listCard.Width = width;
            _listCard.Location = new Point(bounds.Left, _searchCard.Bottom + 16);
            _listCard.PerformLayout();
            FrameworkSettingsPageLayoutHelper.HideHorizontalScroll(_container);
        }

        private void LayoutSearchCard()
        {
            int width = Math.Max(1, _searchCard.ClientSize.Width);
            _searchCard.Controls[0].SetBounds(24, 18, 260, 34);
            _searchCard.Controls[1].SetBounds(24, 56, width - 48, 26);

            int rowY = 90;
            int gap = 14;
            int right = width - 24;
            _clearButton.SetBounds(right - 96, rowY, 96, 42);
            _statusLabel.SetBounds(_clearButton.Left - 166, rowY + 6, 152, 30);
            _addButton.SetBounds(_statusLabel.Left - gap - 96, rowY, 96, 42);
            _keywordInput.SetBounds(102, rowY, Math.Max(280, _addButton.Left - gap - 102), 42);
            _searchCard.Controls.Cast<Control>().First(control => control.Text == "关键字").SetBounds(24, rowY + 9, 68, 28);

            _candidateList.SetBounds(_keywordInput.Left, _keywordInput.Bottom + 4, _keywordInput.Width, 112);
            _searchCard.Height = _candidateList.Visible ? ItemMonitorRedesignPageModel.SearchCardExpandedHeight : ItemMonitorRedesignPageModel.SearchCardHeight;
            LayoutCards();
        }

        private void RefreshFromStore()
        {
            RefreshItemRows();
        }

        private void RefreshItemRows()
        {
            List<ItemMonitorConfig> items = ItemMonitorPageModel.OrderItemsForDisplay(ItemConfigs);
            _listTitle.Text = "已监控单品";
            bool empty = items.Count == 0;
            _emptyIcon.Visible = empty;
            _emptyText.Visible = empty;
            _emptyHint.Visible = empty;
            _itemRowsHost.Visible = !empty;
            _listHeader.Visible = !empty;
            _toggleItemRowsButton.Visible = !empty && items.Count > ItemMonitorRedesignPageModel.InitialVisibleItemRows;
            _toggleItemRowsButton.Text = _showAllItemRows ? "收起" : "显示更多";

            int visibleLimit = ItemMonitorRedesignPageModel.GetVisibleItemRowCount(items.Count, _showAllItemRows);
            _itemRowsSummary.Text = empty
                ? "暂无监控项"
                : _showAllItemRows || items.Count <= visibleLimit
                    ? $"共 {items.Count} 个监控项"
                    : $"已显示 {visibleLimit} / {items.Count} 个，点击“显示更多”查看全部";

            var visibleItems = items.Take(visibleLimit).ToList();
            var visibleKeys = visibleItems
                .Select(GetItemExpansionKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _itemRowsHost.SuspendLayout();
            try
            {
                PruneItemRowCache(visibleKeys);

                int y = 0;
                var desiredRows = new List<Panel>(visibleItems.Count);
                foreach (ItemMonitorConfig item in visibleItems)
                {
                    string key = GetItemExpansionKey(item);
                    bool expanded = _expandedItemKeys.Contains(key);
                    string signature = BuildItemRowSignature(item, expanded);
                    if (!_itemRowCache.TryGetValue(key, out Panel? row)
                        || !_itemRowSignatureByKey.TryGetValue(key, out string? previousSignature)
                        || !string.Equals(previousSignature, signature, StringComparison.Ordinal))
                    {
                        if (row != null)
                        {
                            if (ReferenceEquals(row.Parent, _itemRowsHost))
                                _itemRowsHost.Controls.Remove(row);
                            row.Dispose();
                        }

                        row = CreateItemRow(item);
                        _itemRowCache[key] = row;
                        _itemRowSignatureByKey[key] = signature;
                    }

                    row.SetBounds(0, y, _itemRowsHost.Width, row.Height);
                    desiredRows.Add(row);
                    y += row.Height + 8;
                }

                for (int i = 0; i < desiredRows.Count; i++)
                {
                    Panel row = desiredRows[i];
                    if (!ReferenceEquals(row.Parent, _itemRowsHost))
                        _itemRowsHost.Controls.Add(row);
                    _itemRowsHost.Controls.SetChildIndex(row, i);
                }

                _itemRowsHost.Height = empty ? 0 : Math.Max(58, y - 8);
            }
            finally
            {
                _itemRowsHost.ResumeLayout(performLayout: true);
            }

            ResizeItemRows();
            _listCard.PerformLayout();
            LayoutCards();
        }

        private void ResizeItemRows()
        {
            foreach (Control control in _itemRowsHost.Controls)
                control.Width = Math.Max(1, _itemRowsHost.ClientSize.Width);
        }

        private Panel CreateItemRow(ItemMonitorConfig item)
        {
            bool expanded = _expandedItemKeys.Contains(GetItemExpansionKey(item));
            var row = new RedesignCardPanel(UIColors.InputBg, radius: 4)
            {
                Padding = Padding.Empty,
                Height = expanded ? ItemMonitorRedesignPageModel.ExpandedItemRowHeight : ItemMonitorRedesignPageModel.CollapsedItemRowHeight
            };
            var title = CreateLabel(item.Name, UIFonts.Bold(9.5f), UIColors.TextMain, ContentAlignment.MiddleLeft);
            var status = CreateLabel(ItemMonitorPageModel.BuildCompactPriceText(item), UIFonts.Regular(8.5f), ItemMonitorPageModel.GetItemStatusColor(item), ContentAlignment.MiddleLeft);
            var refreshTime = CreatePillLabel(ItemMonitorPageModel.BuildLastRefreshShortText(item), ResolveRefreshBadgeKind(item));
            var summary = CreateLabel(ItemMonitorPageModel.BuildCompactConfigSummary(item), UIFonts.Regular(8.2f), UIColors.TextMain, ContentAlignment.MiddleLeft);
            var detail = CreateLabel(ItemMonitorPageModel.BuildCompactConfigDetail(item), UIFonts.Regular(8.2f), UIColors.TextSub, ContentAlignment.MiddleLeft);
            var configure = new LiteButton(expanded ? "配置 ▲" : "配置 ▼", false) { Width = 108, Height = 32 };
            var delete = new LiteButton("删除", false) { Width = 72, Height = 32 };
            configure.Click += (_, __) => ToggleItemConfig(item);
            delete.Click += (_, __) => DeleteItem(item);
            row.Controls.Add(title);
            row.Controls.Add(status);
            row.Controls.Add(refreshTime);
            row.Controls.Add(summary);
            row.Controls.Add(detail);
            row.Controls.Add(configure);
            row.Controls.Add(delete);
            if (expanded)
                AddItemConfigStrip(row, item);
            row.Layout += (_, __) =>
            {
                int width = Math.Max(1, row.ClientSize.Width);
                int actionWidth = 220;
                int timeWidth = 120;
                int summaryWidth = 260;
                int priceWidth = Math.Max(280, width - actionWidth - timeWidth - summaryWidth - 36);
                int x = 20;
                title.SetBounds(x, 7, priceWidth - 12, 24);
                status.SetBounds(x, 31, priceWidth - 12, 20);
                x += priceWidth;
                refreshTime.SetBounds(x, 20, UIUtils.S(58), 24);
                x += timeWidth;
                summary.SetBounds(x, 9, summaryWidth - 12, 21);
                detail.SetBounds(x, 31, summaryWidth - 12, 19);
                int configureWidth = 108;
                int deleteWidth = 72;
                delete.SetBounds(row.Width - 16 - deleteWidth, 13, deleteWidth, 32);
                configure.SetBounds(delete.Left - 10 - configureWidth, 13, configureWidth, 32);
                foreach (Control child in row.Controls)
                {
                    if (child.Tag as string == "ConfigStrip")
                    {
                        child.SetBounds(20, 76, width - 40, 72);
                        child.PerformLayout();
                    }
                }
            };
            return row;
        }

        private void PruneItemRowCache(ISet<string> visibleKeys)
        {
            var staleKeys = _itemRowCache.Keys
                .Where(key => !visibleKeys.Contains(key))
                .ToList();
            foreach (string key in staleKeys)
            {
                Panel row = _itemRowCache[key];
                if (ReferenceEquals(row.Parent, _itemRowsHost))
                    _itemRowsHost.Controls.Remove(row);
                row.Dispose();
                _itemRowCache.Remove(key);
                _itemRowSignatureByKey.Remove(key);
            }
        }

        private static string BuildItemRowSignature(ItemMonitorConfig item, bool expanded)
        {
            ItemPriceAlertTriggerMode mode = ItemMonitorPageModel.ResolveTriggerMode(item);
            return string.Join(
                "\u001f",
                expanded,
                item.ItemId,
                item.ItemKey,
                item.Name,
                item.ShortName,
                item.Enabled,
                item.RefreshIntervalSec,
                item.DisplayFieldFlags,
                item.VisibleInPanel,
                item.VisibleInTaskbar,
                item.SortIndex,
                item.TaskbarSortIndex,
                item.LastPrice.ToString("R", CultureInfo.InvariantCulture),
                item.LastChange.ToString("R", CultureInfo.InvariantCulture),
                item.LastChangeRatio.ToString("R", CultureInfo.InvariantCulture),
                item.LastUpdateTime,
                item.LastStatus,
                item.MarketHashName,
                item.PlatformItemId,
                item.HasChangeData,
                item.PriceAlertEnabled,
                mode,
                item.PriceAlertAbove.ToString("R", CultureInfo.InvariantCulture),
                item.PriceAlertBelow.ToString("R", CultureInfo.InvariantCulture),
                item.PriceAlertRisePercent.ToString("R", CultureInfo.InvariantCulture),
                item.PriceAlertFallPercent.ToString("R", CultureInfo.InvariantCulture),
                item.PriceAlertWindowMinutes,
                item.PriceAlertCooldownMinutes,
                item.PriceAlertBaselinePrice.ToString("R", CultureInfo.InvariantCulture),
                item.PriceAlertBaselineTime,
                item.PriceAlertLastTriggerTime,
                item.PriceAlertLastMessage);
        }

        private void AddItemConfigStrip(Control row, ItemMonitorConfig item)
        {
            var strip = new RedesignCardPanel(UIColors.ControlBg, radius: 4)
            {
                Padding = Padding.Empty,
                Tag = "ConfigStrip"
            };
            var reminderLabel = CreateLabel("提醒开关", UIFonts.Regular(8.2f), UIColors.TextSub, ContentAlignment.MiddleLeft);
            var reminderCheck = CreateCompactCheck(item.PriceAlertEnabled);
            var desktopLabel = CreateLabel("桌面", UIFonts.Regular(8.2f), UIColors.TextSub, ContentAlignment.MiddleLeft);
            var desktopCheck = CreateCompactCheck(item.VisibleInPanel);
            var taskbarLabel = CreateLabel("任务栏", UIFonts.Regular(8.2f), UIColors.TextSub, ContentAlignment.MiddleLeft);
            var taskbarCheck = CreateCompactCheck(item.VisibleInTaskbar);
            var interval = CreateCompactNumberInput(item.RefreshIntervalSec.ToString(CultureInfo.InvariantCulture), "秒", 5);
            ItemPriceAlertTriggerMode mode = ItemMonitorPageModel.ResolveTriggerMode(item);
            var percentMode = new LiteButton("百分比模式", false) { Width = 116, Height = 24, IsActive = mode == ItemPriceAlertTriggerMode.Percent };
            var breakthroughMode = new LiteButton("突破模式", false) { Width = 88, Height = 24, IsActive = mode == ItemPriceAlertTriggerMode.Breakthrough };
            var restore = new LiteButton("恢复初始值", false) { Width = 116, Height = 26 };
            var save = new LiteButton("保存", true) { Width = 72, Height = 26 };
            percentMode.Font = UIFonts.Regular(8f);
            breakthroughMode.Font = UIFonts.Regular(8f);
            restore.Font = UIFonts.Regular(8f);
            save.Font = UIFonts.Regular(8f);

            var firstTitle = CreateLabel(mode == ItemPriceAlertTriggerMode.Percent ? "上涨" : "高于", UIFonts.Regular(8.2f), UIColors.TextSub, ContentAlignment.MiddleLeft);
            var firstInput = CreateCompactNumberInput(
                FormatNumber(mode == ItemPriceAlertTriggerMode.Percent ? item.PriceAlertRisePercent : item.PriceAlertAbove),
                mode == ItemPriceAlertTriggerMode.Percent ? "%" : "¥",
                7);
            var secondTitle = CreateLabel(mode == ItemPriceAlertTriggerMode.Percent ? "下跌" : "低于", UIFonts.Regular(8.2f), UIColors.TextSub, ContentAlignment.MiddleLeft);
            var secondInput = CreateCompactNumberInput(
                FormatNumber(mode == ItemPriceAlertTriggerMode.Percent ? item.PriceAlertFallPercent : item.PriceAlertBelow),
                mode == ItemPriceAlertTriggerMode.Percent ? "%" : "¥",
                7);
            var unitTitle = CreateLabel("单位时间", UIFonts.Regular(8.2f), UIColors.TextSub, ContentAlignment.MiddleLeft);
            var unitTime = CreateCompactNumberInput(item.PriceAlertWindowMinutes.ToString(CultureInfo.InvariantCulture), "分", 5);
            var modeHint = CreateLabel(
                mode == ItemPriceAlertTriggerMode.Percent
                    ? "百分比模式：在单位时间内涨跌达到设定值时提醒"
                    : "突破模式：价格高于或低于设定价时提醒",
                UIFonts.Regular(8f),
                UIColors.TextSub,
                ContentAlignment.MiddleLeft);

            percentMode.Click += (_, __) =>
            {
                item.PriceAlertTriggerMode = ItemPriceAlertTriggerMode.Percent;
                CommitItemConfigs(forceChanged: true, flushDraft: true);
                RefreshItemRows();
            };
            breakthroughMode.Click += (_, __) =>
            {
                item.PriceAlertTriggerMode = ItemPriceAlertTriggerMode.Breakthrough;
                CommitItemConfigs(forceChanged: true, flushDraft: true);
                RefreshItemRows();
            };
            restore.Click += (_, __) => RestoreInitialItemConfig(item);
            save.Click += (_, __) =>
            {
                SaveItemConfig(
                    item,
                    reminderCheck.Checked,
                    desktopCheck.Checked,
                    taskbarCheck.Checked,
                    interval,
                    firstInput,
                    secondInput,
                    unitTime,
                    mode);
            };

            strip.Controls.AddRange(new Control[]
            {
                reminderLabel,
                reminderCheck,
                desktopLabel,
                desktopCheck,
                taskbarLabel,
                taskbarCheck,
                CreateLabel("刷新间隔", UIFonts.Regular(8.2f), UIColors.TextSub, ContentAlignment.MiddleLeft),
                interval,
                CreateLabel("模式", UIFonts.Regular(8.2f), UIColors.TextSub, ContentAlignment.MiddleLeft),
                percentMode,
                breakthroughMode,
                restore,
                save,
                firstTitle,
                firstInput,
                secondTitle,
                secondInput,
                unitTitle,
                unitTime,
                modeHint
            });

            strip.Layout += (_, __) =>
            {
                int w = Math.Max(1, strip.ClientSize.Width);
                int y1 = 8;
                int y2 = 40;
                bool narrow = w < 930;
                save.SetBounds(w - 88, y1 - 1, 72, 26);
                restore.SetBounds(save.Left - 124, y1 - 1, 116, 26);

                int x1 = 10;
                reminderLabel.SetBounds(x1, y1 + 1, 78, 22);
                reminderCheck.SetBounds(reminderLabel.Right + 2, y1, ItemMonitorRedesignPageModel.ConfigStripCheckWidth, 22);
                x1 = reminderCheck.Right + 6;
                desktopLabel.SetBounds(x1, y1 + 1, 42, 22);
                desktopCheck.SetBounds(desktopLabel.Right + 2, y1, ItemMonitorRedesignPageModel.ConfigStripCheckWidth, 22);
                x1 = desktopCheck.Right + 6;
                taskbarLabel.SetBounds(x1, y1 + 1, 54, 22);
                taskbarCheck.SetBounds(taskbarLabel.Right + 2, y1, ItemMonitorRedesignPageModel.ConfigStripCheckWidth, 22);
                x1 = taskbarCheck.Right + 8;
                strip.Controls[6].SetBounds(x1, y1 + 1, 68, 22);
                interval.SetBounds(strip.Controls[6].Right + 2, y1 - 1, 50, 26);
                if (!narrow)
                {
                    x1 = interval.Right + 8;
                    strip.Controls[8].SetBounds(x1, y1 + 1, 44, 22);
                    percentMode.SetBounds(strip.Controls[8].Right + 2, y1, 116, 24);
                    breakthroughMode.SetBounds(percentMode.Right + 4, y1, 88, 24);

                    firstTitle.SetBounds(18, y2 + 1, ItemMonitorRedesignPageModel.ConfigStripValueLabelWidth, 22);
                    firstInput.SetBounds(firstTitle.Right + 4, y2 - 1, 58, 26);
                }
                else
                {
                    int x2 = 10;
                    strip.Controls[8].SetBounds(x2, y2 + 1, 44, 22);
                    percentMode.SetBounds(strip.Controls[8].Right + 2, y2, 116, 24);
                    breakthroughMode.SetBounds(percentMode.Right + 4, y2, 88, 24);
                    firstTitle.SetBounds(breakthroughMode.Right + 12, y2 + 1, ItemMonitorRedesignPageModel.ConfigStripValueLabelWidth, 22);
                    firstInput.SetBounds(firstTitle.Right + 4, y2 - 1, 58, 26);
                }

                secondTitle.SetBounds(firstInput.Right + 12, y2 + 1, ItemMonitorRedesignPageModel.ConfigStripValueLabelWidth, 22);
                secondInput.SetBounds(secondTitle.Right + 4, y2 - 1, 58, 26);
                unitTitle.SetBounds(secondInput.Right + 12, y2 + 1, 68, 22);
                unitTime.SetBounds(unitTitle.Right + 4, y2 - 1, 52, 26);
                modeHint.SetBounds(unitTime.Right + 14, y2 + 1, Math.Max(140, w - unitTime.Right - 34), 22);
            };
            row.Controls.Add(strip);
        }

        private void ToggleItemConfig(ItemMonitorConfig item)
        {
            string key = GetItemExpansionKey(item);
            if (!_expandedItemKeys.Add(key))
                _expandedItemKeys.Remove(key);
            RefreshItemRows();
        }

        private void RestoreInitialItemConfig(ItemMonitorConfig item)
        {
            int defaultInterval = GetDefaultItemRefreshIntervalSec();
            double defaultRise = GetDouble(nameof(Settings.DefaultItemPriceAlertRisePercent), 0);
            double defaultFall = GetDouble(nameof(Settings.DefaultItemPriceAlertFallPercent), 0);
            int defaultWindow = ItemMonitorListCardModel.NormalizeDefaultWindowMinutes((int)Math.Round(GetDouble(nameof(Settings.DefaultItemPriceAlertWindowMinutes), 10)));

            item.PriceAlertEnabled = defaultRise > 0 || defaultFall > 0;
            item.VisibleInPanel = _settingsStore?.Get(nameof(Settings.ItemMonitorDefaultVisibleInPanel), false) ?? false;
            item.VisibleInTaskbar = _settingsStore?.Get(nameof(Settings.ItemMonitorDefaultVisibleInTaskbar), false) ?? false;
            item.RefreshIntervalSec = defaultInterval;
            item.PriceAlertTriggerMode = ItemPriceAlertTriggerMode.Percent;
            item.PriceAlertAbove = 0;
            item.PriceAlertBelow = 0;
            item.PriceAlertRisePercent = ItemMonitorListCardModel.NormalizeDefaultPercent(defaultRise);
            item.PriceAlertFallPercent = ItemMonitorListCardModel.NormalizeDefaultPercent(defaultFall);
            item.PriceAlertWindowMinutes = defaultWindow;
            item.PriceAlertCooldownMinutes = ItemMonitorListCardModel.NormalizeDefaultCooldownMinutes(item.PriceAlertCooldownMinutes <= 0 ? 10 : item.PriceAlertCooldownMinutes);
            CommitItemConfigs(forceChanged: true, flushDraft: true);
            RefreshItemRows();
            SetStatus("已恢复初始值：" + item.Name, warn: false);
        }

        private void SaveItemConfig(
            ItemMonitorConfig item,
            bool alertEnabled,
            bool visibleInPanel,
            bool visibleInTaskbar,
            LiteNumberInput interval,
            LiteNumberInput firstInput,
            LiteNumberInput secondInput,
            LiteNumberInput unitTime,
            ItemPriceAlertTriggerMode mode)
        {
            item.PriceAlertEnabled = alertEnabled;
            item.VisibleInPanel = visibleInPanel;
            item.VisibleInTaskbar = visibleInTaskbar;
            item.RefreshIntervalSec = ItemMonitorPageModel.NormalizeItemRefreshInterval(interval.ValueInt, GetDefaultItemRefreshIntervalSec());
            item.PriceAlertTriggerMode = mode;
            item.PriceAlertWindowMinutes = ItemMonitorListCardModel.NormalizeDefaultWindowMinutes(unitTime.ValueInt);
            item.PriceAlertCooldownMinutes = ItemMonitorListCardModel.NormalizeDefaultCooldownMinutes(item.PriceAlertCooldownMinutes <= 0 ? 10 : item.PriceAlertCooldownMinutes);

            if (mode == ItemPriceAlertTriggerMode.Breakthrough)
            {
                item.PriceAlertAbove = Math.Max(0, firstInput.ValueDouble);
                item.PriceAlertBelow = Math.Max(0, secondInput.ValueDouble);
            }
            else
            {
                item.PriceAlertRisePercent = ItemMonitorListCardModel.NormalizeDefaultPercent(firstInput.ValueDouble);
                item.PriceAlertFallPercent = ItemMonitorListCardModel.NormalizeDefaultPercent(secondInput.ValueDouble);
            }

            CommitItemConfigs(forceChanged: true, flushDraft: true);
            RefreshItemRows();
            SetStatus("已保存配置：" + item.Name, warn: false);
        }

        private static LiteNumberInput CreateCompactNumberInput(string text, string unit, int maxLength)
        {
            var input = new LiteNumberInput(text, unit, "", 58, null, maxLength)
            {
                Width = UIUtils.S(58),
                Height = UIUtils.S(26)
            };
            input.Inner.Font = UIFonts.Regular(8.5f);
            return input;
        }

        private static LiteCheck CreateCompactCheck(bool value)
        {
            return new LiteCheck(value, "")
            {
                Width = UIUtils.S(ItemMonitorRedesignPageModel.ConfigStripCheckWidth),
                Height = UIUtils.S(22)
            };
        }

        private static CompactPillLabel CreatePillLabel(string text, string kind)
        {
            return new CompactPillLabel(text, kind)
            {
                Width = UIUtils.S(58),
                Height = UIUtils.S(24)
            };
        }

        private static string ResolveRefreshBadgeKind(ItemMonitorConfig item)
        {
            if (item.LastUpdateTime > 0)
                return "fresh";
            if (item.LastPrice > 0)
                return "cache";
            return "empty";
        }

        private static string GetItemExpansionKey(ItemMonitorConfig item)
        {
            if (!string.IsNullOrWhiteSpace(item.ItemKey))
                return item.ItemKey;
            if (!string.IsNullOrWhiteSpace(item.ItemId))
                return item.ItemId;
            return item.Name ?? "";
        }

        private void ScheduleCandidateSearch()
        {
            if (_disposed || IsDisposed)
                return;

            if (string.IsNullOrWhiteSpace(_keywordInput.Inner.Text))
            {
                Interlocked.Increment(ref _searchRequestVersion);
                _searchDebounceTimer.Stop();
                ClearCandidateDropdown(clearText: false);
                SetStatus("请先选择候选", warn: false);
                return;
            }

            _candidateList.Visible = true;
            SetStatus("正在准备搜索候选...", warn: false);
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
            LayoutSearchCard();
        }

        private async void OnSearchDebounceTick(object? sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();
            await SearchCandidatesAsync();
        }

        private async Task SearchCandidatesAsync()
        {
            CancellationToken cancellationToken = PageToken;
            if (cancellationToken.IsCancellationRequested || _disposed || IsDisposed)
                return;

            string keyword = _keywordInput.Inner.Text.Trim();
            if (string.IsNullOrWhiteSpace(keyword))
                return;

            long requestVersion = Interlocked.Increment(ref _searchRequestVersion);
            SetBusy(true);
            SetStatus("正在搜索...", warn: false);
            try
            {
                List<SteamDtSearchCandidate> results = await _steamDtItemService.SearchItemsAsync(keyword);
                if (cancellationToken.IsCancellationRequested || _disposed || IsDisposed)
                    return;
                if (requestVersion != Interlocked.Read(ref _searchRequestVersion)
                    || !string.Equals(keyword, _keywordInput.Inner.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                RenderCandidates(results, keyword);
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested || _disposed || IsDisposed)
                    return;

                _candidateItems.Clear();
                _candidateList.Items.Clear();
                _candidateList.Visible = true;
                SetStatus("搜索失败：" + ex.Message, warn: true);
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested && !_disposed && !IsDisposed)
                {
                    SetBusy(false);
                    UpdateAddButtonState();
                    LayoutSearchCard();
                }
            }
        }

        private void RenderCandidates(IEnumerable<SteamDtSearchCandidate> results, string keyword)
        {
            List<SteamDtSearchCandidate> allResults = results.ToList();
            _candidateItems.Clear();
            _candidateItems.AddRange(allResults
                .Take(ItemMonitorSearchPanelController.CandidateDisplayLimit)
                .Select(candidate => new CandidateListItem(candidate, ItemMonitorPageModel.GetCandidateDisplay(candidate))));

            _candidateList.BeginUpdate();
            try
            {
                _candidateList.Items.Clear();
                foreach (CandidateListItem item in _candidateItems)
                    _candidateList.Items.Add(item);
                _candidateList.SelectedIndex = -1;
                _candidateList.Visible = !string.IsNullOrWhiteSpace(keyword);
            }
            finally
            {
                _candidateList.EndUpdate();
            }

            bool hasKey = !string.IsNullOrWhiteSpace(GetSteamDtApiKey());
            ItemMonitorSearchStatusViewModel status = ItemMonitorSearchPanelController.BuildSearchResultStatus(
                _candidateItems.Count,
                allResults.Count,
                hasKey,
                _steamDtItemService.IsLocalItemDatabaseAvailable);
            SetStatus(status.Text, status.Warn);
        }

        private Task AddSelectedCandidateAsync()
        {
            if (_candidateList.SelectedItem is not CandidateListItem selected)
            {
                SetStatus("请先从下拉候选中选择单品。", warn: true);
                UpdateAddButtonState();
                return Task.CompletedTask;
            }

            if (ItemMonitorPageModel.IsDuplicate(ItemConfigs, selected.Candidate))
            {
                SetStatus($"单品 “{selected.Candidate.Name}” 已在监控列表中。", warn: true);
                UpdateAddButtonState();
                return Task.CompletedTask;
            }

            ItemMonitorConfig item = ItemMonitorPageModel.CreateCandidateConfig(
                selected.Candidate,
                GetDefaultItemRefreshIntervalSec(),
                ItemMonitorPageModel.NextSortIndex(ItemConfigs, taskbar: false),
                ItemMonitorPageModel.NextSortIndex(ItemConfigs, taskbar: true),
                _settingsStore?.Get(nameof(Settings.ItemMonitorDefaultVisibleInPanel), false) ?? false,
                _settingsStore?.Get(nameof(Settings.ItemMonitorDefaultVisibleInTaskbar), false) ?? false,
                GetDouble(nameof(Settings.DefaultItemPriceAlertRisePercent), 0),
                GetDouble(nameof(Settings.DefaultItemPriceAlertFallPercent), 0),
                (int)Math.Round(GetDouble(nameof(Settings.DefaultItemPriceAlertWindowMinutes), 10)));

            ItemConfigs.Add(item);
            CommitItemConfigs(
                ItemMonitorPageModel.NormalizeItemIndexes(ItemConfigs, GetDefaultItemRefreshIntervalSec()),
                forceChanged: true,
                flushDraft: true);
            ClearCandidateDropdown(clearText: true);
            RefreshFromStore();
            SetStatus("已添加，后台读取中", warn: false);
            _ = FetchItemPriceInBackgroundAsync(item, added: true);
            return Task.CompletedTask;
        }

        private async Task FetchItemPriceInBackgroundAsync(ItemMonitorConfig item, bool added)
        {
            try
            {
                bool ok = await _steamDtItemService.FetchItemPriceAsync(item, persistSettings: false);
                if (PageToken.IsCancellationRequested || _disposed || IsDisposed)
                    return;

                if (!ItemConfigs.Contains(item))
                    return;

                CommitItemConfigs(forceChanged: true, flushDraft: true);
                RefreshItemRows();
                SetStatus(
                    ok
                        ? (added ? "已添加并更新价格：" : "已刷新：") + item.Name
                        : (added ? "已添加，价格稍后自动重试" : "刷新失败，稍后自动重试"),
                    warn: !ok);
            }
            catch (Exception ex)
            {
                if (PageToken.IsCancellationRequested || _disposed || IsDisposed)
                    return;

                item.LastStatus = "失败 (" + ex.Message + ")";
                CommitItemConfigs(forceChanged: true, flushDraft: true);
                RefreshItemRows();
                SetStatus(added ? "已添加，价格稍后自动重试" : "刷新失败，稍后自动重试", warn: true);
            }
        }

        private async Task RefreshAllPricesAsync(Control button)
        {
            if (_refreshingPrice)
                return;

            List<ItemMonitorConfig> items = ItemMonitorPageModel.OrderItemsForDisplay(ItemConfigs);
            if (items.Count == 0)
            {
                SetStatus("暂无监控单品可刷新。", warn: true);
                return;
            }

            _refreshingPrice = true;
            button.Enabled = false;
            try
            {
                foreach (ItemMonitorConfig item in items)
                {
                    if (PageToken.IsCancellationRequested || _disposed || IsDisposed)
                        return;
                    await _steamDtItemService.FetchItemPriceAsync(item, persistSettings: false);
                }
                CommitItemConfigs(forceChanged: true, flushDraft: true);
                RefreshFromStore();
                SetStatus("已刷新监控列表。", warn: false);
            }
            finally
            {
                _refreshingPrice = false;
                if (!button.IsDisposed)
                    button.Enabled = true;
            }
        }

        private async Task RefreshItemPriceAsync(ItemMonitorConfig item, Control button)
        {
            if (_refreshingPrice)
                return;

            _refreshingPrice = true;
            button.Enabled = false;
            try
            {
                bool ok = await _steamDtItemService.FetchItemPriceAsync(item, persistSettings: false);
                CommitItemConfigs(forceChanged: true, flushDraft: true);
                RefreshFromStore();
                SetStatus(ok ? "已刷新：" + item.Name : "刷新失败：" + item.LastStatus, warn: !ok);
            }
            finally
            {
                _refreshingPrice = false;
                if (!button.IsDisposed)
                    button.Enabled = true;
            }
        }

        private void DeleteItem(ItemMonitorConfig item)
        {
            if (!ItemConfigs.Remove(item))
                return;

            _expandedItemKeys.Remove(GetItemExpansionKey(item));
            CommitItemConfigs(
                ItemMonitorPageModel.NormalizeItemIndexes(ItemConfigs, GetDefaultItemRefreshIntervalSec()),
                forceChanged: true,
                flushDraft: true);
            RefreshFromStore();
            SetStatus("已删除：" + item.Name, warn: false);
        }

        private void UpdateAddButtonState()
        {
            if (_candidateList.SelectedItem is CandidateListItem item && ItemMonitorPageModel.IsDuplicate(ItemConfigs, item.Candidate))
            {
                _addButton.Text = "已添加";
                _addButton.Enabled = false;
                _statusLabel.Text = "已在监控";
                return;
            }

            bool canAdd = _candidateList.SelectedItem is not null;
            _addButton.Text = "添加";
            _addButton.Enabled = true;
            if (!canAdd && _candidateItems.Count > 0)
                SetStatus("请先选择候选", warn: false);
        }

        private void ClearCandidateDropdown(bool clearText)
        {
            Interlocked.Increment(ref _searchRequestVersion);
            _candidateItems.Clear();
            _candidateList.Items.Clear();
            _candidateList.Visible = false;
            if (clearText)
                _keywordInput.Inner.Text = "";
            UpdateAddButtonState();
            LayoutSearchCard();
        }

        private void SetBusy(bool busy)
        {
            _addButton.Enabled = !busy;
            _addButton.Text = busy ? "搜索中" : "添加";
        }

        private void SetStatus(string text, bool warn)
        {
            _statusLabel.Text = text;
            _statusLabel.ForeColor = warn ? UIColors.TextWarn : UIColors.Positive;
        }

        private double GetDouble(string key, double fallback)
        {
            object? value = _settingsStore?.Get<object?>(key, null);
            return value switch
            {
                int intValue => intValue,
                double doubleValue => doubleValue,
                float floatValue => floatValue,
                decimal decimalValue => (double)decimalValue,
                _ => fallback
            };
        }

        private void SetNumber(string key, double value)
        {
            switch (key)
            {
                case nameof(Settings.DefaultItemRefreshIntervalSec):
                    _settingsStore?.Set(key, ItemMonitorListCardModel.NormalizeDefaultInterval((int)Math.Round(value)));
                    break;
                case nameof(Settings.DefaultItemPriceAlertRisePercent):
                case nameof(Settings.DefaultItemPriceAlertFallPercent):
                    _settingsStore?.Set(key, ItemMonitorListCardModel.NormalizeDefaultPercent(value));
                    break;
                case nameof(Settings.DefaultItemPriceAlertWindowMinutes):
                    _settingsStore?.Set(key, ItemMonitorListCardModel.NormalizeDefaultWindowMinutes((int)Math.Round(value)));
                    break;
                case nameof(Settings.DefaultItemPriceAlertCooldownMinutes):
                    _settingsStore?.Set(key, ItemMonitorListCardModel.NormalizeDefaultCooldownMinutes((int)Math.Round(value)));
                    break;
            }
        }

        private static RedesignCardPanel CreateShellCard(int height)
        {
            return new RedesignCardPanel(UIColors.CardBg)
            {
                Height = height,
                Padding = Padding.Empty
            };
        }

        private static ListBox CreateCandidateList()
        {
            var list = new ListBox
            {
                IntegralHeight = false,
                BorderStyle = BorderStyle.FixedSingle,
                Font = UIFonts.Regular(9f),
                BackColor = UIColors.CardBg,
                ForeColor = UIColors.TextMain,
                Visible = false
            };
            UIColors.ConfigureThemedListBox(list);
            return list;
        }

        private static Label CreateLabel(string text, Font font, Color color, ContentAlignment align)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                Font = font,
                ForeColor = color,
                BackColor = Color.Transparent,
                TextAlign = align
            };
        }

        private static Control CreateDivider()
        {
            var divider = new Panel
            {
                BackColor = UIColors.Border,
                Width = UIUtils.S(1),
                Height = UIUtils.S(44)
            };
            return divider;
        }

        private static string FormatNumber(double value)
        {
            return Math.Abs(value - Math.Round(value)) < 0.0001
                ? value.ToString("0", CultureInfo.InvariantCulture)
                : value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static void RefreshTheme(Control root)
        {
            foreach (Control child in root.Controls)
            {
                switch (child)
                {
                    case RedesignCardPanel card:
                        card.BorderColor = UIColors.Border;
                        break;
                    case LiteButton button:
                        button.RefreshTheme();
                        break;
                    case LiteUnderlineInput input:
                        input.RefreshTheme();
                        break;
                    case LiteCheck check:
                        check.Invalidate();
                        break;
                    case RedesignSwitch sw:
                        sw.Invalidate();
                        break;
                    case CompactPillLabel pill:
                        pill.Invalidate();
                        break;
                    case ListBox listBox:
                        UIColors.ConfigureThemedListBox(listBox);
                        break;
                }

                if (child.HasChildren)
                    RefreshTheme(child);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                CancelPageWork();
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Tick -= OnSearchDebounceTick;
                _searchDebounceTimer.Dispose();
                foreach (Panel row in _itemRowCache.Values.ToList())
                    row.Dispose();
                _itemRowCache.Clear();
                _itemRowSignatureByKey.Clear();
            }

            base.Dispose(disposing);
        }
    }

    internal static class ItemMonitorRedesignPageModel
    {
        public const int SearchCardHeight = 146;
        public const int SearchCardExpandedHeight = 262;
        public const int ListCardHeight = 645;
        public const int InitialVisibleItemRows = 12;
        public const int CollapsedItemRowHeight = 58;
        public const int ExpandedItemRowHeight = 156;
        public const int RuleStepperMinTileWidth = 148;
        public const int RuleStepperMaxTileWidth = 168;
        public const int RuleStepperMinGap = 8;
        public const int RuleStepperButtonWidth = 36;
        public const int RuleStepperHeight = 40;
        public const int ConfigStripValueLabelWidth = 54;
        public const int ConfigStripCheckWidth = 26;

        public static int GetVisibleItemRowCount(int totalCount, bool showAll)
        {
            totalCount = Math.Max(0, totalCount);
            return showAll ? totalCount : Math.Min(totalCount, InitialVisibleItemRows);
        }
    }

    internal sealed class CompactPillLabel : Control
    {
        private readonly string _kind;

        public CompactPillLabel(string text, string kind)
        {
            _kind = kind;
            Text = text;
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = UIFonts.Regular(8.2f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Color fill;
            Color border;
            Color text;
            switch (_kind)
            {
                case "fresh":
                    fill = Color.FromArgb(16, 37, 31);
                    border = Color.FromArgb(29, 104, 87);
                    text = Color.FromArgb(34, 211, 166);
                    break;
                case "cache":
                    fill = Color.FromArgb(23, 31, 42);
                    border = UIColors.Border;
                    text = UIColors.TextSub;
                    break;
                default:
                    fill = Color.FromArgb(17, 21, 27);
                    border = Color.FromArgb(38, 49, 61);
                    text = UIColors.TextDisabled;
                    break;
            }

            Rectangle rect = new(0, 0, Width - 1, Height - 1);
            using var path = CreateRoundPath(rect, Math.Max(1, rect.Height / 2));
            using var fillBrush = new SolidBrush(fill);
            using var borderPen = new Pen(border);
            using var textBrush = new SolidBrush(text);
            e.Graphics.FillPath(fillBrush, path);
            e.Graphics.DrawPath(borderPen, path);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                rect,
                text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private static System.Drawing.Drawing2D.GraphicsPath CreateRoundPath(Rectangle rect, int radius)
        {
            int r = Math.Max(1, radius * 2);
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(rect.Left, rect.Top, r, r, 180, 90);
            path.AddArc(rect.Right - r, rect.Top, r, r, 270, 90);
            path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - r, r, r, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
