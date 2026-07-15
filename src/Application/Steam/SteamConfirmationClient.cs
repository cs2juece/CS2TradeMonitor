using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.Domain.Steam;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CS2TradeMonitor.Application.Steam.Auth;

namespace CS2TradeMonitor.Application.Steam
{
    public sealed class SteamConfirmationClient : ISteamConfirmationClient
    {
        private readonly HttpClient? _http;
        private readonly ISteamRoutedHttpClientFactory _httpFactory;
        private readonly SemaphoreSlim _throttleSemaphore = new(1, 1);
        private DateTime _lastRequestTime = DateTime.MinValue;
        private readonly TimeSpan _minInterval = TimeSpan.FromMilliseconds(500);
        private bool _timeSyncAttempted;

        public long TimeOffset { get; set; } = 0;

        public SteamConfirmationClient(HttpClient? http = null)
            : this(SteamServiceRuntimeServices.ResolveRoutedHttpFactory(), http)
        {
        }

        internal SteamConfirmationClient(ISteamRoutedHttpClientFactory httpFactory, HttpClient? http = null)
        {
            _httpFactory = httpFactory ?? throw new ArgumentNullException(nameof(httpFactory));
            _http = http;
        }

        private async Task ThrottleAsync()
        {
            var now = DateTime.UtcNow;
            var elapsed = now - _lastRequestTime;
            if (elapsed < _minInterval)
            {
                var delay = _minInterval - elapsed;
                await Task.Delay(delay);
            }
            _lastRequestTime = DateTime.UtcNow;
        }

