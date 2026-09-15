using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Reports;
using System.Globalization;
using YouPinPurchaseMonitor.Infrastructure;
using YouPinPurchaseMonitor.Domain;
using YouPinPurchaseMonitor.Models;

namespace YouPinPurchaseMonitor.Services;

public sealed record AddStoreObservationRequest(
    YouPinAuditSubject Subject,
    string SafeNote,
    WatchPurposeChoice Purpose,
    ObservationScopeChoice Scope,
    int IntervalMinutes,
    IReadOnlyList<long>? SelectedTemplateIds = null)
{
    public CS2TradeMonitor.Application.YouPin.PurchaseMonitoring.PurchaseAccountBinding Account { get; init; } = CS2TradeMonitor.Application.YouPin.PurchaseMonitoring.PurchaseAccountBinding.Unselected;
}

public sealed record MonitoringTickOutcome(
    StoreObservationView Observation,
    ChangeBatchView ChangeBatch,
    YouPinCoverageWatchRunResult CoreResult);

public sealed record DualLayerScheduleView(
    StoreObservationView Priority,
    StoreObservationView Fallback);

public sealed class MonitoringTickCompletedEventArgs : EventArgs
{
    public MonitoringTickCompletedEventArgs(MonitoringTickOutcome outcome) => Outcome = outcome;
    public MonitoringTickOutcome Outcome { get; }
}

public sealed partial class MonitoringSession : IDisposable
{
    private readonly IYouPinAuditOperations _operations;
    private readonly StoreObservationRegistryStore _registryStore;
    private readonly ChangeReportStore _changeStore;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _sync = new();
    private readonly Dictionary<Guid, ObservationEntry> _entries = [];
    private CancellationTokenSource? _activeCancellation;
    private Guid? _activeWatchId;
    private AppSettings? _settings;
    private CoveragePlanInputs? _planInputs;
    private bool _initialized;
    private bool _disposed;

    public MonitoringSession(
        IYouPinAuditOperations operations,
        StoreObservationRegistryStore registryStore,
        ChangeReportStore changeStore)
    {
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _registryStore = registryStore ?? throw new ArgumentNullException(nameof(registryStore));
        _changeStore = changeStore ?? throw new ArgumentNullException(nameof(changeStore));
    }

    public event EventHandler<MonitoringTickCompletedEventArgs>? TickCompleted;
    public event EventHandler? StateChanged;

    public bool IsBusy
    {
        get
        {
            lock (_sync)
                return _activeCancellation is not null;
        }
    }

    public async Task InitializeAsync(
        AppSettings settings,
        CoveragePlanInputs planInputs,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(planInputs);
        lock (_sync)
        {
            if (_initialized && (_entries.Count > 0 || _activeCancellation is not null))
            {
                throw new InvalidOperationException(
                    "存在店铺观察项或正在执行的操作时不能重新载入覆盖计划。");
            }
        }
        IReadOnlyList<StoreObservationRegistration> registrations =
            await _registryStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var restored = new Dictionary<Guid, ObservationEntry>();
        foreach (StoreObservationRegistration registration in registrations)
        {
            YouPinHotCoveragePlan plan = CreatePlan(planInputs, registration);
            if (!string.Equals(plan.Fingerprint, registration.PlanFingerprint, StringComparison.Ordinal))
                throw new InvalidDataException($"店铺观察项 {registration.WatchId:N} 的计划指纹不一致。");
            YouPinCoverageWatchState? state = await _operations.LoadWatchStateAsync(
                registration.WatchId,
                settings,
                cancellationToken).ConfigureAwait(false);
            restored.Add(registration.WatchId, new ObservationEntry(
                registration,
                plan,
                state,
                Watch: null,
                StoreObservationStatus.PendingReauthorization,
                "重启后需要重新粘贴对应店铺链接。"));
        }

        lock (_sync)
        {
            _settings = settings;
            _planInputs = planInputs;
            _initialized = true;
            _entries.Clear();
            _pauseRequested.Clear();
            foreach ((Guid id, ObservationEntry entry) in restored)
                _entries.Add(id, entry);
        }
    }

