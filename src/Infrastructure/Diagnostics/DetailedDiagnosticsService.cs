using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CS2TradeMonitor.Application.Abstractions;

namespace CS2TradeMonitor.Infrastructure.Diagnostics
{
    public sealed class DetailedDiagnosticsService : IDetailedDiagnosticsService, IDisposable
    {
        public const string DiagnosticLogFileName = "CS2TradeMonitor_Diagnostic.jsonl";
        private static readonly JsonSerializerOptions EventJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        private readonly object _sync = new();
        private readonly IClock _clock;
        private readonly DetailedDiagnosticsOptions _options;
        private readonly DetailedDiagnosticsStateStore _stateStore;
        private readonly DetailedDiagnosticsRetentionManager _retention;
        private readonly DetailedDiagnosticsRedactor _redactor;
        private readonly DiagnosticCorrelationService _correlation;
        private readonly BoundedDiagnosticQueue _queue;
        private readonly CancellationTokenSource _writerCancellation = new();
        private readonly Task _writerTask;
        private readonly global::System.Threading.Timer _expiryTimer;
        private readonly string _sessionsRoot;
        private readonly string _softwareVersion;
        private DetailedDiagnosticsPersistedState _state = new();
        private bool _initialized;
        private bool _disposed;
        private string? _lastError;
        private long _quotaDroppedEvents;
        private long _knownTotalBytes = -1;
        private long _reportedQueueDrops;
        private long _persistedQueueDrops;
        private int _enabledSnapshot;

