using System.Diagnostics;
using CS2TradeMonitor.Application.YouPin.PurchaseMonitoring;
using CS2TradeMonitor.Application.YouPin.PurchaseMonitoring.Links;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using YouPinPurchaseMonitor.Models;

namespace CS2TradeMonitor.src.UI.Framework;

internal enum StoreDetailTab { Activity, Purchases }

internal sealed class YouPinStoreDetailPanel : UserControl
{
    private readonly IYouPinPurchaseMonitoringModule _module;
    private readonly Label _title = PurchaseMonitorUi.Text(size: 18, bold: true);
    private readonly Label _status = PurchaseMonitorUi.Text(role: "sub");
    private readonly Label _summary = PurchaseMonitorUi.Text(bold: true);
    private readonly Label _description = PurchaseMonitorUi.Text(role: "sub");
    private readonly Label _warning = PurchaseMonitorUi.Text(role: "warn");
    private readonly LiteComboBox _period = PurchaseMonitorUi.Choice("近24小时", "近7天", "全部历史", "全部未读");
    private readonly PurchaseMonitorTable _changes = new(("饰品", 32), ("变化", 16), ("价格", 16), ("数量", 12), ("磨损条件", 12), ("观察时间", 12));
    private readonly PurchaseMonitorTable _purchases = new(("饰品", 31), ("求购价", 14), ("剩余数量", 10), ("磨损", 18), ("自动收货", 11), ("成功观察时间", 16));
    private readonly Panel _content = new() { Dock = DockStyle.Fill };
    private readonly FrameworkTopTabHeader<StoreDetailTab> _tabs;
    private readonly LiteButton _restore;
    private readonly LiteButton _more;
    private readonly LiteButton _refresh;
    private readonly LiteButton _read;
    private readonly List<StoreActivity> _items = [];
    private readonly CancellationTokenSource _lifetime = new();
    private StoreMonitorView? _store;
    private StoreDetailTab _tab;
    private StoreActivityCursor? _cursor;
    private DateTimeOffset? _loadedThrough;
    private int _version;
    private bool _loading;

    public YouPinStoreDetailPanel(IYouPinPurchaseMonitoringModule module, Action back, Action<StoreMonitorView, Control> menu,
        Action<StoreMonitorView> restore, Action openStatus)
    {
        _module = module;
        Dock = DockStyle.Fill;
        var backButton = PurchaseMonitorUi.Button("返回店铺列表", back);
        var settings = PurchaseMonitorUi.Button("店铺规则与操作", () => { });
        settings.Click += (_, _) => { if (_store is not null) menu(_store, settings); };
        _restore = PurchaseMonitorUi.Button("恢复观察", () => { if (_store is not null) restore(_store); }, true);
        _tabs = new([new(StoreDetailTab.Activity, "最新动态", 110), new(StoreDetailTab.Purchases, "当前求购", 110)], _tab, "店铺详情");
        _tabs.TabSelected += SetTab;
        _period.Inner.SelectedIndexChanged += (_, _) => _ = LoadAsync(false);
        _changes.ToneColumn = 1;
        _changes.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _purchases.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _changes.SelectionChanged += (_, _) =>
        {
            StoreActivity? item = _items.FirstOrDefault(activity => activity.Id == _changes.SelectedItem?.Key);
            _description.Text = item is null ? "按观察时间分组；选择一条动态查看完整变化条件。"
                : item.Change.CommodityName + " · " + string.Join("；", item.Changes.Select(change => change.Description).Distinct());
        };
        _content.Controls.AddRange([_changes, _purchases]);
        var market = PurchaseMonitorUi.Button("打开市场求购", OpenMarket);
        var scan = PurchaseMonitorUi.Button("扫描状态", openStatus);
        scan.Width = UIUtils.S(100);
        _refresh = PurchaseMonitorUi.Button("刷新动态", () => _ = LoadAsync(false));
        _refresh.Width = UIUtils.S(160);
        _more = PurchaseMonitorUi.Button("加载更早动态", () => _ = LoadAsync(true));
        _read = PurchaseMonitorUi.Button("全部标为已读", async () =>
        {
            if (_store is null) return;
            try { await _module.MarkStoreReadAsync(_store.StoreId, _store.LatestChange?.ObservedAt ?? _store.ReadThrough, _lifetime.Token); }
            catch (OperationCanceledException)
            {
                // Navigation or disposal cancels page work without showing an error.
            }
            catch (Exception error) { if (!IsDisposed) _warning.Text = error.Message; }
        });
        Controls.Add(PurchaseMonitorUi.Stack((PurchaseMonitorUi.Actions(backButton, settings, _restore), 42), (_title, 38),
            (_status, 28), (_tabs, 40), (PurchaseMonitorUi.Actions(_period, _refresh, market, scan), 40),
            (_summary, 30), (_content, 0), (PurchaseMonitorUi.Actions(_more, _read), 44), (_description, 48), (_warning, 28)));
    }

