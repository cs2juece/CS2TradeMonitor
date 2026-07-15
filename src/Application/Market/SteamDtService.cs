using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Application.Market
{
    public class SteamDtData
    {
        public double Index { get; set; }
        public double DiffYesterday { get; set; }
        public double DiffYesterdayRatio { get; set; }
        public long UpdateTime { get; set; }
        public DateTime RetrievedAt { get; set; } = DateTime.Now;
        public bool IsStale { get; set; }
        public string Source { get; set; } = "未获取";

        public string FormatIndex() => MarketDisplayFormatter.FormatIndex(Index);
        public string FormatChange() => MarketDisplayFormatter.FormatSignedChange(DiffYesterday);
        public string FormatRatio() => MarketDisplayFormatter.FormatSignedPercent(DiffYesterdayRatio);
    }

    internal class SteamDtApiResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        [JsonPropertyName("data")]
        public SteamDtApiData? Data { get; set; }
        [JsonPropertyName("errorCode")]
        public int ErrorCode { get; set; }
        [JsonPropertyName("errorMsg")]
        public string? ErrorMsg { get; set; }
        [JsonPropertyName("errorCodeStr")]
        public string? ErrorCodeStr { get; set; }
    }

    internal class SteamDtApiData
    {
        [JsonPropertyName("broadMarketIndex")]
        public double BroadMarketIndex { get; set; }
        [JsonPropertyName("updateTime")]
        public long UpdateTime { get; set; }
        [JsonPropertyName("diffYesterday")]
        public double DiffYesterday { get; set; }
        [JsonPropertyName("diffYesterdayRatio")]
        public double DiffYesterdayRatio { get; set; }
    }

    internal class SteamDtSystemConfigResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        [JsonPropertyName("data")]
        public SteamDtSystemConfigData? Data { get; set; }
    }

    internal class SteamDtSystemConfigData
    {
        [JsonPropertyName("systemTime")]
        public JsonElement SystemTime { get; set; }
    }

    internal class SteamDtPublicResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        [JsonPropertyName("data")]
        public SteamDtPublicData? Data { get; set; }
        [JsonPropertyName("errorMsg")]
        public string? ErrorMsg { get; set; }
        [JsonPropertyName("errorCodeStr")]
        public string? ErrorCodeStr { get; set; }
    }

    internal class SteamDtPublicData
    {
        [JsonPropertyName("index")]
        public double Index { get; set; }
        [JsonPropertyName("riseFallDiff")]
        public double RiseFallDiff { get; set; }
        [JsonPropertyName("riseFallRate")]
        public double RiseFallRate { get; set; }
        [JsonPropertyName("updateTime")]
        public JsonElement UpdateTime { get; set; }
    }

    public class SteamDtService : ISteamDtService
    {
        private const string BaseUrl = SteamDtUrls.OpenApiBase;
        private const string IndexEndpoint = "/open/cs2/broad/v1/index";

        private static SteamDtService? _instance;
        public static SteamDtService Instance => _instance ??= new SteamDtService();

        private readonly HttpClient _http;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly SemaphoreSlim _fetchLock = new(1, 1);
        private System.Threading.Timer? _timer;
        private string _apiKey = "";
        private int _refreshSec = Settings.DefaultMarketRefreshSec;
        private SteamDtData? _latest;
        private DateTime _publicCooldownUntil = DateTime.MinValue;
        private string _publicCooldownReason = "";
        private bool _lastPublicFailureWasCooldown;
        private int _publicRateLimitFailureCount;

        public SteamDtData? Latest => Volatile.Read(ref _latest);
        public string LastError { get; private set; } = "";
        public event Action? DataUpdated;

        private SteamDtService()
            : this(MarketServiceRuntimeServices.Resolve().DomesticHttpFactory)
        {
        }

        internal SteamDtService(IDomesticHttpClientFactory httpFactory)
        {
            if (httpFactory == null) throw new ArgumentNullException(nameof(httpFactory));

            _http = httpFactory.Create(15, new Uri(BaseUrl));
            _http.DefaultRequestHeaders.Add("User-Agent", "CS2TradeMonitor/1.0 (Windows; .NET)");

            _jsonOptions = ServiceInfra.DefaultJsonOptions;
        }

        public void Configure(string apiKey, int refreshSec)
        {
            _apiKey = apiKey ?? "";
            _refreshSec = Math.Max(Settings.DefaultMarketRefreshSec, refreshSec <= 0 ? Settings.DefaultMarketRefreshSec : refreshSec);

            _timer?.Dispose();
            _timer = null;
        }

        public async Task<bool> TestAndUpdateAsync(string apiKey, int refreshSec)
        {
            _apiKey = apiKey ?? "";
            _refreshSec = Math.Max(Settings.DefaultMarketRefreshSec, refreshSec <= 0 ? Settings.DefaultMarketRefreshSec : refreshSec);

            _timer?.Dispose();
            _timer = null;
            await FetchAsync(waitForLock: true);
            var latest = Volatile.Read(ref _latest);
            return latest != null && !latest.IsStale;
        }

        private async Task<long> GetServerTimeOffsetAsync()
        {
            try
            {
                var response = await _http.GetAsync(SteamDtUrls.PublicDefaultConfig);
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    var config = JsonSerializer.Deserialize<SteamDtSystemConfigResponse>(body, _jsonOptions);
                    if (config != null && config.Success && config.Data != null)
                    {
                        long serverMs = 0;
                        var sysTime = config.Data.SystemTime;
                        if (sysTime.ValueKind == JsonValueKind.String)
                        {
                            long.TryParse(sysTime.GetString(), out serverMs);
                        }
                        else if (sysTime.ValueKind == JsonValueKind.Number)
                        {
                            sysTime.TryGetInt64(out serverMs);
                        }

                        if (serverMs > 0)
                        {
                            long localMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            return serverMs - localMs;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Info("SteamDT", "Failed to calibrate server time, using local system time. Error: " + ex.Message);
            }
            return 0;
        }

        private async Task<bool> FetchFromPublicPageAsync(long timeOffset)
        {
            _lastPublicFailureWasCooldown = false;
            if (TryUseActivePublicCooldown())
            {
                return false;
            }

            if (SteamDtPublicThrottle.IsCoolingDown(out string throttleMessage))
            {
                LastError = throttleMessage;
                _lastPublicFailureWasCooldown = true;
                return false;
            }

            using SteamDtPublicLease? lease = await SteamDtPublicThrottle.TryAcquireAsync().ConfigureAwait(false);
            if (lease == null)
            {
                if (SteamDtPublicThrottle.IsCoolingDown(out throttleMessage))
                    LastError = throttleMessage;
                _lastPublicFailureWasCooldown = true;
                return false;
            }

            try
            {
                long currentMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + timeOffset;
                string msStr = currentMs.ToString();
                string ts = msStr;
                if (msStr.Length >= 12)
                {
                    string first12 = msStr.Substring(0, 12);
                    int sum = 0;
                    foreach (char c in first12)
                    {
                        if (char.IsDigit(c))
                        {
                            sum += (c - '0');
                        }
                    }
                    ts = first12 + (sum % 10).ToString();
                }

                var payload = new
                {
                    type = "BROAD",
                    level = 0,
                    platform = "ALL",
                    typeVal = ""
                };
                var content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, SteamDtUrls.WithTimestamp(SteamDtUrls.PublicBlockSummary, ts));
                request.Content = content;
                request.Headers.TryAddWithoutValidation("x-device-id", "");
                request.Headers.Add("x-device", "1");
                request.Headers.Add("x-app-version", "1.0.0");
                request.Headers.TryAddWithoutValidation("access-token", "");
                request.Headers.Add("x-currency", "CNY");
                request.Headers.Add("language", "zh_CN");
                request.Headers.TryAddWithoutValidation("Referer", SteamDtUrls.BroadSectionReferer);
                request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                var response = await _http.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    LastError = BuildPublicHttpError(response, body);
                    ApplyPublicCooldown(LastError);
                    return false;
                }

                SteamDtPublicResponse? dt;
                try
                {
                    dt = JsonSerializer.Deserialize<SteamDtPublicResponse>(body, _jsonOptions);
                }
                catch (JsonException ex)
                {
                    LastError = BuildPublicContentError(body, ex.Message);
                    ApplyPublicCooldown(LastError);
                    DiagnosticsLogger.Error("SteamDT", "SteamDT public response parse failed", ex);
                    return false;
                }

                if (dt == null || dt.Data == null || !dt.Success)
                {
                    string classified = ClassifyPublicFailure(body, "");
                    string apiError = !string.IsNullOrWhiteSpace(classified)
                        ? classified
                        : (dt?.ErrorMsg ?? dt?.ErrorCodeStr ?? "SteamDT 公开页面返回数据为空或格式异常");
                    LastError = $"SteamDT 公开页面返回失败：{apiError}";
                    ApplyPublicCooldown(apiError);
                    return false;
                }

                long updateTimeMs = 0;
                var ut = dt.Data.UpdateTime;
                if (ut.ValueKind == JsonValueKind.String)
                {
                    long.TryParse(ut.GetString(), out updateTimeMs);
                }
                else if (ut.ValueKind == JsonValueKind.Number)
                {
                    ut.TryGetInt64(out updateTimeMs);
                }

                if (updateTimeMs > 0 && updateTimeMs < 100000000000L)
                {
                    updateTimeMs *= 1000;
                }

                Volatile.Write(ref _latest, new SteamDtData
                {
                    Index = dt.Data.Index,
                    UpdateTime = updateTimeMs,
                    DiffYesterday = dt.Data.RiseFallDiff,
                    DiffYesterdayRatio = dt.Data.RiseFallRate,
                    RetrievedAt = DateTime.Now,
                    IsStale = false,
                    Source = "公开页面接口"
                });
                LastError = "";
                _publicCooldownUntil = DateTime.MinValue;
                _publicCooldownReason = "";
                _publicRateLimitFailureCount = 0;
                SteamDtPublicThrottle.ReportSuccess();
                NotifyDataUpdated();
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"SteamDT 公开页面网络访问失败：{ex.Message}";
                DiagnosticsLogger.Error("SteamDT", "FetchFromPublicPageAsync failed", ex);
                ApplyPublicCooldown(LastError);
                return false;
            }
        }

        public async Task FetchAsync(bool waitForLock = false)
        {
            if (waitForLock)
            {
                await _fetchLock.WaitAsync();
            }
            else if (!await _fetchLock.WaitAsync(0))
            {
                return;
            }

            try
            {
                bool success = false;
                string officialError = "";

                if (!string.IsNullOrWhiteSpace(_apiKey))
                {
                    try
                    {
                        var request = new HttpRequestMessage(HttpMethod.Get, IndexEndpoint);
                        request.Headers.Add("Authorization", "Bearer " + _apiKey);

                        var response = await _http.SendAsync(request);
                        var body = await response.Content.ReadAsStringAsync();
                        if (response.IsSuccessStatusCode)
                        {
                            var dt = JsonSerializer.Deserialize<SteamDtApiResponse>(body, _jsonOptions);

                            if (dt != null && dt.Data != null && dt.Success)
                            {
                                Volatile.Write(ref _latest, new SteamDtData
                                {
                                    Index = dt.Data.BroadMarketIndex,
                                    UpdateTime = dt.Data.UpdateTime,
                                    DiffYesterday = dt.Data.DiffYesterday,
                                    DiffYesterdayRatio = dt.Data.DiffYesterdayRatio,
                                    RetrievedAt = DateTime.Now,
                                    IsStale = false,
                                    Source = "官方 API"
                                });
                                LastError = "";
                                NotifyDataUpdated();
                                success = true;
                            }
                            else
                            {
                                officialError = dt?.ErrorMsg ?? dt?.ErrorCodeStr ?? "SteamDT API 返回数据为空或格式异常";
                            }
                        }
                        else
                        {
                            officialError = BuildHttpStatusError(response, body);
                        }
                    }
                    catch (Exception ex)
                    {
                        officialError = ex.Message;
                    }
                }

                if (!success)
                {
                    if (IsPublicCooldownActive())
                    {
                        success = await FetchFromPublicPageAsync(0);
                    }
                    else
                    {
                        long offset = await GetServerTimeOffsetAsync();
                        success = await FetchFromPublicPageAsync(offset);
                    }
                }

                if (!success)
                {
                    string combinedError = "";
                    if (!string.IsNullOrWhiteSpace(_apiKey))
                    {
                        combinedError = $"官方 API 失败 ({officialError})，且公开页面接口失败 ({LastError})";
                    }
                    else
                    {
                        combinedError = LastError;
                    }
                    SetFailure(combinedError, log: !_lastPublicFailureWasCooldown);
                }
            }
            finally
            {
                _fetchLock.Release();
            }
        }

        public string GetValue(string key)
        {
            var d = Volatile.Read(ref _latest);
            if (d == null)
            {
                return key.Equals("Display", StringComparison.OrdinalIgnoreCase)
                    ? MarketDisplayFormatter.GetCompactFullText(MarketDataSourceManager.SteamDtDisplayKey)
                    : "";
            }

            return key switch
            {
                "Index" => d.FormatIndex(),
                "Change" => d.FormatChange(),
                "Ratio" => d.FormatRatio(),
                "Display" => MarketDisplayFormatter.FormatCompactFullText(MarketDataSourceManager.SteamDtDisplayKey, d.Index, d.DiffYesterdayRatio),
                "Stale" => d.IsStale ? "旧" : "",
                _ => "?"
            };
        }

        private void ApplyPublicCooldown(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return;

            SteamDtPublicThrottle.ReportFailure(reason);
            var now = DateTime.Now;
            if (ContainsAny(reason, "今日访问次数超限", "访问次数超限", "明日再试"))
            {
                _publicCooldownUntil = DateTime.Today.AddDays(1).AddMinutes(5);
                _publicCooldownReason = "今日访问次数超限";
                return;
            }

            if (ContainsAny(reason, "访问速度太快", "请求太快", "频繁"))
            {
                _publicRateLimitFailureCount = Math.Min(_publicRateLimitFailureCount + 1, 4);
                int minutes = Math.Min(30, 5 * (1 << (_publicRateLimitFailureCount - 1)));
                _publicCooldownUntil = now.AddMinutes(minutes);
                _publicCooldownReason = "访问速度太快";
                return;
            }

            if (ContainsAny(reason, "HTTP 429", "Too Many Requests"))
            {
                _publicRateLimitFailureCount = Math.Min(_publicRateLimitFailureCount + 1, 4);
                int minutes = Math.Min(30, 5 * (1 << (_publicRateLimitFailureCount - 1)));
                _publicCooldownUntil = now.AddMinutes(minutes);
                _publicCooldownReason = "请求过于频繁";
                return;
            }

            if (ContainsAny(reason, "HTTP 500", "Internal Server Error", "Cannot read properties of null"))
            {
                _publicCooldownUntil = now.AddMinutes(30);
                _publicCooldownReason = "SteamDT 服务异常";
            }
        }

        private bool IsPublicCooldownActive()
        {
            return DateTime.Now < _publicCooldownUntil;
        }

        private bool TryUseActivePublicCooldown()
        {
            var now = DateTime.Now;
            if (now >= _publicCooldownUntil)
                return false;

            LastError = $"SteamDT 公开页面暂时不可用：{_publicCooldownReason}，{_publicCooldownUntil:MM-dd HH:mm:ss} 后自动重试";
            _lastPublicFailureWasCooldown = true;
            return true;
        }

        private static string BuildPublicHttpError(HttpResponseMessage response, string body)
        {
            string classified = ClassifyPublicFailure(body, "");
            string status = BuildHttpStatusError(response, body);
            return string.IsNullOrWhiteSpace(classified)
                ? $"SteamDT 公开页面请求失败：{status}"
                : $"SteamDT 公开页面请求失败：{classified}（{status}）";
        }

        private static string BuildHttpStatusError(HttpResponseMessage response, string body)
        {
            string status = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".Trim();
            string classified = ClassifyPublicFailure(body, "");
            return string.IsNullOrWhiteSpace(classified) ? status : $"{status}，{classified}";
        }

        private static string BuildPublicContentError(string body, string parseError)
        {
            string classified = ClassifyPublicFailure(body, "");
            return string.IsNullOrWhiteSpace(classified)
                ? $"SteamDT 公开页面返回内容无法解析：{parseError}"
                : $"SteamDT 公开页面返回异常：{classified}";
        }

        private static string ClassifyPublicFailure(string? body, string fallback)
        {
            if (string.IsNullOrWhiteSpace(body))
                return fallback;

            if (ContainsAny(body, "今日访问次数超限", "访问次数超限", "明日再试"))
                return "今日访问次数超限，请明日再试";

            if (ContainsAny(body, "Cannot read properties of null", "Internal Server Error"))
                return "SteamDT 服务端返回 500，页面数据暂不可用";

            if (ContainsAny(body, "Too Many Requests", "HTTP 429"))
                return "请求过于频繁";

            return fallback;
        }

        private static bool ContainsAny(string source, params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrEmpty(value) && source.Contains(value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private void SetFailure(string message, Exception? ex = null, bool log = true)
        {
            LastError = ex == null ? message : $"{message}：{ex.Message}";
            var latest = Volatile.Read(ref _latest);
            if (latest != null)
            {
                latest.IsStale = true;
                latest.Source = "缓存";
                Volatile.Write(ref _latest, latest);
            }
            if (log)
                DiagnosticsLogger.Error("SteamDT", message, ex);
            NotifyDataUpdated();
        }

        private void NotifyDataUpdated()
        {
            try
            {
                DataUpdated?.Invoke();
            }
            catch
            {
                // UI subscribers must not be able to break the refresh timer.
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _http.Dispose();
        }
    }
}
