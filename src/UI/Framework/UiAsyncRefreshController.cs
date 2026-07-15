#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.src.UI.Framework
{
    public readonly record struct UiRefreshReason(string Key, string Source, bool Immediate = false)
    {
        public static UiRefreshReason Deferred(string key, string source = "")
            => new(key ?? string.Empty, source ?? string.Empty, Immediate: false);

        public static UiRefreshReason Now(string key, string source = "")
            => new(key ?? string.Empty, source ?? string.Empty, Immediate: true);

        public override string ToString()
        {
            if (string.IsNullOrWhiteSpace(Source))
                return Key;

            return Key + ":" + Source;
        }
    }

    public sealed class UiRefreshOptions
    {
        public const int DefaultDebounceMs = 80;

        public string Name { get; init; } = "UiAsyncRefresh";

        public int DebounceMs { get; init; } = DefaultDebounceMs;

        public long BuildWarnThresholdMs { get; init; } = 16;

        public long ApplyWarnThresholdMs { get; init; } = 16;

        public SynchronizationContext? UiContext { get; init; }

        public UiRefreshOptions WithFallbackContext(SynchronizationContext? context)
        {
            if (UiContext is not null || context is null)
                return this;

            return new UiRefreshOptions
            {
                Name = Name,
                DebounceMs = DebounceMs,
                BuildWarnThresholdMs = BuildWarnThresholdMs,
                ApplyWarnThresholdMs = ApplyWarnThresholdMs,
                UiContext = context
            };
        }
    }

    public readonly record struct UiAsyncRefreshStats(
        int Requests,
        int Builds,
        int Applies,
        int Coalesced,
        int Canceled,
        int Discarded,
        int Faults);

    internal interface IUiAsyncRefreshController : IDisposable
    {
        void CancelPending();
    }

    public sealed class UiAsyncRefreshAppliedEventArgs<TSnapshot> : EventArgs
    {
        public UiAsyncRefreshAppliedEventArgs(TSnapshot snapshot, UiRefreshReason reason, int version)
        {
            Snapshot = snapshot;
            Reason = reason;
            Version = version;
        }

        public TSnapshot Snapshot { get; }

        public UiRefreshReason Reason { get; }

        public int Version { get; }
    }

    public sealed class UiAsyncRefreshController<TSnapshot> : IUiAsyncRefreshController
    {
        private readonly Control _owner;
        private readonly Func<UiRefreshReason, CancellationToken, Task<TSnapshot>> _buildSnapshotAsync;
        private readonly Action<TSnapshot> _applySnapshot;
        private readonly UiRefreshOptions _options;
        private readonly object _gate = new();
        private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
        private readonly CancellationTokenSource _lifetimeCts = new();

        private Task? _worker;
        private CancellationTokenSource? _currentBuildCts;
        private UiRefreshReason _latestReason = UiRefreshReason.Deferred("Initial");
        private int _requestedVersion;
        private int _lastStartedVersion;
        private int _disposed;
        private int _requests;
        private int _builds;
        private int _applies;
        private int _coalesced;
        private int _canceled;
        private int _discarded;
        private int _faults;

        public UiAsyncRefreshController(
            Control owner,
            Func<UiRefreshReason, CancellationToken, Task<TSnapshot>> buildSnapshotAsync,
            Action<TSnapshot> applySnapshot,
            UiRefreshOptions? options = null)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _buildSnapshotAsync = buildSnapshotAsync ?? throw new ArgumentNullException(nameof(buildSnapshotAsync));
            _applySnapshot = applySnapshot ?? throw new ArgumentNullException(nameof(applySnapshot));
            _options = (options ?? new UiRefreshOptions()).WithFallbackContext(SynchronizationContext.Current);
        }

        public event EventHandler<Exception>? Faulted;

        public event EventHandler<UiAsyncRefreshAppliedEventArgs<TSnapshot>>? Applied;

        public UiAsyncRefreshStats Stats => new(
            Volatile.Read(ref _requests),
            Volatile.Read(ref _builds),
            Volatile.Read(ref _applies),
            Volatile.Read(ref _coalesced),
            Volatile.Read(ref _canceled),
            Volatile.Read(ref _discarded),
            Volatile.Read(ref _faults));

        public void Request(UiRefreshReason reason)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            lock (_gate)
            {
                if (_requestedVersion > _lastStartedVersion || _currentBuildCts is not null)
                {
                    Interlocked.Increment(ref _coalesced);
                    UiJankProfiler.Log(
                        _options.Name + ".Coalesce",
                        $"CurrentVersion={_requestedVersion}; Reason={reason}");
                }

                _latestReason = reason;
                _requestedVersion++;
                _currentBuildCts?.Cancel();
                Interlocked.Increment(ref _requests);
                EnsureWorkerLocked();
            }

            ReleaseSignal();
        }

        public void CancelPending()
        {
            lock (_gate)
            {
                _requestedVersion++;
                _lastStartedVersion = _requestedVersion;
                _currentBuildCts?.Cancel();
                _currentBuildCts = null;
                Interlocked.Increment(ref _canceled);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _lifetimeCts.Cancel();
            CancelPending();
            ReleaseSignal();
            try
            {
                _worker?.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Disposal must not throw during page/window teardown.
            }

            _signal.Dispose();
            _lifetimeCts.Dispose();
        }

        private void EnsureWorkerLocked()
        {
            if (_worker is { IsCompleted: false })
                return;

            _worker = Task.Run(RunAsync);
        }

        private async Task RunAsync()
        {
            while (!_lifetimeCts.IsCancellationRequested && Volatile.Read(ref _disposed) == 0)
            {
                try
                {
                    await _signal.WaitAsync(_lifetimeCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                while (TryCaptureNextRequest(out int version, out UiRefreshReason reason))
                {
                    using CancellationTokenSource requestCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
                    lock (_gate)
                    {
                        _currentBuildCts?.Dispose();
                        _currentBuildCts = requestCts;
                    }

                    try
                    {
                        int debounceMs = Math.Max(0, _options.DebounceMs);
                        if (!reason.Immediate && debounceMs > 0)
                        {
                            await Task.Delay(debounceMs, requestCts.Token).ConfigureAwait(false);
                            if (HasNewerRequest(version))
                            {
                                Interlocked.Increment(ref _discarded);
                                continue;
                            }
                        }

                        TSnapshot snapshot;
                        using (UiJankProfiler.Measure(
                            _options.Name + ".Build",
                            $"Reason={reason}; Version={version}",
                            _options.BuildWarnThresholdMs))
                        {
                            snapshot = await _buildSnapshotAsync(reason, requestCts.Token).ConfigureAwait(false);
                        }

                        Interlocked.Increment(ref _builds);
                        if (requestCts.IsCancellationRequested || HasNewerRequest(version))
                        {
                            Interlocked.Increment(ref _discarded);
                            continue;
                        }

                        await ApplyOnUiThreadAsync(snapshot, version, reason, requestCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        Interlocked.Increment(ref _canceled);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref _faults);
                        DiagnosticsLogger.Error("Refresh", $"{_options.Name} failed.", ex);
                        Faulted?.Invoke(this, ex);
                    }
                    finally
                    {
                        lock (_gate)
                        {
                            if (ReferenceEquals(_currentBuildCts, requestCts))
                                _currentBuildCts = null;
                        }
                    }
                }
            }
        }

        private bool TryCaptureNextRequest(out int version, out UiRefreshReason reason)
        {
            lock (_gate)
            {
                if (_lastStartedVersion >= _requestedVersion)
                {
                    version = 0;
                    reason = default;
                    return false;
                }

                _lastStartedVersion = _requestedVersion;
                version = _lastStartedVersion;
                reason = _latestReason;
                return true;
            }
        }

        private bool HasNewerRequest(int version)
        {
            lock (_gate)
            {
                return _requestedVersion != version;
            }
        }

        private Task ApplyOnUiThreadAsync(TSnapshot snapshot, int version, UiRefreshReason reason, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested || !CanApplyToOwner())
                return Task.CompletedTask;

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Apply()
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested || HasNewerRequest(version) || !CanApplyToOwner())
                    {
                        Interlocked.Increment(ref _discarded);
                        tcs.TrySetResult(null);
                        return;
                    }

                    using (UiJankProfiler.Measure(
                        _options.Name + ".Apply",
                        $"Reason={reason}; Version={version}",
                        _options.ApplyWarnThresholdMs))
                    {
                        _applySnapshot(snapshot);
                    }

                    Interlocked.Increment(ref _applies);
                    Applied?.Invoke(this, new UiAsyncRefreshAppliedEventArgs<TSnapshot>(snapshot, reason, version));
                    tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _faults);
                    DiagnosticsLogger.Error("Refresh", $"{_options.Name} apply failed.", ex);
                    Faulted?.Invoke(this, ex);
                    tcs.TrySetResult(null);
                }
            }

            try
            {
                if (_options.UiContext is not null)
                {
                    _options.UiContext.Post(_ => Apply(), null);
                }
                else if (_owner.InvokeRequired && _owner.IsHandleCreated)
                {
                    _owner.BeginInvoke(new Action(Apply));
                }
                else
                {
                    Apply();
                }
            }
            catch (ObjectDisposedException)
            {
                tcs.TrySetResult(null);
            }
            catch (InvalidOperationException)
            {
                tcs.TrySetResult(null);
            }

            return tcs.Task;
        }

        private bool CanApplyToOwner()
        {
            return Volatile.Read(ref _disposed) == 0
                && !_owner.IsDisposed
                && !_owner.Disposing;
        }

        private void ReleaseSignal()
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // 已有刷新信号排队时不重复释放，保持合并语义。
            }
            catch (ObjectDisposedException)
            {
                // 控制器释放后收到延迟信号，按页面已隐藏处理。
            }
        }
    }
}
