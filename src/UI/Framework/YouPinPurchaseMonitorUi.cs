using System.Drawing;
using System.Globalization;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using YouPinPurchaseMonitor.Models;

namespace CS2TradeMonitor.src.UI.Framework;

internal static class PurchaseMonitorUi
{
    public static Label Text(string text = "", float size = 10, bool bold = false, string role = "main") => new()
    {
        Text = text,
        AutoEllipsis = true,
        Dock = DockStyle.Fill,
        Margin = Padding.Empty,
        Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = ColorFor(role),
        BackColor = Color.Transparent,
        Tag = role,
        TextAlign = ContentAlignment.MiddleLeft
    };

    public static Color ColorFor(string role) => role switch
    {
        "sub" => UIColors.TextSub,
        "warn" => UIColors.TextWarn,
        "positive" => UIColors.Positive,
        "link" => UIColors.Link,
        _ => UIColors.TextMain
    };

    public static LiteButton Button(string text, Action action, bool primary = false)
    {
        var button = new LiteButton(text, primary)
        {
            Width = UIUtils.S(130),
            Height = UIUtils.S(36),
            Margin = new Padding(0, UIUtils.S(3), UIUtils.S(10), UIUtils.S(3)),
            AccessibleName = text
        };
        button.Click += (_, _) => action();
        return button;
    }

    public static FlowLayoutPanel Actions(params Control[] controls)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false, BackColor = Color.Transparent };
        flow.Controls.AddRange(controls);
        return flow;
    }

    public static TableLayoutPanel Stack(params (Control Control, int Height)[] rows)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = rows.Length,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < rows.Length; i++)
        {
            panel.RowStyles.Add(rows[i].Height == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, UIUtils.S(rows[i].Height)));
            rows[i].Control.Dock = DockStyle.Fill;
            panel.Controls.Add(rows[i].Control, 0, i);
        }
        return panel;
    }

    public static LiteTextBox Input(string placeholder) => new()
    {
        PlaceholderText = placeholder,
        AccessibleName = placeholder,
        BorderStyle = BorderStyle.None,
        BackColor = UIColors.InputBg,
        ForeColor = UIColors.TextMain,
        Font = new Font("Microsoft YaHei UI", 10),
        Width = UIUtils.S(270),
        Margin = new Padding(0, UIUtils.S(7), UIUtils.S(12), 0)
    };

    public static LiteComboBox Choice(params string[] items)
    {
        var combo = new LiteComboBox { Width = UIUtils.S(175), Height = UIUtils.S(34), Margin = new Padding(0, UIUtils.S(3), UIUtils.S(12), 0) };
        combo.Items.AddRange(items);
        if (items.Length > 0) combo.SelectedIndex = 0;
        return combo;
    }

    public static string Time(DateTimeOffset? value) => value is null ? "尚无成功观察" : value == DateTimeOffset.MinValue ? "等待执行" : value.Value.ToLocalTime().ToString("MM-dd HH:mm");
    public static string Money(decimal? value) => value is null ? "未提供" : "¥" + value.Value.ToString("0.00", CultureInfo.InvariantCulture);
    public static string Quantity(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "未提供";
    public static string Kind(PurchaseChangeKind kind) => kind switch
    {
        PurchaseChangeKind.Appeared => "新增求购",
        PurchaseChangeKind.CeasedToBeObserved => "本次未再观察到",
        PurchaseChangeKind.QuantityIncreased => "数量增加",
        PurchaseChangeKind.QuantityDecreased => "数量减少",
        PurchaseChangeKind.PriceChanged => "价格变化",
        PurchaseChangeKind.AmbiguousReplacement => "无法唯一配对",
        _ => "其他字段变化"
    };
    public static string Status(StoreObservationStatus status) => status switch
    {
        StoreObservationStatus.Active => "观察中",
        StoreObservationStatus.RunningTick => "扫描中",
        StoreObservationStatus.Paused => "已暂停",
        StoreObservationStatus.PendingReauthorization => "待恢复观察",
        _ => "失败待处理"
    };

    public static string StoreFreshness(StoreMonitorView store)
    {
        string state = Status(store.Status);
        string latest = store.LatestChange is null ? "尚无可确认变化" : "最新变化 " + Time(store.LatestChange.ObservedAt);
        CoverageHealthSample? health = store.LatestBatch?.Health;
        string coverage = health is null ? "等待成功复查" : $"本轮成功{health.AttemptedTemplateCount - health.FailedTemplateCount}/{health.AttemptedTemplateCount}项"
            + (health.FailedTemplateCount > 0 ? $" · 覆盖不完整，{health.FailedTemplateCount}项未完成；查看扫描状态" : "");
        return store.NeedsReauthorization ? $"{state} · {latest} · 历史已保留，重新提供链接后继续"
            : $"{state} · {latest} · {coverage}";
    }

    public static string FailureReason(string reason)
    {
        if (reason.EndsWith(".code_84101", StringComparison.Ordinal)) return "需要登录，已暂停读取（84101）";
        string label = reason.Contains("page_limit", StringComparison.Ordinal) ? "达到3页上限，覆盖未完成"
            : reason.Contains("batch_aborted", StringComparison.Ordinal) ? "平台拒绝后未继续请求"
            : reason.Contains("platform_error", StringComparison.Ordinal) ? "平台业务拒绝"
            : reason.Contains("empty_response", StringComparison.Ordinal) ? "空响应，补读一次仍失败"
            : reason.Contains("invalid_response", StringComparison.Ordinal) ? "响应为空或格式异常"
            : reason.Contains("timeout", StringComparison.Ordinal) ? "读取超时"
            : reason.Contains("http_error", StringComparison.Ordinal) ? "HTTP请求失败"
            : reason;
        int codeIndex = reason.LastIndexOf(".code_", StringComparison.Ordinal);
        return codeIndex < 0 ? label : $"{label}（{reason[(codeIndex + 6)..]}）";
    }

    public static string CycleEstimate(int templates, int intervalMinutes)
    {
        int batches = (templates + 19) / 20;
        TimeSpan duration = TimeSpan.FromMinutes((long)batches * intervalMinutes);
        string time = duration.TotalDays >= 1 ? $"{(int)duration.TotalDays}天{duration.Hours}小时"
            : duration.TotalHours >= 1 ? $"{(int)duration.TotalHours}小时{duration.Minutes}分钟" : $"{duration.Minutes}分钟";
        return $"每批最多20项 · 共{batches}批 · 整轮间隔预算约{time}；排队、请求和失败会延长。";
    }

    public static void Theme(Control control)
    {
        if (control is LiteTextBox input) input.RefreshTheme();
        else if (control is Label label) label.ForeColor = ColorFor(label.Tag as string ?? "main");
        else if (control is TextBox) { control.BackColor = UIColors.InputBg; control.ForeColor = UIColors.TextMain; }
        else if (control is PurchaseMonitorTable table) table.ApplyTheme();
        else if (control is Panel or TableLayoutPanel or FlowLayoutPanel) control.BackColor = control.Tag as string == "surface" || control.Parent is ConsoleCardPanel || control.Parent?.BackColor == UIColors.CardBg ? UIColors.CardBg : UIColors.MainBg;
        foreach (Control child in control.Controls) Theme(child);
    }
}