    public void ShowPurchases() => SetTab(StoreDetailTab.Purchases);

    private void SetTab(StoreDetailTab tab)
    {
        _tab = tab;
        _tabs.SetActiveTab(tab);
        _changes.Visible = _period.Visible = _refresh.Visible = _more.Visible = _read.Visible = tab == StoreDetailTab.Activity;
        _purchases.Visible = tab == StoreDetailTab.Purchases;
        if (tab == StoreDetailTab.Purchases) RefreshPurchases();
        else _summary.Text = $"已显示 {_items.Count} 条动态 · {_store?.UnreadCount ?? 0} 条未读";
    }

    public void UpdateStore(StoreMonitorView store, bool busy)
    {
        bool changed = _store?.StoreId != store.StoreId;
        _store = store;
        _title.Text = store.Note;
        _status.Text = PurchaseMonitorUi.StoreFreshness(store);
        _restore.Visible = store.NeedsReauthorization;
        _restore.Enabled = !busy;
        _read.Enabled = !busy && store.UnreadCount > 0;
        _warning.Text = store.NeedsReauthorization ? "以下为历史观察；重新提供店铺链接后继续复查。"
            : "未再观察到不代表成交或撤单；读取失败保留旧数据及原观察时间。";
        if (changed)
        {
            _items.Clear();
            SetTab(StoreDetailTab.Activity);
            _ = LoadAsync(false);
        }
        else if (!_loading && store.LatestChange?.ObservedAt != _loadedThrough) _refresh.Text = "有新动态，点击刷新";
        if (_tab == StoreDetailTab.Purchases) RefreshPurchases();
    }

    private void RefreshPurchases()
    {
        if (_store is null) return;
        _summary.Text = $"当前覆盖范围内已观察到 {_store.PurchaseSpeciesCount} 种 / {_store.Purchases.Count} 条求购 · 各条记录时间不同";
        _description.Text = "这里只包含已成功读取的公开求购；旧记录的观察时间不会因其他模板刷新而改变。";
        _purchases.SetRows(_store.Purchases.Select((item, index) => new PurchaseTableRow(
            $"{item.Purchase.TemplateId}:{item.Purchase.AbradeText}:{item.Purchase.PurchasePrice}:{index}",
            [item.Purchase.CommodityName ?? "未知饰品", PurchaseMonitorUi.Money(item.Purchase.PurchasePrice),
             PurchaseMonitorUi.Quantity(item.Purchase.SurplusQuantity) + "件", item.Purchase.AbradeText ?? "未提供",
             item.Purchase.AutoReceived is null ? "未提供" : item.Purchase.AutoReceived.Value ? "是" : "否",
             PurchaseMonitorUi.Time(item.ObservedAt)], TemplateId: item.Purchase.TemplateId)).ToArray());
    }

