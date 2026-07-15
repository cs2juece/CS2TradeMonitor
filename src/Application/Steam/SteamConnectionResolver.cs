using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.Steam;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CS2TradeMonitor.Application.Steam
{
    public sealed class SteamConnectionResolver : ISteamConnectionResolver
    {
        private const string ProbeUrl = SteamUrls.ServerInfoProbe;
        private static readonly TimeSpan FreshProfileTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(2800);
        private static readonly TimeSpan DirectProbeTimeout = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan TcpProbeTimeout = TimeSpan.FromMilliseconds(650);
        private static readonly TimeSpan FailureCooldown = TimeSpan.FromMinutes(3);
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly object _lock = new();
        private readonly string _profilePath;
        private readonly ISteamManualProxyStore _manualProxyStore;
        private SteamConnectionProfile? _profile;
        private bool _profileLoaded;
        private Task<SteamConnectionProfile>? _resolveTask;

        public static SteamConnectionResolver Instance { get; } = new();

        public event Action? StatusChanged;

        private SteamConnectionResolver()
            : this(SteamServiceRuntimeServices.ResolveManualProxyStore())
        {
        }

        internal SteamConnectionResolver(ISteamManualProxyStore manualProxyStore)
        {
            _manualProxyStore = manualProxyStore ?? throw new ArgumentNullException(nameof(manualProxyStore));
            _profilePath = RuntimeDataPaths.GetCacheFilePath("steam_connection_profile.json");
        }

        public SteamConnectionProfile GetSnapshot()
        {
            lock (_lock)
                return LoadProfileNoLock().Clone();
        }

        public SteamConnectionProfile GetBestKnownProfile()
        {
            lock (_lock)
            {
                var profile = LoadProfileNoLock();
                if (profile.Mode == SteamConnectionMode.Failed && DateTime.Now < profile.CooldownUntil)
                    return profile.Clone();
                return profile.Clone();
            }
        }

        public string GetManualProxyDisplay()
        {
            try
            {
                string value = _manualProxyStore.Load();
                return string.IsNullOrWhiteSpace(value) ? "未设置" : RedactProxyUri(value);
            }
            catch (InvalidOperationException)
            {
                return "手动代理凭据不可用（原文件已保留）";
            }
        }

        public bool SaveManualProxy(string proxyUri, out string message)
        {
            proxyUri = (proxyUri ?? "").Trim();
            if (string.IsNullOrWhiteSpace(proxyUri))
            {
                try
                {
                    _manualProxyStore.Clear();
                }
                catch (InvalidOperationException ex)
                {
                    message = DiagnosticsLogger.Redact(ex.Message);
                    return false;
                }
                InvalidateCachedProfile("已清空手动代理。");
                message = "已清空手动代理。";
                return true;
            }

            if (!TryCreateProxyUri(proxyUri, out var uri, out message))
                return false;

            try
            {
                _manualProxyStore.Save(uri.ToString());
            }
            catch (InvalidOperationException ex)
            {
                message = DiagnosticsLogger.Redact(ex.Message);
                return false;
            }
            InvalidateCachedProfile("已更新手动代理。");
            message = "手动代理已加密保存：" + RedactProxyUri(uri.ToString());
            return true;
        }

        public void EnsureResolvedInBackground()
        {
            try
            {
                _ = ResolveAsync(force: false);
            }
            catch
            {
                // Background detection must never block Steam UI creation.
            }
        }

        public Task<SteamConnectionProfile> ResolveAsync(bool force = false, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                var cached = LoadProfileNoLock();
                if (!force && IsFreshSuccess(cached))
                    return Task.FromResult(cached.Clone());

                if (_resolveTask is { IsCompleted: false })
                    return _resolveTask;

                _resolveTask = ResolveCoreAsync(force, cancellationToken);
                return _resolveTask;
            }
        }

        public void ReportFailure(string reason)
        {
            reason = SteamOfferAuditLog.RedactSecrets(reason ?? "");
            lock (_lock)
            {
                var profile = LoadProfileNoLock().Clone();
                profile.FailureCount++;
                profile.FailureReason = string.IsNullOrWhiteSpace(reason) ? "Steam 网络请求失败。" : reason;
                if (profile.FailureCount >= 3)
                    profile.CooldownUntil = DateTime.Now.Add(FailureCooldown);
                SaveProfileNoLock(profile);
            }

            RaiseStatusChanged();
        }

        public void ReportSuccess()
        {
            lock (_lock)
            {
                var profile = LoadProfileNoLock().Clone();
                if (!profile.IsUsable)
                {
                    profile.Mode = SteamConnectionMode.Direct;
                    profile.ProxyUri = "";
                    profile.RouteName = "直连（实际请求已验证）";
                }

                bool stateChanged = profile.FailureCount != 0
                    || profile.CooldownUntil != DateTime.MinValue
                    || !string.IsNullOrWhiteSpace(profile.FailureReason);
                if (!stateChanged
                    && profile.LastSuccessAt != DateTime.MinValue
                    && DateTime.Now - profile.LastSuccessAt < TimeSpan.FromMinutes(1))
                    return;

                profile.LastSuccessAt = DateTime.Now;
                profile.FailureCount = 0;
                profile.CooldownUntil = DateTime.MinValue;
                profile.FailureReason = "";
                SaveProfileNoLock(profile);
            }

            RaiseStatusChanged();
        }

        internal static string RedactProxyUri(string? proxyUri)
        {
            string text = (proxyUri ?? "").Trim();
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
                return SteamOfferAuditLog.RedactSecrets(text);

            string authority = uri.Authority;
            if (!string.IsNullOrWhiteSpace(uri.UserInfo))
            {
                string hostPort = uri.IsDefaultPort ? uri.Host : uri.Host + ":" + uri.Port;
                authority = "***:***@" + hostPort;
            }

            return uri.Scheme + "://" + authority;
        }

        internal static IWebProxy? CreateProxy(string proxyUri)
        {
            if (!TryCreateProxyUri(proxyUri, out var uri, out _))
                return null;

            var proxy = new WebProxy(uri);
            if (!string.IsNullOrWhiteSpace(uri.UserInfo))
            {
                string[] parts = uri.UserInfo.Split(':', 2);
                string user = Uri.UnescapeDataString(parts[0]);
                string password = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
                proxy.Credentials = new NetworkCredential(user, password);
            }
            else
            {
                proxy.Credentials = CredentialCache.DefaultCredentials;
            }

            return proxy;
        }

        private async Task<SteamConnectionProfile> ResolveCoreAsync(bool force, CancellationToken cancellationToken)
        {
            SteamConnectionProfile? cached;
            lock (_lock)
                cached = LoadProfileNoLock().Clone();

            if (!force && cached.IsUsable && DateTime.Now >= cached.CooldownUntil)
            {
                var verified = await ProbeProfileAsync(cached, cancellationToken).ConfigureAwait(false);
                if (verified.IsUsable)
                    return PersistResolved(verified);
            }

            foreach (var candidate in BuildCandidates())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await ShouldSkipCandidateAsync(candidate, cancellationToken).ConfigureAwait(false))
                    continue;

                var result = await ProbeCandidateAsync(candidate, cancellationToken).ConfigureAwait(false);
                if (result.IsUsable)
                    return PersistResolved(result);
            }

            var failed = new SteamConnectionProfile
            {
                Mode = SteamConnectionMode.Failed,
                RouteName = "失败",
                FailureCount = Math.Max(1, cached.FailureCount + 1),
                CooldownUntil = DateTime.Now.Add(FailureCooldown),
                FailureReason = "未能连接 Steam，请确认网络或开启代理软件；也可以在 Steam 报价页设置手动代理。"
            };
            return PersistResolved(failed);
        }

        private SteamConnectionProfile PersistResolved(SteamConnectionProfile profile)
        {
            lock (_lock)
            {
                SaveProfileNoLock(profile.Clone());
            }

            RaiseStatusChanged();
            return profile.Clone();
        }

        private async Task<SteamConnectionProfile> ProbeProfileAsync(SteamConnectionProfile profile, CancellationToken cancellationToken)
        {
            var candidate = profile.Mode switch
            {
                SteamConnectionMode.ManualProxy => ConnectionCandidate.Manual(_manualProxyStore.Load()),
                SteamConnectionMode.AutoProxy => ConnectionCandidate.Auto(profile.ProxyUri, profile.RouteName),
                SteamConnectionMode.Direct => ConnectionCandidate.Direct(),
                _ => null
            };

            return candidate == null
                ? FailedProfile(profile.Mode, profile.ProxyUri, profile.RouteName, "缓存连接策略不可用。")
                : await ProbeCandidateAsync(candidate, cancellationToken).ConfigureAwait(false);
        }

        private async Task<bool> ShouldSkipCandidateAsync(ConnectionCandidate candidate, CancellationToken cancellationToken)
        {
            if (!candidate.RequiresTcpProbe)
                return false;
            if (candidate.ProxyUri == null || !candidate.ProxyUri.IsLoopback)
                return false;
            if (candidate.ProxyUri.Port <= 0)
                return true;

            try
            {
                using var tcp = new TcpClient();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TcpProbeTimeout);
                await tcp.ConnectAsync(candidate.ProxyUri.Host, candidate.ProxyUri.Port, cts.Token).ConfigureAwait(false);
                return false;
            }
            catch
            {
                return true;
            }
        }

        private async Task<SteamConnectionProfile> ProbeCandidateAsync(ConnectionCandidate candidate, CancellationToken cancellationToken)
        {
            int maxAttempts = candidate.Mode == SteamConnectionMode.Direct ? 2 : 1;
            using CancellationTokenSource? directBudget = candidate.Mode == SteamConnectionMode.Direct
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : null;
            directBudget?.CancelAfter(DirectProbeTimeout);
            CancellationToken probeToken = directBudget?.Token ?? cancellationToken;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                using var handler = CreateHandler(candidate, out SteamEndpointRouteTracker? endpointTracker);
                using var client = new HttpClient(handler)
                {
                    Timeout = candidate.Mode == SteamConnectionMode.Direct
                        ? Timeout.InfiniteTimeSpan
                        : GetProbeTimeout(candidate.Mode)
                };
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "CS2TradeMonitor/1.0 SteamConnectionProbe");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
                using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);

                try
                {
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, probeToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        return new SteamConnectionProfile
                        {
                            Mode = candidate.Mode,
                            ProxyUri = candidate.ProxyUriText,
                            RouteName = candidate.DisplayName,
                            LastSuccessAt = DateTime.Now,
                            FailureCount = 0,
                            CooldownUntil = DateTime.MinValue,
                            FailureReason = response.StatusCode == HttpStatusCode.TooManyRequests
                                ? "Steam 轻量探测返回 429，网络路径可达但已被限流。"
                                : ""
                        };
                    }

                    return FailedProfile(candidate.Mode, candidate.ProxyUriText, candidate.DisplayName, $"Steam 探测返回 HTTP {(int)response.StatusCode}。");
                }
                catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    if (candidate.Mode == SteamConnectionMode.Direct
                        && !probeToken.IsCancellationRequested
                        && attempt + 1 < maxAttempts)
                    {
                        ReportProbeEndpointFailure(endpointTracker, request);
                        continue;
                    }

                    return FailedProfile(candidate.Mode, candidate.ProxyUriText, candidate.DisplayName, NetworkDiagnostics.BuildFailureMessage("Steam", "ConnectionProbe", ex));
                }
                catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException)
                {
                    if (candidate.Mode == SteamConnectionMode.Direct
                        && !probeToken.IsCancellationRequested
                        && attempt + 1 < maxAttempts)
                    {
                        ReportProbeEndpointFailure(endpointTracker, request);
                        continue;
                    }

                    return FailedProfile(candidate.Mode, candidate.ProxyUriText, candidate.DisplayName, NetworkDiagnostics.BuildFailureMessage("Steam", "ConnectionProbe", ex));
                }
            }

            return FailedProfile(candidate.Mode, candidate.ProxyUriText, candidate.DisplayName, "Steam 探测失败。");
        }

        private static void ReportProbeEndpointFailure(
            SteamEndpointRouteTracker? tracker,
            HttpRequestMessage request)
        {
            if (tracker?.TryGet(request, out SteamEndpointConnectionIdentity identity) == true)
                SteamEndpointConnectionManager.Instance.ReportTransportFailure(identity);
        }

        private static SocketsHttpHandler CreateHandler(
            ConnectionCandidate candidate,
            out SteamEndpointRouteTracker? endpointTracker)
        {
            endpointTracker = null;
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                AllowAutoRedirect = false,
                UseCookies = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 8,
                ConnectTimeout = GetProbeTimeout(candidate.Mode)
            };

            if (candidate.Mode is SteamConnectionMode.ManualProxy or SteamConnectionMode.AutoProxy)
            {
                handler.UseProxy = true;
                handler.Proxy = candidate.Proxy ?? CreateProxy(candidate.ProxyUriText);
                handler.DefaultProxyCredentials = CredentialCache.DefaultCredentials;
            }
            else
            {
                handler.UseProxy = false;
                endpointTracker = new SteamEndpointRouteTracker();
                SteamEndpointRouteTracker tracker = endpointTracker;
                handler.ConnectCallback = (context, cancellationToken) =>
                    SteamEndpointConnectionManager.Instance.ConnectAsync(
                        context,
                        tracker,
                        cancellationToken);
            }

            return handler;
        }

        internal static TimeSpan GetProbeTimeout(SteamConnectionMode mode)
        {
            return mode == SteamConnectionMode.Direct
                ? DirectProbeTimeout
                : ProbeTimeout;
        }

        private IEnumerable<ConnectionCandidate> BuildCandidates()
        {
            string manual = _manualProxyStore.Load();
            if (!string.IsNullOrWhiteSpace(manual))
                yield return ConnectionCandidate.Manual(manual);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var system = TryGetSystemProxy();
            if (system != null)
                yield return ConnectionCandidate.SystemProxy(system);

            foreach (string envName in new[] { "HTTPS_PROXY", "HTTP_PROXY", "ALL_PROXY" })
            {
                string value = Environment.GetEnvironmentVariable(envName) ?? "";
                if (TryCreateProxyUri(value, out var uri, out _) && seen.Add(uri.ToString()))
                    yield return ConnectionCandidate.Auto(uri.ToString(), "环境代理 " + envName);
            }

            foreach (var uri in BuildLocalProxyUris())
            {
                if (seen.Add(uri))
                    yield return ConnectionCandidate.Auto(uri, "本地代理 " + RedactProxyUri(uri), requiresTcpProbe: true);
            }

            yield return ConnectionCandidate.Direct();
        }

        private static IWebProxy? TryGetSystemProxy()
        {
            try
            {
                var proxy = WebRequest.GetSystemWebProxy();
                proxy.Credentials = CredentialCache.DefaultCredentials;
                Uri probe = new(ProbeUrl);
                Uri? resolved = proxy.GetProxy(probe);
                if (resolved == null || resolved == probe)
                    return null;
                return proxy;
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored("SteamConnection", "ResolveSystemProxy", ex, retryable: true, category: "Proxy");
                return null;
            }
        }

        private static IEnumerable<string> BuildLocalProxyUris()
        {
            int[] httpPorts = { 7890, 7891, 7897, 7899, 8080, 8889 };
            int[] socksPorts = { 10809, 10808, 10810, 10811, 1080, 1086, 2080 };

            foreach (int port in httpPorts)
                yield return "http://127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach (int port in socksPorts)
                yield return "socks5://127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static bool TryCreateProxyUri(string text, out Uri uri, out string message)
        {
            uri = null!;
            message = "";
            text = (text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                message = "代理地址为空。";
                return false;
            }

            if (!Uri.TryCreate(text, UriKind.Absolute, out uri!))
            {
                message = "代理地址格式不正确，示例：http://127.0.0.1:7890 或 socks5://127.0.0.1:10808";
                return false;
            }

            if (!uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                && !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
                && !uri.Scheme.Equals("socks5", StringComparison.OrdinalIgnoreCase)
                && !uri.Scheme.Equals("socks4", StringComparison.OrdinalIgnoreCase))
            {
                message = "仅支持 http、https、socks5、socks4 代理。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(uri.Host) || uri.Port <= 0)
            {
                message = "代理地址必须包含主机和端口。";
                return false;
            }

            return true;
        }

        private static bool IsFreshSuccess(SteamConnectionProfile profile)
        {
            return profile.IsUsable
                && DateTime.Now >= profile.CooldownUntil
                && profile.LastSuccessAt != DateTime.MinValue
                && DateTime.Now - profile.LastSuccessAt < FreshProfileTtl
                && (profile.Mode != SteamConnectionMode.Direct
                    || SteamEndpointConnectionManager.Instance
                        .GetSnapshot(new Uri(ProbeUrl).Host)
                        .IsConnected);
        }

        private SteamConnectionProfile LoadProfileNoLock()
        {
            if (_profileLoaded)
                return _profile ?? new SteamConnectionProfile();

            _profileLoaded = true;
            try
            {
                if (File.Exists(_profilePath))
                {
                    string json = File.ReadAllText(_profilePath);
                    _profile = JsonSerializer.Deserialize<SteamConnectionProfile>(json, JsonOptions) ?? new SteamConnectionProfile();
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored("SteamConnection", "LoadProfile", ex, retryable: true, category: "Cache");
                _profile = new SteamConnectionProfile();
            }

            _profile ??= new SteamConnectionProfile();
            _profile.ProxyUri = SteamOfferAuditLog.RedactSecrets(_profile.ProxyUri ?? "");
            return _profile;
        }

        private void SaveProfileNoLock(SteamConnectionProfile profile)
        {
            try
            {
                profile.ProxyUri = SteamOfferAuditLog.RedactSecrets(profile.ProxyUri ?? "");
                _profile = profile;
                _profileLoaded = true;
                RuntimeDataPaths.WriteTextAtomic(_profilePath, JsonSerializer.Serialize(profile, JsonOptions));
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored("SteamConnection", "SaveProfile", ex, retryable: true, category: "Cache");
            }
        }

        private void InvalidateCachedProfile(string reason)
        {
            lock (_lock)
            {
                SaveProfileNoLock(new SteamConnectionProfile
                {
                    Mode = SteamConnectionMode.Unknown,
                    FailureReason = reason
                });
            }

            RaiseStatusChanged();
        }

        private void RaiseStatusChanged()
        {
            try
            {
                StatusChanged?.Invoke();
            }
            catch
            {
                // Status subscribers are UI-only; do not let them affect networking.
            }
        }

        private static SteamConnectionProfile FailedProfile(SteamConnectionMode mode, string proxyUri, string routeName, string reason)
        {
            return new SteamConnectionProfile
            {
                Mode = SteamConnectionMode.Failed,
                ProxyUri = RedactProxyUri(proxyUri),
                RouteName = routeName,
                FailureCount = 1,
                FailureReason = SteamOfferAuditLog.RedactSecrets(reason)
            };
        }

        private sealed class ConnectionCandidate
        {
            private ConnectionCandidate(
                SteamConnectionMode mode,
                string proxyUriText,
                string displayName,
                IWebProxy? proxy = null,
                bool requiresTcpProbe = false)
            {
                Mode = mode;
                ProxyUriText = proxyUriText;
                DisplayName = displayName;
                Proxy = proxy;
                RequiresTcpProbe = requiresTcpProbe;
                ProxyUri = Uri.TryCreate(proxyUriText, UriKind.Absolute, out var uri) ? uri : null;
            }

            public SteamConnectionMode Mode { get; }
            public string ProxyUriText { get; }
            public string DisplayName { get; }
            public IWebProxy? Proxy { get; }
            public bool RequiresTcpProbe { get; }
            public Uri? ProxyUri { get; }

            public static ConnectionCandidate Manual(string proxyUri)
                => new(SteamConnectionMode.ManualProxy, proxyUri, "手动代理 " + RedactProxyUri(proxyUri), CreateProxy(proxyUri), requiresTcpProbe: true);

            public static ConnectionCandidate Auto(string proxyUri, string displayName, bool requiresTcpProbe = false)
                => new(SteamConnectionMode.AutoProxy, proxyUri, displayName, CreateProxy(proxyUri), requiresTcpProbe);

            public static ConnectionCandidate SystemProxy(IWebProxy proxy)
                => new(SteamConnectionMode.AutoProxy, "", "系统代理", proxy);

            public static ConnectionCandidate Direct()
                => new(SteamConnectionMode.Direct, "", "直连");
        }
    }

    internal sealed class SteamManualProxyStore : ISteamManualProxyStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CS2DesktopMonitor.SteamManualProxy.v1");
        private readonly object _lock = new();
        private readonly string _path;
        private string? _cache;
        private bool _loaded;
        private bool _writeBlocked;
        private string _lastError = "";

        public static SteamManualProxyStore Instance { get; } = new();

        private SteamManualProxyStore()
            : this(RuntimeDataPaths.GetSecureFilePath("steam_manual_proxy.dat"))
        {
        }

        internal SteamManualProxyStore(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            _path = Path.GetFullPath(path);
        }

        public string Load()
        {
            lock (_lock)
            {
                if (_loaded && !_writeBlocked)
                    return _cache ?? "";

                try
                {
                    if (!File.Exists(_path))
                    {
                        _cache = "";
                        _loaded = true;
                        _writeBlocked = false;
                        _lastError = "";
                        return "";
                    }

                    string text = File.ReadAllText(_path).Trim();
                    if (string.IsNullOrWhiteSpace(text))
                        throw new InvalidDataException("Steam 手动代理凭据内容为空。");

                    byte[] protectedBytes = Convert.FromBase64String(text);
                    byte[] plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                    _cache = Encoding.UTF8.GetString(plain).Trim();
                    if (string.IsNullOrWhiteSpace(_cache))
                        throw new InvalidDataException("Steam 手动代理凭据结构无效。");
                    _loaded = true;
                    _writeBlocked = false;
                    _lastError = "";
                    return _cache;
                }
                catch (Exception ex)
                {
                    _cache = "";
                    _loaded = true;
                    _writeBlocked = true;
                    _lastError = "读取 Steam 手动代理凭据失败：" + DiagnosticsLogger.Redact(ex.Message);
                    DiagnosticsLogger.Error("SteamConnection", _lastError);
                    throw new InvalidOperationException(_lastError, ex);
                }
            }
        }

        public void Save(string proxyUri)
        {
            lock (_lock)
            {
                ValidateExistingFileBeforeMutationNoLock();
                EnsureWritableNoLock();
                Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? RuntimeDataPaths.SecureDirectory);
                string value = (proxyUri ?? "").Trim();
                byte[] protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);
                RuntimeDataPaths.WriteTextAtomic(_path, Convert.ToBase64String(protectedBytes));
                _cache = value;
                _loaded = true;
                _writeBlocked = false;
                _lastError = "";
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                ValidateExistingFileBeforeMutationNoLock();
                EnsureWritableNoLock();
                try
                {
                    if (File.Exists(_path))
                        File.Delete(_path);
                }
                catch (Exception ex)
                {
                    _lastError = "清除 Steam 手动代理凭据失败：" + DiagnosticsLogger.Redact(ex.Message);
                    DiagnosticsLogger.Error("SteamConnection", _lastError);
                    throw new InvalidOperationException(_lastError, ex);
                }

                _cache = "";
                _loaded = true;
                _writeBlocked = false;
            }
        }

        private void EnsureWritableNoLock()
        {
            if (_writeBlocked && File.Exists(_path))
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(_lastError)
                        ? "Steam 手动代理凭据不可用，已保留原文件。"
                        : _lastError + " 原文件已保留，不能覆盖或删除。");
        }

        private void ValidateExistingFileBeforeMutationNoLock()
        {
            if (!_loaded && File.Exists(_path))
                _ = Load();
        }
    }
}