internal sealed record PurchaseTableRow(string Key, string[] Values, string Role = "main", long? TemplateId = null);

internal sealed class PurchaseMonitorTable : DataGridView
{
    public int ToneColumn { get; set; } = -1;
    public bool AutoSelectFirstRow { get; set; } = true;
    private IReadOnlyList<PurchaseTableRow> _rows = [];
    public PurchaseTableRow? SelectedItem => CurrentCell is not null && CurrentCell.RowIndex < _rows.Count ? _rows[CurrentCell.RowIndex] : null;

    public PurchaseMonitorTable(params (string Name, int Weight)[] columns)
    {
        DoubleBuffered = true;
        LiteCorners.Clip(this);
        VirtualMode = true;
        ReadOnly = true;
        AllowUserToAddRows = AllowUserToDeleteRows = AllowUserToResizeRows = false;
        RowHeadersVisible = false;
        MultiSelect = false;
        SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        BorderStyle = BorderStyle.None;
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        ColumnHeadersHeight = UIUtils.S(40);
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        RowTemplate.Height = UIUtils.S(52);
        RowHeightInfoNeeded += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.RowIndex < _rows.Count)
                e.Height = UIUtils.S(_rows[e.RowIndex].Key.StartsWith("group:", StringComparison.Ordinal) ? 32 : 52);
        };
        Font = new Font("Microsoft YaHei UI", 10);
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        foreach ((string name, int weight) in columns)
            Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, FillWeight = weight, MinimumWidth = UIUtils.S(64), SortMode = DataGridViewColumnSortMode.NotSortable });
        CellValueNeeded += (_, e) => { if (e.RowIndex < _rows.Count) e.Value = _rows[e.RowIndex].Values[e.ColumnIndex]; };
        CellFormatting += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.RowIndex < _rows.Count && e.CellStyle is not null)
            {
                Color tone = PurchaseMonitorUi.ColorFor(ToneColumn < 0 || e.ColumnIndex == ToneColumn ? _rows[e.RowIndex].Role : "main");
                e.CellStyle.ForeColor = tone;
                e.CellStyle.SelectionForeColor = tone;
            }
        };
        ApplyTheme();
    }

    public void SetRows(IReadOnlyList<PurchaseTableRow> rows)
    {
        if (_rows.Count == rows.Count && _rows.Zip(rows).All(pair => pair.First.Key == pair.Second.Key
            && pair.First.Role == pair.Second.Role && pair.First.Values.SequenceEqual(pair.Second.Values))) return;
        string? selected = SelectedItem?.Key;
        int first = FirstDisplayedScrollingRowIndex;
        _rows = rows;
        RowCount = rows.Count;
        if (rows.Count > 0)
        {
            int index = selected is null ? rows.ToList().FindIndex(row => row.TemplateId is not null)
                : rows.ToList().FindIndex(row => row.Key == selected);
            if (AutoSelectFirstRow || selected is not null) CurrentCell = this[0, Math.Max(0, index)];
            else { CurrentCell = null; ClearSelection(); }
            if (first >= 0) FirstDisplayedScrollingRowIndex = Math.Min(first, rows.Count - 1);
        }
        Invalidate();
    }

    public void ApplyTheme()
    {
        BackgroundColor = UIColors.MainBg;
        GridColor = UIColors.Border;
        EnableHeadersVisualStyles = false;
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = UIColors.CardBg, ForeColor = UIColors.TextSub, Padding = new Padding(UIUtils.S(12), 0, 0, 0) };
        DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = UIColors.MainBg,
            ForeColor = UIColors.TextMain,
            SelectionBackColor = UIColors.NavSelected,
            SelectionForeColor = UIColors.TextMain,
            Padding = new Padding(UIUtils.S(12), 0, 0, 0),
            NullValue = "未提供",
            WrapMode = DataGridViewTriState.True
        };
    }
}
