using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.Steam;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace CS2TradeMonitor.Application.Steam
{
    public sealed class SteamEndpointConnectionManager
    {
        private static readonly TimeSpan LastKnownGoodTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan RejectedEndpointTtl = TimeSpan.FromMinutes(3);
        private static readonly string[] FallbackHostSuffixes =
        {
            "steamcommunity.com",
            "steampowered.com",
            "steamstatic.com",
            "steamusercontent.com",
            "steamcontent.com",
            "steamserver.net"
        };

        private readonly ISteamEndpointAddressSource _addressSource;
        private readonly TimeSpan _attemptStagger;
        private readonly TimeSpan _attemptTimeout;
        private readonly TimeSpan _totalTimeout;
        private readonly TimeSpan _systemDnsTimeout;
        private readonly ConcurrentDictionary<string, SuccessfulEndpoint> _lastKnownGood = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<IPAddress, DateTime>> _rejectedEndpoints = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, FallbackPreference> _fallbackPreferences = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, SteamEndpointConnectionSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _recoveryGates = new(StringComparer.OrdinalIgnoreCase);
        private SteamEndpointConnectionSnapshot _latestSnapshot = new();
        private long _connectionGeneration;

        public static SteamEndpointConnectionManager Instance { get; } = new(
            new DefaultSteamEndpointAddressSource(),
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(1500),
            TimeSpan.FromSeconds(6));

        internal SteamEndpointConnectionManager(
            ISteamEndpointAddressSource addressSource,
            TimeSpan attemptStagger,
            TimeSpan attemptTimeout,
            TimeSpan totalTimeout)
        {
            _addressSource = addressSource ?? throw new ArgumentNullException(nameof(addressSource));
            _attemptStagger = attemptStagger;
            _attemptTimeout = attemptTimeout;
            _totalTimeout = totalTimeout;
            _systemDnsTimeout = TimeSpan.FromMilliseconds(
                Math.Clamp(totalTimeout.TotalMilliseconds / 3, 50, 900));
        }

        public SteamEndpointConnectionSnapshot GetSnapshot(string host)
        {
            string key = NormalizeHost(host);
            return _snapshots.TryGetValue(key, out SteamEndpointConnectionSnapshot? snapshot)
                ? snapshot
                : new SteamEndpointConnectionSnapshot { Host = key };
        }

        public SteamEndpointConnectionSnapshot GetLatestSnapshot()
        {
            return Volatile.Read(ref _latestSnapshot);
        }

        public void ReportTransportFailure(SteamEndpointConnectionIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(identity);
            string key = NormalizeHost(identity.Host);
            if (string.IsNullOrWhiteSpace(key))
                return;

            if (!TryRemoveCurrentEndpoint(key, identity))
                return;

            RejectAddress(key, identity.Address);

            if (IsFallbackAllowed(key))
            {
                _fallbackPreferences[key] = new FallbackPreference(
                    DateTime.UtcNow.Add(RejectedEndpointTtl),
                    identity.Generation);
            }

            SteamEndpointConnectionSnapshot snapshot = GetSnapshot(key);
            if (snapshot.ConnectionGeneration != identity.Generation)
                return;

            SetSnapshot(new SteamEndpointConnectionSnapshot
            {
                Host = key,
                EndpointAddress = identity.Address,
                AddressSource = snapshot.AddressSource,
                UsedFallbackDns = snapshot.UsedFallbackDns,
                ConnectionGeneration = identity.Generation,
                AttemptCount = snapshot.AttemptCount,
                LastAttemptAt = DateTime.Now,
                FailureReason = "TCP 已连通，但 Steam HTTPS 未通过；已隔离该端点并切换连接路径。"
            });
        }

        public ValueTask<Stream> ConnectAsync(
            DnsEndPoint endpoint,
            CancellationToken cancellationToken)
        {
            return ConnectCoreAsync(endpoint, onConnected: null, cancellationToken);
        }

        internal ValueTask<Stream> ConnectAsync(
            SocketsHttpConnectionContext context,
            SteamEndpointRouteTracker tracker,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(tracker);
            return ConnectCoreAsync(
                context.DnsEndPoint,
                identity => tracker.Record(context.InitialRequestMessage, identity),
                cancellationToken);
        }

        private async ValueTask<Stream> ConnectCoreAsync(
            DnsEndPoint endpoint,
            Action<SteamEndpointConnectionIdentity>? onConnected,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            string host = NormalizeHost(endpoint.Host);
            using var total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            total.CancelAfter(_totalTimeout);
            var failures = new List<string>();
            int attemptCount = 0;
            string attemptedSources = "系统 DNS";

            try
            {
                bool preferFallback = ShouldPreferFallback(host);
                if (!preferFallback && TryGetCachedEndpoint(host, out SuccessfulEndpoint cached))
                {
                    ConnectionBatchResult cachedResult = await TryConnectBatchAsync(
                        new[] { cached.Address },
                        endpoint.Port,
                        total.Token)
                        .ConfigureAwait(false);
                    attemptCount += cachedResult.AttemptCount;
                    failures.AddRange(cachedResult.Failures);
                    if (cachedResult.Stream is not null && cachedResult.Address is not null)
                    {
                        return RecordSuccess(
                            host,
                            cachedResult,
                            cached.AddressSource,
                            cached.UsedFallbackDns,
                            attemptCount,
                            onConnected);
                    }

                    RejectCachedEndpointIfCurrent(host, cached);
                }

                SemaphoreSlim recoveryGate = _recoveryGates.GetOrAdd(
                    host,
                    static _ => new SemaphoreSlim(1, 1));
                await recoveryGate.WaitAsync(total.Token).ConfigureAwait(false);
                try
                {
                    preferFallback = ShouldPreferFallback(host);
                    if (!preferFallback && TryGetCachedEndpoint(host, out cached))
                    {
                        ConnectionBatchResult cachedResult = await TryConnectBatchAsync(
                            new[] { cached.Address },
                            endpoint.Port,
                            total.Token)
                            .ConfigureAwait(false);
                        attemptCount += cachedResult.AttemptCount;
                        failures.AddRange(cachedResult.Failures);
                        if (cachedResult.Stream is not null && cachedResult.Address is not null)
                        {
                            return RecordSuccess(
                                host,
                                cachedResult,
                                cached.AddressSource,
                                cached.UsedFallbackDns,
                                attemptCount,
                                onConnected);
                        }

                        RejectCachedEndpointIfCurrent(host, cached);
                    }

                    if (preferFallback && IsFallbackAllowed(host))
                    {
                        attemptedSources = "备用 DNS";
                        ConnectionBatchResult preferredFallbackResult = await TryFallbackAsync(
                            host,
                            endpoint.Port,
                            total.Token)
                            .ConfigureAwait(false);
                        attemptCount += preferredFallbackResult.AttemptCount;
                        failures.AddRange(preferredFallbackResult.Failures);
                        if (preferredFallbackResult.Stream is not null && preferredFallbackResult.Address is not null)
                        {
                            return RecordSuccess(
                                host,
                                preferredFallbackResult,
                                "备用 DNS",
                                usedFallbackDns: true,
                                attemptCount,
                                onConnected);
                        }
                    }

                    attemptedSources = preferFallback ? "备用 DNS + 系统 DNS" : "系统 DNS";
                    AddressResolutionResult systemResolution = await ResolveSystemWithBudgetAsync(
                        host,
                        total.Token)
                        .ConfigureAwait(false);
                    if (systemResolution.TimedOut)
                        failures.Add("系统 DNS 解析超时。");
                    ConnectionBatchResult systemResult = await TryConnectBatchAsync(
                        OrderAddresses(host, systemResolution.Addresses),
                        endpoint.Port,
                        total.Token)
                        .ConfigureAwait(false);
                    attemptCount += systemResult.AttemptCount;
                    failures.AddRange(systemResult.Failures);
                    if (systemResult.Stream is not null && systemResult.Address is not null)
                    {
                        return RecordSuccess(
                            host,
                            systemResult,
                            "系统 DNS",
                            usedFallbackDns: false,
                            attemptCount,
                            onConnected);
                    }

                    if (!preferFallback && IsFallbackAllowed(host))
                    {
                        attemptedSources = "系统 DNS + 备用 DNS";
                        ConnectionBatchResult fallbackResult = await TryFallbackAsync(
                            host,
                            endpoint.Port,
                            total.Token)
                            .ConfigureAwait(false);
                        attemptCount += fallbackResult.AttemptCount;
                        failures.AddRange(fallbackResult.Failures);
                        if (fallbackResult.Stream is not null && fallbackResult.Address is not null)
                        {
                            return RecordSuccess(
                                host,
                                fallbackResult,
                                "备用 DNS",
                                usedFallbackDns: true,
                                attemptCount,
                                onConnected);
                        }
                    }

                    string reason = failures.Count == 0
                        ? "未解析到可用地址。"
                        : "所有候选地址均无法建立 TCP 连接。";
                    SetFailureSnapshot(host, attemptedSources, attemptCount, reason);
                    throw new HttpRequestException("Steam 端点连接失败：" + reason);
                }
                finally
                {
                    recoveryGate.Release();
                }
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                string reason = "Steam 端点连接恢复总预算已耗尽。";
                SetFailureSnapshot(host, attemptedSources, attemptCount, reason);
                throw new HttpRequestException(reason, ex);
            }
        }

        private void SetFailureSnapshot(
            string host,
            string addressSource,
            int attemptCount,
            string reason)
        {
            SetSnapshot(new SteamEndpointConnectionSnapshot
            {
                Host = host,
                AddressSource = addressSource,
                AttemptCount = attemptCount,
                LastAttemptAt = DateTime.Now,
                FailureReason = reason
            });
        }

        private async Task<ConnectionBatchResult> TryFallbackAsync(
            string host,
            int port,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<IPAddress> fallbackAddresses = await _addressSource
                .ResolveFallbackAsync(host, cancellationToken)
                .ConfigureAwait(false);
            return await TryConnectBatchAsync(
                OrderAddresses(host, fallbackAddresses),
                port,
                cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task<AddressResolutionResult> ResolveSystemWithBudgetAsync(
            string host,
            CancellationToken cancellationToken)
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(_systemDnsTimeout);
            try
            {
                IReadOnlyList<IPAddress> addresses = await _addressSource
                    .ResolveSystemAsync(host, budget.Token)
                    .ConfigureAwait(false);
                return new AddressResolutionResult(addresses, TimedOut: false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new AddressResolutionResult(Array.Empty<IPAddress>(), TimedOut: true);
            }
        }

        private Stream RecordSuccess(
            string host,
            ConnectionBatchResult result,
            string addressSource,
            bool usedFallbackDns,
            int attemptCount,
            Action<SteamEndpointConnectionIdentity>? onConnected)
        {
            long generation = Interlocked.Increment(ref _connectionGeneration);
            var identity = new SteamEndpointConnectionIdentity(
                host,
                result.Address!,
                generation);
            _lastKnownGood[host] = new SuccessfulEndpoint(
                result.Address!,
                DateTime.UtcNow,
                addressSource,
                usedFallbackDns,
                generation);
            if (_rejectedEndpoints.TryGetValue(host, out ConcurrentDictionary<IPAddress, DateTime>? rejected))
                rejected.TryRemove(result.Address!, out _);
            SetSnapshot(new SteamEndpointConnectionSnapshot
            {
                Host = host,
                EndpointAddress = result.Address,
                AddressSource = addressSource,
                UsedFallbackDns = usedFallbackDns,
                ConnectionGeneration = generation,
                AttemptCount = attemptCount,
                LastAttemptAt = DateTime.Now
            });
            onConnected?.Invoke(identity);
            return result.Stream!;
        }

        private bool TryGetCachedEndpoint(string host, out SuccessfulEndpoint endpoint)
        {
            if (_lastKnownGood.TryGetValue(host, out SuccessfulEndpoint? cached)
                && cached is not null
                && DateTime.UtcNow - cached.SucceededAt <= LastKnownGoodTtl)
            {
                endpoint = cached;
                return true;
            }

            if (cached is not null)
            {
                ((ICollection<KeyValuePair<string, SuccessfulEndpoint>>)_lastKnownGood)
                    .Remove(new KeyValuePair<string, SuccessfulEndpoint>(host, cached));
            }
            endpoint = null!;
            return false;
        }

        private void RejectAddress(string host, IPAddress address)
        {
            ConcurrentDictionary<IPAddress, DateTime> rejected = _rejectedEndpoints.GetOrAdd(
                host,
                static _ => new ConcurrentDictionary<IPAddress, DateTime>());
            rejected[address] = DateTime.UtcNow.Add(RejectedEndpointTtl);
        }

        private void RejectCachedEndpointIfCurrent(
            string host,
            SuccessfulEndpoint cached)
        {
            RejectCachedEndpoint(new SteamEndpointConnectionIdentity(
                host,
                cached.Address,
                cached.Generation));
        }

        internal bool RejectCachedEndpoint(SteamEndpointConnectionIdentity identity)
        {
            string host = NormalizeHost(identity.Host);
            if (!TryRemoveCurrentEndpoint(host, identity))
                return false;

            RejectAddress(host, identity.Address);
            return true;
        }

        private bool TryRemoveCurrentEndpoint(
            string host,
            SteamEndpointConnectionIdentity identity)
        {
            if (!_lastKnownGood.TryGetValue(host, out SuccessfulEndpoint? current)
                || current is null
                || current.Generation != identity.Generation
                || !current.Address.Equals(identity.Address))
            {
                return false;
            }

            return ((ICollection<KeyValuePair<string, SuccessfulEndpoint>>)_lastKnownGood)
                .Remove(new KeyValuePair<string, SuccessfulEndpoint>(host, current));
        }

        private void SetSnapshot(SteamEndpointConnectionSnapshot snapshot)
        {
            _snapshots[snapshot.Host] = snapshot;
            Volatile.Write(ref _latestSnapshot, snapshot);
        }

        private async Task<ConnectionBatchResult> TryConnectBatchAsync(
            IReadOnlyList<IPAddress> addresses,
            int port,
            CancellationToken cancellationToken)
        {
            if (addresses.Count == 0)
                return ConnectionBatchResult.Empty;

            using var batch = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task<ConnectionAttempt>[] tasks = addresses
                .Select((address, index) => ConnectOneAsync(address, port, index, batch.Token))
                .ToArray();
            var pending = tasks.ToList();
            var failures = new List<string>();
            int attemptCount = 0;

            while (pending.Count > 0)
            {
                Task<ConnectionAttempt> completed = await Task.WhenAny(pending).ConfigureAwait(false);
                pending.Remove(completed);
                ConnectionAttempt attempt = await completed.ConfigureAwait(false);
                if (attempt.Attempted)
                    attemptCount++;
                if (attempt.Stream is not null)
                {
                    batch.Cancel();
                    await DisposeLosersAsync(pending).ConfigureAwait(false);
                    return new ConnectionBatchResult(
                        attempt.Stream,
                        attempt.Address,
                        attemptCount,
                        failures);
                }

                if (!string.IsNullOrWhiteSpace(attempt.Failure))
                    failures.Add(attempt.Failure);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new ConnectionBatchResult(null, null, attemptCount, failures);
        }

        private async Task<ConnectionAttempt> ConnectOneAsync(
            IPAddress address,
            int port,
            int index,
            CancellationToken cancellationToken)
        {
            Socket? socket = null;
            try
            {
                if (index > 0)
                    await Task.Delay(TimeSpan.FromTicks(_attemptStagger.Ticks * index), cancellationToken).ConfigureAwait(false);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_attemptTimeout);
                socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = true
                };
                await socket.ConnectAsync(new IPEndPoint(address, port), timeout.Token).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var stream = new NetworkStream(socket, ownsSocket: true);
                socket = null;
                return new ConnectionAttempt(address, stream, Attempted: true, Failure: "");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new ConnectionAttempt(address, null, Attempted: true, Failure: address + " 超时");
            }
            catch (OperationCanceledException)
            {
                return new ConnectionAttempt(address, null, Attempted: false, Failure: "");
            }
            catch (SocketException ex)
            {
                return new ConnectionAttempt(address, null, Attempted: true, Failure: address + " " + ex.SocketErrorCode);
            }
            finally
            {
                socket?.Dispose();
            }
        }

        private static async Task DisposeLosersAsync(IEnumerable<Task<ConnectionAttempt>> pending)
        {
            foreach (Task<ConnectionAttempt> task in pending)
            {
                try
                {
                    ConnectionAttempt result = await task.ConfigureAwait(false);
                    result.Stream?.Dispose();
                }
                catch
                {
                    // A losing connection attempt must not affect the selected route.
                }
            }
        }

        private IPAddress[] OrderAddresses(string host, IEnumerable<IPAddress> addresses)
        {
            DateTime now = DateTime.UtcNow;
            ConcurrentDictionary<IPAddress, DateTime>? rejected = null;
            if (_rejectedEndpoints.TryGetValue(host, out rejected))
            {
                foreach ((IPAddress address, DateTime expiry) in rejected)
                {
                    if (expiry <= now)
                        rejected.TryRemove(address, out _);
                }
            }

            var distinct = addresses
                .Where(static address => address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                .Where(address => rejected == null
                    || !rejected.TryGetValue(address, out DateTime expiry)
                    || expiry <= now)
                .Distinct()
                .Take(12)
                .ToList();

            if (_lastKnownGood.TryGetValue(host, out SuccessfulEndpoint? successful))
            {
                if (DateTime.UtcNow - successful.SucceededAt <= LastKnownGoodTtl
                    && distinct.Remove(successful.Address))
                {
                    distinct.Insert(0, successful.Address);
                }
                else if (DateTime.UtcNow - successful.SucceededAt > LastKnownGoodTtl)
                {
                    ((ICollection<KeyValuePair<string, SuccessfulEndpoint>>)_lastKnownGood)
                        .Remove(new KeyValuePair<string, SuccessfulEndpoint>(host, successful));
                }
            }

            var ipv6 = new Queue<IPAddress>(distinct.Where(static address => address.AddressFamily == AddressFamily.InterNetworkV6));
            var ipv4 = new Queue<IPAddress>(distinct.Where(static address => address.AddressFamily == AddressFamily.InterNetwork));
            bool preferIpv6 = distinct.FirstOrDefault()?.AddressFamily == AddressFamily.InterNetworkV6;
            var ordered = new List<IPAddress>(Math.Min(6, distinct.Count));
            while (ordered.Count < 6 && (ipv4.Count > 0 || ipv6.Count > 0))
            {
                Queue<IPAddress> preferred = preferIpv6 ? ipv6 : ipv4;
                Queue<IPAddress> alternate = preferIpv6 ? ipv4 : ipv6;
                if (preferred.Count > 0)
                    ordered.Add(preferred.Dequeue());
                if (ordered.Count < 6 && alternate.Count > 0)
                    ordered.Add(alternate.Dequeue());
            }

            return ordered.ToArray();
        }

        private bool ShouldPreferFallback(string host)
        {
            if (!_fallbackPreferences.TryGetValue(host, out FallbackPreference? preference)
                || preference is null)
                return false;
            if (preference.Expiry <= DateTime.UtcNow)
            {
                _fallbackPreferences.TryRemove(host, out _);
                return false;
            }

            if (_lastKnownGood.TryGetValue(host, out SuccessfulEndpoint? current)
                && current is not null
                && current.Generation > preference.FailedGeneration)
            {
                _fallbackPreferences.TryRemove(host, out _);
                return false;
            }

            return true;
        }

        private static bool IsFallbackAllowed(string host)
        {
            return FallbackHostSuffixes.Any(suffix =>
                host.Equals(suffix, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeHost(string host)
        {
            return (host ?? "").Trim().TrimEnd('.').ToLowerInvariant();
        }

        private sealed record SuccessfulEndpoint(
            IPAddress Address,
            DateTime SucceededAt,
            string AddressSource,
            bool UsedFallbackDns,
            long Generation);

        private sealed record FallbackPreference(
            DateTime Expiry,
            long FailedGeneration);

        private sealed record AddressResolutionResult(
            IReadOnlyList<IPAddress> Addresses,
            bool TimedOut);

        private sealed record ConnectionAttempt(
            IPAddress Address,
            Stream? Stream,
            bool Attempted,
            string Failure);

        private sealed record ConnectionBatchResult(
            Stream? Stream,
            IPAddress? Address,
            int AttemptCount,
            IReadOnlyList<string> Failures)
        {
            public static ConnectionBatchResult Empty { get; } = new(null, null, 0, Array.Empty<string>());
        }
    }

    internal sealed class SteamEndpointRouteTracker
    {
        private static readonly HttpRequestOptionsKey<SteamEndpointConnectionIdentity> IdentityKey =
            new("CS2TradeMonitor.SteamEndpointIdentity");

        public void Record(
            HttpRequestMessage? initialRequest,
            SteamEndpointConnectionIdentity identity)
        {
            initialRequest?.Options.Set(IdentityKey, identity);
        }

        public bool TryGet(
            HttpRequestMessage request,
            out SteamEndpointConnectionIdentity identity)
        {
            if (request.Options.TryGetValue(IdentityKey, out SteamEndpointConnectionIdentity? requestIdentity)
                && requestIdentity is not null)
            {
                identity = requestIdentity;
                return true;
            }

            identity = null!;
            return false;
        }
    }
}
