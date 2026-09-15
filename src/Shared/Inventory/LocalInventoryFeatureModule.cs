using System.Text.Json;
using CS2TradeMonitor.Application.Inventory;
using CS2TradeMonitor.Domain.InventoryMonitoring;
using CS2TradeMonitor.Shared.Configuration;
using CS2TradeMonitor.Shared.Contracts;
using CS2TradeMonitor.Shared.Core;
using CS2TradeMonitor.Shared.Ports;

namespace CS2TradeMonitor.Shared.Inventory;

/// <summary>
/// Shared orchestration for the desktop local-inventory monitor. The platform
/// supplies transport, durable storage, notifications and time only.
/// </summary>
public sealed class LocalInventoryFeatureModule : ITradeMonitorCoreModule
{
    public const string QueryBinding = "LocalInventoryMonitor";
    public const string RefreshBinding = "RefreshLocalInventoryMonitor";
    public const string SaveListAndRefreshBinding = "SaveInventoryWatchListAndRefresh";
    public const string SaveSettingsBinding = "SaveLocalInventorySettings";
    public const string StatePartitionKey = "local-inventory-monitor.v1";

    private static readonly TimeSpan FailureRetryInterval = TimeSpan.FromMinutes(1);
    private static readonly JsonSerializerOptions JsonOptions = new();
    private readonly ISettingsSnapshotStore _settings;
    private readonly ICsqaqInventoryGateway _gateway;
    private readonly ITradeStateStore _stateStore;
    private readonly IUserNotificationSink _notifications;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private LocalInventoryMonitorProcessor _processor = new(new LocalInventoryMonitorHistory());
    private long _stateVersion;
    private bool _initialized;
    private DateTimeOffset? _lastAttemptAt;
    private DateTimeOffset? _lastRefreshAt;
    private string _status = "未启动";
    private string _error = string.Empty;

    public LocalInventoryFeatureModule(
        ISettingsSnapshotStore settings,
        ICsqaqInventoryGateway gateway,
        ITradeStateStore stateStore,
        IUserNotificationSink notifications,
        IClock clock)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public bool CanHandle(string bindingName)
        => bindingName is QueryBinding or RefreshBinding or SaveListAndRefreshBinding or SaveSettingsBinding;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;
            TradeStateDocument? document = await _stateStore.LoadAsync(StatePartitionKey, cancellationToken)
                .ConfigureAwait(false);
            if (document is not null)
            {
                try
                {
                    LocalInventoryMonitorHistory? history = JsonSerializer.Deserialize<LocalInventoryMonitorHistory>(
                        document.Json,
                        JsonOptions);
                    _processor = new LocalInventoryMonitorProcessor(history ?? new LocalInventoryMonitorHistory());
                    _stateVersion = document.Version;
                }
                catch (JsonException)
                {
                    _processor = new LocalInventoryMonitorProcessor(new LocalInventoryMonitorHistory());
                    _stateVersion = document.Version;
                }
            }
            _initialized = true;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task<FeatureStateProjection> QueryAsync(
        FeatureStateQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        SettingsSnapshot settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        LocalInventoryFeatureProjection projection;
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LocalInventoryMonitorSnapshot snapshot = _processor.CreateSnapshot(
                settings.Settings.LocalInventoryMonitorEnabled,
                settings.Settings.LocalInventoryMonitorEnabled ? _status : "未启用",
                settings.Settings.LocalInventoryMonitorEnabled ? _error : string.Empty,
                _lastRefreshAt,
                settings.Settings.LocalInventoryWatchList);
            projection = Project(settings.Settings, snapshot);
        }
        finally
        {
            _stateGate.Release();
        }

