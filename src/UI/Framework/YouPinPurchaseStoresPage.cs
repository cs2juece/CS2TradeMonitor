using System.Drawing;
using CS2TradeMonitor.Application.YouPin.PurchaseMonitoring;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using YouPinPurchaseMonitor.Domain;
using YouPinPurchaseMonitor.Models;

namespace CS2TradeMonitor.src.UI.Framework;

internal sealed class YouPinPurchaseStoresPage : YouPinPurchaseTabPage
{
    private readonly LiteTextBox _search = PurchaseMonitorUi.Input("搜索店铺或备注");
    private readonly LiteComboBox _filter = PurchaseMonitorUi.Choice("全部店铺", "有未读", "观察中", "已暂停", "待恢复观察", "失败待处理");
    private readonly LiteComboBox _period = PurchaseMonitorUi.Choice("近24小时", "近7天", "全部未读");
    private readonly Label _summary = PurchaseMonitorUi.Text(role: "sub");
    private readonly Label _empty = PurchaseMonitorUi.Text("尚未添加店铺\r\n点击右上角“添加店铺”，开始查看这家店铺的求购动态。", 12, role: "sub");
    private readonly Panel _list = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly Panel _body = new() { Dock = DockStyle.Fill };
    private readonly Control _home;
    private readonly LiteButton _add;
    private readonly Dictionary<Guid, YouPinStoreRow> _rows = [];
    private readonly List<Guid> _order = [];
    private readonly Action _openStatus;
    private readonly ContextMenuStrip _storeMenu = new();
    private YouPinStoreDetailPanel? _detail;
    private YouPinStoreEditor? _editor;
    private Guid? _selectedStore;
    private IReadOnlyList<StoreMonitorView> _stores = [];
    private Point _homeScroll;
    private (Guid StoreId, string View)? _pendingNavigation;

    public YouPinPurchaseStoresPage(IYouPinPurchaseMonitoringModule module, Action openStatus, Action openAccounts) : base(module)
    {
        _openStatus = openStatus;
        _add = PurchaseMonitorUi.Button("添加店铺", () => OpenEditor(StoreEditorMode.Add), true);
        var accounts = PurchaseMonitorUi.Button("读取账号", openAccounts);
        var heading = new Panel { Dock = DockStyle.Fill };
        Label title = PurchaseMonitorUi.Text("店铺求购动态", 18, true);
        heading.Controls.AddRange([title, accounts, _add]);
        heading.Layout += (_, _) =>
        {
            title.Dock = DockStyle.None;
            title.SetBounds(0, 0, Math.Max(1, heading.Width - UIUtils.S(310)), heading.Height);
            accounts.SetBounds(Math.Max(0, heading.Width - UIUtils.S(290)), UIUtils.S(8), UIUtils.S(140), UIUtils.S(36));
            _add.SetBounds(Math.Max(0, heading.Width - UIUtils.S(140)), UIUtils.S(8), UIUtils.S(140), UIUtils.S(36));
        };
        var sort = PurchaseMonitorUi.Button("按最新变化排序", () =>
        {
            _order.Clear();
            _order.AddRange(_stores.Select(store => store.StoreId));
            _list.AutoScrollPosition = Point.Empty;
            LayoutRows();
        });
        sort.Width = UIUtils.S(150);
        sort.Margin = new Padding(0, UIUtils.S(23), 0, 0);
        var filters = PurchaseMonitorUi.Actions(FilterField("搜索店铺或备注", _search, 270),
            FilterField("店铺筛选", _filter, 165), FilterField("动态范围", _period, 165), sort);
        _empty.Dock = DockStyle.None;
        _empty.TextAlign = ContentAlignment.MiddleCenter;
        _list.Controls.Add(_empty);
        _home = PurchaseMonitorUi.Stack((heading, 48),
            (PurchaseMonitorUi.Text("按店铺查看新增、未再观察到和数量变化；展开即可查看具体求购。", role: "sub"), 26),
            (filters, 60), (_summary, 28), (_list, 0));
        var surface = new ConsoleCardPanel { Dock = DockStyle.Fill, Padding = new Padding(UIUtils.S(16)) };
        surface.Controls.Add(_home);
        _body.Controls.Add(surface);
        Label footnote = PurchaseMonitorUi.Text("仅比较成功读取的范围；未再观察到不代表成交或撤单，读取失败不计入减少。", 9, role: "sub");
        Container.Controls.Add(PurchaseMonitorUi.Stack((_body, 0), (footnote, 36)));
        _search.TextChanged += (_, _) => RefreshSnapshot(Module.GetSnapshot());
        _filter.Inner.SelectedIndexChanged += (_, _) => RefreshSnapshot(Module.GetSnapshot());
        _period.Inner.SelectedIndexChanged += (_, _) => RefreshSnapshot(Module.GetSnapshot());
        _list.Resize += (_, _) => LayoutRows();
        UIColors.ApplyNativeTheme(_list);
        Resize += (_, _) => LayoutEditor();
    }