    private async Task LoadAsync(bool append)
    {
        if (_store is null || append && (_loading || _cursor is null)) return;
        StoreMonitorView store = _store;
        int version = ++_version;
        _loading = true;
        _more.Enabled = false;
        DateTimeOffset since = _period.SelectedIndex switch
        {
            0 => DateTimeOffset.UtcNow.AddHours(-24),
            1 => DateTimeOffset.UtcNow.AddDays(-7),
            3 => store.ReadThrough == DateTimeOffset.MaxValue ? store.ReadThrough : store.ReadThrough.AddTicks(1),
            _ => DateTimeOffset.MinValue
        };
        try
        {
            StoreActivityPage page = await _module.LoadStoreActivityAsync(new(store.HistoryIds, since, Before: append ? _cursor : null), _lifetime.Token);
            if (IsDisposed || version != _version) return;
            if (!append) _items.Clear();
            _items.AddRange(page.Items.Where(item => !_items.Any(existing => existing.Id == item.Id)));
            _cursor = page.NextCursor;
            _loadedThrough = store.LatestChange?.ObservedAt;
            _refresh.Text = "刷新动态";
            _changes.SetRows(GroupedRows(_items));
            _more.Enabled = _cursor is not null;
            _more.Text = _cursor is null ? "已显示全部" : "加载更早动态";
            if (_tab == StoreDetailTab.Activity) _summary.Text = page.TotalCount == 0
                ? "此范围内无可确认变化；可切换“当前求购”或“全部历史”"
                : $"已显示 {_items.Count}/{page.TotalCount} 条动态 · {store.UnreadCount} 条未读 · 价格与数量变化合并呈现";
        }
        catch (OperationCanceledException)
        {
            // Navigation or disposal cancels page work without showing an error.
        }
        catch (Exception error) { if (!IsDisposed && version == _version) _warning.Text = "动态读取失败：" + error.Message; }
        finally { if (version == _version) _loading = false; }
    }

    private static IReadOnlyList<PurchaseTableRow> GroupedRows(IEnumerable<StoreActivity> activities)
        => activities.GroupBy(item => (item.WatchId, item.ObservedAt)).SelectMany(group =>
            new[] { new PurchaseTableRow("group:" + group.Key, [PurchaseMonitorUi.Time(group.Key.ObservedAt), $"{group.Count()}条动态", "", "", "", ""], "sub") }
                .Concat(group.Select(ToActivityRow))).ToArray();

    internal static PurchaseTableRow ToActivityRow(StoreActivity activity)
    {
        PurchaseChangeView item = activity.Change;
        bool ceased = activity.Contains(PurchaseChangeKind.CeasedToBeObserved);
        string price = activity.Contains(PurchaseChangeKind.PriceChanged)
            ? $"{PurchaseMonitorUi.Money(item.PreviousPrice)} → {PurchaseMonitorUi.Money(item.CurrentPrice)}"
            : (ceased ? "原" : "") + PurchaseMonitorUi.Money(item.CurrentPrice ?? item.PreviousPrice);
        string quantity = activity.Contains(PurchaseChangeKind.QuantityIncreased) || activity.Contains(PurchaseChangeKind.QuantityDecreased)
            ? $"{PurchaseMonitorUi.Quantity(item.PreviousQuantity)} → {PurchaseMonitorUi.Quantity(item.CurrentQuantity)}件"
            : (ceased ? "原需" : "需要") + PurchaseMonitorUi.Quantity(item.CurrentQuantity ?? item.PreviousQuantity) + "件";
        return new(activity.Id, [item.CommodityName, string.Join(" / ", activity.Changes.Select(change => PurchaseMonitorUi.Kind(change.Kind)).Distinct()),
            price, quantity, item.WearRange ?? "磨损未提供", PurchaseMonitorUi.Time(activity.ObservedAt)],
            activity.Contains(PurchaseChangeKind.Appeared) ? "positive" : ceased || activity.Contains(PurchaseChangeKind.QuantityDecreased) ? "warn" : "link", item.TemplateId);
    }

    internal static PurchaseTableRow ToRow(ChangeBatchView batch, PurchaseChangeView item, int index)
        => ToActivityRow(new(batch.WatchId, batch.ObservedAt, index, batch.SafeNote, [item]));

    private void OpenMarket()
    {
        long? templateId = (_tab == StoreDetailTab.Purchases ? _purchases : _changes).SelectedItem?.TemplateId;
        if (templateId is null) return;
        try { Process.Start(new ProcessStartInfo(YouPinMarketPurchaseLink.Create(templateId.Value).AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception error) { GlobalPromptService.Show(FindForm(), error.Message, "打开市场求购失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _lifetime.Cancel(); _lifetime.Dispose(); }
        base.Dispose(disposing);
    }
}
