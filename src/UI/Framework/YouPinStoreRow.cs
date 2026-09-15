using System.Drawing;
using CS2TradeMonitor.Application.YouPin.PurchaseMonitoring;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using YouPinPurchaseMonitor.Domain;
using YouPinPurchaseMonitor.Models;

namespace CS2TradeMonitor.src.UI.Framework;

internal sealed class YouPinStoreRow : UserControl
{
    private readonly IYouPinPurchaseMonitoringModule _module;
    private readonly Func<CancellationToken> _token;
    private readonly Action _resize;
    private readonly Label _name = PurchaseMonitorUi.Text(size: 14, bold: true);
    private readonly Label _account = PurchaseMonitorUi.Text(role: "sub");
    private readonly Label _status = PurchaseMonitorUi.Text(role: "sub");
    private readonly Label _summary = PurchaseMonitorUi.Text();
    private readonly Label _empty = PurchaseMonitorUi.Text(role: "sub");
    private readonly Label _unread = PurchaseMonitorUi.Text(role: "link", bold: true);
    private readonly Label _peek = PurchaseMonitorUi.Text(role: "sub");
    private readonly PurchaseMonitorTable _table = new(("变化", 15), ("饰品与求购条件", 37), ("求购价", 17), ("数量", 17), ("观察时间", 14));
    private readonly LiteButton _toggle;
    private readonly LiteButton _read;
    private readonly LiteButton _all;
    private readonly LiteButton _restore;
    private readonly Panel _header = new();
    private readonly FlowLayoutPanel _actions;
    private readonly FlowLayoutPanel _footer;
    private StoreMonitorView? _store;
    private IReadOnlyList<StoreActivity> _items = [];
    private bool _expanded;
    private string? _queryKey;
    private int _version;

    public YouPinStoreRow(IYouPinPurchaseMonitoringModule module, Action<StoreMonitorView> open,
        Action<StoreMonitorView> purchases, Action<StoreMonitorView> rules, Action<StoreMonitorView, Control> menu,
        Action<StoreMonitorView> restore, Action<StoreMonitorView> markRead, Action resize, bool expanded, Func<CancellationToken> token)
    {
        _module = module;
        _token = token;
        _resize = resize;
        _expanded = expanded;
        DoubleBuffered = true;
        LiteCorners.Clip(this);
        BackColor = UIColors.MainBg;
        _toggle = PurchaseMonitorUi.Button(expanded ? "收起" : "展开", () =>
        {
            _expanded = !_expanded;
            _toggle!.Text = _expanded ? "收起" : "展开";
            LayoutContent();
            _resize();
        });
        _toggle.Width = UIUtils.S(62);
        LiteButton current = PurchaseMonitorUi.Button("当前求购", () => { if (_store is not null) purchases(_store); });
        current.Width = UIUtils.S(100);
        LiteButton edit = PurchaseMonitorUi.Button("规则", () => { if (_store is not null) rules(_store); });
        edit.Width = UIUtils.S(64);
        LiteButton more = PurchaseMonitorUi.Button("更多", () => { });
        more.Width = UIUtils.S(64);
        more.Click += (_, _) => { if (_store is not null) menu(_store, more); };
        _actions = PurchaseMonitorUi.Actions(current, edit, more, _toggle);
        _header.Controls.AddRange([_name, _unread, _actions]);
        _all = PurchaseMonitorUi.Button("查看该店全部动态", () => { if (_store is not null) open(_store); });
        _all.Width = UIUtils.S(200);
        _read = PurchaseMonitorUi.Button("全部标为已读", () => { if (_store is not null) markRead(_store); });
        _read.Width = UIUtils.S(140);
        _restore = PurchaseMonitorUi.Button("恢复观察", () => { if (_store is not null) restore(_store); });
        _restore.Width = UIUtils.S(110);
        _footer = PurchaseMonitorUi.Actions(_all, _read, _restore);
        _table.ToneColumn = 0;
        _table.AutoSelectFirstRow = false;
        _name.Cursor = Cursors.Hand;
        _table.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        Controls.AddRange([_header, _status, _account, _summary, _table, _empty, _peek, _footer]);
        foreach (Control control in Controls) control.Dock = DockStyle.None;
        _name.Click += (_, _) => _toggle.PerformClick();
        Resize += (_, _) => LayoutContent();
    }

    public int ContentHeight => UIUtils.S(_expanded ? 172 + (_items.Count == 0 ? 42 : 40 + _items.Count * 52) : 132);