    public async Task<StoreObservationView> AddAsync(
        AddStoreObservationRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        string note = SafeNoteValidator.Validate(request.SafeNote);
        if (request.IntervalMinutes is < 10 or > 1440)
            throw new ArgumentOutOfRangeException(nameof(request), "扫描间隔必须在 10 到 1440 分钟之间。");

        AppSettings settings = RequireSettings() with
        {
            WatchPurpose = request.Purpose,
            WatchIntervalMinutes = request.IntervalMinutes
        };
        CoveragePlanInputs inputs = RequirePlanInputs();
        YouPinHotCoveragePlan plan = request.Scope == ObservationScopeChoice.ObservedOnly
            ? inputs.CreatePlan(request.Scope, request.SelectedTemplateIds)
            : inputs.CreatePlan(request.Scope);

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                int storeCount = _entries.Values.Count(
                    item => item.Registration.ScheduleLane != ObservationScheduleLane.Priority);
                if (storeCount >= StoreObservationRegistryStore.MaximumStoreCount)
                    throw new InvalidOperationException("最多允许三个店铺观察目标。");
            }
            using CancellationTokenSource linked = BeginOperation(cancellationToken);
            ObservationEntry entry = await CreateEntryAsync(
                request,
                note,
                plan,
                settings,
                ObservationScheduleLane.Standalone,
                partnerWatchId: null,
                linked.Token).ConfigureAwait(false);
            lock (_sync)
                _entries.Add(entry.Registration.WatchId, entry);
            try
            {
                await SaveRegistryAsync(linked.Token).ConfigureAwait(false);
            }
            catch
            {
                lock (_sync)
                    _entries.Remove(entry.Registration.WatchId);
                await _operations.DeleteWatchStateAsync(
                    entry.Registration.WatchId,
                    settings,
                    CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            return Project(entry);
        }
        finally
        {
            EndOperation();
            _operationGate.Release();
        }
    }

