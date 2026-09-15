using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Monitoring;
using CS2TradeMonitor.Domain.InventoryMonitoring;
using CS2TradeMonitor.Shared.Inventory;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Application.Inventory
{
    internal sealed class LocalInventoryMonitorService : ILocalInventoryMonitorService
    {
        private static readonly TimeSpan LoopInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan FailureRetryInterval = TimeSpan.FromMinutes(1);

        private readonly ICsqaqInventoryGateway _gateway;
        private readonly IClock _clock;
        private readonly LocalInventoryMonitorStore _store;
        private readonly SemaphoreSlim _refreshGate = new(1, 1);
        private readonly object _stateLock = new();
        private readonly LocalInventoryMonitorProcessor _processor;
        private Settings _settings = new();
        private CancellationTokenSource? _loopCts;
        private Task? _loopTask;
        private DateTimeOffset? _lastAttemptAt;
        private DateTimeOffset? _lastRefreshAt;
        private string _status = "未启动";
        private string _error = string.Empty;
        private bool _disposed;

        internal LocalInventoryMonitorService(
            ICsqaqInventoryGateway gateway,
            IAppDataPathProvider pathProvider,
            IClock clock)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            ArgumentNullException.ThrowIfNull(pathProvider);
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _store = new LocalInventoryMonitorStore(pathProvider.GetDataFilePath("local_inventory_monitor.json"));
            _processor = new LocalInventoryMonitorProcessor(_store.Load());
        }

        public event EventHandler? DataUpdated;

        public void Start(Settings settings)
        {
            ThrowIfDisposed();
            Configure(settings);
            lock (_stateLock)
            {
                if (_loopTask != null)
                    return;

                _loopCts = new CancellationTokenSource();
                _loopTask = Task.Run(() => RunLoopAsync(_loopCts.Token));
                _status = _settings.LocalInventoryMonitorEnabled ? "等待首次检查" : "未启用";
            }

            NotifyDataUpdated();
        }

        public void Configure(Settings settings)
        {
            ThrowIfDisposed();
            lock (_stateLock)
            {
                _settings = settings ?? new Settings();
                if (!_settings.LocalInventoryMonitorEnabled)
                {
                    _status = "未启用";
                    _error = string.Empty;
                }
            }

            NotifyDataUpdated();
        }

        public async Task StopAsync()
        {
            CancellationTokenSource? cts;
            Task? loopTask;
            lock (_stateLock)
            {
                cts = _loopCts;
                loopTask = _loopTask;
                _loopCts = null;
                _loopTask = null;
            }

            if (cts == null)
                return;

            try
            {
                cts.Cancel();
                if (loopTask != null)
                    await loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            finally
            {
                cts.Dispose();
                lock (_stateLock)
                {
                    _status = "已停止";
                }
                NotifyDataUpdated();
            }
        }

        public async Task RefreshAsync(bool force = false, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            Settings settings = GetSettings();
            if (!force && !settings.LocalInventoryMonitorEnabled)
                return;

            if (!force && !IsDue(settings))
                return;

            await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                settings = GetSettings();
                if (!force && (!settings.LocalInventoryMonitorEnabled || !IsDue(settings)))
                    return;

                DateTimeOffset now = Now();
                lock (_stateLock)
                {
                    _lastAttemptAt = now;
                    _status = "正在刷新";
                    _error = string.Empty;
                }
                NotifyDataUpdated();

                string token = (settings.CsqaqApiToken ?? string.Empty).Trim();
                IReadOnlyList<LocalInventoryWatchTargetSpec> specs = LocalInventoryWatchListParser.Parse(settings.LocalInventoryWatchList);
                string pruneError = PruneRemovedTargets(specs);
                if (pruneError.Length > 0)
                {
                    SetFailure(pruneError, now);
                    return;
                }
                if (token.Length == 0)
                {
                    SetFailure("未配置 CSQAQ API Token，请先到“大盘数据源”填写。", now);
                    return;
                }
                if (specs.Count == 0)
                {
                    SetFailure("观察名单为空，请至少填写一个 SteamID。", now);
                    return;
                }

                List<LocalInventoryChangeEvent> alertEvents = new();
                var targetErrors = new List<string>();
                foreach (LocalInventoryWatchTargetSpec spec in specs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    LocalInventoryStoredTarget stored = GetOrCreateTarget(spec.SteamId);
                    try
                    {
                        CsqaqInventoryTargetRecord? resolved = await _gateway.ResolveTargetAsync(
                            token,
                            spec.SteamId,
                            cancellationToken).ConfigureAwait(false);
                        if (resolved == null)
                            throw new InvalidOperationException("CSQAQ 未找到对应库存监控任务。");

                        IReadOnlyList<LocalInventoryChangeEvent> events = await _gateway.GetRecentEventsAsync(
                            token,
                            resolved,
                            cancellationToken).ConfigureAwait(false);
                        ProcessTargetResult(stored, resolved, events, settings, now, alertEvents);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        string safeError = DiagnosticsLogger.Redact(ex.Message);
                        lock (_stateLock)
                        {
                            stored.LastCheckedAt = now;
                            stored.Status = "检查失败";
                            stored.Error = safeError;
                        }
                        targetErrors.Add($"{spec.DisplayName}：{safeError}");
                        DiagnosticsLogger.Error("LocalInventory", $"Inventory target refresh failed. SteamId={spec.SteamId}", ex);
                    }
                }

                lock (_stateLock)
                {
                    LocalInventoryMonitorStore.Normalize(_processor.History);
                    _lastRefreshAt = now;
                    _status = targetErrors.Count == 0 ? "监控正常" : $"部分失败（{targetErrors.Count}）";
                    _error = targetErrors.Count == 0 ? string.Empty : string.Join("；", targetErrors.Take(3));
                    if (!_store.TrySave(_processor.History, out string saveError))
                    {
                        _status = "基线保存失败";
                        _error = saveError;
                    }
                }

                if (alertEvents.Count > 0)
                    PublishAlert(settings, alertEvents);
                NotifyDataUpdated();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                lock (_stateLock)
                {
                    _status = "刷新已取消";
                }
                NotifyDataUpdated();
                throw;
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        public LocalInventoryMonitorSnapshot GetSnapshot()
        {
            lock (_stateLock)
            {
                return _processor.CreateSnapshot(
                    _settings.LocalInventoryMonitorEnabled,
                    _status,
                    _error,
                    _lastRefreshAt,
                    _settings.LocalInventoryWatchList);
            }
        }

        private async Task RunLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await RefreshAsync(force: false, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        DiagnosticsLogger.Error("LocalInventory", "Background inventory refresh failed.", ex);
                        SetFailure("后台刷新失败：" + DiagnosticsLogger.Redact(ex.Message), Now());
                    }

                    await Task.Delay(LoopInterval, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal shutdown.
            }
        }

        private void ProcessTargetResult(
            LocalInventoryStoredTarget stored,
            CsqaqInventoryTargetRecord resolved,
            IReadOnlyList<LocalInventoryChangeEvent> events,
            Settings settings,
            DateTimeOffset now,
            List<LocalInventoryChangeEvent> alertEvents)
        {
            lock (_stateLock)
            {
                alertEvents.AddRange(_processor.ProcessTargetResult(
                    stored,
                    resolved,
                    events,
                    settings.LocalInventoryMinimumChangeCount,
                    now));
            }
        }

        private void PublishAlert(Settings settings, IReadOnlyList<LocalInventoryChangeEvent> events)
        {
            if (settings.DoNotDisturbEnabled)
                return;

            LocalInventoryAlertBatch batch = _processor.CreateAlertBatch(
                events,
                LocalInventoryWatchListParser.Parse(settings.LocalInventoryWatchList));
            AppNotificationHub.Instance.Request(
                batch.Title,
                batch.Message,
                AppNotificationSeverity.Warning,
                AppNotificationPlacement.BottomLeft,
                playSound: false,
                showToast: true,
                source: AlertHistorySources.LocalInventory,
                dedupKey: batch.DeduplicationKey,
                sendToPhone: settings.LocalInventoryPhoneAlertEnabled);
        }

        private bool IsDue(Settings settings)
        {
            DateTimeOffset now = Now();
            lock (_stateLock)
            {
                if (_lastAttemptAt == null)
                    return true;

                TimeSpan interval = string.IsNullOrWhiteSpace(_error)
                    ? TimeSpan.FromMinutes(Math.Clamp(settings.LocalInventoryRefreshMinutes, 5, 1440))
                    : FailureRetryInterval;
                return now - _lastAttemptAt.Value >= interval;
            }
        }

        private Settings GetSettings()
        {
            lock (_stateLock)
            {
                return _settings;
            }
        }

        private LocalInventoryStoredTarget GetOrCreateTarget(string steamId)
        {
            lock (_stateLock)
            {
                return _processor.GetOrCreateTarget(steamId);
            }
        }

        private string PruneRemovedTargets(IReadOnlyList<LocalInventoryWatchTargetSpec> specs)
        {
            lock (_stateLock)
            {
                if (!_processor.PruneRemovedTargets(specs))
                    return string.Empty;

                return _store.TrySave(_processor.History, out string saveError) ? string.Empty : saveError;
            }
        }

        private void SetFailure(string error, DateTimeOffset attemptAt)
        {
            lock (_stateLock)
            {
                _lastAttemptAt = attemptAt;
                _lastRefreshAt = attemptAt;
                _status = "无法监控";
                _error = DiagnosticsLogger.Redact(error);
            }
            NotifyDataUpdated();
        }

        private void NotifyDataUpdated()
        {
            EventHandler? handlers = DataUpdated;
            if (handlers == null)
                return;

            foreach (EventHandler handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Ignored("LocalInventory", "NotifyDataUpdated", ex, retryable: true, category: "UI");
                }
            }
        }

        private DateTimeOffset Now()
        {
            DateTime now = _clock.Now;
            return now.Kind == DateTimeKind.Unspecified
                ? new DateTimeOffset(now, TimeZoneInfo.Local.GetUtcOffset(now))
                : new DateTimeOffset(now);
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            try
            {
                StopAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored(ex);
            }
            _refreshGate.Dispose();
        }
    }
}
