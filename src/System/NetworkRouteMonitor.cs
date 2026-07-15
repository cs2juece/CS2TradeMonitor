using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.Application.Abstractions;
using System.Net;
using System.Net.NetworkInformation;

namespace CS2TradeMonitor.src.SystemServices
{
    public enum NetworkRouteState
    {
        Unknown,
        Offline,
        DirectReady,
        ProxyWaiting,
        ProxyReady
    }

    public sealed record NetworkRouteSnapshot(
        NetworkRouteState State,
        string Fingerprint,
        string ProxyDisplay)
    {
        public bool IsReady => State is NetworkRouteState.DirectReady or NetworkRouteState.ProxyReady;

        public static NetworkRouteSnapshot Unknown(string fingerprint)
            => new(NetworkRouteState.Unknown, fingerprint ?? "", "");

        public static NetworkRouteSnapshot DirectReady(string fingerprint)
            => new(NetworkRouteState.DirectReady, fingerprint ?? "", "直连");

        public static NetworkRouteSnapshot ProxyWaiting(string fingerprint, string proxyDisplay)
            => new(NetworkRouteState.ProxyWaiting, fingerprint ?? "", proxyDisplay ?? "");

        public static NetworkRouteSnapshot ProxyReady(string fingerprint, string proxyDisplay)
            => new(NetworkRouteState.ProxyReady, fingerprint ?? "", proxyDisplay ?? "");
    }

    internal static class NetworkRouteRecoveryPolicy
    {
        public static bool ShouldRecover(NetworkRouteSnapshot previous, NetworkRouteSnapshot current)
        {
            if (!current.IsReady || previous.State == NetworkRouteState.Unknown)
                return false;
            if (!previous.IsReady)
                return true;
            return !string.Equals(previous.Fingerprint, current.Fingerprint, StringComparison.Ordinal)
                || !string.Equals(previous.ProxyDisplay, current.ProxyDisplay, StringComparison.OrdinalIgnoreCase);
        }
    }

    public interface INetworkRouteMonitor : IDisposable
    {
        event Action<NetworkRouteSnapshot, NetworkRouteSnapshot>? RouteChanged;
        NetworkRouteSnapshot Snapshot { get; }
        void Start();
        void Stop();
    }

    public sealed class NetworkRouteMonitor : INetworkRouteMonitor
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
        private static readonly IWebProxy RouteProxy = AdaptiveSystemProxyFactory.Create();
        private readonly object _gate = new();
        private readonly Func<CancellationToken, Task<NetworkRouteSnapshot>> _capture;
        private System.Threading.Timer? _timer;
        private int _polling;

        public static NetworkRouteMonitor Instance { get; } = new();

        public event Action<NetworkRouteSnapshot, NetworkRouteSnapshot>? RouteChanged;
        public NetworkRouteSnapshot Snapshot { get; private set; } = NetworkRouteSnapshot.Unknown("initial");

        private NetworkRouteMonitor()
            : this(CaptureCurrentAsync)
        {
        }

        internal NetworkRouteMonitor(Func<CancellationToken, Task<NetworkRouteSnapshot>> capture)
        {
            _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        }

        public void Start()
        {
            lock (_gate)
                _timer ??= new System.Threading.Timer(_ => _ = PollOnceAsync(), null, TimeSpan.Zero, PollInterval);
        }

        public void Stop()
        {
            lock (_gate)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }

