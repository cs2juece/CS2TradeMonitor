using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Links;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Reports;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using YouPinPurchaseMonitor.Infrastructure;
using YouPinPurchaseMonitor.Domain;
using YouPinPurchaseMonitor.Models;
using YouPinPurchaseMonitor.Services;

namespace CS2TradeMonitor.Application.YouPin.PurchaseMonitoring;

public sealed class YouPinPurchaseMonitoringModule : IYouPinPurchaseMonitoringModule
{
    private static readonly TimeSpan SchedulePollInterval = TimeSpan.FromSeconds(30);

    private readonly object _sync = new();
    private readonly MonitorPaths _paths;
    private readonly AppSettingsStore _settingsStore;
    private readonly AuditReportStore _auditStore;
    private readonly MonitoringSession _session;
    private readonly CoveragePlanLoader _planLoader = new();
    private AppSettings? _settings;
    private CoveragePlanInputs? _planInputs;
    private YouPinHotCoveragePlan? _top1000Plan;
    private CancellationTokenSource? _lifetime;
    private Task? _scheduleLoop;
    private AuditReportView? _latestAudit;
    private IReadOnlyList<ChangeBatchView> _recentChanges = [];
    private readonly StoreActivityArchive _activityArchive;
    private string _status = "尚未启动";
    private string? _error;
    private int _completedTemplates;
    private int _totalTemplates;
    private bool _started;
    private bool _disposed;

    public PurchaseAccounts? Accounts { get; }

    public YouPinPurchaseMonitoringModule(IAppDataPathProvider pathProvider, PurchaseAccounts? accounts = null)
    {
        Accounts = accounts;
        _paths = MonitorPaths.Create(pathProvider);
        _settingsStore = new AppSettingsStore(_paths);
        _activityArchive = new StoreActivityArchive(_paths.ApplicationDirectory);
        _auditStore = new AuditReportStore();
        var runtime = new YouPinAuditRuntime(_auditStore, accounts);
        _session = new MonitoringSession(
            runtime,
            new StoreObservationRegistryStore(_paths.ApplicationDirectory),
            new ChangeReportStore());
        _session.TickCompleted += OnTickCompleted;
        _session.StateChanged += OnSessionStateChanged;
    }

    public event EventHandler? StateChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        lock (_sync)
        {
            if (_started)
                return;
            _status = "正在载入内嵌 Top 1000 覆盖计划";
            _error = null;
        }
        RaiseStateChanged();

