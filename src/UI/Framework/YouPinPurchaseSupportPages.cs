using System.Diagnostics;
using System.Drawing;
using CS2TradeMonitor.Application.YouPin.PurchaseMonitoring;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using YouPinPurchaseMonitor.Models;

namespace CS2TradeMonitor.src.UI.Framework;

internal sealed class YouPinPurchaseStatusPage : YouPinPurchaseTabPage
{
    private readonly Label _summary = PurchaseMonitorUi.Text(role: "sub");
    private readonly Label _warning = PurchaseMonitorUi.Text(role: "warn");
    private readonly PurchaseMonitorTable _stores = new(("店铺 / 通道", 25), ("状态", 15), ("下一批", 12), ("成功基线", 15), ("最旧基线", 18), ("下次到期", 15));
    private readonly PurchaseMonitorTable _health = new(("店铺", 19), ("批次时间", 18), ("失败 / 尝试", 15), ("失败率", 12), ("原因分布", 36));
    private readonly LiteButton _run;
    private readonly LiteButton _stop;
    private readonly LiteButton _audit;

    public YouPinPurchaseStatusPage(IYouPinPurchaseMonitoringModule module) : base(module)
    {
        _run = PurchaseMonitorUi.Button("执行到期批次", async () => await ExecuteAsync(() => Module.RunNextDueAsync(PageToken)), true);
        _stop = PurchaseMonitorUi.Button("停止当前扫描", Module.CancelActive);
        _audit = PurchaseMonitorUi.Button("临时完整扫描", async () => await StartAuditAsync());
        var history = PurchaseMonitorUi.Button("完整扫描历史", async () => await ShowAuditHistoryAsync());
        Container.Controls.Add(PurchaseMonitorUi.Stack((PurchaseMonitorUi.Text("店铺扫描状态", 18, true), 54),
            (PurchaseMonitorUi.Actions(_run, _stop, _audit, history), 48), (_summary, 40),
            (_stores, 210), (PurchaseMonitorUi.Text("批次健康趋势 · 最近20批", 11, true), 40), (_health, 0), (_warning, 54)));
    }

    protected override void RefreshSnapshot(YouPinPurchaseMonitoringSnapshot snapshot)
    {
        _run.Enabled = _audit.Enabled = snapshot.IsStarted && !snapshot.IsBusy && !CommandBusy;
        _stop.Enabled = snapshot.IsBusy;
        _summary.Text = snapshot.IsBusy ? snapshot.Observations.FirstOrDefault(lane => lane.Status == StoreObservationStatus.RunningTick) is { } running
            ? $"正在扫描 {running.Registration.SafeNote} · 批次 {running.NextBatchNumber}/{running.TotalBatchCount}"
            : snapshot.TotalTemplates > 0 ? $"{snapshot.Status} · {snapshot.CompletedTemplates}/{snapshot.TotalTemplates}" : "正在处理店铺操作…"
            : "首轮建档逐批推进；遍历后按配置周期复查。各店铺共用串行队列。";
        _stores.SetRows(snapshot.Stores.SelectMany(store => store.Lanes.Select(lane => new PurchaseTableRow(
            lane.Registration.WatchId.ToString(), [store.Note + (store.HasPriorityLane ? lane.Registration.ScheduleLane == ObservationScheduleLane.Priority ? " · 重点" : " · 兜底" : ""),
                PurchaseMonitorUi.Status(lane.Status), $"{(lane.CompletedCoverageCycles == 0 ? "建档" : "复查")} {lane.NextBatchNumber}/{lane.TotalBatchCount}",
                $"{lane.BaselineTemplateCount}/{lane.CandidateTemplateCount}", PurchaseMonitorUi.Time(lane.OldestBaselineAt),
                lane.NextDueAt is null ? "暂不调度" : PurchaseMonitorUi.Time(lane.NextDueAt)]))).ToArray());
        var batches = snapshot.Stores.SelectMany(store => store.History.Where(batch => batch.Health is not null)
            .Select(batch => (Store: store, Batch: batch))).OrderByDescending(item => item.Batch.ObservedAt).Take(20).ToArray();
        _health.SetRows(batches.Select(item => new PurchaseTableRow($"{item.Batch.WatchId}:{item.Batch.ObservedAt:O}",
            [item.Store.Note, PurchaseMonitorUi.Time(item.Batch.ObservedAt),
             $"{item.Batch.Health!.FailedTemplateCount}/{item.Batch.Health.AttemptedTemplateCount}",
             item.Batch.Health.FailureRate.ToString("P0"), string.Join("；", item.Batch.Health.ReasonDistribution.Select(reason => $"{PurchaseMonitorUi.FailureReason(reason.ReasonCode)}: {reason.Count}"))],
            item.Batch.Health.HasProtocolDriftWarning ? "warn" : "main")).ToArray());
        _warning.Text = snapshot.Error is not null ? snapshot.Status
            : snapshot.Stores.Any(store => store.LatestBatch?.Health?.HasProtocolDriftWarning == true)
                ? "最新批次覆盖不完整；分页上限、读取异常和平台拒绝请分别查看原因，不能据此认定店铺没有求购。"
                : "失败模板保留旧基线；每批最多20模板，每模板最多3页，登录读取跨店铺请求间隔至少10秒。";
    }

