using CS2TradeMonitor.Application.YouPin.PurchaseMonitoring;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using YouPinPurchaseMonitor.Models;

namespace CS2TradeMonitor.src.UI.Framework;

internal sealed class YouPinPurchaseHistoryPage : YouPinPurchaseTabPage
{
    private readonly LiteComboBox _shops = PurchaseMonitorUi.Choice("全部店铺");
    private readonly LiteComboBox _period = PurchaseMonitorUi.Choice("近24小时", "近7天", "全部历史");
    private readonly LiteComboBox _kind = PurchaseMonitorUi.Choice("全部变化", "新增求购", "未再观察到", "数量变化", "价格变化", "其他变化");
    private readonly PurchaseMonitorTable _table = new(("店铺", 16), ("饰品", 27), ("变化", 16), ("价格", 14), ("数量", 12), ("时间", 15));
    private readonly Label _summary = PurchaseMonitorUi.Text(role: "sub");
    private readonly Label _detail = PurchaseMonitorUi.Text(role: "sub");
    private readonly LiteButton _more;
    private readonly List<StoreActivity> _items = [];
    private readonly Dictionary<Guid, string> _names = [];
    private IReadOnlyList<ShopChoice> _choices = [];
    private StoreActivityCursor? _cursor;
    private int _version;
    private bool _updating;
    private bool _loading;

    public YouPinPurchaseHistoryPage(IYouPinPurchaseMonitoringModule module) : base(module)
    {
        _more = PurchaseMonitorUi.Button("加载更早记录", () => _ = LoadAsync(true));
        var refresh = PurchaseMonitorUi.Button("刷新记录", () => _ = LoadAsync(false));
        _table.ToneColumn = 2;
        _table.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        Container.Controls.Add(PurchaseMonitorUi.Stack((PurchaseMonitorUi.Text("店铺历史动态", 18, true), 54),
            (PurchaseMonitorUi.Actions(_shops, _period, _kind, refresh), 50), (_summary, 36),
            (_table, 0), (PurchaseMonitorUi.Actions(_more), 44), (_detail, 60)));
        foreach (LiteComboBox choice in new[] { _shops, _period, _kind })
            choice.Inner.SelectedIndexChanged += (_, _) => { if (!_updating) _ = LoadAsync(false); };
        _table.SelectionChanged += (_, _) =>
        {
            StoreActivity? item = _items.FirstOrDefault(activity => activity.Id == _table.SelectedItem?.Key);
            _detail.Text = item is null ? "记录按店铺归属；时间为实际观察时间，非交易发生时间。"
                : string.Join("；", item.Changes.Select(change => change.CommodityName + " · " + change.Description));
        };
    }

    protected override void RefreshSnapshot(YouPinPurchaseMonitoringSnapshot snapshot)
    {
        _names.Clear();
        var choices = new List<ShopChoice>();
        foreach (StoreMonitorView store in snapshot.Stores.OrderBy(item => item.StoreId))
        {
            choices.Add(new(store.StoreId, store.Note, store.HistoryIds));
            foreach (Guid id in store.HistoryIds) _names[id] = store.Note;
        }
        foreach ((Guid id, string name) in snapshot.ArchivedWatchNotes.OrderBy(item => item.Key))
        {
            if (_names.ContainsKey(id)) continue;
            _names[id] = name + "（历史店铺）";
            choices.Add(new(id, _names[id], [id]));
        }
        bool changed = !_choices.Select(item => (item.Id, item.Name)).SequenceEqual(choices.Select(item => (item.Id, item.Name)));
        if (!changed && _version != 0) return;
        Guid? selected = _shops.SelectedIndex > 0 && _shops.SelectedIndex <= _choices.Count ? _choices[_shops.SelectedIndex - 1].Id : null;
        _choices = choices;
        _updating = true;
        _shops.Items.Clear();
        _shops.Items.Add("全部店铺");
        foreach (ShopChoice choice in choices) _shops.Items.Add(choice.Name);
        _shops.SelectedIndex = selected is null ? 0 : Math.Max(0, choices.FindIndex(item => item.Id == selected) + 1);
        _updating = false;
        if (snapshot.IsStarted) _ = LoadAsync(false);
    }

    private async Task LoadAsync(bool append)
    {
        if (!Module.GetSnapshot().IsStarted || append && (_loading || _cursor is null)) return;
        int version = ++_version;
        _loading = true;
        _more.Enabled = false;
        DateTimeOffset since = _period.SelectedIndex switch { 0 => DateTimeOffset.UtcNow.AddHours(-24), 1 => DateTimeOffset.UtcNow.AddDays(-7), _ => DateTimeOffset.MinValue };
        IReadOnlyCollection<Guid>? ids = _shops.SelectedIndex > 0 && _shops.SelectedIndex <= _choices.Count ? _choices[_shops.SelectedIndex - 1].WatchIds : null;
        PurchaseChangeKind[]? kinds = _kind.SelectedIndex switch
        {
            1 => [PurchaseChangeKind.Appeared],
            2 => [PurchaseChangeKind.CeasedToBeObserved],
            3 => [PurchaseChangeKind.QuantityIncreased, PurchaseChangeKind.QuantityDecreased],
            4 => [PurchaseChangeKind.PriceChanged],
            5 => [PurchaseChangeKind.AmbiguousReplacement, PurchaseChangeKind.ConfirmedFieldChange],
            _ => null
        };
        try
        {
            StoreActivityPage page = await Module.LoadStoreActivityAsync(new(ids, since, kinds, append ? _cursor : null), PageToken);
            if (IsDisposed || version != _version) return;
            if (!append) _items.Clear();
            _items.AddRange(page.Items.Where(item => !_items.Any(existing => existing.Id == item.Id)));
            _cursor = page.NextCursor;
            _table.SetRows(_items.Select(item =>
            {
                PurchaseTableRow row = YouPinStoreDetailPanel.ToActivityRow(item);
                return row with
                {
                    Values = [_names.GetValueOrDefault(item.WatchId) ?? item.SafeNote,
                    row.Values[0], row.Values[1], row.Values[2], row.Values[3], row.Values[5]]
                };
            }).ToArray());
            _summary.Text = $"已显示 {_items.Count}/{page.TotalCount} 条动态 · 按观察时间倒序 · 查询已保留的全部历史";
            _more.Enabled = _cursor is not null;
            _more.Text = _cursor is null ? "已显示全部" : "加载更早记录";
        }
        catch (OperationCanceledException)
        {
            // Navigation or disposal cancels page work without showing an error.
        }
        catch (Exception error) { if (!IsDisposed && version == _version) _summary.Text = "读取失败：" + error.Message; }
        finally { if (version == _version) _loading = false; }
    }

    private sealed record ShopChoice(Guid Id, string Name, IReadOnlyCollection<Guid> WatchIds);
}