        internal async Task PollOnceAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _polling, 1, 0) != 0)
                return;

            try
            {
                NetworkRouteSnapshot next = await _capture(cancellationToken).ConfigureAwait(false);
                NetworkRouteSnapshot previous;
                lock (_gate)
                {
                    previous = Snapshot;
                    if (previous == next)
                        return;
                    Snapshot = next;
                }

                PublishRouteChanged(previous, next);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Stop/poll cancellation is an expected lifecycle transition.
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored("NetworkRoute", "Poll", ex, retryable: true, category: "Network");
            }
            finally
            {
                Interlocked.Exchange(ref _polling, 0);
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private void PublishRouteChanged(NetworkRouteSnapshot previous, NetworkRouteSnapshot current)
        {
            Delegate[] subscribers = RouteChanged?.GetInvocationList() ?? Array.Empty<Delegate>();
            foreach (Delegate subscriber in subscribers)
            {
                try
                {
                    ((Action<NetworkRouteSnapshot, NetworkRouteSnapshot>)subscriber)(previous, current);
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Ignored("NetworkRoute", "RouteChangedSubscriber", ex, retryable: false, category: "Observer");
                }
            }
        }

        private static Task<NetworkRouteSnapshot> CaptureCurrentAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fingerprint = SystemProxySettingsFingerprint.Capture();
            if (!NetworkInterface.GetIsNetworkAvailable())
                return Task.FromResult(new NetworkRouteSnapshot(NetworkRouteState.Offline, fingerprint, "离线"));

            Uri destination = new("https://api.csqaq.com/");
            Uri? proxyUri = RouteProxy.GetProxy(destination);
            if (proxyUri == null || proxyUri == destination)
                return Task.FromResult(NetworkRouteSnapshot.DirectReady(fingerprint));

            return Task.FromResult(ClassifyRoute(
                fingerprint,
                proxyUri,
                LoopbackProxyAvailability.IsAvailable));
        }

        internal static NetworkRouteSnapshot ClassifyRoute(
            string fingerprint,
            Uri proxyUri,
            Func<Uri, bool> isProxyAvailable)
        {
            ArgumentNullException.ThrowIfNull(proxyUri);
            ArgumentNullException.ThrowIfNull(isProxyAvailable);
            string display = RedactProxy(proxyUri);
            return isProxyAvailable(proxyUri)
                ? NetworkRouteSnapshot.ProxyReady(fingerprint, display)
                : NetworkRouteSnapshot.ProxyWaiting(fingerprint, display);
        }

        private static string RedactProxy(Uri proxyUri)
        {
            string port = proxyUri.IsDefaultPort ? "" : ":" + proxyUri.Port;
            return proxyUri.Scheme + "://" + proxyUri.Host + port;
        }
    }

    public sealed class NetworkRouteRecoveryCoordinator : IDisposable
    {
        private readonly object _gate = new();
        private readonly INetworkRouteMonitor _monitor;
        private readonly Func<MarketRefreshRequest, Task> _refreshMarket;
        private readonly Func<Task> _refreshSteamRoute;
        private readonly NetworkRecoverySignal? _recoverySignal;
        private readonly Func<bool> _shouldRetryMarket;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly TimeSpan _stabilizationDelay;
        private CancellationTokenSource? _pendingCancellation;
        private Task _pendingTask = Task.CompletedTask;
        private bool _started;

        internal NetworkRouteRecoveryCoordinator(
            INetworkRouteMonitor monitor,
            Func<MarketRefreshRequest, Task> refreshMarket,
            TimeSpan stabilizationDelay,
            Func<Task>? refreshSteamRoute = null,
            NetworkRecoverySignal? recoverySignal = null,
            Func<bool>? shouldRetryMarket = null,
            Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
            _refreshMarket = refreshMarket ?? throw new ArgumentNullException(nameof(refreshMarket));
            _refreshSteamRoute = refreshSteamRoute ?? (() => Task.CompletedTask);
            _recoverySignal = recoverySignal;
            _shouldRetryMarket = shouldRetryMarket ?? (() => false);
            _delay = delay ?? Task.Delay;
            _stabilizationDelay = stabilizationDelay < TimeSpan.Zero ? TimeSpan.Zero : stabilizationDelay;
        }

        public void Start()
        {
            lock (_gate)
            {
                if (_started)
                    return;
                _started = true;
                _monitor.RouteChanged += OnRouteChanged;
                _monitor.Start();
            }
        }

        public void Stop()
        {
            StopAsync().GetAwaiter().GetResult();
        }

        public async Task StopAsync()
        {
            Task pendingTask;
            lock (_gate)
            {
                if (!_started)
                    return;
                _started = false;
                _monitor.RouteChanged -= OnRouteChanged;
                _pendingCancellation?.Cancel();
                _pendingCancellation?.Dispose();
                _pendingCancellation = null;
                _monitor.Stop();
                pendingTask = _pendingTask;
            }

            try
            {
                await pendingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // StopAsync cancels the pending stabilization/retry delay before draining it.
            }
        }

        internal async Task WaitForIdleAsync()
        {
            Task pending;
            lock (_gate)
                pending = _pendingTask;
            await pending.ConfigureAwait(false);
        }

        public void Dispose()
        {
            Stop();
        }

        private void OnRouteChanged(NetworkRouteSnapshot previous, NetworkRouteSnapshot current)
        {
            if (!NetworkRouteRecoveryPolicy.ShouldRecover(previous, current))
                return;

            lock (_gate)
            {
                if (!_started)
                    return;
                _pendingCancellation?.Cancel();
                _pendingCancellation?.Dispose();
                _pendingCancellation = new CancellationTokenSource();
                _pendingTask = RecoverAfterDelayAsync(previous, current, _pendingCancellation.Token);
            }
        }

        private async Task RecoverAfterDelayAsync(
            NetworkRouteSnapshot previous,
            NetworkRouteSnapshot current,
            CancellationToken cancellationToken)
        {
            try
            {
                if (_stabilizationDelay > TimeSpan.Zero)
                    await Task.Delay(_stabilizationDelay, cancellationToken).ConfigureAwait(false);

                DiagnosticsLogger.Info(
                    "NetworkRoute",
                    $"Route recovered: {previous.State} -> {current.State}; Proxy={current.ProxyDisplay}");
                Task steamRouteTask = RunIsolatedAsync("SteamRoute", _refreshSteamRoute);
                try
                {
                    await RecoverMarketWithRetryAsync(cancellationToken).ConfigureAwait(false);
                    _recoverySignal?.Publish();
                }
                finally
                {
                    await steamRouteTask.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // A newer route event or application shutdown superseded this recovery.
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("NetworkRoute", "Route recovery refresh failed.", ex);
            }
        }

        private async Task RecoverMarketWithRetryAsync(CancellationToken cancellationToken)
        {
            TimeSpan[] retryDelays = { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15) };
            for (int attempt = 0; ; attempt++)
            {
                await RunIsolatedAsync(
                    "MarketRefresh",
                    () => _refreshMarket(MarketRefreshRequest.For(MarketRefreshTrigger.RouteRecovered))).ConfigureAwait(false);

                if (!_shouldRetryMarket() || attempt >= retryDelays.Length)
                    return;

                await _delay(retryDelays[attempt], cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task RunIsolatedAsync(string operation, Func<Task> action)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("NetworkRoute", operation + " failed.", ex);
            }
        }

    }

    public sealed class NetworkRecoverySignal : INetworkRecoverySignal
    {
        public event Action? Recovered;

        internal void Publish()
        {
            Delegate[] subscribers = Recovered?.GetInvocationList() ?? Array.Empty<Delegate>();
            foreach (Delegate subscriber in subscribers)
            {
                try
                {
                    ((Action)subscriber)();
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Ignored("NetworkRoute", "RecoverySubscriber", ex, retryable: false, category: "Observer");
                }
            }
        }

    }
}
