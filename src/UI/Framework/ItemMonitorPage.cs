using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Market;
using CS2TradeMonitor.Domain.Market;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.SettingsPage;
using static CS2TradeMonitor.src.UI.Framework.ItemMonitorPageControls;

namespace CS2TradeMonitor.src.UI.Framework
{
    public sealed class LegacyItemMonitorHostPage : SettingsPageBase
    {
        private readonly PageHost _pageHost;
        private readonly SettingsTransaction _settingsTransaction;
        private readonly FrameworkItemMonitorPage _itemMonitorPage;
        private readonly ISteamDtItemService _steamDtItemService;
        private AutoSaveCoordinator? _autoSaveCoordinator;
        private bool _hosted;

        public LegacyItemMonitorHostPage()
            : this(ItemMonitorPageRuntimeServices.Resolve())
        {
        }

        internal LegacyItemMonitorHostPage(ItemMonitorPageRuntimeServices runtimeServices)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);

            _pageHost = new PageHost();
            _settingsTransaction = new SettingsTransaction(() => Config);
            _itemMonitorPage = new FrameworkItemMonitorPage(runtimeServices);
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
                _pageHost.ShowPage(_itemMonitorPage);
                _hosted = true;
            }
            else
            {
                _itemMonitorPage.Activate();
            }

            _autoSaveCoordinator = new AutoSaveCoordinator(_settingsTransaction.Draft);
        }

        public override void OnThemeChanged()
        {
            base.OnThemeChanged();
            _itemMonitorPage.ApplySystemTheme();
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
            _itemMonitorPage.Deactivate();
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

    public sealed class FrameworkItemMonitorPage : UserControl, IUiPage
    {
        private readonly BufferedPanel _container;
        private readonly List<LiteSettingsGroup> _groups = new List<LiteSettingsGroup>();
        private readonly ISteamDtItemService _steamDtItemService;
        private SettingsStore? _settingsStore;
        private ItemMonitorSearchPanelController? _searchPanel;
        private Panel? _itemListWrapper;
        private CancellationTokenSource? _pageCts;
        private System.Windows.Forms.Timer? _searchDebounceTimer;
        private long _searchRequestVersion;
        private bool _disposed;
        private bool _refreshingItemList;
        private string _renderedItemListSignature = string.Empty;

        public FrameworkItemMonitorPage()
            : this(ItemMonitorPageRuntimeServices.Resolve())
        {
        }

        internal FrameworkItemMonitorPage(ItemMonitorPageRuntimeServices runtimeServices)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);

            BackColor = UIColors.MainBg;
            Dock = DockStyle.Fill;
            Padding = new Padding(0);
            _steamDtItemService = runtimeServices.SteamDtItems;

            _container = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(18, 18, 18, 72),
                BackColor = UIColors.MainBg
            };
            Controls.Add(_container);

            CreateSearchCard();
            CreateItemListCard();
        }

        public void Initialize(SettingsStore settingsStore)
        {
            _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
            EnsureConfig();
            RefreshItemList();
        }

        public void Activate()
        {
            BeginPageWork();
            EnsureConfig();
            RefreshItemList();
        }

        public void Deactivate()
        {
            CancelPageWork();
            if (!_disposed && !IsDisposed)
                _searchPanel?.SetBusy(false);
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
            RefreshTheme(_container);
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
            int rawDefault = _settingsStore.Get(nameof(Settings.DefaultItemRefreshIntervalSec), 600);
            int defaultInterval = ItemMonitorListCardModel.NormalizeDefaultInterval(rawDefault);
            if (rawDefault != defaultInterval)
                SetDefaultItemRefreshIntervalSec(defaultInterval);

            NormalizeItemIndexes();
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

        private int GetDefaultItemRefreshIntervalSec()
        {
            int value = _settingsStore?.Get(nameof(Settings.DefaultItemRefreshIntervalSec), 600) ?? 600;
            return Math.Max(60, value <= 0 ? 600 : value);
        }

        private void SetDefaultItemRefreshIntervalSec(int value)
        {
            _settingsStore?.Set(nameof(Settings.DefaultItemRefreshIntervalSec), Math.Max(60, value));
        }

        private string GetSteamDtApiKey()
        {
            return _settingsStore?.Get(nameof(Settings.SteamDtApiKey), string.Empty) ?? string.Empty;
        }

        private void CommitItemConfigs(List<ItemMonitorConfig>? items = null)
        {
            if (_settingsStore is null)
                return;

            _settingsStore.Set(nameof(Settings.ItemConfigs), items ?? ItemConfigs);
        }

        private void CreateSearchCard()
        {
            ItemMonitorSearchCard searchCard = ItemMonitorSearchCardFactory.Create(
                ScheduleCandidateSearch,
                AddSelectedCandidateAsync,
                ShowSearchMoreMenu,
                () =>
                {
                    ClearCandidateDropdown(clearText: true);
                    _searchPanel?.SetStatus("", warn: false);
                },
                () =>
                {
                    ClearCandidateDropdown(clearText: false);
                    _searchPanel?.SetStatus("", warn: false);
                },
                UpdateAddButtonState);

            _searchPanel = new ItemMonitorSearchPanelController(searchCard);

            AddGroupToPage(searchCard.Group);
        }

        private void ShowSearchMoreMenu(Control anchor)
        {
            var menu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                BackColor = UIColors.CardBg,
                ForeColor = UIColors.TextMain
            };
            var refreshBaseInfo = new ToolStripMenuItem("刷新基础信息缓存");
            refreshBaseInfo.Click += async (_, __) => await RefreshBaseCacheAsync(anchor);
            menu.Items.Add(refreshBaseInfo);
            menu.Closed += (_, __) => menu.Dispose();
            menu.Show(anchor, new Point(0, anchor.Height + UIUtils.S(2)));
        }

        private void ScheduleCandidateSearch()
        {
            if (_searchPanel is null || _disposed || IsDisposed)
                return;

            string keyword = _searchPanel.Keyword;
            if (string.IsNullOrWhiteSpace(keyword))
            {
                Interlocked.Increment(ref _searchRequestVersion);
                StopSearchDebounce();
                ClearCandidateDropdown(clearText: false);
                _searchPanel.SetStatus("", warn: false);
                return;
            }

            _searchPanel.ShowCandidateDropdown();
            _searchPanel.SetStatus("正在准备搜索候选...", warn: false);

            _searchDebounceTimer ??= new System.Windows.Forms.Timer { Interval = 350 };
            _searchDebounceTimer.Tick -= OnSearchDebounceTick;
            _searchDebounceTimer.Tick += OnSearchDebounceTick;
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private async void OnSearchDebounceTick(object? sender, EventArgs e)
        {
            StopSearchDebounce();
            await SearchCandidatesAsync();
        }

        private void StopSearchDebounce()
        {
            if (_searchDebounceTimer is null)
                return;

            _searchDebounceTimer.Stop();
        }

        private async Task SearchCandidatesAsync()
        {
            if (_searchPanel is null)
                return;

            CancellationToken cancellationToken = PageToken;
            if (cancellationToken.IsCancellationRequested || _disposed || IsDisposed)
                return;

            string keyword = _searchPanel.Keyword;
            if (string.IsNullOrWhiteSpace(keyword))
            {
                Interlocked.Increment(ref _searchRequestVersion);
                ClearCandidateDropdown(clearText: false);
                _searchPanel.SetStatus("", warn: false);
                return;
            }

            long requestVersion = Interlocked.Increment(ref _searchRequestVersion);
            _searchPanel.ShowCandidateDropdown();
            _searchPanel.SetBusy(true);
            _searchPanel.SetStatus("正在搜索...", warn: false);
            try
            {
                _steamDtItemService.Configure(GetSteamDtApiKey());
                List<SteamDtSearchCandidate> results = await _steamDtItemService.SearchItemsAsync(keyword);
                if (cancellationToken.IsCancellationRequested || _disposed || IsDisposed)
                    return;
                if (requestVersion != Interlocked.Read(ref _searchRequestVersion)
                    || _searchPanel is null
                    || !string.Equals(keyword, _searchPanel.Keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                bool hasKey = !string.IsNullOrWhiteSpace(GetSteamDtApiKey());
                _searchPanel.RenderCandidates(
                    results,
                    keyword,
                    hasKey,
                    _steamDtItemService.IsLocalItemDatabaseAvailable);
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested || _disposed || IsDisposed)
                    return;

                _searchPanel?.ClearCandidateItems(keepDropdownVisible: !string.IsNullOrWhiteSpace(_searchPanel?.Keyword));
                _searchPanel?.SetStatus("搜索失败：" + ex.Message, warn: true);
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested && !_disposed && !IsDisposed)
                {
                    _searchPanel?.SetBusy(false);
                    UpdateAddButtonState();
                }
            }
        }

        private async Task AddSelectedCandidateAsync()
        {
            CancellationToken cancellationToken = PageToken;
            if (cancellationToken.IsCancellationRequested || _disposed || IsDisposed)
                return;

            if (_searchPanel?.SelectedCandidate is not CandidateListItem selected)
            {
                _searchPanel?.SetStatus("请先从下拉候选中选择单品。", warn: true);
                UpdateAddButtonState();
                return;
            }

            if (ItemMonitorPageModel.IsDuplicate(ItemConfigs, selected.Candidate))
            {
                _searchPanel.SetStatus($"单品 “{selected.Candidate.Name}” 已在监控列表中。", warn: true);
                UpdateAddButtonState();
                return;
            }

            ItemMonitorConfig? addedItem = AddCandidate(selected.Candidate);
            if (addedItem is null)
                return;

            _searchPanel.SetStatus("已添加，正在读取价格...", warn: false);
            bool ok = await _steamDtItemService.FetchItemPriceAsync(addedItem, persistSettings: false);
            if (cancellationToken.IsCancellationRequested || _disposed || IsDisposed)
                return;

            CommitItemConfigs();
            RefreshItemList();
            _searchPanel.SetStatus(ok ? "已添加并刷新价格：" + addedItem.Name : "已添加，价格读取失败：" + addedItem.LastStatus, warn: !ok);
        }

        private async Task RefreshBaseCacheAsync(Control button)
        {
            CancellationToken cancellationToken = PageToken;
            if (cancellationToken.IsCancellationRequested || _disposed || IsDisposed)
                return;

            button.Enabled = false;
            _searchPanel?.SetStatus("正在刷新基础信息缓存...", warn: false);
            try
            {
                _steamDtItemService.Configure(GetSteamDtApiKey());
                var result = await _steamDtItemService.ForceRefreshBaseCacheAsync();
                if (cancellationToken.IsCancellationRequested || _disposed || IsDisposed)
                    return;

                _searchPanel?.SetStatus(
                    result.Success
                        ? $"基础信息刷新成功，已缓存 {result.Count} 条数据，更新时间：{result.RefreshTime:HH:mm:ss}"
                        : "基础信息刷新失败：" + result.Message,
                    warn: !result.Success);
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested || _disposed || IsDisposed)
                    return;

                _searchPanel?.SetStatus("基础信息刷新异常：" + ex.Message, warn: true);
            }
            finally
            {
                if (!_disposed && !IsDisposed)
                    button.Enabled = true;
            }
        }

        private void UpdateAddButtonState()
        {
            _searchPanel?.UpdateAddButtonState(item => ItemMonitorPageModel.IsDuplicate(ItemConfigs, item.Candidate));
        }

        private void CreateItemListCard()
        {
            if (_refreshingItemList)
                return;

            _refreshingItemList = true;
            try
            {
                if (_itemListWrapper is not null)
                {
                    foreach (Control control in _itemListWrapper.Controls)
                    {
                        if (control is LiteSettingsGroup staleGroup)
                            _groups.Remove(staleGroup);
                    }

                    _container.Controls.Remove(_itemListWrapper);
                    _itemListWrapper.Dispose();
                    _itemListWrapper = null;
                }

                List<ItemMonitorConfig> items = ItemMonitorPageModel.OrderItemsForDisplay(ItemConfigs);
                LiteSettingsGroup group = ItemMonitorListCardFactory.Create(
                    _settingsStore,
                    items,
                    GetDefaultItemRefreshIntervalSec(),
                    ApplyDefaultRefreshInterval,
                    () => CommitItemConfigs(),
                    MoveItem,
                    RefreshItemPriceAsync,
                    DeleteItem);

                _itemListWrapper = AddGroupToPage(group);
            }
            finally
            {
                _refreshingItemList = false;
            }
        }

        private void ApplyDefaultRefreshInterval(int next)
        {
            if (_settingsStore is null)
                return;

            SetDefaultItemRefreshIntervalSec(next);
            foreach (ItemMonitorConfig item in ItemConfigs)
                item.RefreshIntervalSec = next;

            CommitItemConfigs();
        }

        private void RefreshItemList()
        {
            EnsureConfig();
            string signature = BuildItemListSignature();
            if (_itemListWrapper is not null
                && string.Equals(signature, _renderedItemListSignature, StringComparison.Ordinal))
            {
                return;
            }

            _renderedItemListSignature = signature;
            CreateItemListCard();
        }

        private string BuildItemListSignature()
        {
            return ItemMonitorPageModel.BuildItemListSignature(
                ItemConfigs,
                GetDefaultItemRefreshIntervalSec(),
                _settingsStore?.Get(nameof(Settings.DefaultItemPriceAlertRisePercent), 0d) ?? 0d,
                _settingsStore?.Get(nameof(Settings.DefaultItemPriceAlertFallPercent), 0d) ?? 0d,
                _settingsStore?.Get(nameof(Settings.DefaultItemPriceAlertWindowMinutes), 10) ?? 10,
                _settingsStore?.Get(nameof(Settings.DefaultItemPriceAlertCooldownMinutes), 10) ?? 10);
        }

        private ItemMonitorConfig? AddCandidate(SteamDtSearchCandidate candidate)
        {
            if (_settingsStore is null)
                return null;

            EnsureConfig();
            List<ItemMonitorConfig> itemConfigs = ItemConfigs;
            if (ItemMonitorPageModel.IsDuplicate(itemConfigs, candidate))
                return null;

            var item = ItemMonitorPageModel.CreateCandidateConfig(
                candidate,
                GetDefaultItemRefreshIntervalSec(),
                ItemMonitorPageModel.NextSortIndex(itemConfigs, taskbar: false),
                ItemMonitorPageModel.NextSortIndex(itemConfigs, taskbar: true),
                _settingsStore.Get(nameof(Settings.ItemMonitorDefaultVisibleInPanel), false),
                _settingsStore.Get(nameof(Settings.ItemMonitorDefaultVisibleInTaskbar), false));

            itemConfigs.Add(item);
            NormalizeItemIndexes();
            CommitItemConfigs();
            ClearCandidates();
            return item;
        }

        private async Task RefreshItemPriceAsync(ItemMonitorConfig item, Control button)
        {
            CancellationToken cancellationToken = PageToken;
            if (cancellationToken.IsCancellationRequested || _disposed || IsDisposed)
                return;

            button.Enabled = false;
            _searchPanel?.SetStatus("正在刷新：" + item.Name, warn: false);
            try
            {
                _steamDtItemService.Configure(GetSteamDtApiKey());
                bool ok = await _steamDtItemService.FetchItemPriceAsync(item, persistSettings: false);
                if (cancellationToken.IsCancellationRequested || _disposed || IsDisposed)
                    return;

                CommitItemConfigs();
                RefreshItemList();
                _searchPanel?.SetStatus(ok ? "已刷新：" + item.Name : "刷新失败：" + item.LastStatus, warn: !ok);
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested || _disposed || IsDisposed)
                    return;

                _searchPanel?.SetStatus("刷新失败：" + ex.Message, warn: true);
            }
            finally
            {
                if (!_disposed && !IsDisposed)
                    button.Enabled = true;
            }
        }

        private void DeleteItem(ItemMonitorConfig item)
        {
            List<ItemMonitorConfig> items = ItemConfigs;
            if (!items.Remove(item))
                return;

            NormalizeItemIndexes();
            CommitItemConfigs();
            RefreshItemList();
        }

        private void MoveItem(ItemMonitorConfig item, int direction)
        {
            List<ItemMonitorConfig>? ordered = ItemMonitorPageModel.MoveItem(ItemConfigs, item, direction);
            if (ordered is null)
                return;

            CommitItemConfigs(ordered);
            RefreshItemList();
        }

        private void NormalizeItemIndexes()
        {
            if (_settingsStore is null)
                return;

            List<ItemMonitorConfig> ordered = ItemMonitorPageModel.NormalizeItemIndexes(
                ItemConfigs,
                GetDefaultItemRefreshIntervalSec());
            CommitItemConfigs(ordered);
        }

        private void ClearCandidates()
        {
            Interlocked.Increment(ref _searchRequestVersion);
            StopSearchDebounce();
            ClearCandidateDropdown(clearText: true);
        }

        private void ClearCandidateDropdown(bool clearText)
        {
            _searchPanel?.ClearDropdown(clearText);
        }

        private Panel AddGroupToPage(LiteSettingsGroup group)
        {
            _groups.Add(group);
            var wrapper = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(0, 0, 0, UIUtils.S(16)),
                BackColor = Color.Transparent
            };
            wrapper.Resize += (_, __) => ClampGroupWidth(wrapper, group);
            wrapper.Layout += (_, __) => ClampGroupWidth(wrapper, group);
            wrapper.Controls.Add(group);
            _container.Controls.Add(wrapper);
            _container.Controls.SetChildIndex(wrapper, 0);
            return wrapper;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                CancelPageWork();
                if (_searchDebounceTimer is not null)
                {
                    _searchDebounceTimer.Stop();
                    _searchDebounceTimer.Tick -= OnSearchDebounceTick;
                    _searchDebounceTimer.Dispose();
                    _searchDebounceTimer = null;
                }
            }

            base.Dispose(disposing);
        }
    }
}