    public async Task<StoreObservationView> ReauthorizeAsync(
        Guid watchId,
        YouPinAuditSubject subject,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(subject);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObservationEntry entry = GetEntry(watchId);
            string mask = MaskUserId(subject.UserId);
            if (!string.Equals(mask, entry.Registration.TargetMask, StringComparison.Ordinal))
                throw new InvalidOperationException("该链接与观察项保存的目标掩码不一致。");
            ObservationEntry authorizedEntry = await RestoreAuthorizedEntryAsync(
                entry,
                subject,
                cancellationToken)
                .ConfigureAwait(false);
            ObservationEntry? authorizedPartner = null;
            if (entry.Registration.PartnerWatchId is Guid partnerWatchId)
            {
                ObservationEntry partner = GetEntry(partnerWatchId);
                if (!string.Equals(mask, partner.Registration.TargetMask, StringComparison.Ordinal))
                    throw new InvalidDataException("关联调度通道的目标掩码不一致。");
                authorizedPartner = await RestoreAuthorizedEntryAsync(
                    partner,
                    subject,
                    cancellationToken)
                    .ConfigureAwait(false);
                authorizedEntry = authorizedEntry with { LastMessage = "已重新授权并恢复两个调度通道。" };
            }
            lock (_sync)
            {
                _entries[authorizedEntry.Registration.WatchId] = authorizedEntry;
                _pauseRequested.Remove(authorizedEntry.Registration.WatchId);
                if (authorizedPartner is not null)
                {
                    _entries[authorizedPartner.Registration.WatchId] = authorizedPartner;
                    _pauseRequested.Remove(authorizedPartner.Registration.WatchId);
                }
            }
            return Project(authorizedEntry);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<DualLayerScheduleView> EnableDualLayerAsync(
        Guid sourceWatchId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObservationEntry source = GetEntry(sourceWatchId);
            if (source.Watch is null)
                throw new InvalidOperationException("请先重新授权该店铺观察项。");
            if (source.Status != StoreObservationStatus.Active)
                throw new InvalidOperationException("只有排队中的观察项可以启用双层调度。");
            if (source.Registration.Scope != ObservationScopeChoice.Top1000)
                throw new InvalidOperationException("双层调度的兜底通道必须使用 Top 1000 计划。");
            if (source.Registration.ScheduleLane != ObservationScheduleLane.Standalone)
                throw new InvalidOperationException("该观察项已经属于双层调度。");
            long[] templateIds = source.State?.CurrentObservations
                .Select(item => item.TemplateId)
                .Distinct()
                .Order()
                .ToArray() ?? [];
            if (templateIds.Length == 0)
                throw new InvalidOperationException("该观察项尚未命中饰品，暂时无法冻结重点通道。");

            string suffix = "·重点";
            string sourceNote = source.Registration.SafeNote;
            string priorityNote = sourceNote.Length + suffix.Length <= 40
                ? sourceNote + suffix
                : sourceNote[..(40 - suffix.Length)] + suffix;
            var priorityRequest = new AddStoreObservationRequest(
                source.Watch.Subject,
                priorityNote,
                source.Registration.Purpose,
                ObservationScopeChoice.ObservedOnly,
                DualLayerSchedulePolicy.PriorityIntervalMinutes,
                templateIds);
            YouPinHotCoveragePlan priorityPlan = RequirePlanInputs().CreatePlan(
                ObservationScopeChoice.ObservedOnly,
                templateIds);
            AppSettings prioritySettings = RequireSettings() with
            {
                WatchPurpose = source.Registration.Purpose,
                WatchIntervalMinutes = DualLayerSchedulePolicy.PriorityIntervalMinutes
            };
            using CancellationTokenSource linked = BeginOperation(cancellationToken, sourceWatchId);
            ObservationEntry priority = await CreateEntryAsync(
                priorityRequest,
                priorityNote,
                priorityPlan,
                prioritySettings,
                ObservationScheduleLane.Priority,
                sourceWatchId,
                linked.Token).ConfigureAwait(false);
            StoreObservationRegistration fallbackRegistration = source.Registration with
            {
                IntervalMinutes = DualLayerSchedulePolicy.FallbackIntervalMinutes,
                ScheduleLane = ObservationScheduleLane.Fallback,
                PartnerWatchId = priority.Registration.WatchId
            };
            Guid storeId = source.Registration.StoreId == Guid.Empty ? sourceWatchId : source.Registration.StoreId;
            priority = priority with { Registration = priority.Registration with { StoreId = storeId, Account = source.Registration.Account } };
            fallbackRegistration = fallbackRegistration with { StoreId = storeId };
            YouPinAuthorizedCoverageWatch fallbackWatch = YouPinAuthorizedCoverageWatch.Restore(
                source.Registration.WatchId,
                source.Watch.Subject,
                source.Plan.Fingerprint,
                SettingsFor(fallbackRegistration).ToCorePurpose(),
                TimeSpan.FromMinutes(fallbackRegistration.IntervalMinutes));
            ObservationEntry fallback = source with
            {
                Registration = fallbackRegistration,
                Watch = fallbackWatch,
                LastMessage = "双层调度已启用：Top 1000 每 240 分钟执行一个兜底批次。"
            };
            lock (_sync)
            {
                _entries[sourceWatchId] = fallback;
                _entries.Add(priority.Registration.WatchId, priority);
            }
            try
            {
                await SaveRegistryAsync(linked.Token).ConfigureAwait(false);
            }
            catch
            {
                lock (_sync)
                {
                    _entries[sourceWatchId] = source;
                    _entries.Remove(priority.Registration.WatchId);
                }
                await _operations.DeleteWatchStateAsync(
                    priority.Registration.WatchId,
                    prioritySettings,
                    CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            return new DualLayerScheduleView(Project(priority), Project(fallback));
        }
        finally
        {
            EndOperation();
            _operationGate.Release();
        }
    }

    public async Task<AuditExecutionResult> RunFullAuditAsync(
        YouPinAuditSubject subject,
        YouPinHotCoveragePlan plan,
        IProgress<YouPinHotCoverageProgress> progress,
        CancellationToken cancellationToken,
        CS2TradeMonitor.Application.YouPin.PurchaseMonitoring.PurchaseAccountBinding? account = null)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using CancellationTokenSource linked = BeginOperation(cancellationToken);
            if (account is not null)
                return await _operations.RunAccountAuditAsync(subject, plan, RequireSettings().ReportDirectory,
                    progress, account, linked.Token).ConfigureAwait(false);
            return await _operations.RunFullAuditAsync(
                subject,
                plan,
                RequireSettings().ReportDirectory,
                progress,
                linked.Token).ConfigureAwait(false);
        }
        finally
        {
            EndOperation();
            _operationGate.Release();
        }
    }