    protected override void RefreshSnapshot(YouPinPurchaseMonitoringSnapshot snapshot)
    {
        _stores = snapshot.Stores;
        _add.Enabled = snapshot.IsStarted && !CommandBusy && !snapshot.IsBusy && _stores.Count < 3;
        _summary.Text = !snapshot.IsStarted ? snapshot.Status
            : $"{_stores.Count} 家店铺 · {_stores.Sum(store => store.UnreadCount)} 条未读动态 · 自动更新，保持当前阅读位置"
                + (_stores.Count >= 3 ? " · 已达当前3家监控容量" : "");
        StoreMonitorView? selected = _stores.FirstOrDefault(store => store.StoreId == _selectedStore);
        if (_selectedStore is not null && selected is null) ShowHome();
        if (selected is not null) _detail?.UpdateStore(selected, snapshot.IsBusy || CommandBusy);
        HashSet<Guid> storeIds = _stores.Select(store => store.StoreId).ToHashSet();
        foreach (Guid removed in _rows.Keys.Where(id => !storeIds.Contains(id)).ToArray())
        {
            _rows[removed].Dispose();
            _rows.Remove(removed);
            _order.Remove(removed);
        }
        foreach (StoreMonitorView store in _stores)
        {
            if (!_rows.TryGetValue(store.StoreId, out YouPinStoreRow? row))
            {
                row = new YouPinStoreRow(Module, ShowDetails,
                    item => { ShowDetails(item); _detail?.ShowPurchases(); },
                    item => OpenEditor(StoreEditorMode.Edit, item), ShowMenu,
                    item => OpenEditor(StoreEditorMode.Reauthorize, item),
                    async item => await ExecuteAsync(() => Module.MarkStoreReadAsync(item.StoreId,
                        item.LatestChange?.ObservedAt ?? item.ReadThrough, PageToken)), LayoutRows, _rows.Count == 0, () => PageToken);
                _rows.Add(store.StoreId, row);
                _order.Add(store.StoreId);
                _list.Controls.Add(row);
            }
            row.Visible = MatchesFilter(store);
            row.UpdateStore(store, snapshot.IsBusy || CommandBusy, _period.SelectedIndex);
        }
        _empty.Visible = !_stores.Any(MatchesFilter);
        _empty.Text = _stores.Count == 0
            ? "尚未添加店铺\r\n\r\n先到右上角“读取账号”确认登录状态，再点击“添加店铺”。\r\n首次读取建立基线，后续在这里查看新增与减少。"
            : "没有符合筛选条件的店铺";
        LayoutRows();
        if (_pendingNavigation is { } navigation && _stores.FirstOrDefault(store => store.StoreId == navigation.StoreId) is { } target)
        {
            _pendingNavigation = null;
            if (navigation.View == "rules") OpenEditor(StoreEditorMode.Edit, target);
            else
            {
                ShowDetails(target);
                if (navigation.View == "purchases") _detail?.ShowPurchases();
            }
        }
    }

    public void OpenStore(Guid storeId, string view)
    {
        _pendingNavigation = (storeId, view);
        RefreshSnapshot(Module.GetSnapshot());
    }

