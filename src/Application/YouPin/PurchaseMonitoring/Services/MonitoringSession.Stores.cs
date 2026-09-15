using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Reports;
using YouPinPurchaseMonitor.Models;

namespace YouPinPurchaseMonitor.Services;

public sealed partial class MonitoringSession
{
    private readonly HashSet<Guid> _pauseRequested = [];

    public void PauseStore(Guid watchId)
    {
        ObservationEntry[] entries = GetStoreEntries(watchId);
        lock (_sync)
        {
            foreach (ObservationEntry entry in entries)
            {
                Guid id = entry.Registration.WatchId;
                _pauseRequested.Add(id);
                _entries[id] = entry with
                {
                    Status = entry.Watch is null
                    ? StoreObservationStatus.PendingReauthorization : StoreObservationStatus.Paused,
                    LastMessage = "店铺观察已暂停。"
                };
                if (_activeWatchId == id) _activeCancellation?.Cancel();
            }
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResumeStore(Guid watchId)
    {
        ObservationEntry[] entries = GetStoreEntries(watchId);
        lock (_sync)
        {
            if (entries.Any(entry => entry.Watch is null))
                throw new InvalidOperationException("请先重新粘贴该店铺链接。");
            if (entries.Any(entry => entry.Registration.WatchId == _activeWatchId))
                throw new InvalidOperationException("正在停止该店铺的批次，请稍后继续。");
            foreach (ObservationEntry entry in entries)
            {
                _pauseRequested.Remove(entry.Registration.WatchId);
                _entries[entry.Registration.WatchId] = entry with
                {
                    Status = StoreObservationStatus.Active,
                    LastMessage = "店铺观察已继续，按原计划等待到期。"
                };
            }
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RemoveStoreAsync(Guid watchId, CancellationToken cancellationToken = default)
    {
        PauseStore(watchId);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ReplaceStoreEntriesAsync(GetStoreEntries(watchId), [], cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task UpdateStoreAsync(Guid watchId, string safeNote, ObservationScopeChoice scope,
        int intervalMinutes, YouPinAuditSubject? subject = null, CancellationToken cancellationToken = default,
        CS2TradeMonitor.Application.YouPin.PurchaseMonitoring.PurchaseAccountBinding? account = null)
    {
        string note = SafeNoteValidator.Validate(safeNote);
        if (intervalMinutes is < 10 or > 1440)
            throw new ArgumentOutOfRangeException(nameof(intervalMinutes), "批次间隔必须为10到1440分钟。");
        ObservationEntry[] beforeChange = GetStoreEntries(watchId);
        bool accountChanged = account is not null && beforeChange.Any(entry => entry.Registration.Account != account);
        bool continueAfterAccountChange = accountChanged && beforeChange.Any(entry =>
            entry.Status is StoreObservationStatus.Active or StoreObservationStatus.RunningTick);
        if (accountChanged)
            foreach (ObservationEntry entry in beforeChange) CancelActive(entry.Registration.WatchId);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObservationEntry[] previous = GetStoreEntries(watchId);
            ObservationEntry root = previous.First(entry => entry.Registration.ScheduleLane != ObservationScheduleLane.Priority);
            Guid storeId = root.Registration.StoreId == Guid.Empty ? root.Registration.WatchId : root.Registration.StoreId;
            if (subject is not null && (MaskUserId(subject.UserId) != root.Registration.TargetMask
                || root.Watch is not null && subject.UserId != root.Watch.Subject.UserId))
                throw new InvalidOperationException("店铺链接与当前观察项不一致。");
            account ??= root.Registration.Account;
            bool samePlan = root.Registration.Scope == scope && account == root.Registration.Account;
            if (samePlan && (previous.Length == 1 || intervalMinutes == root.Registration.IntervalMinutes))
            {
                ObservationEntry[] updated = previous.Select(entry =>
                {
                    StoreObservationRegistration registration = (entry.Registration with
                    {
                        SafeNote = note,
                        StoreId = storeId,
                        IntervalMinutes = entry.Registration.ScheduleLane == ObservationScheduleLane.Priority ? 10 : intervalMinutes
                    }).Validate();
                    YouPinAuditSubject? authorizedSubject = entry.Watch?.Subject ?? subject;
                    YouPinAuthorizedCoverageWatch? watch = authorizedSubject is null ? null : YouPinAuthorizedCoverageWatch.Restore(
                        registration.WatchId, authorizedSubject, entry.Plan.Fingerprint,
                        SettingsFor(registration).ToCorePurpose(), TimeSpan.FromMinutes(registration.IntervalMinutes));
                    return entry with
                    {
                        Registration = registration,
                        Watch = watch,
                        Status = entry.Status == StoreObservationStatus.PendingReauthorization && watch is not null
                            ? StoreObservationStatus.Active : entry.Status,
                        LastMessage = "店铺规则已更新，保留原基线。"
                    };
                }).ToArray();
                await ReplaceStoreEntriesAsync(previous, updated, cancellationToken).ConfigureAwait(false);
                return;
            }

            subject ??= root.Watch?.Subject;
            if (subject is null)
                throw new InvalidOperationException("修改扫描范围或读取账号前，请重新粘贴该店铺链接。");
            if (MaskUserId(subject.UserId) != root.Registration.TargetMask)
                throw new InvalidOperationException("店铺链接与当前观察项不一致。");
            if (root.Watch is not null && subject.UserId != root.Watch.Subject.UserId)
                throw new InvalidOperationException("不能将规则应用到另一家店铺。");
            YouPinHotCoveragePlan plan = RequirePlanInputs().CreatePlan(scope);
            var request = new AddStoreObservationRequest(subject, note, root.Registration.Purpose, scope, intervalMinutes) { Account = account };
            Guid[] historyIds = previous.SelectMany(entry => entry.Registration.HistoryWatchIds
                .Append(entry.Registration.WatchId)).Distinct().ToArray();
            if (historyIds.Length > 1000) throw new InvalidOperationException("店铺规则变更记录已达上限，请保留历史后重新添加店铺。");
            ObservationEntry replacement = await CreateEntryAsync(request, note, plan,
                RequireSettings() with { WatchPurpose = request.Purpose, WatchIntervalMinutes = intervalMinutes },
                ObservationScheduleLane.Standalone, null, cancellationToken).ConfigureAwait(false);
            replacement = replacement with
            {
                Registration = (replacement.Registration with
                {
                    StoreId = storeId,
                    CreatedAt = root.Registration.CreatedAt,
                    HistoryWatchIds = historyIds
                }).Validate(),
                Status = !continueAfterAccountChange && root.Status == StoreObservationStatus.Paused
                    ? StoreObservationStatus.Paused : StoreObservationStatus.Active,
                LastMessage = "扫描范围或账号已更新；首次成功读取只建立基线，历史动态仍保留。"
            };
            try
            {
                await ReplaceStoreEntriesAsync(previous, [replacement], cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await _operations.DeleteWatchStateAsync(replacement.Registration.WatchId,
                    SettingsFor(replacement.Registration), CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _operationGate.Release();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private ObservationEntry[] GetStoreEntries(Guid watchId)
    {
        lock (_sync)
        {
            ObservationEntry entry = GetEntry(watchId);
            return entry.Registration.PartnerWatchId is Guid partnerId
                ? [entry, GetEntry(partnerId)] : [entry];
        }
    }

    // Registry and baseline changes have a compensating rollback; a failed write never publishes
    // a half-updated shop or leaves one scheduling lane orphaned in the UI.
    private async Task ReplaceStoreEntriesAsync(ObservationEntry[] previous, ObservationEntry[] replacements,
        CancellationToken cancellationToken)
    {
        HashSet<Guid> oldIds = previous.Select(entry => entry.Registration.WatchId).ToHashSet();
        HashSet<Guid> newIds = replacements.Select(entry => entry.Registration.WatchId).ToHashSet();
        StoreObservationRegistration[] before;
        lock (_sync) before = _entries.Values.Select(entry => entry.Registration).ToArray();
        StoreObservationRegistration[] after = before.Where(item => !oldIds.Contains(item.WatchId))
            .Concat(replacements.Select(entry => entry.Registration)).ToArray();
        await _registryStore.SaveAsync(after, cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (ObservationEntry entry in previous.Where(entry => !newIds.Contains(entry.Registration.WatchId)))
                await _operations.DeleteWatchStateAsync(entry.Registration.WatchId, SettingsFor(entry.Registration), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            foreach (ObservationEntry entry in previous.Where(entry => entry.State is not null))
                await _operations.SaveWatchStateAsync(entry.State!, SettingsFor(entry.Registration), CancellationToken.None).ConfigureAwait(false);
            await _registryStore.SaveAsync(before, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        lock (_sync)
        {
            bool pauseRequested = oldIds.Any(id => _pauseRequested.Contains(id));
            foreach (Guid id in oldIds)
            {
                _entries.Remove(id);
                _pauseRequested.Remove(id);
            }
            foreach (ObservationEntry entry in replacements)
            {
                ObservationEntry current = pauseRequested ? entry with { Status = StoreObservationStatus.Paused } : entry;
                _entries[entry.Registration.WatchId] = current;
                if (current.Status == StoreObservationStatus.Paused) _pauseRequested.Add(entry.Registration.WatchId);
            }
        }
    }
}
