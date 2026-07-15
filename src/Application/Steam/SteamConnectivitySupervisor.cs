using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.Steam;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Application.Steam
{
    public sealed class SteamConnectivitySupervisor : IDisposable
    {
        private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(3);
        private readonly object _gate = new();
        private readonly ISteamConnectionResolver _resolver;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private CancellationTokenSource? _cancellation;
        private Task _recoveryTask = Task.CompletedTask;
        private bool _started;

        public SteamConnectivitySupervisor(ISteamConnectionResolver resolver)
            : this(resolver, Task.Delay)
        {
        }

        internal SteamConnectivitySupervisor(
            ISteamConnectionResolver resolver,
            Func<TimeSpan, CancellationToken, Task> delay)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        }

        public void Start()
        {
            lock (_gate)
            {
                if (_started)
                    return;
                _started = true;
                _resolver.StatusChanged += OnStatusChanged;
                if (!IsHealthy(_resolver.GetSnapshot()))
                    StartRecoveryNoLock();
            }
        }

        public async Task StopAsync()
        {
            Task recoveryTask;
            lock (_gate)
            {
                if (!_started)
                    return;
                _started = false;
                _resolver.StatusChanged -= OnStatusChanged;
                _cancellation?.Cancel();
                recoveryTask = _recoveryTask;
            }

            try
            {
                await recoveryTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // StopAsync or a healthy status canceled the fixed retry loop.
            }
        }

        internal async Task WaitForCurrentAttemptAsync()
        {
            Task task;
            lock (_gate)
                task = _recoveryTask;
            await task.ConfigureAwait(false);
        }

        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
            lock (_gate)
            {
                _cancellation?.Dispose();
                _cancellation = null;
            }
        }

        private void OnStatusChanged()
        {
            lock (_gate)
            {
                if (!_started)
                    return;
                if (IsHealthy(_resolver.GetSnapshot()))
                {
                    _cancellation?.Cancel();
                    return;
                }
                StartRecoveryNoLock();
            }
        }

        private void StartRecoveryNoLock()
        {
            if (_recoveryTask is { IsCompleted: false })
                return;

            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            _recoveryTask = Task.Run(() => RecoverUntilHealthyAsync(_cancellation.Token));
        }

        private async Task RecoverUntilHealthyAsync(CancellationToken cancellationToken)
        {
            bool firstAttempt = true;
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!firstAttempt)
                {
                    try
                    {
                        await _delay(RetryInterval, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                }
                firstAttempt = false;

                SteamConnectionProfile profile;
                try
                {
                    profile = await _resolver.ResolveAsync(force: true, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Ignored("SteamConnection", "AutomaticRecovery", ex, retryable: true, category: "Network");
                    continue;
                }

                if (IsHealthy(profile))
                    return;
            }
        }

        private static bool IsHealthy(SteamConnectionProfile profile)
        {
            return profile.IsUsable
                && profile.FailureCount == 0
                && DateTime.Now >= profile.CooldownUntil
                && profile.LastSuccessAt != DateTime.MinValue;
        }
    }
}