        public async Task SyncTimeOffsetAsync()
        {
            try
            {
                using var owned = await CreateOwnedClientIfNeededAsync();
                var http = _http ?? owned!;
                using var req = new HttpRequestMessage(HttpMethod.Post, SteamUrls.QueryTimeV0001);
                req.Content = new FormUrlEncodedContent(new Dictionary<string, string>());
                using var resp = await http.SendAsync(req);
                _timeSyncAttempted = true;
                if (resp.IsSuccessStatusCode)
                {
                    string json = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("response", out var response) &&
                        response.TryGetProperty("server_time", out var serverTimeProp))
                    {
                        long serverTime = ReadInt64(serverTimeProp);
                        if (serverTime > 0)
                        {
                            long localTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            TimeOffset = serverTime - localTime;
                            SteamOfferAuditLog.InfoThrottled(
                                "steam-time-sync-success",
                                $"Synced Steam time offset. ServerTime={serverTime}, LocalTime={localTime}, Offset={TimeOffset}s",
                                TimeSpan.FromMinutes(10));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _timeSyncAttempted = true;
                SteamOfferAuditLog.Error("Failed to sync Steam time offset", ex);
            }
        }

        public async Task<string> FetchConfirmationsRawAsync(SteamAuthCredential credential)
        {
            await _throttleSemaphore.WaitAsync();
            try
            {
                if (!_timeSyncAttempted)
                {
                    await SyncTimeOffsetAsync();
                }

                await ThrottleAsync();
                string url = SteamUrls.CommunityBase + "/mobileconf/getlist?" + BuildConfirmationQuery(credential, "conf");
                using var owned = await CreateOwnedClientIfNeededAsync();
                var http = _http ?? owned!;
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                ApplySteamCookies(req, credential);
                using var resp = await http.SendAsync(req);
                string text = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                    throw BuildHttpException(resp.StatusCode, text, "Steam 移动确认列表");
                return text;
            }
            finally
            {
                _throttleSemaphore.Release();
            }
        }

        public async Task<string> FetchConfirmationDetailsHtmlAsync(SteamAuthCredential credential, string confirmationId)
        {
            confirmationId = (confirmationId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(confirmationId))
                throw new ArgumentException("Steam 移动确认 ID 不能为空。", nameof(confirmationId));

            await _throttleSemaphore.WaitAsync();
            try
            {
                if (!_timeSyncAttempted)
                {
                    await SyncTimeOffsetAsync();
                }

                await ThrottleAsync();
                string url = SteamUrls.CommunityBase + "/mobileconf/details/" + Uri.EscapeDataString(confirmationId)
                    + "?" + BuildConfirmationQuery(credential, "details" + confirmationId);
                using var owned = await CreateOwnedClientIfNeededAsync();
                var http = _http ?? owned!;
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                ApplySteamCookies(req, credential);
                using var resp = await http.SendAsync(req);
                string text = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                    throw BuildHttpException(resp.StatusCode, text, "Steam 移动确认详情");

                try
                {
                    using var doc = JsonDocument.Parse(text);
                    if (GetBool(doc.RootElement, "needauth", "needAuthentication"))
                        throw new SteamAuthExpiredException("Steam 移动确认详情返回 needauth，登录状态已失效。");

                    string html = GetString(doc.RootElement, "html");
                    if (!string.IsNullOrWhiteSpace(html))
                        return html;

                    string message = GetString(doc.RootElement, "message", "msg", "error", "errmsg");
                    if (!GetBool(doc.RootElement, "success") && !string.IsNullOrWhiteSpace(message))
                        throw new InvalidOperationException(message);
                }
                catch (JsonException)
                {
                    // Older Steam endpoints may return raw HTML; keep it as a fallback.
                }

                return text;
            }
            finally
            {
                _throttleSemaphore.Release();
            }
        }

        public async Task<bool> SendConfirmationAjaxAsync(SteamAuthCredential credential, string confirmationId, string confirmationKey, string op)
        {
            await _throttleSemaphore.WaitAsync();
            try
            {
                await ThrottleAsync();
                string tag = op == "allow" ? "accept" : "reject";
                string url = SteamUrls.CommunityBase + "/mobileconf/ajaxop?op=" + Uri.EscapeDataString(op)
                    + "&" + BuildConfirmationQuery(credential, tag)
                    + "&cid=" + Uri.EscapeDataString(confirmationId)
                    + "&ck=" + Uri.EscapeDataString(confirmationKey);

                using var owned = await CreateOwnedClientIfNeededAsync();
                var http = _http ?? owned!;
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                ApplySteamCookies(req, credential);
                using var resp = await http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                    return false;

                string text = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(text);
                return GetBool(doc.RootElement, "success");
            }
            finally
            {
                _throttleSemaphore.Release();
            }
        }

        public async Task<SteamConfirmationBatchResult> SendMultipleConfirmationsAsync(
            SteamAuthCredential credential,
            IReadOnlyList<SteamConfirmationRequest> confirmations,
            string op)
        {
            if (confirmations == null || confirmations.Count == 0)
                return SteamConfirmationBatchResult.Failed("没有可提交的移动确认。");

            await _throttleSemaphore.WaitAsync();
            try
            {
                await ThrottleAsync();
                string tag = op == "allow" ? "accept" : "reject";
                string url = SteamUrls.CommunityBase + "/mobileconf/multiajaxop?op=" + Uri.EscapeDataString(op)
                    + "&" + BuildConfirmationQuery(credential, tag);

                var form = new List<KeyValuePair<string, string>>(confirmations.Count * 2);
                foreach (var item in confirmations)
                {
                    form.Add(new KeyValuePair<string, string>("cid[]", item.ConfirmationId));
                    form.Add(new KeyValuePair<string, string>("ck[]", item.ConfirmationKey));
                }

                using var owned = await CreateOwnedClientIfNeededAsync();
                var http = _http ?? owned!;
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                ApplySteamCookies(req, credential);
                req.Content = new FormUrlEncodedContent(form);
                using var resp = await http.SendAsync(req);
                string text = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                    return SteamConfirmationBatchResult.Failed($"Steam HTTP {(int)resp.StatusCode}");

                try
                {
                    using var doc = JsonDocument.Parse(text);
                    if (GetBool(doc.RootElement, "success"))
                        return SteamConfirmationBatchResult.Success(confirmations.Count);

                    string message = GetString(doc.RootElement, "message", "error", "errmsg");
                    return SteamConfirmationBatchResult.Failed(string.IsNullOrWhiteSpace(message)
                        ? "Steam 返回批量确认失败。"
                        : message);
                }
                catch (JsonException)
                {
                    return SteamConfirmationBatchResult.Failed("Steam 返回无法解析。");
                }
            }
            finally
            {
                _throttleSemaphore.Release();
            }
        }

        private static void ApplySteamCookies(HttpRequestMessage req, SteamAuthCredential credential)
        {
            // Do NOT log the cookies or credentials.
            req.Headers.TryAddWithoutValidation("Cookie", SteamLoginCookieHelper.BuildCookieHeader(new SteamWebCookies
            {
                SessionId = credential.SessionId,
                SteamLoginSecure = credential.SteamLoginSecure,
                SteamLogin = credential.SteamLogin
            }));
            req.Headers.TryAddWithoutValidation("Referer", SteamUrls.CommunityBase + "/mobileconf/conf");
            req.Headers.TryAddWithoutValidation("X-Requested-With", "com.valvesoftware.android.steam.community");
        }

        private async Task<HttpClient?> CreateOwnedClientIfNeededAsync()
        {
            if (_http != null)
                return null;

            var http = await _httpFactory.CreateResolvedAsync(
                10,
                decompression: DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                useCookies: false).ConfigureAwait(false);
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) CS2TradeMonitor/1.0");
            return http;
        }

        private string BuildConfirmationQuery(SteamAuthCredential credential, string tag)
        {
            long time = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + TimeOffset;
            return "p=" + Uri.EscapeDataString(credential.DeviceId)
                + "&a=" + Uri.EscapeDataString(credential.SteamId)
                + "&k=" + Uri.EscapeDataString(SteamCryptoHelper.GenerateConfirmationHash(credential.IdentitySecret, time, tag))
                + "&t=" + time.ToString(CultureInfo.InvariantCulture)
                + "&m=react"
                + "&tag=" + Uri.EscapeDataString(tag);
        }

        private static bool GetBool(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out var value, names)) return false;
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            return bool.TryParse(value.ToString(), out bool result) && result;
        }