    private async Task StartAuditAsync()
    {
        using var dialog = new Form
        {
            Text = "临时完整扫描",
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(UIUtils.S(530), UIUtils.S(430)),
            BackColor = UIColors.MainBg,
            ForeColor = UIColors.TextMain,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            Padding = new Padding(UIUtils.S(20))
        };
        var account = new PurchaseAccountPicker(Module.Accounts);
        LiteTextBox link = PurchaseMonitorUi.Input("粘贴悠悠官方店铺分享链接");
        LiteComboBox scope = PurchaseMonitorUi.Choice("热门 Top 100", "热门 Top 300", "热门 Top 1000");
        Label error = PurchaseMonitorUi.Text("本次单独读取结果，不建立新的店铺监控。", role: "sub");
        LiteButton start = PurchaseMonitorUi.Button("开始扫描", () => dialog.DialogResult = DialogResult.OK, true);
        start.Enabled = false;
        link.TextChanged += (_, _) => { string? problem = Module.ValidateShopLink(link.Text); start.Enabled = problem is null && account.Source != PurchaseAccountSource.Unselected; error.Text = problem ?? "串行低频读取，完整范围可能需要较长时间。"; };
        account.SelectionChanged += () => start.Enabled = Module.ValidateShopLink(link.Text) is null && account.Source != PurchaseAccountSource.Unselected;
        dialog.Controls.Add(PurchaseMonitorUi.Stack((PurchaseMonitorUi.Text("店铺分享链接"), 30), (link, 42), (scope, 42), (account, 124), (error, 48),
            (PurchaseMonitorUi.Actions(PurchaseMonitorUi.Button("取消", () => dialog.Close()), start), 48)));
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        string rawLink = link.Text;
        link.Clear();
        await ExecuteAsync(() => Module.RunAccountAuditAsync(rawLink, (YouPinPurchaseScope)scope.SelectedIndex, account.Source, PageToken));
        if (Module.GetSnapshot().LatestAudit is not null) await ShowAuditHistoryAsync();
    }

    private async Task ShowAuditHistoryAsync()
    {
        await ExecuteAsync(async () =>
        {
            IReadOnlyList<AuditReportView> reports = await Module.LoadAuditHistoryAsync(PageToken);
            if (IsDisposed) return;
            using var dialog = new Form
            {
                Text = "完整扫描历史",
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(UIUtils.S(1000), UIUtils.S(660)),
                BackColor = UIColors.MainBg,
                Padding = new Padding(UIUtils.S(20))
            };
            LiteComboBox picker = PurchaseMonitorUi.Choice();
            picker.Items.AddRange(reports.Select(report => report.DisplayName).Cast<object>().ToArray());
            var grid = new PurchaseMonitorTable(("饰品", 40), ("求购价", 15), ("剩余", 10), ("磨损", 20), ("自动收货", 15));
            picker.Inner.SelectedIndexChanged += (_, _) =>
            {
                if (picker.SelectedIndex < 0 || picker.SelectedIndex >= reports.Count) return;
                grid.SetRows(reports[picker.SelectedIndex].Purchases.Select((row, index) => new PurchaseTableRow(index.ToString(),
                    [row.MarketHashName, PurchaseMonitorUi.Money(row.PurchasePrice), PurchaseMonitorUi.Quantity(row.SurplusQuantity),
                     row.AbradeText ?? "未提供", row.AutoReceived is null ? "未提供" : row.AutoReceived.Value ? "是" : "否"], TemplateId: row.TemplateId)).ToArray());
            };
            if (reports.Count > 0) picker.SelectedIndex = 0;
            dialog.Controls.Add(PurchaseMonitorUi.Stack((picker, 44), (grid, 0),
                (PurchaseMonitorUi.Text("历史完整扫描独立保存，不与店铺监控的成功基线混合比较。", role: "sub"), 40)));
            dialog.ShowDialog(FindForm());
        });
    }
}