        try
        {
            Directory.CreateDirectory(_paths.ApplicationDirectory);
            AppSettings settings;
            try
            {
                settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                settings = _paths.CreateDefaultSettings();
                DiagnosticsLogger.Error(
                    "YouPinPurchaseMonitoring",
                    "Loading purchase-monitor settings failed; safe defaults are used without overwriting the file.",
                    ex);
            }

            CoveragePlanInputs inputs = await _planLoader.LoadInputsAsync(
                settings.CatalogPath,
                settings.RawDirectory,
                cancellationToken).ConfigureAwait(false);
            YouPinHotCoveragePlan top1000Plan = inputs.CreatePlan(ObservationScopeChoice.Top1000);
            await _session.InitializeAsync(settings, inputs, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<AuditReportView> audits = await LoadAuditHistoryCoreAsync(
                settings,
                cancellationToken).ConfigureAwait(false);
            IReadOnlyList<ChangeBatchView> changes = await _session.LoadChangeHistoryAsync(
                cancellationToken).ConfigureAwait(false);
            await Task.Run(() => _activityArchive.InitializeAsync(settings.ReportDirectory, cancellationToken), cancellationToken)
                .ConfigureAwait(false);

            var lifetime = new CancellationTokenSource();
            lock (_sync)
            {
                _settings = settings;
                _planInputs = inputs;
                _top1000Plan = top1000Plan;
                _latestAudit = audits.FirstOrDefault();
                _recentChanges = changes;
                _lifetime = lifetime;
                _started = true;
                _status = "就绪 · 请为店铺选择读取账号";
                _error = null;
                _scheduleLoop = RunScheduleLoopAsync(lifetime.Token);
            }
            RaiseStateChanged();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SetFailure("求购监控初始化失败；主程序将继续运行", ex);
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? lifetime;
        Task? scheduleLoop;
        lock (_sync)
        {
            if (!_started)
                return;
            _started = false;
            _status = "正在停止";
            lifetime = _lifetime;
            scheduleLoop = _scheduleLoop;
            _lifetime = null;
            _scheduleLoop = null;
        }
        _session.CancelActive();
        lifetime?.Cancel();
        if (scheduleLoop is not null)
        {
            try
            {
                await scheduleLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }
        lifetime?.Dispose();
        lock (_sync)
            _status = "已停止";
        RaiseStateChanged();
    }

    public YouPinPurchaseMonitoringSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            YouPinHotCoveragePlan? plan = _top1000Plan;
            IReadOnlyList<StoreObservationView> observations = _session.GetViews();
            return new YouPinPurchaseMonitoringSnapshot(
                _started,
                _session.IsBusy,
                _settings?.NotificationsEnabled ?? true,
                _status,
                _error,
                _paths.ApplicationDirectory,
                plan?.CandidateCount ?? 0,
                plan?.ResolvedCount ?? 0,
                plan?.BatchCount ?? 0,
                _completedTemplates,
                _totalTemplates,
                _latestAudit,
                observations,
                _recentChanges)
            {
                Stores = StoreMonitorProjection.Build(observations, _recentChanges).Select(store =>
                {
                    StoreActivityPreferences preferences = _activityArchive.Preferences(store.StoreId);
                    return store with
                    {
                        ReadThrough = preferences.ReadThrough,
                        UnreadCount = _activityArchive.Count(store.HistoryIds, preferences.ReadThrough, exclusive: true),
                        NotificationsEnabled = preferences.NotificationsEnabled,
                        RecentActivityCount = _activityArchive.Count(store.HistoryIds, DateTimeOffset.UtcNow.AddHours(-24))
                    };
                }).ToArray(),
                ArchivedWatchNotes = _activityArchive.WatchNotes()
            };
        }
    }

    public Task RunFullAuditAsync(string shopLink, YouPinPurchaseScope scope, CancellationToken cancellationToken = default)
        => RunAccountAuditAsync(shopLink, scope, PurchaseAccountSource.Unselected, cancellationToken);

    public async Task RunAccountAuditAsync(string shopLink, YouPinPurchaseScope scope, PurchaseAccountSource source,
        CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        PurchaseAccountBinding account = (Accounts ?? throw new InvalidOperationException("读取账号服务不可用。")).Bind(source);
        YouPinAuditSubject subject = ParseSubject(shopLink);
        YouPinHotCoveragePlan plan = RequirePlanInputs().CreatePlan(ToScope(scope));
        lock (_sync)
        {
            _status = $"正在扫描 {plan.CandidateCount} 个热门饰品模板";
            _error = null;
            _completedTemplates = 0;
            _totalTemplates = plan.CandidateCount;
        }
        RaiseStateChanged();

        var progress = new Progress<YouPinHotCoverageProgress>(value =>
        {
            lock (_sync)
            {
                _completedTemplates = value.CompletedTemplateCount;
                _totalTemplates = plan.CandidateCount;
                _status = $"扫描中 · {value.CompletedTemplateCount}/{plan.CandidateCount} · 批次 {value.CompletedBatchCount}/{value.TotalBatchCount}";
            }
            RaiseStateChanged();
        });
        try
        {
            AuditExecutionResult result = await _session.RunFullAuditAsync(
                subject,
                plan,
                progress,
                cancellationToken, account).ConfigureAwait(false);
            lock (_sync)
            {
                _latestAudit = result.Report.View;
                _completedTemplates = result.Snapshot.CompletedTemplateIds.Count;
                _totalTemplates = result.Snapshot.CandidateCount;
                _status = result.Snapshot.IsPartial
                    ? "扫描完成，但存在读取失败；失败不解释为无求购"
                    : $"扫描完成 · {result.Snapshot.Observations.Count} 条公开求购";
                _error = null;
            }
        }
        catch (OperationCanceledException)
        {
            lock (_sync)
                _status = "扫描已取消";
            throw;
        }
        catch (Exception ex)
        {
            SetFailure("扫描失败，未自动重试", ex);
            throw;
        }
        finally
        {
            RaiseStateChanged();
        }
    }