        private static string GetString(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out var value, names)) return "";
            if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? "";
            if (value.ValueKind == JsonValueKind.Number) return value.ToString();
            return "";
        }

        private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
        {
            value = default;
            if (element.ValueKind != JsonValueKind.Object) return false;
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out value))
                    return true;
            }
            foreach (var prop in element.EnumerateObject())
            {
                if (names.Any(name => prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    value = prop.Value;
                    return true;
                }
            }
            return false;
        }

        private static long ReadInt64(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
                return number;
            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number))
                return number;
            return 0;
        }

        private static Exception BuildHttpException(HttpStatusCode statusCode, string body, string step)
        {
            string summary = BuildBodySummary(body);
            int code = (int)statusCode;
            if (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden)
                return new SteamAuthExpiredException($"{step}返回 HTTP {code}，Steam 登录状态已失效。", code);

            return new SteamTransientSteamException($"{step}暂时不可用：HTTP {code}，响应摘要：{summary}", code);
        }

        private static string BuildBodySummary(string body)
        {
            string clean = SteamOfferAuditLog.RedactSecrets(body ?? "");
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+", " ").Trim();
            return clean.Length <= 180 ? clean : clean[..180] + "...";
        }
    }

    public sealed class SteamConfirmationRequest
    {
        public string TradeOfferId { get; set; } = "";
        public string ConfirmationId { get; set; } = "";
        public string ConfirmationKey { get; set; } = "";
    }

    public sealed class SteamConfirmationBatchResult
    {
        public bool Ok { get; set; }
        public int AcceptedCount { get; set; }
        public string Message { get; set; } = "";

        public static SteamConfirmationBatchResult Success(int acceptedCount) => new()
        {
            Ok = true,
            AcceptedCount = acceptedCount,
            Message = $"Steam 已批量确认 {acceptedCount} 条。"
        };

        public static SteamConfirmationBatchResult Failed(string message) => new()
        {
            Ok = false,
            AcceptedCount = 0,
            Message = message
        };
    }
}