internal sealed class YouPinPurchaseSettingsPage : YouPinPurchaseTabPage
{
    private readonly CheckBox _notifications = new() { Text = "发现可确认变化时发送桌面提醒", AutoSize = true };
    private readonly PurchaseAccountManagement _accounts;
    private readonly Label _coverage = PurchaseMonitorUi.Text(role: "sub");
    private bool _updating;

    public YouPinPurchaseSettingsPage(IYouPinPurchaseMonitoringModule module) : base(module)
    {
        _accounts = new PurchaseAccountManagement(module.Accounts);
        _notifications.Font = new Font("Microsoft YaHei UI", 11);
        _notifications.ForeColor = UIColors.TextMain;
        _notifications.CheckedChanged += async (_, _) =>
        {
            if (!_updating) await ExecuteAsync(() => Module.SetNotificationsEnabledAsync(_notifications.Checked, PageToken));
        };
        LiteButton folder = PurchaseMonitorUi.Button("打开历史数据目录", () =>
        {
            try { Process.Start(new ProcessStartInfo(Module.GetSnapshot().DataDirectory) { UseShellExecute = true }); }
            catch (Exception error) { ShowError("打开数据目录失败", error); }
        });
        folder.Width = UIUtils.S(175);
        Container.AutoScroll = true;
        TableLayoutPanel settings = PurchaseMonitorUi.Stack((PurchaseMonitorUi.Text("监控设置", 18, true), 60),
            (_accounts, 310),
            (PurchaseMonitorUi.Text("店铺动态提醒", 12, true), 44), (_notifications, 44),
            (PurchaseMonitorUi.Text("同一批次的变化按店铺汇总，默认无声音；首次成功读取仅建立基线。", role: "sub"), 52),
            (PurchaseMonitorUi.Text("扫描范围与频率", 12, true), 44), (_coverage, 60),
            (PurchaseMonitorUi.Text("店铺菜单可修改备注、范围和批次间隔。Top1000店铺已命中后，可启用重点复查。\r\n首轮建档逐批推进；之后重点10分钟一批、兜底240分钟一批。\r\n首轮遍历不代表全部读取成功，失败项保留旧基线；范围外饰品不扫描。", role: "sub"), 80),
            (PurchaseMonitorUi.Text("历史与重启恢复", 12, true), 44),
            (PurchaseMonitorUi.Text("保留店铺备注、成功基线、扫描进度及脱敏变化记录。\r\n原始店铺链接不保存；重启后重新粘贴链接即可继续。", role: "sub"), 74),
            (PurchaseMonitorUi.Actions(folder), 48),
            (PurchaseMonitorUi.Text("每家店铺由你选择读取账号；独立账号加密保存，不自动切换账号。读取失败保留旧数据。", role: "sub"), 50));
        settings.Dock = DockStyle.Top;
        settings.Height = UIUtils.S(1010);
        Container.Controls.Add(settings);
    }

    public void ShowAccounts() => Container.AutoScrollPosition = Point.Empty;

    protected override void RefreshSnapshot(YouPinPurchaseMonitoringSnapshot snapshot)
    {
        _accounts.RefreshAccounts();
        _updating = true;
        _notifications.Checked = snapshot.NotificationsEnabled;
        _notifications.Enabled = snapshot.IsStarted && !CommandBusy;
        _updating = false;
        _coverage.Text = $"内嵌热门目录：{snapshot.ResolvedCount}/{snapshot.CandidateCount} 已映射 · {snapshot.BatchCount} 批\r\n每批最多20模板，每模板最多3页，登录读取跨店铺请求间隔至少10秒。";
    }
}
