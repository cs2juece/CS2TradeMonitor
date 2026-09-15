using System.Drawing;
using CS2TradeMonitor.Application.YouPin.PurchaseMonitoring;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using YouPinPurchaseMonitor.Models;

namespace CS2TradeMonitor.src.UI.Framework;

internal enum StoreEditorMode { Add, Edit, Reauthorize }

internal sealed class YouPinStoreEditor : UserControl
{
    private readonly IYouPinPurchaseMonitoringModule _module;
    private readonly StoreMonitorView? _store;
    private readonly StoreEditorMode _mode;
    private readonly LiteTextBox _link = PurchaseMonitorUi.Input("粘贴悠悠官方店铺分享链接");
    private readonly LiteTextBox _note = PurchaseMonitorUi.Input("输入店铺备注，最多40字");
    private readonly LiteComboBox _scope = PurchaseMonitorUi.Choice("热门 Top 100", "热门 Top 300", "热门 Top 1000");
    private readonly NumericUpDown _interval = new() { Minimum = 10, Maximum = 1440, Value = 30 };
    private readonly Label _error = PurchaseMonitorUi.Text(role: "warn");
    private readonly Label _budget = PurchaseMonitorUi.Text(role: "sub");
    private readonly LiteButton _save;
    private readonly LiteButton _cancel;
    private readonly PurchaseAccountPicker _account;
    private bool _saving;
    public event Action? Closed;
    public event Action? Saved;