        internal DetailedDiagnosticsService(
            IInstanceRuntimeContext instance,
            IClock clock,
            ISecureDataProtector protector,
            DetailedDiagnosticsOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(instance);
            ArgumentNullException.ThrowIfNull(clock);
            ArgumentNullException.ThrowIfNull(protector);
            _clock = clock;
            _options = options ?? DetailedDiagnosticsOptions.Production;
            DiagnosticsDirectory = Path.Combine(instance.LogsDirectory, "diagnostics");
            _sessionsRoot = Path.Combine(DiagnosticsDirectory, "sessions");
            _stateStore = new DetailedDiagnosticsStateStore(instance.GetDataFile("diagnostics-state.json"));
            _correlation = new DiagnosticCorrelationService(
                instance.GetSecureFile("diagnostics-correlation-key.dat"),
                instance.InstanceHash,
                protector);
            _redactor = new DetailedDiagnosticsRedactor(
                _options.MaximumBodyBytes,
                _correlation,
                instance.InstallRoot,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            _retention = new DetailedDiagnosticsRetentionManager(_sessionsRoot, clock, _options);
            _queue = new BoundedDiagnosticQueue(_options.QueueCapacity);
            _softwareVersion = ResolveSoftwareVersion();
            _expiryTimer = new global::System.Threading.Timer(_ => RefreshLifecycle(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _writerTask = Task.Run(WriterLoopAsync);
        }

        public string DiagnosticsDirectory { get; }

        internal bool IsEnabledFast => Volatile.Read(ref _enabledSnapshot) != 0;

        internal string StateFilePath => _stateStore.StatePath;
        public void Initialize()
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                if (_initialized)
                    return;

                try
                {
                    _state = _stateStore.Load();
                }
                catch (Exception ex)
                {
                    _state = new DetailedDiagnosticsPersistedState();
                    _lastError = "详细诊断状态不可用：" + ex.GetType().Name;
                }

                _initialized = true;
                RefreshLifecycleLocked();
                DetailedDiagnosticsMaintenanceResult maintenance = _retention.Maintain(_state.SessionId);
                ApplyMaintenanceLocked(maintenance);
                _knownTotalBytes = maintenance.TotalBytes;
                if (_state.IsEnabled)
                {
                    _correlation.EnsureAvailable();
                    Volatile.Write(ref _enabledSnapshot, 1);
                    EnqueueLocked(
                        "Information",
                        "Diagnostics",
                        "SessionResumed",
                        new Dictionary<string, object?> { ["expiresAtUtc"] = _state.ExpiresAtUtc },
                        null,
                        DetailedDiagnosticPriority.Critical);
                    ScheduleExpiryLocked();
                }
            }
        }

        public DetailedDiagnosticsStatus GetStatus()
        {
            Initialize();
            lock (_sync)
            {
                RefreshLifecycleLocked();
                DetailedDiagnosticsMaintenanceResult maintenance = _retention.Maintain(_state.SessionId);
                ApplyMaintenanceLocked(maintenance);
                _knownTotalBytes = maintenance.TotalBytes;
                return BuildStatusLocked(maintenance.TotalBytes);
            }
        }

        public DetailedDiagnosticsStatus Enable()
        {
            Initialize();
            lock (_sync)
            {
                ThrowIfDisposed();
                RefreshLifecycleLocked();
                if (_state.IsEnabled)
                    return BuildStatusLocked(GetKnownTotalBytesLocked());

                DateTime now = EnsureUtc(_clock.UtcNow);
                _state = new DetailedDiagnosticsPersistedState
                {
                    IsEnabled = true,
                    SessionId = Guid.NewGuid().ToString("N"),
                    StartedAtUtc = now,
                    ExpiresAtUtc = now + _options.SessionDuration,
                    LastSessionEndedAtUtc = _state.LastSessionEndedAtUtc,
                    LastStopReason = null,
                    DroppedEventCount = _state.DroppedEventCount,
                    CapacityCleanupCount = _state.CapacityCleanupCount
                };
                _lastError = null;
                _correlation.EnsureAvailable();
                Volatile.Write(ref _enabledSnapshot, 1);
                SaveStateLocked();
                EnqueueLocked(
                    "Information",
                    "Diagnostics",
                    "SessionStarted",
                    new Dictionary<string, object?>
                    {
                        ["startedAtUtc"] = _state.StartedAtUtc,
                        ["expiresAtUtc"] = _state.ExpiresAtUtc,
                        ["maximumTotalBytes"] = _options.MaximumTotalBytes,
                        ["maximumBodyBytes"] = _options.MaximumBodyBytes,
                        ["retentionDays"] = _options.EndedSessionRetention.TotalDays
                    },
                    null,
                    DetailedDiagnosticPriority.Critical);
                ScheduleExpiryLocked();
                return BuildStatusLocked(GetKnownTotalBytesLocked());
            }
        }

        public DetailedDiagnosticsStatus Disable()
        {
            Initialize();
            lock (_sync)
            {
                DisableLocked("Manual");
                return BuildStatusLocked(GetKnownTotalBytesLocked());
            }
        }

        internal void Record(
            string level,
            string module,
            string eventName,
            IReadOnlyDictionary<string, object?>? data = null,
            string? correlation = null,
            DetailedDiagnosticPriority priority = DetailedDiagnosticPriority.Normal)
        {
            Initialize();
            lock (_sync)
            {
                RefreshLifecycleLocked();
                if (!_state.IsEnabled)
                    return;
                if (string.Equals(level, "Error", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(level, "Critical", StringComparison.OrdinalIgnoreCase))
                {
                    priority = DetailedDiagnosticPriority.Critical;
                }

                EnqueueLocked(level, module, eventName, data, correlation, priority);
            }
        }

        internal DetailedDiagnosticBodyCapture CaptureBody(string? body, string? contentType)
            => _redactor.CaptureBody(body, contentType);

        internal string? Correlate(string category, string? value)
            => _correlation.Correlate(category, value);

        internal string SanitizeText(string? text) => _redactor.SanitizeText(text);

        internal JsonNode? SanitizeData(IReadOnlyDictionary<string, object?>? data)
            => _redactor.SanitizeEventData(data);

        public async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            Initialize();
            while (!_queue.IsIdle)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
        }

        public void Shutdown()
        {
            if (_disposed)
                return;

            try
            {
                lock (_sync)
                {
                    if (_initialized && _state.IsEnabled)
                    {
                        EnqueueLocked(
                            "Information",
                            "Diagnostics",
                            "ProcessStopping",
                            null,
                            null,
                            DetailedDiagnosticPriority.Critical);
                    }
                    PersistDroppedCountLocked();
                    SaveStateLocked();
                }

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                FlushAsync(timeout.Token).GetAwaiter().GetResult();
            }
            catch
            {
                // Diagnostics shutdown must not delay or replace the application shutdown result.
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            Shutdown();
            _disposed = true;
            _expiryTimer.Dispose();
            _writerCancellation.Cancel();
            try
            {
                _writerTask.Wait(TimeSpan.FromSeconds(1));
            }
            catch
            {
                // Writer cancellation is expected during process shutdown.
            }
            _queue.Dispose();
            _writerCancellation.Dispose();
        }

        internal void RefreshLifecycle()
        {
            if (_disposed)
                return;
            lock (_sync)
            {
                if (!_initialized)
                    return;
                RefreshLifecycleLocked();
            }
        }

        private async Task WriterLoopAsync()
        {
            while (!_writerCancellation.IsCancellationRequested)
            {
                int dequeuedCount = 0;
                try
                {
                    DetailedDiagnosticEnvelope first = await _queue
                        .DequeueAsync(_writerCancellation.Token)
                        .ConfigureAwait(false);
                    dequeuedCount = 1;
                    var batch = new List<DetailedDiagnosticEnvelope>(64) { first };
                    while (batch.Count < 64 && _queue.TryDequeue(out DetailedDiagnosticEnvelope? next))
                    {
                        batch.Add(next!);
                        dequeuedCount++;
                    }

                    long queueDrops = _queue.DroppedCount;
                    if (queueDrops > _reportedQueueDrops)
                    {
                        long newlyDropped = queueDrops - _reportedQueueDrops;
                        _reportedQueueDrops = queueDrops;
                        DetailedDiagnosticEnvelope overload = CreateEnvelope(
                            first.SessionId,
                            "Warning",
                            "Diagnostics",
                            "QueueOverload",
                            new Dictionary<string, object?> { ["droppedEvents"] = newlyDropped },
                            null,
                            DetailedDiagnosticPriority.Critical);
                        WriteEnvelope(overload);
                    }

                    foreach (DetailedDiagnosticEnvelope envelope in batch)
                        WriteEnvelope(envelope);
                }
                catch (OperationCanceledException) when (_writerCancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    lock (_sync)
                        _lastError = "详细诊断写入失败：" + ex.GetType().Name;
                }
                finally
                {
                    _queue.CompleteProcessing(dequeuedCount);
                }
            }
        }

        private void WriteEnvelope(DetailedDiagnosticEnvelope envelope)
        {
            string line = SerializeEnvelope(envelope) + "\n";
            byte[] bytes = Encoding.UTF8.GetBytes(line);
            DetailedDiagnosticsMaintenanceResult maintenance = EnsureCapacity(envelope.SessionId, bytes.LongLength);
            if (!maintenance.HasCapacity)
            {
                Interlocked.Increment(ref _quotaDroppedEvents);
                return;
            }

            string sessionDirectory = Path.Combine(_sessionsRoot, envelope.SessionId);
            Directory.CreateDirectory(sessionDirectory);
            string logPath = Path.Combine(sessionDirectory, DiagnosticLogFileName);
            using (var stream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                stream.Write(bytes);
            }
            Interlocked.Add(ref _knownTotalBytes, bytes.LongLength);

            if (envelope.EndsSession)
            {
                try
                {
                    Directory.SetLastWriteTimeUtc(sessionDirectory, envelope.TimestampUtc);
                }
                catch
                {
                    // Retention can fall back to the filesystem's natural last-write timestamp.
                }
            }
        }

        private DetailedDiagnosticsMaintenanceResult EnsureCapacity(string activeSessionId, long incomingBytes)
        {
            long known = Interlocked.Read(ref _knownTotalBytes);
            if (known >= 0 && known + incomingBytes <= _options.MaximumTotalBytes)
            {
                return new DetailedDiagnosticsMaintenanceResult(known, 0, 0, true);
            }

            string? currentActiveSessionId;
            lock (_sync)
                currentActiveSessionId = _state.IsEnabled ? _state.SessionId : null;
            string[] protectedSessionIds = string.IsNullOrWhiteSpace(currentActiveSessionId)
                || string.Equals(currentActiveSessionId, activeSessionId, StringComparison.OrdinalIgnoreCase)
                ? new[] { activeSessionId }
                : new[] { activeSessionId, currentActiveSessionId };
            DetailedDiagnosticsMaintenanceResult maintenance = _retention.MaintainProtected(
                protectedSessionIds,
                incomingBytes);
            Interlocked.Exchange(ref _knownTotalBytes, maintenance.TotalBytes);
            if (maintenance.DeletedForCapacity > 0)
            {
                lock (_sync)
                {
                    _state.CapacityCleanupCount += maintenance.DeletedForCapacity;
                    SaveStateLocked();
                }
            }
            return maintenance;
        }

        private string SerializeEnvelope(DetailedDiagnosticEnvelope envelope)
        {
            var record = new JsonObject
            {
                ["timestampUtc"] = envelope.TimestampUtc,
                ["level"] = _redactor.SanitizeText(envelope.Level),
                ["module"] = _redactor.SanitizeText(envelope.Module),
                ["event"] = _redactor.SanitizeText(envelope.EventName),
                ["session"] = envelope.SessionId,
                ["correlation"] = string.IsNullOrWhiteSpace(envelope.Correlation)
                    ? null
                    : _redactor.SanitizeText(envelope.Correlation),
                ["version"] = _softwareVersion,
                ["data"] = envelope.Data?.DeepClone()
            };
            return record.ToJsonString(EventJsonOptions);
        }

        private void EnqueueLocked(
            string level,
            string module,
            string eventName,
            IReadOnlyDictionary<string, object?>? data,
            string? correlation,
            DetailedDiagnosticPriority priority,
            bool endsSession = false)
        {
            if (string.IsNullOrWhiteSpace(_state.SessionId))
                return;
            DetailedDiagnosticEnvelope envelope = CreateEnvelope(
                _state.SessionId,
                level,
                module,
                eventName,
                data,
                correlation,
                priority,
                endsSession);
            _queue.TryEnqueue(envelope);
        }

        private DetailedDiagnosticEnvelope CreateEnvelope(
            string sessionId,
            string level,
            string module,
            string eventName,
            IReadOnlyDictionary<string, object?>? data,
            string? correlation,
            DetailedDiagnosticPriority priority,
            bool endsSession = false)
        {
            return new DetailedDiagnosticEnvelope(
                sessionId,
                EnsureUtc(_clock.UtcNow),
                _redactor.SanitizeText(level),
                _redactor.SanitizeText(module),
                _redactor.SanitizeText(eventName),
                _redactor.SanitizeEventData(data),
                string.IsNullOrWhiteSpace(correlation) ? null : _redactor.SanitizeText(correlation),
                priority,
                endsSession);
        }

        private void DisableLocked(string reason)
        {
            RefreshLifecycleLocked(expireOnly: true);
            if (!_state.IsEnabled || string.IsNullOrWhiteSpace(_state.SessionId))
                return;

            PersistDroppedCountLocked();
            EnqueueLocked(
                "Information",
                "Diagnostics",
                "SessionEnded",
                new Dictionary<string, object?>
                {
                    ["reason"] = reason,
                    ["droppedEvents"] = _state.DroppedEventCount
                },
                null,
                DetailedDiagnosticPriority.Critical,
                endsSession: true);
            _state.IsEnabled = false;
            Volatile.Write(ref _enabledSnapshot, 0);
            _state.LastSessionEndedAtUtc = EnsureUtc(_clock.UtcNow);
            _state.LastStopReason = reason;
            _state.SessionId = null;
            _state.StartedAtUtc = null;
            _state.ExpiresAtUtc = null;
            SaveStateLocked();
            _expiryTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        private void RefreshLifecycleLocked(bool expireOnly = false)
        {
            if (!_state.IsEnabled || _state.ExpiresAtUtc is null)
                return;
            if (EnsureUtc(_clock.UtcNow) < EnsureUtc(_state.ExpiresAtUtc.Value))
            {
                if (!expireOnly)
                    ScheduleExpiryLocked();
                return;
            }

            string? sessionId = _state.SessionId;
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                PersistDroppedCountLocked();
                EnqueueLocked(
                    "Information",
                    "Diagnostics",
                    "SessionEnded",
                    new Dictionary<string, object?>
                    {
                        ["reason"] = "Expired",
                        ["droppedEvents"] = _state.DroppedEventCount
                    },
                    null,
                    DetailedDiagnosticPriority.Critical,
                    endsSession: true);
            }
            _state.IsEnabled = false;
            Volatile.Write(ref _enabledSnapshot, 0);
            _state.LastSessionEndedAtUtc = EnsureUtc(_clock.UtcNow);
            _state.LastStopReason = "Expired";
            _state.SessionId = null;
            _state.StartedAtUtc = null;
            _state.ExpiresAtUtc = null;
            SaveStateLocked();
            _expiryTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        private void ScheduleExpiryLocked()
        {
            if (!_state.IsEnabled || _state.ExpiresAtUtc is null)
                return;
            TimeSpan due = EnsureUtc(_state.ExpiresAtUtc.Value) - EnsureUtc(_clock.UtcNow);
            if (due <= TimeSpan.Zero)
                due = TimeSpan.FromMilliseconds(1);
            if (due > TimeSpan.FromDays(1))
                due = TimeSpan.FromDays(1);
            _expiryTimer.Change(due, Timeout.InfiniteTimeSpan);
        }

        private DetailedDiagnosticsStatus BuildStatusLocked(long totalBytes)
        {
            return new DetailedDiagnosticsStatus(
                _state.IsEnabled,
                _state.StartedAtUtc,
                _state.ExpiresAtUtc,
                _state.LastSessionEndedAtUtc,
                _state.IsEnabled && !string.IsNullOrWhiteSpace(_state.SessionId)
                    ? GetSessionLogPath(_state.SessionId)
                    : null,
                Math.Max(0, totalBytes),
                _options.MaximumTotalBytes,
                _state.DroppedEventCount
                    + Math.Max(0, _queue.DroppedCount - _persistedQueueDrops)
                    + Interlocked.Read(ref _quotaDroppedEvents),
                _state.CapacityCleanupCount,
                _state.LastStopReason,
                _lastError ?? _correlation.LastError);
        }

        private void ApplyMaintenanceLocked(DetailedDiagnosticsMaintenanceResult maintenance)
        {
            if (maintenance.DeletedForCapacity <= 0)
                return;
            _state.CapacityCleanupCount += maintenance.DeletedForCapacity;
            SaveStateLocked();
        }

        private void PersistDroppedCountLocked()
        {
            long queueCurrent = _queue.DroppedCount;
            long current = Math.Max(0, queueCurrent - _persistedQueueDrops)
                + Interlocked.Read(ref _quotaDroppedEvents);
            if (current <= 0)
                return;
            _state.DroppedEventCount += current;
            _persistedQueueDrops = queueCurrent;
            _reportedQueueDrops = queueCurrent;
            Interlocked.Exchange(ref _quotaDroppedEvents, 0);
        }

        private void SaveStateLocked()
        {
            try
            {
                _stateStore.Save(_state);
            }
            catch (Exception ex)
            {
                _lastError = "详细诊断状态保存失败：" + ex.GetType().Name;
            }
        }

        private long GetKnownTotalBytesLocked()
        {
            long known = Interlocked.Read(ref _knownTotalBytes);
            if (known >= 0)
                return known;
            known = _retention.GetTotalBytes();
            Interlocked.Exchange(ref _knownTotalBytes, known);
            return known;
        }

        private string GetSessionLogPath(string sessionId)
            => Path.Combine(_sessionsRoot, sessionId, DiagnosticLogFileName);

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        private static DateTime EnsureUtc(DateTime value)
            => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

        private static string ResolveSoftwareVersion()
        {
            return Assembly.GetEntryAssembly()
                ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
                ?.Split('+')[0]
                ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? "unknown";
        }
    }
}