        return new FeatureStateProjection(
            query.SemanticId,
            FeatureAvailability.Available,
            "ok",
            BuildStateMessage(projection),
            JsonSerializer.Serialize(projection, JsonOptions),
            Math.Max(settings.Version, _stateVersion),
            _clock.UtcNow);
    }

    public async Task<CoreCommandResult> ExecuteAsync(
        FeatureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        return command.BindingName switch
        {
            RefreshBinding => await RefreshAsync(force: true, command.CorrelationId, cancellationToken).ConfigureAwait(false),
            SaveListAndRefreshBinding => await SaveListAndRefreshAsync(command, cancellationToken).ConfigureAwait(false),
            SaveSettingsBinding => await SaveSettingsAsync(command, cancellationToken).ConfigureAwait(false),
            _ => CoreCommandResult.Disabled(
                "local-inventory.command-unavailable",
                "该库存监控命令未注册。",
                command.CorrelationId)
        };
    }

    public async Task<AutomationCycleResult?> RunCycleAsync(
        AutomationCycleTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        CoreCommandResult result = await RefreshAsync(
            force: trigger.Kind == AutomationCycleTriggerKind.UserRequested,
            trigger.CorrelationId,
            cancellationToken).ConfigureAwait(false);
        AutomationCycleStatus status = result.Status switch
        {
            CoreCommandStatus.Success => AutomationCycleStatus.Completed,
            CoreCommandStatus.Failed => AutomationCycleStatus.Failed,
            CoreCommandStatus.Pending => AutomationCycleStatus.Pending,
            _ => AutomationCycleStatus.Skipped
        };
        return new AutomationCycleResult(
            status,
            result.ReasonCode,
            result.Message,
            trigger.CorrelationId,
            result.SnapshotVersion,
            status == AutomationCycleStatus.Completed ? 1 : 0);
    }

    private async Task<CoreCommandResult> SaveSettingsAsync(
        FeatureCommand command,
        CancellationToken cancellationToken)
    {
        SaveLocalInventorySettingsCommand? payload = Deserialize<SaveLocalInventorySettingsCommand>(command.PayloadJson);
        if (payload is null)
        {
            return CoreCommandResult.NeedsUserAction(
                "local-inventory.settings-required",
                "请提供库存监控设置。",
                command.CorrelationId);
        }

        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        Settings next = snapshot.Settings.DeepClone();
        next.LocalInventoryMonitorEnabled = payload.Enabled;
        next.LocalInventoryRefreshMinutes = Math.Clamp(payload.RefreshMinutes <= 0 ? 5 : payload.RefreshMinutes, 5, 1440);
        next.LocalInventoryMinimumChangeCount = Math.Clamp(payload.MinimumChangeCount <= 0 ? 1 : payload.MinimumChangeCount, 1, 100000);
        next.LocalInventoryPhoneAlertEnabled = payload.PhoneAlertEnabled;
        SettingsSnapshot saved = await _settings.SaveAsync(next, snapshot.Version, cancellationToken).ConfigureAwait(false);

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!next.LocalInventoryMonitorEnabled)
            {
                _status = "未启用";
                _error = string.Empty;
            }
            else if (_status is "未启动" or "未启用")
            {
                _status = "等待首次检查";
            }
        }
        finally
        {
            _stateGate.Release();
        }

        return CoreCommandResult.Success("库存监控设置已保存。", command.CorrelationId, saved.Version);
    }

    private async Task<CoreCommandResult> SaveListAndRefreshAsync(
        FeatureCommand command,
        CancellationToken cancellationToken)
    {
        SaveInventoryWatchListCommand? payload = Deserialize<SaveInventoryWatchListCommand>(command.PayloadJson);
        if (payload is null)
        {
            return CoreCommandResult.NeedsUserAction(
                "local-inventory.watch-list-required",
                "请填写大商观察名单。",
                command.CorrelationId);
        }

        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        Settings next = snapshot.Settings.DeepClone();
        next.LocalInventoryWatchList = LocalInventoryWatchListParser.Normalize(payload.WatchList);
        await _settings.SaveAsync(next, snapshot.Version, cancellationToken).ConfigureAwait(false);
        return await RefreshAsync(force: true, command.CorrelationId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CoreCommandResult> RefreshAsync(
        bool force,
        string correlationId,
        CancellationToken cancellationToken)
    {
        SettingsSnapshot settingsSnapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        Settings settings = settingsSnapshot.Settings;
        if (!force && !settings.LocalInventoryMonitorEnabled)
        {
            return CoreCommandResult.Skipped(
                "local-inventory.disabled",
                "库存监控未启用。",
                correlationId,
                Math.Max(settingsSnapshot.Version, _stateVersion));
        }
        if (!force && !await IsDueAsync(settings, cancellationToken).ConfigureAwait(false))
        {
            return CoreCommandResult.Skipped(
                "local-inventory.not-due",
                "库存监控尚未到检查时间。",
                correlationId,
                Math.Max(settingsSnapshot.Version, _stateVersion));
        }

        DateTimeOffset now = _clock.UtcNow;
        await SetStatusAsync("正在刷新", string.Empty, now, cancellationToken).ConfigureAwait(false);
        string token = (settings.CsqaqApiToken ?? string.Empty).Trim();
        IReadOnlyList<LocalInventoryWatchTargetSpec> specs = LocalInventoryWatchListParser.Parse(
            settings.LocalInventoryWatchList);

        bool pruned;
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            pruned = _processor.PruneRemovedTargets(specs);
        }
        finally
        {
            _stateGate.Release();
        }
        if (pruned)
            await SaveHistoryAsync(cancellationToken).ConfigureAwait(false);

        if (token.Length == 0)
            return await FailAsync("未配置 CSQAQ API Token，请先到“大盘数据源”填写。", now, correlationId, cancellationToken).ConfigureAwait(false);
        if (specs.Count == 0)
            return await FailAsync("观察名单为空，请至少填写一个 SteamID。", now, correlationId, cancellationToken).ConfigureAwait(false);

        var alertEvents = new List<LocalInventoryChangeEvent>();
        var targetErrors = new List<string>();
        foreach (LocalInventoryWatchTargetSpec spec in specs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LocalInventoryStoredTarget stored;
            await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                stored = _processor.GetOrCreateTarget(spec.SteamId);
            }
            finally
            {
                _stateGate.Release();
            }

            try
            {
                CsqaqInventoryTargetRecord? resolved = await _gateway.ResolveTargetAsync(
                    token,
                    spec.SteamId,
                    cancellationToken).ConfigureAwait(false);
                if (resolved is null)
                    throw new InvalidOperationException("CSQAQ 未找到对应库存监控任务。");
                IReadOnlyList<LocalInventoryChangeEvent> events = await _gateway.GetRecentEventsAsync(
                    token,
                    resolved,
                    cancellationToken).ConfigureAwait(false);
                await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    alertEvents.AddRange(_processor.ProcessTargetResult(
                        stored,
                        resolved,
                        events,
                        settings.LocalInventoryMinimumChangeCount,
                        now));
                }
                finally
                {
                    _stateGate.Release();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                string safeError = SafeError(exception.Message);
                await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    stored.LastCheckedAt = now;
                    stored.Status = "检查失败";
                    stored.Error = safeError;
                }
                finally
                {
                    _stateGate.Release();
                }
                targetErrors.Add($"{spec.DisplayName}：{safeError}");
            }
        }

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LocalInventoryMonitorProcessor.Normalize(_processor.History);
            _lastRefreshAt = now;
            _status = targetErrors.Count == 0 ? "监控正常" : $"部分失败（{targetErrors.Count}）";
            _error = targetErrors.Count == 0 ? string.Empty : string.Join("；", targetErrors.Take(3));
        }
        finally
        {
            _stateGate.Release();
        }

        try
        {
            await SaveHistoryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            return await FailAsync(
                "本机库存监控基线保存失败；为避免重启后重复提醒，请检查用户数据目录权限。",
                now,
                correlationId,
                cancellationToken).ConfigureAwait(false);
        }

        if (alertEvents.Count > 0 && !settings.DoNotDisturbEnabled)
        {
            LocalInventoryAlertBatch batch = _processor.CreateAlertBatch(alertEvents, specs);
            await _notifications.PublishAsync(
                new UserNotification(
                    batch.DeduplicationKey,
                    batch.Title,
                    batch.Message,
                    UserNotificationSeverity.Warning,
                    "/more/inventory-monitor"),
                cancellationToken).ConfigureAwait(false);
        }

        string message = targetErrors.Count == 0
            ? $"已检查 {specs.Count} 个观察对象。"
            : $"已检查 {specs.Count} 个观察对象，其中 {targetErrors.Count} 个失败。";
        return CoreCommandResult.Success(
            message,
            correlationId,
            Math.Max(settingsSnapshot.Version, _stateVersion));
    }

    private async Task<bool> IsDueAsync(Settings settings, CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_lastAttemptAt is null)
                return true;
            TimeSpan interval = string.IsNullOrWhiteSpace(_error)
                ? TimeSpan.FromMinutes(Math.Clamp(settings.LocalInventoryRefreshMinutes, 5, 1440))
                : FailureRetryInterval;
            return _clock.UtcNow - _lastAttemptAt.Value >= interval;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private async Task SetStatusAsync(
        string status,
        string error,
        DateTimeOffset attemptAt,
        CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _lastAttemptAt = attemptAt;
            _status = status;
            _error = error;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private async Task<CoreCommandResult> FailAsync(
        string error,
        DateTimeOffset attemptAt,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _lastAttemptAt = attemptAt;
            _lastRefreshAt = attemptAt;
            _status = "无法监控";
            _error = SafeError(error);
        }
        finally
        {
            _stateGate.Release();
        }
        return CoreCommandResult.Failed(
            "local-inventory.refresh-failed",
            _error,
            correlationId,
            _stateVersion);
    }

    private async Task SaveHistoryAsync(CancellationToken cancellationToken)
    {
        string json;
        long expectedVersion;
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LocalInventoryMonitorProcessor.Normalize(_processor.History);
            json = JsonSerializer.Serialize(_processor.History, JsonOptions);
            expectedVersion = _stateVersion;
        }
        finally
        {
            _stateGate.Release();
        }

        var document = new TradeStateDocument(StatePartitionKey, expectedVersion + 1, json);
        await _stateStore.SaveAsync(document, expectedVersion, cancellationToken).ConfigureAwait(false);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _stateVersion = document.Version;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private LocalInventoryFeatureProjection Project(Settings settings, LocalInventoryMonitorSnapshot snapshot)
        => new(
            settings.LocalInventoryMonitorEnabled,
            Math.Clamp(settings.LocalInventoryRefreshMinutes <= 0 ? 5 : settings.LocalInventoryRefreshMinutes, 5, 1440),
            Math.Clamp(settings.LocalInventoryMinimumChangeCount <= 0 ? 1 : settings.LocalInventoryMinimumChangeCount, 1, 100000),
            settings.LocalInventoryPhoneAlertEnabled,
            !string.IsNullOrWhiteSpace(settings.CsqaqApiToken),
            settings.LocalInventoryWatchList,
            snapshot.Status,
            snapshot.Error,
            snapshot.LastRefreshAt,
            snapshot.Targets,
            snapshot.RecentEvents,
            _clock.UtcNow);

    private static string BuildStateMessage(LocalInventoryFeatureProjection projection)
    {
        if (!projection.Enabled)
            return "库存监控未启用。";
        if (!string.IsNullOrWhiteSpace(projection.Error))
            return projection.Error;
        return $"{projection.Targets.Count} 个观察对象 · {projection.Status}";
    }

    private static T? Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string SafeError(string? message)
    {
        string value = string.IsNullOrWhiteSpace(message) ? "未知错误。" : message.Trim();
        return value.Length <= 400 ? value : value[..400];
    }
}