    public YouPinStoreEditor(IYouPinPurchaseMonitoringModule module, StoreEditorMode mode, StoreMonitorView? store)
    {
        _module = module;
        _mode = mode;
        _store = store;
        _account = new PurchaseAccountPicker(module.Accounts, store?.Registration.Account.Source ?? PurchaseAccountSource.Unselected);
        BackColor = UIColors.CardBg;
        Tag = "surface";
        Padding = new Padding(UIUtils.S(22));
        _note.MaxLength = 40;
        _note.Text = store?.Note ?? "";
        _scope.SelectedIndex = store?.Registration.Scope switch
        {
            ObservationScopeChoice.Top300 => 1,
            ObservationScopeChoice.Top1000 => 2,
            _ => 0
        };
        _interval.Value = store?.Registration.IntervalMinutes ?? 30;
        _interval.BackColor = UIColors.InputBg;
        _interval.ForeColor = UIColors.TextMain;
        _interval.Font = new Font("Microsoft YaHei UI", 10);
        _save = PurchaseMonitorUi.Button(mode switch
        {
            StoreEditorMode.Edit => "保存店铺规则",
            StoreEditorMode.Reauthorize => "验证并继续观察",
            _ => "添加并开始观察"
        }, async () => await SaveAsync(), true);
        _save.Width = UIUtils.S(180);
        _cancel = PurchaseMonitorUi.Button("取消", () => Closed?.Invoke());
        _cancel.Width = UIUtils.S(90);
        var footer = PurchaseMonitorUi.Actions(_cancel, _save);
        footer.Dock = DockStyle.Bottom;
        footer.Height = UIUtils.S(48);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UIColors.CardBg, Tag = "surface" };
        string title = mode switch { StoreEditorMode.Edit => "编辑店铺", StoreEditorMode.Reauthorize => "恢复店铺观察", _ => "添加店铺" };
        var rows = new List<(Control Control, int Height)>
        {
            (PurchaseMonitorUi.Text(title, 17, true), 48),
            (PurchaseMonitorUi.Text(mode == StoreEditorMode.Reauthorize ? $"{store?.Note} · 历史记录与进度已保留" : "持续查看这家店铺新增或减少了什么求购", role: "sub"), 42),
            (PurchaseMonitorUi.Text(mode == StoreEditorMode.Edit ? "店铺分享链接（恢复授权或修改范围时使用）" : "店铺分享链接 *"), 30),
            (_link, 38), (_error, 48),
            (PurchaseMonitorUi.Text("读取账号 *"), 30), (_account, 124)
        };
        if (mode != StoreEditorMode.Reauthorize)
        {
            rows.AddRange([(PurchaseMonitorUi.Text("店铺备注 *"), 30), (_note, 38),
                (PurchaseMonitorUi.Text("扫描范围"), 36), (_scope, 40),
                (PurchaseMonitorUi.Text("批次间隔（分钟）"), 36), (_interval, 38),
                (_budget, 76),
                (PurchaseMonitorUi.Text("在店铺“更多”中单独开关桌面提醒；未读动态始终保留。", role: "sub"), 46)]);
        }
        rows.Add((PurchaseMonitorUi.Text(mode == StoreEditorMode.Edit
            ? "修改范围会重新建立基线，保留历史动态。\r\n已启用双层的店铺，修改范围或间隔后恢复常规扫描。"
            : "首次成功读取建立基线，不发送新增提醒。\r\n原始链接不保存；重启后需重新粘贴。", role: "sub"), 74));
        TableLayoutPanel fields = PurchaseMonitorUi.Stack(rows.ToArray());
        fields.Dock = DockStyle.Top;
        fields.Height = UIUtils.S(rows.Sum(row => row.Height));
        fields.Tag = "surface";
        scroll.Controls.Add(fields);
        Controls.Add(scroll);
        Controls.Add(footer);
        _link.TextChanged += (_, _) => ValidateFields();
        _note.TextChanged += (_, _) => ValidateFields();
        _scope.Inner.SelectedIndexChanged += (_, _) => ValidateFields();
        _interval.ValueChanged += (_, _) => ValidateFields();
        _account.SelectionChanged += ValidateFields;
        ValidateFields();
    }

    private void ValidateFields()
    {
        if (_saving) return;
        bool scopeChanged = _store is not null && Scope != _store.Registration.Scope;
        bool linkRequired = _mode != StoreEditorMode.Edit || _store?.NeedsReauthorization == true && scopeChanged;
        string? error = linkRequired || !string.IsNullOrWhiteSpace(_link.Text) ? _module.ValidateShopLink(_link.Text) : null;
        if (error is null && _mode != StoreEditorMode.Reauthorize)
        {
            try { SafeNoteValidator.Validate(_note.Text); }
            catch (ArgumentException) { error = "请输入1到40字的备注，不包含链接或完整平台ID。"; }
        }
        if (error is null && _account.Source == PurchaseAccountSource.Unselected) error = "请选择读取账号，不会自动使用或切换账号。";
        _error.Text = error ?? "仅用于识别店铺，不保存原始链接。";
        _error.ForeColor = error is null ? UIColors.TextSub : UIColors.TextWarn;
        _save.Enabled = error is null;
        _budget.Text = PurchaseMonitorUi.CycleEstimate(_scope.SelectedIndex switch { 1 => 300, 2 => 1000, _ => 100 }, (int)_interval.Value);
    }

    private ObservationScopeChoice Scope => _scope.SelectedIndex switch
    {
        1 => ObservationScopeChoice.Top300,
        2 => ObservationScopeChoice.Top1000,
        _ => ObservationScopeChoice.Top100
    };

    private async Task SaveAsync()
    {
        if (_saving || !_save.Enabled) return;
        _saving = true;
        _save.Enabled = _cancel.Enabled = false;
        string link = _link.Text;
        _link.Clear();
        string? failure = null;
        try
        {
            await _module.SaveAccountStoreAsync(_store?.WatchId, link, _note.Text.Trim(),
                (YouPinPurchaseScope)_scope.SelectedIndex, (int)_interval.Value, _account.Source,
                _mode == StoreEditorMode.Reauthorize);
            if (!IsDisposed) Saved?.Invoke();
        }
        catch (Exception error)
        {
            failure = error.Message;
        }
        finally
        {
            _saving = false;
            if (!IsDisposed)
            {
                _cancel.Enabled = true;
                ValidateFields();
                if (failure is not null) { _error.Text = failure; _error.ForeColor = UIColors.TextWarn; }
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _link.Clear();
        base.Dispose(disposing);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && !_saving)
        {
            Closed?.Invoke();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