    private bool MatchesFilter(StoreMonitorView store)
    {
        if (!store.Note.Contains(_search.Text.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        return _filter.SelectedIndex switch
        {
            1 => store.UnreadCount > 0,
            2 => store.Status is StoreObservationStatus.Active or StoreObservationStatus.RunningTick,
            3 => store.Status == StoreObservationStatus.Paused,
            4 => store.NeedsReauthorization,
            5 => store.Status == StoreObservationStatus.Failed,
            _ => true
        };
    }

    private void LayoutRows()
    {
        int top = _list.AutoScrollPosition.Y;
        int width = Math.Max(1, _list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
        int total = 0;
        foreach (Guid id in _order)
        {
            if (!_rows.TryGetValue(id, out YouPinStoreRow? row) || !row.Visible) continue;
            row.SetBounds(0, top, width, row.ContentHeight);
            top += row.Height;
            total += row.Height;
        }
        _empty.SetBounds(0, 0, width, UIUtils.S(220));
        _list.AutoScrollMinSize = new Size(0, total);
    }

    private void ShowDetails(StoreMonitorView store)
    {
        CloseEditor();
        _selectedStore = store.StoreId;
        _homeScroll = _list.AutoScrollPosition;
        if (_detail is null)
        {
            _detail = new YouPinStoreDetailPanel(Module, ShowHome, ShowMenu,
                item => OpenEditor(StoreEditorMode.Reauthorize, item), _openStatus);
            _body.Controls.Add(_detail);
        }
        _home.Visible = false;
        _detail.Visible = true;
        _detail.BringToFront();
        _detail.UpdateStore(store, Module.GetSnapshot().IsBusy);
    }

    private void ShowHome()
    {
        _selectedStore = null;
        if (_detail is not null) _detail.Visible = false;
        _home.Visible = true;
        LayoutRows();
        _list.AutoScrollPosition = new Point(-_homeScroll.X, -_homeScroll.Y);
    }

    private void ShowMenu(StoreMonitorView store, Control anchor)
    {
        ContextMenuStrip menu = CreateStoreMenu(store);
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    internal ContextMenuStrip CreateStoreMenu(StoreMonitorView store)
    {
        ContextMenuStrip menu = _storeMenu;
        menu.Close();
        while (menu.Items.Count > 0) menu.Items[0].Dispose();
        menu.BackColor = UIColors.CardBg;
        menu.ForeColor = UIColors.TextMain;
        menu.Items.Add("编辑备注与扫描规则", null, (_, _) => OpenEditor(StoreEditorMode.Edit, store));
        menu.Items.Add(store.NotificationsEnabled ? "关闭该店桌面提醒" : "开启该店桌面提醒", null,
            async (_, _) => await ExecuteAsync(() => Module.SetStoreNotificationsAsync(store.StoreId, !store.NotificationsEnabled, PageToken)));
        if (store.NeedsReauthorization)
            menu.Items.Add("粘贴链接恢复", null, (_, _) => OpenEditor(StoreEditorMode.Reauthorize, store));
        else
            menu.Items.Add(store.Status is StoreObservationStatus.Paused or StoreObservationStatus.Failed ? "继续观察" : "暂停观察", null,
                async (_, _) => await ExecuteAsync(() =>
                {
                    if (store.Status is StoreObservationStatus.Paused or StoreObservationStatus.Failed) Module.Resume(store.WatchId);
                    else Module.Pause(store.WatchId);
                    return Task.CompletedTask;
                }));
        if (!store.HasPriorityLane && store.Registration.Scope == ObservationScopeChoice.Top1000
            && store.Purchases.Count > 0 && store.Status == StoreObservationStatus.Active)
            menu.Items.Add("启用已命中优先复查", null, async (_, _) => await ExecuteAsync(() => Module.EnablePriorityAsync(store.WatchId, PageToken)));
        menu.Items.Add("移除店铺", null, async (_, _) =>
        {
            if (GlobalPromptService.Show(FindForm(), $"确定移除“{store.Note}”及其扫描状态吗？已保存的历史报告仍保留。",
                    "移除店铺", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                await ExecuteAsync(() => Module.RemoveAsync(store.WatchId, PageToken));
        });
        // WinForms still accesses the dropdown after Closed; the page owns its lifetime.
        return menu;
    }

    public override void Deactivate()
    {
        _storeMenu.Close();
        base.Deactivate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _storeMenu.Dispose();
        base.Dispose(disposing);
    }

    private void OpenEditor(StoreEditorMode mode, StoreMonitorView? store = null)
    {
        if (_editor is not null) return;
        _editor = new YouPinStoreEditor(Module, mode, store);
        Container.Enabled = false;
        _editor.Closed += CloseEditor;
        _editor.Saved += () => { CloseEditor(); RefreshSnapshot(Module.GetSnapshot()); };
        Controls.Add(_editor);
        _editor.BringToFront();
        LayoutEditor();
    }

    private void LayoutEditor()
    {
        if (_editor is null) return;
        int width = Math.Min(ClientSize.Width, UIUtils.S(480));
        _editor.SetBounds(ClientSize.Width - width, 0, width, ClientSize.Height);
    }

    private void CloseEditor()
    {
        _editor?.Dispose();
        _editor = null;
        Container.Enabled = true;
    }

    private static Control FilterField(string title, Control input, int width)
    {
        if (input is LiteTextBox textBox) textBox.AutoSize = false;
        var panel = new Panel { Width = UIUtils.S(width), Height = UIUtils.S(56), Margin = new Padding(0, 0, UIUtils.S(12), 0) };
        Label label = PurchaseMonitorUi.Text(title, 9, role: "sub");
        label.Dock = DockStyle.Top;
        label.Height = UIUtils.S(22);
        input.Dock = DockStyle.Bottom;
        input.Height = UIUtils.S(32);
        panel.Controls.AddRange([input, label]);
        return panel;
    }

}
