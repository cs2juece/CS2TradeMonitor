using CS2TradeMonitor.src.SystemServices;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CS2TradeMonitor.Application.YouPin
{
    internal static class YouPinMobileApiClient
    {
        public const string BaseUrl = YouPinUrls.ApiBase;
        private const string DeviceProfileFileName = "youpin_device_profile.json";
        internal const string AppVersion = "5.45.4";
        private const string AndroidVersion = "15";
        private const string DeviceBrand = "xiaomi";
        private static readonly object ProfileLock = new();
        private static readonly JsonSerializerOptions ProfileJsonOptions = new() { WriteIndented = true };
        private static readonly ConcurrentDictionary<string, EndpointGate> EndpointGates = new(StringComparer.OrdinalIgnoreCase);
        private static YouPinDeviceProfile? _profile;
        private static string _profileError = "";
        private static string? _deviceProfilePathForTests;
        private static string DeviceProfilePath => _deviceProfilePathForTests
            ?? RuntimeDataPaths.GetSecureFilePath(DeviceProfileFileName);

        public static StringContent JsonContent(object payload)
        {
            return new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        }

        public static void ApplyHeaders(HttpRequestMessage req, string token, string deviceToken, string uk = "", string deviceId = "")
        {
            var profile = GetProfile();
            string device = NormalizeDeviceIdentifier(deviceToken, profile.DeviceToken);
            string deviceIdValue = NormalizeDeviceIdentifier(deviceId, device);
            string requestTag = profile.RequestTag;
            string ukValue = FirstText(uk, profile.Uk, profile.DeviceUk);
            if (!string.IsNullOrWhiteSpace(uk))
                PersistUk(ukValue);

            req.Headers.TryAddWithoutValidation("authorization", string.IsNullOrWhiteSpace(token) ? "Bearer " : "Bearer " + token.Trim());
            if (!string.IsNullOrWhiteSpace(ukValue))
                req.Headers.TryAddWithoutValidation("uk", ukValue);
            req.Headers.TryAddWithoutValidation("user-agent", $"Android/{AndroidVersion} official com.uu898.uuhavequality/{AppVersion} okhttp/4.9.3");
            req.Headers.TryAddWithoutValidation("App-Version", AppVersion);
            req.Headers.TryAddWithoutValidation("AppType", "4");
            req.Headers.TryAddWithoutValidation("deviceType", "1");
            req.Headers.TryAddWithoutValidation("package-type", "uuyp");
            req.Headers.TryAddWithoutValidation("DeviceToken", device);
            req.Headers.TryAddWithoutValidation("DeviceId", deviceIdValue);
            req.Headers.TryAddWithoutValidation("deviceUk", profile.DeviceUk);
            req.Headers.TryAddWithoutValidation("platform", "android");
            req.Headers.TryAddWithoutValidation("Gameid", "730");
            req.Headers.TryAddWithoutValidation("requestTag", requestTag);
            req.Headers.TryAddWithoutValidation("deviceBrand", DeviceBrand);
            req.Headers.TryAddWithoutValidation("systemVersion", AndroidVersion);
            req.Headers.TryAddWithoutValidation("App-Source", "h5");
            req.Headers.TryAddWithoutValidation("traceId", RandomHex(32));
            req.Headers.TryAddWithoutValidation("currentTheme", "Dark");
            req.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.8");
            req.Headers.TryAddWithoutValidation("Device-Info", JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["deviceType"] = deviceIdValue,
                ["systemName "] = "Android",
                ["hasSteamApp"] = 1,
                ["deviceId"] = deviceIdValue,
                ["requestTag"] = requestTag,
                ["systemVersion"] = AndroidVersion
            }));
        }

        public static void ApplyH5WebViewHeaders(HttpRequestMessage req, string uk = "", string userId = "", string deviceToken = "")
        {
            var profile = GetProfile();
            string ukValue = FirstText(uk, profile.Uk, profile.DeviceUk);
            string device = NormalizeDeviceIdentifier(deviceToken, profile.DeviceToken);
            string uid = (userId ?? "").Trim();
            foreach (string header in new[]
            {
                "user-agent",
                "AppType",
                "DeviceToken",
                "DeviceId",
                "deviceType",
                "Gameid",
                "requestTag",
                "deviceBrand",
                "Device-Info",
                "traceId",
                "currentTheme",
                "Accept-Language"
            })
            {
                req.Headers.Remove(header);
            }

            req.Headers.Remove("user-agent");
            req.Headers.TryAddWithoutValidation(
                "user-agent",
                $"Mozilla/5.0 (Linux; Android {AndroidVersion}; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/148.0.7778.215 Mobile Safari/537.36 {{\"package-type\":\"uuyp\"}} uuyp/appVersion={AppVersion}&uid={uid}&platform=Android&currentTheme=Dark&uk={ukValue}&deviceUk={profile.DeviceUk}&globalCache=false");
            req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            req.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7");
            req.Headers.TryAddWithoutValidation("appType", "4");
            req.Headers.TryAddWithoutValidation("deviceToken", device);
            req.Headers.TryAddWithoutValidation("Origin", YouPinUrls.HybridBase);
            req.Headers.TryAddWithoutValidation("Referer", YouPinUrls.HybridBaseWithSlash);
            req.Headers.TryAddWithoutValidation("X-Requested-With", "com.uu898.uuhavequality");
            req.Headers.TryAddWithoutValidation("sec-ch-ua", "\"Chromium\";v=\"148\", \"Android WebView\";v=\"148\", \"Not/A)Brand\";v=\"99\"");
            req.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?1");
            req.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Android\"");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-site");
        }

        public static string GetDeviceToken() => GetProfile().DeviceToken;

        public static string GetDeviceId() => GetProfile().DeviceId;

        public static string GetPersistedUk() => GetProfile().Uk;

        internal static string DeviceProfileError
        {
            get
            {
                lock (ProfileLock)
                    return _profileError;
            }
        }

        public static void PersistUk(string uk)
        {
            string value = (uk ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
                return;

            lock (ProfileLock)
            {
                var profile = LoadOrCreateProfileNoLock();
                if (string.Equals(profile.Uk, value, StringComparison.Ordinal))
                    return;

                YouPinDeviceProfile updated = CloneProfile(profile);
                updated.Uk = value;
                SaveProfileNoLock(updated);
                _profile = updated;
            }
        }

        public static async Task<HttpResponseMessage> SendAsync(
            HttpClient http,
            HttpRequestMessage request,
            string action,
            CancellationToken cancellationToken = default)
        {
            var uri = ResolveUri(http.BaseAddress, request.RequestUri);
            var gate = EndpointGates.GetOrAdd(BuildEndpointKey(uri), _ => new EndpointGate());
            await gate.WaitAsync(uri, cancellationToken).ConfigureAwait(false);

            var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                gate.ReportRateLimit("HTTP 429 Too Many Requests");
            else if (IsTransientServerError(response.StatusCode))
                gate.ReportTransientFailure($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            else if ((int)response.StatusCode < 400)
                gate.ReportSuccess();
            return response;
        }

        public static async Task<JsonDocument> ReadJsonDocumentAsync(HttpResponseMessage response, string action)
        {
            string body = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(body))
                throw new InvalidOperationException($"{action}失败：悠悠有品返回空响应。");

            JsonDocument document = ParseJson(body, action);
            ObserveResponse(response.RequestMessage?.RequestUri, body, document.RootElement);
            return document;
        }

        public static JsonDocument ParseJson(string body, string action)
        {
            try
            {
                return JsonDocument.Parse(body);
            }
            catch (JsonException ex)
            {
                string message = !string.IsNullOrEmpty(body) && body[0] == '\u001f'
                    ? $"{action}失败：悠悠有品响应压缩内容未正确解压，请重试。"
                    : $"{action}失败：悠悠有品返回内容格式异常。";
                throw new InvalidOperationException(message, ex);
            }
        }

        public static bool IsLoginExpired(int code, string? message)
        {
            string msg = message ?? "";
            return code == 401
                || code == 403
                || msg.Contains("登录状态失效", StringComparison.Ordinal)
                || msg.Contains("登录已失效", StringComparison.Ordinal)
                || msg.Contains("登录过期", StringComparison.Ordinal)
                || msg.Contains("身份验证失效", StringComparison.Ordinal);
        }

        public static bool LooksLikeRateLimitOrRiskControl(string? text)
        {
            string value = text ?? "";
            return value.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
                || value.Contains("HTTP 429", StringComparison.OrdinalIgnoreCase)
                || value.Contains("访问频繁", StringComparison.Ordinal)
                || value.Contains("请求频繁", StringComparison.Ordinal)
                || value.Contains("访问过快", StringComparison.Ordinal)
                || value.Contains("操作频繁", StringComparison.Ordinal)
                || value.Contains("限流", StringComparison.Ordinal)
                || value.Contains("风控", StringComparison.Ordinal);
        }

        public static bool LooksLikeSignatureFailure(string? text)
        {
            string value = text ?? "";
            return value.Contains("signature", StringComparison.OrdinalIgnoreCase)
                || value.Contains("sign-token", StringComparison.OrdinalIgnoreCase)
                || value.Contains("签名", StringComparison.Ordinal)
                || value.Contains("验签", StringComparison.Ordinal);
        }

        public static bool LooksLikeEncryptionRequirement(string? text)
        {
            string value = text ?? "";
            return value.Contains("encrypt", StringComparison.OrdinalIgnoreCase)
                || value.Contains("decrypt", StringComparison.OrdinalIgnoreCase)
                || value.Contains("encryptedData", StringComparison.OrdinalIgnoreCase)
                || value.Contains("加密", StringComparison.Ordinal)
                || value.Contains("解密", StringComparison.Ordinal)
                || value.Contains("密文", StringComparison.Ordinal);
        }

        public static string Sanitize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string value = text.Replace("\r", " ").Replace("\n", " ").Trim();

            // Redact key-value pairs with colon, equals, or JSON format
            string[] keys = { "token", "devicetoken", "cookie", "access-token", "steamloginsecure", "sendkey", "spt", "authorization", "bearer", "deviceid", "sessionid" };
            foreach (var key in keys)
            {
                // Matches key = "value" or key = value or key : "value" or key : value, including support for bearer prefix inside values
                value = System.Text.RegularExpressions.Regex.Replace(
                    value,
                    $@"(?i)""?{key}""?\s*[:=]\s*(?:""[^""]*""|'[^']*'|(?i:bearer\s+[^,\s;]+)|[^,\s;]+)",
                    $"{key}=***");
            }

            // Redact raw "bearer <token>"
            value = System.Text.RegularExpressions.Regex.Replace(
                value,
                @"(?i)bearer\s+[a-zA-Z0-9_\-\.\+/=]{8,}",
                "Bearer ***");

            value = System.Text.RegularExpressions.Regex.Replace(
                value,
                @"(?i)\bSPT[_a-zA-Z0-9\-]{6,}\b",
                "SPT_***");
            value = System.Text.RegularExpressions.Regex.Replace(
                value,
                @"(?i)\bSCT[a-zA-Z0-9_\-]{6,}\b",
                "SCT***");
            value = System.Text.RegularExpressions.Regex.Replace(
                value,
                @"(?i)\bsctp[a-zA-Z0-9_\-]{6,}\b",
                "sctp***");
            value = System.Text.RegularExpressions.Regex.Replace(
                value,
                @"(?i)steamLoginSecure\s+[a-zA-Z0-9_%\-\.\+/=]{8,}",
                "steamLoginSecure ***");

            if (value.Length > 180)
            {
                int originalLength = value.Length;
                value = value[..180] + $"... (len={originalLength})";
            }

            return value;
        }

        public static Exception WrapException(Exception ex, string action)
        {
            if (ex is InvalidOperationException && (ex.Message.Contains("登录") || ex.Message.Contains("token") || ex.Message.Contains("失效")))
            {
                return new InvalidOperationException("悠悠有品登录状态失效，请重新登录。", ex);
            }
            if (ex is InvalidOperationException)
            {
                if (!ex.Message.Contains(action))
                {
                    return new InvalidOperationException($"{action}失败：{ex.Message}", ex);
                }
                return ex;
            }
            if (ex is HttpRequestException httpEx)
            {
                string statusMsg = httpEx.StatusCode.HasValue ? $" (HTTP {(int)httpEx.StatusCode.Value})" : "";
                return new InvalidOperationException($"{action}失败：网络连接异常，无法访问悠悠有品服务器{statusMsg}，请检查网络或代理设置。", ex);
            }
            if (ex is TaskCanceledException || ex is TimeoutException)
            {
                return new InvalidOperationException($"{action}失败：请求悠悠有品超时，请检查网络或重试。", ex);
            }
            if (ex is JsonException)
            {
                return new InvalidOperationException($"{action}失败：悠悠有品返回的数据格式异常，解析失败。", ex);
            }
            return new InvalidOperationException($"{action}失败：{ex.Message}", ex);
        }

        private static void ObserveResponse(Uri? uri, string body, JsonElement root)
        {
            if (uri == null || string.IsNullOrWhiteSpace(body))
                return;

            var gate = EndpointGates.GetOrAdd(BuildEndpointKey(uri), _ => new EndpointGate());
            bool isSuccess = IsSuccessResponse(root);

            // Only check signature/encryption issues on non-success responses.
            // Successful inventory responses often contain sticker names like "签名贴纸"
            // which would falsely trigger these checks.
            if (!isSuccess)
            {
                if (LooksLikeRateLimitOrRiskControl(body))
                    gate.ReportRateLimit(Sanitize(body));
                if (LooksLikeSignatureFailure(body))
                    DiagnosticsLogger.InfoThrottled(
                        "YouPin",
                        "signature-risk:" + BuildEndpointKey(uri),
                        "悠悠有品返回疑似签名/风控提示：" + Sanitize(body),
                        TimeSpan.FromMinutes(30));
                if (LooksLikeEncryptionRequirement(body))
                    DiagnosticsLogger.InfoThrottled(
                        "YouPin",
                        "encryption-risk:" + BuildEndpointKey(uri),
                        "悠悠有品返回疑似加密/解密提示：" + Sanitize(body),
                        TimeSpan.FromMinutes(30));
            }
        }

        internal static bool IsSuccessResponse(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (!root.TryGetProperty("code", out JsonElement code)
                && !root.TryGetProperty("Code", out code))
            {
                return false;
            }

            return code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out int numericCode)
                ? numericCode == 0
                : code.ValueKind == JsonValueKind.String
                    && int.TryParse(code.GetString(), out int textCode)
                    && textCode == 0;
        }

        private static YouPinDeviceProfile GetProfile()
        {
            lock (ProfileLock)
            {
                return CloneProfile(LoadOrCreateProfileNoLock());
            }
        }

        private static YouPinDeviceProfile LoadOrCreateProfileNoLock()
        {
            if (_profile != null)
                return _profile;

            if (!File.Exists(DeviceProfilePath))
            {
                YouPinDeviceProfile created = CreateProfile();
                SaveProfileNoLock(created);
                _profileError = "";
                _profile = created;
                return created;
            }

            try
            {
                string json = File.ReadAllText(DeviceProfilePath);
                YouPinDeviceProfile stored = JsonSerializer.Deserialize<YouPinDeviceProfile>(json)
                    ?? throw new InvalidDataException("设备档案结构为空。");
                ValidateProfile(stored);
                _profileError = "";
                _profile = stored;
                return stored;
            }
            catch (Exception ex)
            {
                _profileError = "悠悠设备档案读取失败：" + DiagnosticsLogger.Redact(ex.Message);
                DiagnosticsLogger.Error("YouPin", _profileError);
                throw new InvalidOperationException(_profileError, ex);
            }
        }

        internal static void ConfigureDeviceProfilePathForTests(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            lock (ProfileLock)
            {
                _deviceProfilePathForTests = Path.GetFullPath(path);
                _profile = null;
                _profileError = "";
            }
        }

        private static void SaveProfileNoLock(YouPinDeviceProfile profile)
        {
            try
            {
                RuntimeDataPaths.WriteTextAtomic(DeviceProfilePath, JsonSerializer.Serialize(profile, ProfileJsonOptions));
                _profileError = "";
            }
            catch (Exception ex)
            {
                _profileError = "悠悠设备档案保存失败：" + DiagnosticsLogger.Redact(ex.Message);
                DiagnosticsLogger.Error("YouPin", _profileError);
                throw new InvalidOperationException(_profileError, ex);
            }
        }

        private static void ValidateProfile(YouPinDeviceProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.DeviceToken)
                || string.IsNullOrWhiteSpace(profile.DeviceId)
                || IsLegacyDeviceIdentifier(profile.DeviceToken)
                || IsLegacyDeviceIdentifier(profile.DeviceId)
                || !string.Equals(profile.DeviceToken.Trim(), profile.DeviceId.Trim(), StringComparison.Ordinal)
                || profile.DeviceToken.Trim().Length != 24
                || (profile.RequestTag ?? "").Trim().Length != 32
                || (profile.DeviceUk ?? "").Trim().Length != 65)
            {
                throw new InvalidDataException("设备档案缺少有效的设备标识或请求标识。");
            }
        }

        private static YouPinDeviceProfile CreateProfile()
        {
            string deviceToken = CreateStableString("device-token", 24, uppercase: false);
            return new YouPinDeviceProfile
            {
                MachineGuidHash = Sha256Hex(GetMachineGuid()).ToLowerInvariant(),
                DeviceToken = deviceToken,
                DeviceId = deviceToken,
                RequestTag = CreateStableString("request-tag", 32, uppercase: true),
                DeviceUk = CreateStableString("device-uk", 65, uppercase: false),
                CreatedAt = DateTime.Now
            };
        }

        private static YouPinDeviceProfile CloneProfile(YouPinDeviceProfile profile)
        {
            return new YouPinDeviceProfile
            {
                MachineGuidHash = profile.MachineGuidHash,
                DeviceToken = profile.DeviceToken,
                DeviceId = profile.DeviceId,
                RequestTag = profile.RequestTag,
                DeviceUk = profile.DeviceUk,
                Uk = profile.Uk,
                CreatedAt = profile.CreatedAt
            };
        }

        internal static bool IsLegacyDeviceIdentifier(string? value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Trim().StartsWith("CS2M", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeDeviceIdentifier(string? value, string fallback)
        {
            string trimmed = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || IsLegacyDeviceIdentifier(trimmed))
                return fallback.Trim();

            return trimmed;
        }

        private static string CreateStableString(string purpose, int length, bool uppercase)
        {
            const string lowerChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            const string upperChars = "0123456789ABCDEF";
            string chars = uppercase ? upperChars : lowerChars;
            string seed = GetMachineGuid() + "|" + purpose + "|CS2DesktopMonitor.YouPinDevice.v1";
            var result = new StringBuilder(length);
            int counter = 0;
            while (result.Length < length)
            {
                byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed + "|" + counter.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                foreach (byte b in bytes)
                {
                    result.Append(chars[b % chars.Length]);
                    if (result.Length == length)
                        break;
                }
                counter++;
            }

            return result.ToString();
        }

        private static string GetMachineGuid()
        {
            try
            {
                string? value = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null)?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored("YouPin", "GetMachineGuid", ex, retryable: true, category: "DeviceFingerprint");
            }

            return Environment.MachineName + "|" + Environment.UserName;
        }

        private static string Sha256Hex(string text)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? "")));
        }

        private static Uri? ResolveUri(Uri? baseAddress, Uri? requestUri)
        {
            if (requestUri == null)
                return null;
            if (requestUri.IsAbsoluteUri)
                return requestUri;
            return baseAddress == null ? requestUri : new Uri(baseAddress, requestUri);
        }

        private static string BuildEndpointKey(Uri? uri)
        {
            if (uri == null)
                return "unknown";
            return uri.Host + uri.AbsolutePath;
        }

        private static bool IsTransientServerError(HttpStatusCode statusCode)
        {
            int code = (int)statusCode;
            return code == 408 || code == 500 || code == 502 || code == 503 || code == 504;
        }

        private static string FirstText(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        private static string RandomHex(int length)
        {
            const string chars = "0123456789abcdef";
            var buffer = new char[length];
            var bytes = new byte[length];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            for (int i = 0; i < length; i++)
                buffer[i] = chars[bytes[i] % chars.Length];
            return new string(buffer);
        }

        private sealed class YouPinDeviceProfile
        {
            public string MachineGuidHash { get; set; } = "";
            public string DeviceToken { get; set; } = "";
            public string DeviceId { get; set; } = "";
            public string RequestTag { get; set; } = "";
            public string DeviceUk { get; set; } = "";
            public string Uk { get; set; } = "";
            public DateTime CreatedAt { get; set; }
        }

        private sealed class EndpointGate
        {
            private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(800);
            private readonly SemaphoreSlim _semaphore = new(1, 1);
            private DateTime _lastRequestUtc = DateTime.MinValue;
            private DateTime _cooldownUntilUtc = DateTime.MinValue;
            private string _cooldownReason = "";
            private int _rateLimitFailures;

            public async Task WaitAsync(Uri? uri, CancellationToken cancellationToken)
            {
                await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    DateTime now = DateTime.UtcNow;
                    if (now < _cooldownUntilUtc)
                    {
                        string until = _cooldownUntilUtc.ToLocalTime().ToString("MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                        throw new InvalidOperationException($"悠悠有品接口限流冷却中，已停止请求。原因：{_cooldownReason}；下一步：{until} 后自动重试。");
                    }

                    TimeSpan wait = (_lastRequestUtc + MinInterval) - now;
                    if (wait > TimeSpan.Zero)
                        await Task.Delay(wait, cancellationToken).ConfigureAwait(false);

                    _lastRequestUtc = DateTime.UtcNow;
                }
                finally
                {
                    _semaphore.Release();
                }
            }

            public void ReportRateLimit(string reason)
            {
                _rateLimitFailures = Math.Min(_rateLimitFailures + 1, 4);
                int minutes = Math.Min(30, 5 * (1 << (_rateLimitFailures - 1)));
                _cooldownUntilUtc = DateTime.UtcNow.AddMinutes(minutes);
                _cooldownReason = string.IsNullOrWhiteSpace(reason) ? "请求过于频繁" : Sanitize(reason);
            }

            public void ReportTransientFailure(string reason)
            {
                _rateLimitFailures = Math.Min(_rateLimitFailures + 1, 3);
                int seconds = Math.Min(60, 5 * (1 << (_rateLimitFailures - 1)));
                _cooldownUntilUtc = DateTime.UtcNow.AddSeconds(seconds);
                _cooldownReason = string.IsNullOrWhiteSpace(reason) ? "临时服务异常" : Sanitize(reason);
            }

            public void ReportSuccess()
            {
                _rateLimitFailures = 0;
                _cooldownUntilUtc = DateTime.MinValue;
                _cooldownReason = "";
            }
        }
    }
}