    public Task AddObservationAsync(string shopLink, string safeNote, YouPinPurchaseScope scope,
        int intervalMinutes, CancellationToken cancellationToken = default)
        => SaveAccountStoreAsync(null, shopLink, safeNote, scope, intervalMinutes, PurchaseAccountSource.Unselected, false, cancellationToken);

    public async Task SaveAccountStoreAsync(Guid? watchId, string shopLink, string safeNote, YouPinPurchaseScope scope,
        int intervalMinutes, PurchaseAccountSource source, bool resume, CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        PurchaseAccountBinding binding = (Accounts ?? throw new InvalidOperationException("读取账号服务不可用。")).Bind(source);
        if (watchId is null)
        {
            await _session.AddAsync(new AddStoreObservationRequest(ParseSubject(shopLink), safeNote,
                WatchPurposeChoice.SelfAudit, ToScope(scope), intervalMinutes)
            { Account = binding }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            StoreMonitorView previous = GetSnapshot().Stores.First(store => store.WatchId == watchId.Value);
            await _session.UpdateStoreAsync(watchId.Value, safeNote, ToScope(scope), intervalMinutes,
                string.IsNullOrWhiteSpace(shopLink) ? null : ParseSubject(shopLink), cancellationToken, binding).ConfigureAwait(false);
            if (resume)
            {
                StoreMonitorView updated = GetSnapshot().Stores.First(store => store.StoreId == previous.StoreId);
                _session.ResumeStore(updated.WatchId);
            }
        }
        Accounts.RetryManually();
        DiagnosticsLogger.Info("YouPinPurchaseMonitoring", $"店铺读取账号已选择：{source}；账号变更重新建立基线，历史保留。");
        SetStatus("店铺账号与规则已保存；首次成功读取仅建立基线");
    }

    public async Task ReauthorizeAsync(
        Guid watchId,
        string shopLink,
        CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        StoreMonitorView store = GetSnapshot().Stores.First(item => item.WatchId == watchId);
        if (Accounts is null || Accounts.Bind(store.Registration.Account.Source) != store.Registration.Account)
            throw new InvalidOperationException("请在恢复店铺观察中确认读取账号。");
        Accounts.RetryManually();
        await _session.ReauthorizeAsync(
            watchId,
            ParseSubject(shopLink),
            cancellationToken).ConfigureAwait(false);
        SetStatus("店铺观察已重新授权；原链接和认证参数未保存");
    }

    public async Task RunNextDueAsync(CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        try
        {
            MonitoringTickOutcome? outcome = await _session.RunNextDueAsync(cancellationToken)
                .ConfigureAwait(false);
            SetStatus(outcome is null ? "当前没有到期批次" : outcome.Observation.LastMessage ?? "批次完成");
        }
        catch (OperationCanceledException)
        {
            SetStatus("当前批次已取消");
            throw;
        }
        catch (Exception ex)
        {
            SetFailure("店铺观察批次失败，未自动重试", ex);
            throw;
        }
    }

    public void Pause(Guid watchId)
    {
        EnsureStarted();
        _session.PauseStore(watchId);
        SetStatus("观察项已暂停");
    }

    public void Resume(Guid watchId)
    {
        EnsureStarted();
        StoreMonitorView store = GetSnapshot().Stores.First(item => item.WatchId == watchId);
        if (Accounts is null || Accounts.Bind(store.Registration.Account.Source) != store.Registration.Account)
            throw new InvalidOperationException("读取账号已变更，请在店铺规则中确认账号，重新建立基线。");
        Accounts.RetryManually();
        _session.ResumeStore(watchId);
        SetStatus("观察项已继续");
    }

    public async Task RemoveAsync(Guid watchId, CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        await _session.RemoveStoreAsync(watchId, cancellationToken).ConfigureAwait(false);
        SetStatus("观察项及其本地状态已移除");
    }

    public void CancelActive() => _session.CancelActive();

    public string? ValidateShopLink(string shopLink)
    {
        try { ParseSubject(shopLink); return null; }
        catch (Exception error) when (error is ArgumentException or FormatException or InvalidOperationException)
        {
            return "请输入悠悠官方 HTTPS 店铺分享链接。";
        }
    }

    public async Task UpdateObservationAsync(Guid watchId, string safeNote, YouPinPurchaseScope scope,
        int intervalMinutes, string? shopLink = null, CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        await _session.UpdateStoreAsync(watchId, safeNote, ToScope(scope), intervalMinutes,
            string.IsNullOrWhiteSpace(shopLink) ? null : ParseSubject(shopLink), cancellationToken).ConfigureAwait(false);
        SetStatus("店铺规则已保存");
    }

    public async Task EnablePriorityAsync(Guid watchId, CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        await _session.EnableDualLayerAsync(watchId, cancellationToken).ConfigureAwait(false);
        SetStatus("已启用已命中优先复查：重点批次10分钟，兜底批次240分钟");
    }

    private void OnSessionStateChanged(object? sender, EventArgs e) => RaiseStateChanged();

    public async Task SetNotificationsEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        AppSettings settings = RequireSettings() with { NotificationsEnabled = enabled };
        await _settingsStore.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        lock (_sync)
            _settings = settings;
        SetStatus(enabled ? "变化提醒已开启" : "变化提醒已关闭");
    }

