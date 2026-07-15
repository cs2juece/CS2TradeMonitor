using Microsoft.Win32;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CS2TradeMonitor.src.SystemServices
{
    internal sealed class ReloadingSystemProxy : IWebProxy
    {
        private readonly object _gate = new();
        private readonly Func<string> _captureFingerprint;
        private readonly Func<IWebProxy> _resolveProxy;
        private readonly Func<Uri, IWebProxy?>? _resolveFallbackProxy;
        private readonly Func<Uri, bool>? _isProxyAvailable;
        private readonly Func<bool>? _allowDirectFallback;
        private string _fingerprint = "";
        private IWebProxy? _current;
        private ICredentials? _credentials = CredentialCache.DefaultCredentials;

        public ReloadingSystemProxy(Func<string> captureFingerprint, Func<IWebProxy> resolveProxy)
            : this(captureFingerprint, resolveProxy, null, null, null)
        {
        }

        internal ReloadingSystemProxy(
            Func<string> captureFingerprint,
            Func<IWebProxy> resolveProxy,
            Func<Uri, IWebProxy?>? resolveFallbackProxy,
            Func<Uri, bool>? isProxyAvailable)
            : this(captureFingerprint, resolveProxy, resolveFallbackProxy, isProxyAvailable, null)
        {
        }

        internal ReloadingSystemProxy(
            Func<string> captureFingerprint,
            Func<IWebProxy> resolveProxy,
            Func<Uri, IWebProxy?>? resolveFallbackProxy,
            Func<Uri, bool>? isProxyAvailable,
            Func<bool>? allowDirectFallback)
        {
            _captureFingerprint = captureFingerprint ?? throw new ArgumentNullException(nameof(captureFingerprint));
            _resolveProxy = resolveProxy ?? throw new ArgumentNullException(nameof(resolveProxy));
            _resolveFallbackProxy = resolveFallbackProxy;
            _isProxyAvailable = isProxyAvailable;
            _allowDirectFallback = allowDirectFallback;
        }

        public ICredentials? Credentials
        {
            get => _credentials;
            set
            {
                lock (_gate)
                {
                    _credentials = value;
                    if (_current != null)
                        _current.Credentials = value;
                }
            }
        }

        public Uri? GetProxy(Uri destination)
        {
            ArgumentNullException.ThrowIfNull(destination);

            IWebProxy current = GetCurrentProxy();
            Uri? primary = ResolveProxyUri(current, destination);
            if (primary == null || primary == destination)
                return destination;
            if (IsAvailable(primary))
                return primary;

            IWebProxy? fallback = _resolveFallbackProxy?.Invoke(destination);
            if (fallback == null)
                return ResolveUnavailableRoute(primary, destination);

            fallback.Credentials = _credentials;
            Uri? secondary = ResolveProxyUri(fallback, destination);
            if (secondary == destination)
                return destination;
            return secondary != null && IsAvailable(secondary)
                ? secondary
                : ResolveUnavailableRoute(primary, destination);
        }

        public bool IsBypassed(Uri host)
            => GetProxy(host) is not Uri proxyUri || proxyUri == host;

        private bool IsAvailable(Uri proxyUri)
            => _isProxyAvailable?.Invoke(proxyUri) ?? true;

        private Uri ResolveUnavailableRoute(Uri configuredProxy, Uri destination)
            => _allowDirectFallback?.Invoke() == false ? configuredProxy : destination;

        private static Uri? ResolveProxyUri(IWebProxy proxy, Uri destination)
            => proxy.IsBypassed(destination) ? destination : proxy.GetProxy(destination);

        private IWebProxy GetCurrentProxy()
        {
            string fingerprint = _captureFingerprint();
            lock (_gate)
            {
                if (_current != null && string.Equals(_fingerprint, fingerprint, StringComparison.Ordinal))
                    return _current;

                IWebProxy replacement = _resolveProxy();
                replacement.Credentials = _credentials;
                _current = replacement;
                _fingerprint = fingerprint;
                return replacement;
            }
        }
    }

    internal static class SystemProxySettingsFingerprint
    {
        private const string InternetSettingsPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

        public static string Capture()
        {
            var values = new StringBuilder();
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: false);
                Append(values, key, "ProxyEnable");
                Append(values, key, "ProxyServer");
                Append(values, key, "ProxyOverride");
                Append(values, key, "AutoConfigURL");
                Append(values, key, "AutoDetect");
            }
            catch (Exception ex)
            {
                values.Append("registry-error:").Append(ex.GetType().FullName);
            }

            foreach (string name in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY" })
                values.Append('|').Append(name).Append('=').Append(Environment.GetEnvironmentVariable(name) ?? "");

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(values.ToString())));
        }

        private static void Append(StringBuilder output, RegistryKey? key, string name)
        {
            object? value = key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            output.Append('|').Append(name).Append('=').Append(value?.ToString() ?? "");
        }
    }

    internal static class WindowsSystemProxyResolver
    {
        private const string InternetSettingsPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

        public static IWebProxy? Resolve(Uri destination)
        {
            ArgumentNullException.ThrowIfNull(destination);

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: false);
                if (Convert.ToInt32(key?.GetValue("ProxyEnable") ?? 0, System.Globalization.CultureInfo.InvariantCulture) != 1)
                    return null;

                string proxyServer = key?.GetValue("ProxyServer")?.ToString() ?? "";
                if (!TryParseProxyUri(proxyServer, destination.Scheme, out Uri proxyUri))
                    return null;

                string proxyOverride = key?.GetValue("ProxyOverride")?.ToString() ?? "";
                return CreateProxy(proxyUri, proxyOverride);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored(
                    "HTTP",
                    "ResolveWindowsProxyFallback",
                    ex,
                    retryable: true,
                    category: "Proxy");
                return null;
            }
        }

        public static bool IsDirectFallbackAllowed()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: false);
                string autoConfigUrl = key?.GetValue("AutoConfigURL")?.ToString() ?? "";
                bool autoDetect = Convert.ToInt32(
                    key?.GetValue("AutoDetect") ?? 0,
                    System.Globalization.CultureInfo.InvariantCulture) == 1;
                return string.IsNullOrWhiteSpace(autoConfigUrl) && !autoDetect;
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored(
                    "HTTP",
                    "InspectAutomaticProxyPolicy",
                    ex,
                    retryable: true,
                    category: "Proxy");
                return false;
            }
        }

        internal static IWebProxy CreateProxy(Uri proxyUri, string proxyOverride)
        {
            ArgumentNullException.ThrowIfNull(proxyUri);
            string[] entries = (proxyOverride ?? "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool bypassOnLocal = entries.Any(entry => string.Equals(entry, "<local>", StringComparison.OrdinalIgnoreCase));
            string[] bypassList = entries
                .Where(entry => !entry.StartsWith('<') || !entry.EndsWith('>'))
                .Select(ToBypassRegex)
                .ToArray();

            return new WebProxy(proxyUri)
            {
                BypassProxyOnLocal = bypassOnLocal,
                BypassList = bypassList,
                Credentials = CredentialCache.DefaultCredentials
            };
        }

        internal static bool TryParseProxyUri(
            string proxyServer,
            string destinationScheme,
            out Uri proxyUri)
        {
            proxyUri = null!;
            string value = (proxyServer ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string selected = value;
            string selectedKind = "http";
            if (value.Contains('=', StringComparison.Ordinal))
            {
                var entries = value
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(entry => entry.Split('=', 2, StringSplitOptions.TrimEntries))
                    .Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
                    .ToList();
                string scheme = (destinationScheme ?? "").Trim();
                string[] preferredKinds = string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                    ? new[] { "https", "socks", "socks5" }
                    : new[] { "http", "socks", "socks5" };
                string[]? match = preferredKinds
                    .Select(kind => entries.FirstOrDefault(parts => string.Equals(parts[0], kind, StringComparison.OrdinalIgnoreCase)))
                    .FirstOrDefault(parts => parts != null);
                if (match == null)
                    return false;

                selectedKind = match[0];
                selected = match[1];
            }

            string absolute = selected.Contains("://", StringComparison.Ordinal)
                ? selected
                : (selectedKind.StartsWith("socks", StringComparison.OrdinalIgnoreCase) ? "socks5://" : "http://") + selected;
            if (!Uri.TryCreate(absolute, UriKind.Absolute, out Uri? parsed)
                || string.IsNullOrWhiteSpace(parsed.Host)
                || parsed.Port <= 0)
            {
                return false;
            }

            proxyUri = parsed;
            return true;
        }

        private static string ToBypassRegex(string pattern)
        {
            string escaped = Regex.Escape(pattern)
                .Replace("\\*", ".*", StringComparison.Ordinal)
                .Replace("\\?", ".", StringComparison.Ordinal);
            return "^" + escaped + "$";
        }
    }

    internal static class LoopbackProxyAvailability
    {
        public static bool IsAvailable(Uri proxyUri)
        {
            ArgumentNullException.ThrowIfNull(proxyUri);
            if (!proxyUri.IsLoopback)
                return true;
            if (proxyUri.Port <= 0)
                return false;

            try
            {
                IPAddress[] proxyAddresses = IPAddress.TryParse(proxyUri.DnsSafeHost, out IPAddress? parsed)
                    ? new[] { Normalize(parsed) }
                    : Dns.GetHostAddresses(proxyUri.DnsSafeHost).Select(Normalize).ToArray();
                return IPGlobalProperties
                    .GetIPGlobalProperties()
                    .GetActiveTcpListeners()
                    .Any(endpoint => endpoint.Port == proxyUri.Port
                        && CanServe(endpoint.Address, proxyAddresses));
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored(
                    "HTTP",
                    "InspectLoopbackProxyListener",
                    ex,
                    retryable: true,
                    category: "Proxy");
                return false;
            }
        }

        private static bool CanServe(IPAddress listenerAddress, IReadOnlyCollection<IPAddress> proxyAddresses)
        {
            if (listenerAddress.Equals(IPAddress.Any) || listenerAddress.Equals(IPAddress.IPv6Any))
                return true;

            IPAddress normalizedListener = Normalize(listenerAddress);
            return proxyAddresses.Contains(normalizedListener);
        }

        private static IPAddress Normalize(IPAddress address)
            => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    internal static class AdaptiveSystemProxyFactory
    {
        private static readonly IWebProxy Shared = CreateCore();

        public static IWebProxy Create()
            => Shared;

        private static IWebProxy CreateCore()
        {
            return new ReloadingSystemProxy(
                SystemProxySettingsFingerprint.Capture,
                WebRequest.GetSystemWebProxy,
                WindowsSystemProxyResolver.Resolve,
                LoopbackProxyAvailability.IsAvailable,
                WindowsSystemProxyResolver.IsDirectFallbackAllowed)
            {
                Credentials = CredentialCache.DefaultCredentials
            };
        }
    }
}