    public void UpdateStore(StoreMonitorView store, bool busy, int period)
    {
        _store = store;
        _name.Text = store.Note;
        _unread.Text = store.UnreadCount > 0 ? $"未读 {store.UnreadCount}" : "";
        _status.Text = PurchaseMonitorUi.StoreFreshness(store);
        _account.Text = _module.Accounts?.Summary(store.Registration.Account) ?? "读取账号：待选择";
        _read.Enabled = !busy && store.UnreadCount > 0;
        _restore.Visible = store.NeedsReauthorization;
        _restore.Enabled = !busy;
        string key = $"{store.StoreId}:{store.LatestChange?.ObservedAt:O}:{period}:{store.ReadThrough:O}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60}";
        if (_queryKey != key)
        {
            _queryKey = key;
            DateTimeOffset since = period switch
            {
                1 => DateTimeOffset.UtcNow.AddDays(-7),
                2 => store.ReadThrough == DateTimeOffset.MaxValue ? store.ReadThrough : store.ReadThrough.AddTicks(1),
                _ => DateTimeOffset.UtcNow.AddHours(-24)
            };
            _ = LoadAsync(store, since, period, ++_version);
        }
        LayoutContent();
    }

    private async Task LoadAsync(StoreMonitorView store, DateTimeOffset since, int period, int version)
    {
        try
        {
            StoreActivityPage page = await _module.LoadStoreActivityAsync(new(store.HistoryIds, since, PageSize: 4), _token());
            if (IsDisposed || version != _version) return;
            _items = page.Items;
            string range = period switch { 1 => "近7天", 2 => "未读", _ => "近24小时" };
            _summary.Text = page.TotalCount == 0 ? $"{range}无可确认变化 · 当前已观察到{store.PurchaseSpeciesCount}种 / {store.Purchases.Count}条求购"
                : $"{range} {page.TotalCount} 条动态 · 最近{_items.Count}条：{StoreActivityProjection.Summary(_items)}";
            _peek.Text = string.Join("；", _items.Take(3).Select(item =>
                PurchaseMonitorUi.Kind(item.Change.Kind) + " " + item.Change.CommodityName));
            if (_items.Count == 0) _peek.Text = "暂无新动态 · 查看当前求购或展开了解观察状态";
            _all.Text = page.NextCursor is null ? "查看该店全部动态" : $"查看全部动态（本范围{page.TotalCount}条）";
            _empty.Text = store.LastSuccessfulObservationAt is null ? "等待首次成功读取；结果逐步展示，首轮只建立基线。"
                : store.LatestChange is null ? "已读取范围内尚无变化，可查看当前求购。"
                : $"最近一次变化：{PurchaseMonitorUi.Time(store.LatestChange.ObservedAt)} · 点击“查看该店全部动态”追溯。";
            _table.SetRows(_items.Select(activity =>
            {
                PurchaseTableRow row = YouPinStoreDetailPanel.ToActivityRow(activity);
                return row with { Values = [row.Values[1], row.Values[0] + "\n" + row.Values[4], row.Values[2], row.Values[3], row.Values[5]] };
            }).ToArray());
            LayoutContent();
            _resize();
        }
        catch (OperationCanceledException) { if (version == _version) _queryKey = null; }
        catch (Exception error)
        {
            if (IsDisposed || version != _version) return;
            _summary.Text = "动态读取失败，请重新进入页面重试";
            _empty.Text = error.Message;
            _queryKey = null;
            LayoutContent();
        }
    }

    private void LayoutContent()
    {
        int pad = UIUtils.S(12);
        int width = Math.Max(1, ClientSize.Width - pad * 2);
        _header.SetBounds(pad, UIUtils.S(8), width, UIUtils.S(40));
        int actionsWidth = UIUtils.S(335);
        _actions.Dock = DockStyle.None;
        _actions.SetBounds(Math.Max(0, width - actionsWidth), 0, actionsWidth, UIUtils.S(44));
        _name.Dock = _unread.Dock = DockStyle.None;
        _name.SetBounds(0, 0, Math.Max(1, width - actionsWidth - UIUtils.S(90)), UIUtils.S(44));
        _unread.SetBounds(Math.Max(0, width - actionsWidth - UIUtils.S(88)), 0, UIUtils.S(80), UIUtils.S(44));
        _status.SetBounds(pad, UIUtils.S(48), width, UIUtils.S(24));
        _account.SetBounds(pad, UIUtils.S(72), width, UIUtils.S(24));
        _summary.SetBounds(pad, UIUtils.S(96), width, UIUtils.S(28));
        _summary.Visible = _expanded;
        _table.Visible = _expanded && _items.Count > 0;
        _empty.Visible = _expanded && _items.Count == 0;
        _peek.Visible = !_expanded;
        _footer.Visible = _expanded;
        int bodyHeight = _expanded ? _items.Count == 0 ? 42 : 40 + _items.Count * 52 : 0;
        _table.SetBounds(pad, UIUtils.S(128), width, UIUtils.S(bodyHeight));
        _empty.SetBounds(pad, UIUtils.S(128), width, UIUtils.S(bodyHeight));
        _peek.SetBounds(pad, UIUtils.S(96), width, UIUtils.S(32));
        _footer.SetBounds(pad, UIUtils.S(128 + (_expanded ? bodyHeight : 32)), width, UIUtils.S(42));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(UIColors.Border);
        e.Graphics.DrawLine(pen, UIUtils.S(12), Height - 1, Width - UIUtils.S(12), Height - 1);
    }
}