    public Task<IReadOnlyList<AuditReportView>> LoadAuditHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        return LoadAuditHistoryCoreAsync(RequireSettings(), cancellationToken);
    }

    private async Task RunScheduleLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(SchedulePollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await _session.RunNextDueAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    SetFailure("后台观察批次失败，已暂停该观察项且不会自动重试", ex);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private void OnTickCompleted(object? sender, MonitoringTickCompletedEventArgs e)
    {
        DiagnosticsLogger.Info("YouPinPurchaseMonitoring", e.Outcome.Observation.LastMessage ?? "店铺求购批次完成");
        bool newBatch = _activityArchive.Record(e.Outcome.ChangeBatch);
        lock (_sync)
        {
            ChangeBatchView[] previousLatest = _recentChanges.Where(batch => batch.HasChanges)
                .GroupBy(batch => batch.WatchId).Select(group => group.First()).ToArray();
            _recentChanges = new[] { e.Outcome.ChangeBatch }.Concat(_recentChanges.Take(499))
                .Concat(previousLatest).DistinctBy(batch => (batch.WatchId, batch.ObservedAt))
                .OrderByDescending(batch => batch.ObservedAt).ToArray();
        }
        if (newBatch && e.Outcome.ChangeBatch.HasChanges && RequireSettings().NotificationsEnabled)
        {
            StoreMonitorView? store = GetSnapshot().Stores.FirstOrDefault(item =>
                item.Lanes.Any(lane => lane.Registration.WatchId == e.Outcome.ChangeBatch.WatchId));
            if (store?.NotificationsEnabled == false) { RaiseStateChanged(); return; }
            string names = string.Join("、", e.Outcome.ChangeBatch.Changes.Select(change => change.CommodityName).Distinct().Take(3));
            AppNotificationHub.Instance.Request(
                "店铺求购有新动态",
                $"{store?.Note ?? e.Outcome.ChangeBatch.SafeNote}：{StoreActivityProjection.Summary(StoreActivityProjection.Build(e.Outcome.ChangeBatch))}\n{names}",
                AppNotificationSeverity.Info,
                playSound: false,
                source: "YouPinPurchaseMonitoring",
                dedupKey: "youpin-purchase-" + (store?.StoreId ?? e.Outcome.ChangeBatch.WatchId).ToString("N"));
        }
        RaiseStateChanged();
    }

    public async Task MarkStoreReadAsync(Guid storeId, DateTimeOffset through, CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        StoreMonitorView store = GetSnapshot().Stores.FirstOrDefault(item => item.StoreId == storeId)
            ?? throw new InvalidOperationException("店铺已移除。");
        DateTimeOffset latest = store.LatestChange?.ObservedAt ?? store.ReadThrough;
        await _activityArchive.UpdatePreferencesAsync(storeId, readThrough: through < latest ? through : latest,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        RaiseStateChanged();
    }

    public async Task SetStoreNotificationsAsync(Guid storeId, bool enabled, CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        if (!GetSnapshot().Stores.Any(item => item.StoreId == storeId)) throw new InvalidOperationException("店铺已移除。");
        await _activityArchive.UpdatePreferencesAsync(storeId, notificationsEnabled: enabled,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        RaiseStateChanged();
    }

    public Task<StoreActivityPage> LoadStoreActivityAsync(StoreActivityQuery query, CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        return Task.Run(() => _activityArchive.QueryAsync(query, cancellationToken), cancellationToken);
    }

    private Task<IReadOnlyList<AuditReportView>> LoadAuditHistoryCoreAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
        => _auditStore.LoadRecentAsync(
            [settings.ReportDirectory],
            cancellationToken: cancellationToken);

    private static YouPinAuditSubject ParseSubject(string shopLink)
        => YouPinAuditSubject.FromLink(YouPinShopLinkParser.Parse(shopLink));

    private static ObservationScopeChoice ToScope(YouPinPurchaseScope scope)
        => scope switch
        {
            YouPinPurchaseScope.Top100 => ObservationScopeChoice.Top100,
            YouPinPurchaseScope.Top300 => ObservationScopeChoice.Top300,
            YouPinPurchaseScope.Top1000 => ObservationScopeChoice.Top1000,
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };

    private void SetStatus(string status)
    {
        lock (_sync)
        {
            _status = status;
            _error = null;
        }
        RaiseStateChanged();
    }

    private void SetFailure(string status, Exception error)
    {
        lock (_sync)
        {
            _status = status;
            _error = error.Message;
        }
        DiagnosticsLogger.Error("YouPinPurchaseMonitoring", status, error);
        RaiseStateChanged();
    }

    private AppSettings RequireSettings()
    {
        lock (_sync)
            return _settings ?? throw new InvalidOperationException("求购监控模块尚未启动。");
    }

    private CoveragePlanInputs RequirePlanInputs()
    {
        lock (_sync)
            return _planInputs ?? throw new InvalidOperationException("求购监控覆盖计划尚未载入。");
    }

    private void EnsureStarted()
    {
        ThrowIfDisposed();
        lock (_sync)
        {
            if (!_started)
                throw new InvalidOperationException("求购监控模块尚未启动。");
        }
    }

    private void RaiseStateChanged()
    {
        EventHandler? handlers = StateChanged;
        if (handlers is null)
            return;
        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored(
                    "YouPinPurchaseMonitoring",
                    "DispatchStateChanged",
                    ex,
                    retryable: false,
                    category: "UI");
            }
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed)
            return;
        StopAsync().GetAwaiter().GetResult();
        _disposed = true;
        _session.TickCompleted -= OnTickCompleted;
        _session.StateChanged -= OnSessionStateChanged;
        _session.Dispose();
    }
}
