using CS2TradeMonitor.Application.Abstractions;

namespace CS2TradeMonitor.src.SystemServices
{
    internal sealed class SoftwareUpdateAutoCheckCoordinator : IDisposable
    {
        public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);
        public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(3);

        private readonly object _sync = new();
        private readonly ISoftwareUpdateService _softwareUpdates;
        private readonly Func<SoftwareUpdateCheckResult, Task> _notifyAvailableUpdate;
        private readonly TimeSpan _startupDelay;
        private readonly TimeSpan _checkInterval;
        private readonly List<CancellationTokenSource> _retiredCancellations = new();
        private global::System.Threading.Timer? _timer;
        private CancellationTokenSource? _enabledCancellation;
        private string? _lastNotifiedVersion;
        private int _checking;
        private bool _enabled;
        private bool _disposed;

        public SoftwareUpdateAutoCheckCoordinator(
            ISoftwareUpdateService softwareUpdates,
            Func<SoftwareUpdateCheckResult, Task> notifyAvailableUpdate,
            TimeSpan? startupDelay = null,
            TimeSpan? checkInterval = null)
        {
            _softwareUpdates = softwareUpdates ?? throw new ArgumentNullException(nameof(softwareUpdates));
            _notifyAvailableUpdate = notifyAvailableUpdate ?? throw new ArgumentNullException(nameof(notifyAvailableUpdate));
            _startupDelay = startupDelay ?? StartupDelay;
            _checkInterval = checkInterval ?? CheckInterval;
            if (_startupDelay < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(startupDelay));
            if (_checkInterval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(checkInterval));
        }

        public bool Enabled
        {
            get
            {
                lock (_sync)
                    return _enabled;
            }
        }

        public void Configure(bool enabled)
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_enabled == enabled)
                    return;

                _enabled = enabled;
                if (enabled)
                {
                    _enabledCancellation = new CancellationTokenSource();
                    _timer = new global::System.Threading.Timer(
                        _ => _ = RunScheduledCheckAsync(),
                        null,
                        _startupDelay,
                        _checkInterval);
                }
                else
                {
                    StopLocked();
                }
            }

            DiagnosticsLogger.InfoEvent(
                "SoftwareUpdate",
                enabled ? "AutomaticUpdateChecksEnabled" : "AutomaticUpdateChecksDisabled",
                enabled
                    ? "Automatic software update checks enabled."
                    : "Automatic software update checks disabled.",
                new Dictionary<string, object?>
                {
                    ["enabled"] = enabled,
                    ["startupDelaySeconds"] = _startupDelay.TotalSeconds,
                    ["intervalHours"] = _checkInterval.TotalHours
                });
        }

        internal async Task RunScheduledCheckAsync()
        {
            CancellationToken cancellationToken;
            lock (_sync)
            {
                if (_disposed || !_enabled || _enabledCancellation is null)
                    return;
                cancellationToken = _enabledCancellation.Token;
            }

            if (Interlocked.CompareExchange(ref _checking, 1, 0) != 0)
                return;

            try
            {
                SoftwareUpdateCheckResult result = await _softwareUpdates.CheckAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested || !result.HasUpdate)
                    return;

                string version = result.Manifest?.Version?.Trim() ?? string.Empty;
                lock (_sync)
                {
                    if (!_enabled
                        || _disposed
                        || string.Equals(_lastNotifiedVersion, version, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }

                await _notifyAvailableUpdate(result).ConfigureAwait(false);
                lock (_sync)
                {
                    if (!_disposed)
                        _lastNotifiedVersion = version;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Turning the feature off or closing the application cancels the current automatic check.
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.ErrorEvent(
                    "SoftwareUpdate",
                    "AutomaticUpdateCheckFailed",
                    "Automatic software update check failed.",
                    new Dictionary<string, object?>
                    {
                        ["exceptionType"] = ex.GetType().FullName ?? ex.GetType().Name,
                        ["hresult"] = ex.HResult
                    },
                    ex);
            }
            finally
            {
                Interlocked.Exchange(ref _checking, 0);
                lock (_sync)
                    DisposeRetiredCancellationsLocked();
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _enabled = false;
                StopLocked();
            }
        }

        private void StopLocked()
        {
            _timer?.Dispose();
            _timer = null;
            if (_enabledCancellation is not null)
            {
                _enabledCancellation.Cancel();
                _retiredCancellations.Add(_enabledCancellation);
            }
            _enabledCancellation = null;
            if (Volatile.Read(ref _checking) == 0)
                DisposeRetiredCancellationsLocked();
        }

        private void DisposeRetiredCancellationsLocked()
        {
            foreach (CancellationTokenSource cancellation in _retiredCancellations)
                cancellation.Dispose();
            _retiredCancellations.Clear();
        }
    }
}