    public async Task<MonitoringTickOutcome?> RunNextDueAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObservationEntry? entry = SelectNextDue(DateTimeOffset.UtcNow);
            if (entry is null)
                return null;
            entry = entry with
            {
                Status = StoreObservationStatus.RunningTick,
                LastMessage = "正在执行到期扫描批次。"
            };
            SetEntry(entry);
            using CancellationTokenSource linked = BeginOperation(cancellationToken, entry.Registration.WatchId);
            try
            {
                YouPinTemplateBaseline[] shopBaselines = GetStoreEntries(entry.Registration.WatchId)
                    .SelectMany(item => item.State?.Baselines ?? []).ToArray();
                YouPinCoverageWatchRunResult result = await _operations.RunWatchTickAsync(
                    entry.Watch!,
                    entry.Plan,
                    SettingsFor(entry.Registration),
                    linked.Token).ConfigureAwait(false);
                YouPinCoverageWatchState state = result.State;
                string message = result.Status == YouPinCoverageWatchRunStatus.NotDue
                    ? "尚未到期。"
                    : result.Tick?.IsPartial == true
                        ? $"覆盖不完整：本批成功 {result.Tick.CompletedTemplateIds.Count}/{result.Tick.CompletedTemplateIds.Count + result.Tick.IncompleteTemplateIds.Count} 项；"
                            + $"{result.Tick.IncompleteTemplateIds.Count} 项未完成并保留旧基线。不能据此判断全店无变化，请查看扫描状态。"
                    : result.Tick?.HasChanges == true
                        ? $"发现 {result.Tick.Appeared.Count + result.Tick.CeasedToBeObserved.Count} 项集合变化。"
                        : "本批成功读取的范围内无新增变化；不代表已检查全店求购。";
                bool rejected = result.Tick?.Failures.Any(CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi.YouPinPurchasePageReader.IsAccessFailure) == true;
                if (rejected) message += " 账号登录要求或访问限制，已暂停自动扫描；84101 表示需要登录，请检查所选账号后手动恢复。";
                entry = entry with
                {
                    State = state,
                    Status = rejected ? StoreObservationStatus.Failed : StoreObservationStatus.Active,
                    LastMessage = message
                };
                SetEntry(entry);
                if (result.Tick is null)
                    return null;
                ChangeBatchView changeBatch = await _changeStore.WriteAsync(
                    result.Tick,
                    entry.Registration.SafeNote,
                    RequireSettings().ReportDirectory,
                    linked.Token, StorePurchaseChangeClassifier.Classify(shopBaselines, state.Baselines,
                        result.Tick.CompletedTemplateIds)).ConfigureAwait(false);
                var outcome = new MonitoringTickOutcome(Project(entry), changeBatch, result);
                TickCompleted?.Invoke(this, new MonitoringTickCompletedEventArgs(outcome));
                return outcome;
            }
            catch (OperationCanceledException)
            {
                entry = entry with
                {
                    Status = StoreObservationStatus.Paused,
                    LastMessage = "批次已取消；需要手动继续。"
                };
                SetEntry(entry);
                throw;
            }
            catch (Exception error)
            {
                entry = entry with
                {
                    Status = StoreObservationStatus.Failed,
                    LastMessage = "批次失败，未自动重试：" + error.Message
                };
                SetEntry(entry);
                throw;
            }
        }
        finally
        {
            EndOperation();
            _operationGate.Release();
        }
    }

    public StoreObservationView Pause(Guid watchId)
    {
        CancelActive(watchId);
        ObservationEntry entry = GetEntry(watchId) with
        {
            Status = StoreObservationStatus.Paused,
            LastMessage = "已暂停。"
        };
        SetEntry(entry);
        return Project(entry);
    }

    public StoreObservationView Resume(Guid watchId)
    {
        ObservationEntry entry = GetEntry(watchId);
        if (entry.Watch is null)
            throw new InvalidOperationException("该观察项尚未重新授权。");
        entry = entry with
        {
            Status = StoreObservationStatus.Active,
            LastMessage = "已手动继续。"
        };
        SetEntry(entry);
        return Project(entry);
    }

    public async Task RemoveAsync(Guid watchId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        CancelActive(watchId);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObservationEntry entry = GetEntry(watchId);
            ObservationEntry? originalPartner = entry.Registration.PartnerWatchId is Guid partnerWatchId
                ? GetEntry(partnerWatchId)
                : null;
            ObservationEntry? detachedPartner = originalPartner is null
                ? null
                : originalPartner with
                {
                    Registration = originalPartner.Registration with
                    {
                        ScheduleLane = ObservationScheduleLane.Standalone,
                        PartnerWatchId = null
                    },
                    LastMessage = "关联通道已移除；当前通道继续独立运行。"
                };
            lock (_sync)
            {
                _entries.Remove(watchId);
                if (detachedPartner is not null)
                    _entries[detachedPartner.Registration.WatchId] = detachedPartner;
            }
            try
            {
                await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);
                await _operations.DeleteWatchStateAsync(
                    watchId,
                    SettingsFor(entry.Registration),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception removalError)
            {
                lock (_sync)
                {
                    _entries.Add(watchId, entry);
                    if (originalPartner is not null)
                        _entries[originalPartner.Registration.WatchId] = originalPartner;
                }
                try
                {
                    await SaveRegistryAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException(
                        "移除观察项失败，且注册表回滚也未完成。",
                        removalError,
                        rollbackError);
                }
                throw;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public IReadOnlyList<StoreObservationView> GetViews()
    {
        lock (_sync)
            return _entries.Values.OrderBy(item => item.Registration.CreatedAt).Select(Project).ToArray();
    }

    public Task<IReadOnlyList<ChangeBatchView>> LoadChangeHistoryAsync(
        CancellationToken cancellationToken = default)
        => _changeStore.LoadRecentAsync(RequireSettings().ReportDirectory, maximumCount: 500,
            cancellationToken: cancellationToken, retainLatestFor: GetViews().SelectMany(view =>
                view.Registration.HistoryWatchIds.Append(view.Registration.WatchId)).Distinct().ToArray());

    public void CancelActive()
    {
        lock (_sync)
            _activeCancellation?.Cancel();
    }

    private void CancelActive(Guid watchId)
    {
        lock (_sync)
        {
            if (_activeWatchId == watchId)
                _activeCancellation?.Cancel();
        }
    }

    private ObservationEntry? SelectNextDue(DateTimeOffset now)
    {
        lock (_sync)
        {
            return _entries.Values
                .Where(entry => entry.Status == StoreObservationStatus.Active && entry.Watch is not null)
                .Where(entry => DueAt(entry) <= now)
                .OrderBy(entry => DualLayerSchedulePolicy.DuePriority(entry.Registration.ScheduleLane))
                .ThenBy(DueAt)
                .ThenBy(entry => entry.Registration.CreatedAt)
                .FirstOrDefault();
        }
    }

    private static DateTimeOffset DueAt(ObservationEntry entry)
        => entry.State?.GetNextDueAt(TimeSpan.FromMinutes(entry.Registration.IntervalMinutes))
            ?? DateTimeOffset.MinValue;

    private StoreObservationView Project(ObservationEntry entry)
    {
        YouPinCoverageWatchState? state = entry.State;
        DateTimeOffset? oldest = state?.Baselines.Count > 0
            ? state.Baselines.Min(baseline => baseline.ObservedAt)
            : null;
        return new StoreObservationView(
            entry.Registration,
            entry.Status,
            entry.Registration.TargetMask,
            state?.LastTickAt,
            entry.Watch is null || entry.Status is StoreObservationStatus.Paused or StoreObservationStatus.Failed
                ? null
                : DueAt(entry),
            (state?.NextCursor ?? 0) + 1,
            entry.Plan.BatchCount,
            entry.Plan.CandidateCount,
            state?.CompletedCoverageCycles ?? 0,
            state?.BaselineTemplateCount ?? 0,
            state?.CurrentObservations.Count ?? 0,
            oldest,
            entry.LastMessage)
        {
            Baselines = state?.Baselines ?? []
        };
    }

    private static YouPinHotCoveragePlan CreatePlan(
        CoveragePlanInputs inputs,
        StoreObservationRegistration registration)
        => registration.Scope == ObservationScopeChoice.ObservedOnly
            ? inputs.CreatePlan(registration.Scope, registration.SelectedTemplateIds)
            : inputs.CreatePlan(registration.Scope);

    private AppSettings SettingsFor(StoreObservationRegistration registration)
        => RequireSettings() with
        {
            WatchPurpose = registration.Purpose,
            WatchIntervalMinutes = registration.IntervalMinutes,
            Account = registration.Account
        };

    private async Task SaveRegistryAsync(CancellationToken cancellationToken)
    {
        StoreObservationRegistration[] registrations;
        lock (_sync)
            registrations = _entries.Values.Select(item => item.Registration).ToArray();
        await _registryStore.SaveAsync(registrations, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ObservationEntry> CreateEntryAsync(
        AddStoreObservationRequest request,
        string note,
        YouPinHotCoveragePlan plan,
        AppSettings settings,
        ObservationScheduleLane scheduleLane,
        Guid? partnerWatchId,
        CancellationToken cancellationToken)
    {
        (YouPinAuthorizedCoverageWatch watch, YouPinCoverageWatchState state) =
            await _operations.CreateWatchAsync(
                request.Subject,
                plan,
                settings,
                cancellationToken).ConfigureAwait(false);
        var registration = new StoreObservationRegistration(
            watch.Id,
            note,
            MaskUserId(request.Subject.UserId),
            request.Purpose,
            request.Scope,
            request.IntervalMinutes,
            plan.Fingerprint,
            DateTimeOffset.UtcNow)
        {
            Account = request.Account,
            SelectedTemplateIds = request.SelectedTemplateIds?.Distinct().Order().ToArray() ?? [],
            ScheduleLane = scheduleLane,
            PartnerWatchId = partnerWatchId,
            StoreId = watch.Id
        }.Validate();
        return new ObservationEntry(
            registration,
            plan,
            state,
            watch,
            StoreObservationStatus.Active,
            scheduleLane == ObservationScheduleLane.Priority
                ? "重点通道已建立：每 10 分钟执行一个严格串行批次。"
                : "已添加，首次成功读取只建立基线。");
    }

    private async Task<ObservationEntry> RestoreAuthorizedEntryAsync(
        ObservationEntry entry,
        YouPinAuditSubject subject,
        CancellationToken cancellationToken)
    {
        AppSettings settings = SettingsFor(entry.Registration);
        YouPinCoverageWatchState state = entry.State
            ?? await _operations.LoadWatchStateAsync(
                entry.Registration.WatchId,
                settings,
                cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("找不到该观察项的 Coverage Watch 状态。");
        YouPinAuthorizedCoverageWatch watch = YouPinAuthorizedCoverageWatch.Restore(
            entry.Registration.WatchId,
            subject,
            entry.Plan.Fingerprint,
            settings.ToCorePurpose(),
            TimeSpan.FromMinutes(entry.Registration.IntervalMinutes));
        return entry with
        {
            State = state,
            Watch = watch,
            Status = StoreObservationStatus.Active,
            LastMessage = "已重新授权并恢复轮转。"
        };
    }

    private ObservationEntry GetEntry(Guid watchId)
    {
        lock (_sync)
            return _entries.TryGetValue(watchId, out ObservationEntry? entry)
                ? entry
                : throw new KeyNotFoundException("找不到指定店铺观察项。");
    }

    private void SetEntry(ObservationEntry entry)
    {
        lock (_sync)
        {
            if (_pauseRequested.Contains(entry.Registration.WatchId) && entry.Status == StoreObservationStatus.Active)
                entry = entry with { Status = StoreObservationStatus.Paused, LastMessage = "店铺观察已暂停。" };
            _entries[entry.Registration.WatchId] = entry;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private CancellationTokenSource BeginOperation(
        CancellationToken cancellationToken,
        Guid? watchId = null)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_sync)
        {
            _activeCancellation = linked;
            _activeWatchId = watchId;
            if (watchId is Guid id && _pauseRequested.Contains(id)) linked.Cancel();
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
        return linked;
    }

    private void EndOperation()
    {
        lock (_sync)
        {
            _activeCancellation = null;
            _activeWatchId = null;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private AppSettings RequireSettings()
    {
        lock (_sync)
            return _settings ?? throw new InvalidOperationException("Monitoring Session 尚未初始化。");
    }

    private CoveragePlanInputs RequirePlanInputs()
    {
        lock (_sync)
            return _planInputs ?? throw new InvalidOperationException("Monitoring Session 尚未载入覆盖计划。");
    }

    private static string MaskUserId(long userId)
    {
        string value = userId.ToString(CultureInfo.InvariantCulture);
        return value.Length <= 4 ? "***" : "***" + value[^4..];
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        CancelActive();
        // An active async operation still releases this gate in its finally block after cancellation.
        // SemaphoreSlim allocates no native handle here, so leaving it undisposed avoids an exit race.
    }

    private sealed record ObservationEntry(
        StoreObservationRegistration Registration,
        YouPinHotCoveragePlan Plan,
        YouPinCoverageWatchState? State,
        YouPinAuthorizedCoverageWatch? Watch,
        StoreObservationStatus Status,
        string? LastMessage);
}
