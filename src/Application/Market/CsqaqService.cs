using System;
using System.Linq;
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
    public class CsqaqData
    {
        public double MarketIndex { get; set; }
        public double Change { get; set; }
        public double ChangeRate { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime RetrievedAt { get; set; } = DateTime.Now;
        public bool IsStale { get; set; }
        public string Source { get; set; } = "";

        public string FormatIndex() => MarketDisplayFormatter.FormatIndex(MarketIndex);
        public string FormatChange() => MarketDisplayFormatter.FormatSignedChange(Change);
        public string FormatRate() => MarketDisplayFormatter.FormatSignedPercent(ChangeRate);
    }

    internal class CsqaqApiResponse
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("data")]
        public CsqaqApiData? Data { get; set; }
    }

    internal class CsqaqApiData
    {
        [JsonPropertyName("sub_index_data")]
        public CsqaqIndexItem[]? SubIndexData { get; set; }
    }

    internal class CsqaqIndexItem
    {
        [JsonPropertyName("name_key")]
        public string? NameKey { get; set; }

        [JsonPropertyName("market_index")]
        public double MarketIndex { get; set; }

        [JsonPropertyName("chg_num")]
        public double Change { get; set; }

        [JsonPropertyName("chg_rate")]
        public double ChangeRate { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }

    public class CsqaqService : ICsqaqService
    {
        private const string BaseUrl = CsqaqUrls.ApiBase;
        private const string CurrentDataEndpoint = "/api/v1/current_data?type=init";
        private const int StaleAfterConsecutiveFailures = 3;
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

        private static CsqaqService? _instance;
        public static CsqaqService Instance => _instance ??= new CsqaqService();

        private readonly HttpClient _http;
        private readonly CsqaqRequestRateLimiter _requestRateLimiter;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly SemaphoreSlim _fetchLock = new(1, 1);
        private DateTime _lastAttempt = DateTime.MinValue;
        private DateTime _cooldownUntil = DateTime.MinValue;
        private string _cooldownReason = "";
        private int _rateLimitFailureCount;
        private int _consecutiveFailureCount;
        private CsqaqData? _latest;
        private string _apiToken = "";

        public CsqaqData? Latest => Volatile.Read(ref _latest);
        public string LastError { get; private set; } = "";
        public event Action? DataUpdated;

        private void NotifyDataUpdated()
        {
            try
            {
                DataUpdated?.Invoke();
            }
            catch
            {
                // UI 订阅者异常不能反向打断数据源刷新定时器。
            }
        }

        private CsqaqService()
            : this(MarketServiceRuntimeServices.Resolve())
        {
        }

        private CsqaqService(MarketServiceRuntimeServices runtimeServices)
            : this(runtimeServices.DomesticHttpFactory, runtimeServices.CsqaqRateLimiter)
        {
        }

        internal CsqaqService(IDomesticHttpClientFactory httpFactory)
            : this(httpFactory, new CsqaqRequestRateLimiter(TimeSpan.Zero, Task.Delay))
        {
        }

        internal CsqaqService(
            IDomesticHttpClientFactory httpFactory,
            CsqaqRequestRateLimiter requestRateLimiter)
        {
            if (httpFactory == null) throw new ArgumentNullException(nameof(httpFactory));
            _requestRateLimiter = requestRateLimiter ?? throw new ArgumentNullException(nameof(requestRateLimiter));

            _http = httpFactory.Create(15, new Uri(BaseUrl));
            _http.DefaultRequestHeaders.Add("User-Agent", "CS2TradeMonitor/1.0 (Windows; .NET)");

            _jsonOptions = ServiceInfra.DefaultJsonOptions;

            try
            {
                // 构造阶段尚未收到配置快照，先从持久化配置读取一次；后续 Configure 会覆盖。
                _apiToken = Settings.Load().CsqaqApiToken ?? "";
            }
            catch
            {
                // 构造阶段读取旧设置失败时先以公开接口启动，后续 Configure 会发布正式 Token。
                _apiToken = "";
            }
        }

        public void Configure(string apiToken)
        {
            _apiToken = apiToken ?? "";
        }

        public string GetValue(string key)
        {
            EnsureFreshFetch();

            var d = Volatile.Read(ref _latest);
            if (d == null)
            {
                return key.Equals("Display", StringComparison.OrdinalIgnoreCase)
                    ? MarketDisplayFormatter.GetCompactFullText(MarketDataSourceManager.QaqDisplayKey)
                    : "";
            }

            return key switch
            {
                "Index" => d.FormatIndex(),
                "Change" => d.FormatChange(),
                "Ratio" or "Rate" => d.FormatRate(),
                "Display" => MarketDisplayFormatter.FormatCompactFullText(MarketDataSourceManager.QaqDisplayKey, d.MarketIndex, d.ChangeRate),
                "Stale" => d.IsStale ? "old" : "",
                _ => "?"
            };
        }

        private void EnsureFreshFetch()
        {
            var now = DateTime.Now;
            var interval = GetConfiguredRefreshInterval();
            if (now - _lastAttempt < interval) return;
            _ = FetchAsync();
        }

        public async Task<bool> TestAndUpdateAsync(string apiToken, int refreshSec)
        {
            _apiToken = apiToken ?? "";
            return await FetchCoreAsync(force: true);
        }

        public Task FetchAsync(bool force = false) => FetchCoreAsync(force);

        private async Task<bool> FetchCoreAsync(bool force)
        {
            var now = DateTime.Now;
            if (now < _cooldownUntil)
            {
                RecordFailure($"QAQ 数据源限流，使用缓存。原因：{_cooldownReason}；下一步：{_cooldownUntil:MM-dd HH:mm:ss} 后自动重试。", log: false, countFailure: false);
                return false;
            }

            var interval = GetConfiguredRefreshInterval();
            if (!force && _lastAttempt != DateTime.MinValue && now - _lastAttempt < interval)
            {
                // 命中刷新间隔是正常节流，继续使用上次成功数据，不应标成过期/警告。
                return false;
            }

            if (!await _fetchLock.WaitAsync(0)) return false;

            try
            {
                _lastAttempt = now;
                using var request = new HttpRequestMessage(HttpMethod.Get, CurrentDataEndpoint);
                if (!string.IsNullOrEmpty(_apiToken))
                {
                    request.Headers.Add("ApiToken", _apiToken);
                }
                await _requestRateLimiter.WaitAsync().ConfigureAwait(false);
                using var response = await _http.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    string message = (int)response.StatusCode >= 500
                        ? $"QAQ 服务暂时不可用（HTTP {(int)response.StatusCode}），将按刷新间隔自动重试。"
                        : $"QAQ 请求失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                    ApplyCooldown(message);
                    RecordFailure(message);
                    return false;
                }

                var body = await response.Content.ReadAsStringAsync();
                var parsed = JsonSerializer.Deserialize<CsqaqApiResponse>(body, _jsonOptions);
                var index = parsed?.Data?.SubIndexData?
                    .FirstOrDefault(x => string.Equals(x.NameKey, "init", StringComparison.OrdinalIgnoreCase));

                if (parsed?.Code != 200 || index == null)
                {
                    RecordFailure("QAQ 返回数据为空或格式异常");
                    return false;
                }

                Volatile.Write(ref _latest, new CsqaqData
                {
                    MarketIndex = index.MarketIndex,
                    Change = index.Change,
                    ChangeRate = index.ChangeRate,
                    UpdatedAt = index.UpdatedAt,
                    RetrievedAt = DateTime.Now,
                    IsStale = false,
                    Source = string.IsNullOrWhiteSpace(_apiToken) ? "公开接口" : "官方 API"
                });
                LastError = "";
                _rateLimitFailureCount = 0;
                _consecutiveFailureCount = 0;
                _cooldownUntil = DateTime.MinValue;
                _cooldownReason = "";
                NotifyDataUpdated();
                return true;
            }
            catch (Exception ex)
            {
                RecordFailure("QAQ 网络访问失败", ex);
                return false;
            }
            finally
            {
                _fetchLock.Release();
            }
        }

        private void ApplyCooldown(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return;

            if (reason.Contains("HTTP 429", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase))
            {
                _rateLimitFailureCount = Math.Min(_rateLimitFailureCount + 1, 4);
                int minutes = Math.Min(30, 5 * (1 << (_rateLimitFailureCount - 1)));
                _cooldownUntil = DateTime.Now.AddMinutes(minutes);
                _cooldownReason = "HTTP 429 Too Many Requests";
            }
        }

        private static TimeSpan GetConfiguredRefreshInterval()
        {
            int refreshSec = MarketDataSourceRuntimeServices.Resolve().AppConfigState.MarketDisplay.CsqaqRefreshSec;
            if (refreshSec <= 0) refreshSec = Settings.DefaultMarketRefreshSec;
            if (refreshSec < Settings.DefaultMarketRefreshSec) refreshSec = Settings.DefaultMarketRefreshSec;
            return TimeSpan.FromSeconds(refreshSec);
        }

        private void RecordFailure(string message, Exception? ex = null, bool log = true, bool countFailure = true)
        {
            LastError = ex == null ? message : $"{message}：{ex.Message}";
            if (countFailure)
            {
                _consecutiveFailureCount = Math.Min(
                    _consecutiveFailureCount + 1,
                    StaleAfterConsecutiveFailures);
            }

            var latest = Volatile.Read(ref _latest);
            if (latest != null && _consecutiveFailureCount >= StaleAfterConsecutiveFailures)
            {
                latest.IsStale = true;
                Volatile.Write(ref _latest, latest);
            }
            if (log)
                DiagnosticsLogger.Error("QAQ", message, ex);
            NotifyDataUpdated();
        }

        public void Dispose()
        {
            _fetchLock.Dispose();
            _http.Dispose();
        }
    }
}
