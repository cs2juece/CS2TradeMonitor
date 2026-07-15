using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Application.Notify
{
    public sealed class Cs2UpdateReminderService : ICs2UpdateReminderService
    {
        private const string ListUrl = Cs2UpdateUrls.List;
        private const string PageUrl = Cs2UpdateUrls.Page;
        private const string SteamDbRssUrl = Cs2UpdateUrls.SteamDbRss;
        private const string SteamDbSource = "SteamDB";
        private const string AuthSalt = "pc*&bQ2@mkvt";
        private const int FetchPageSize = 2;
        private const int MaxFetchPages = 10;

        internal static string ApplicationUserAgent
        {
            get
            {
                var assembly = typeof(Cs2UpdateReminderService).Assembly;
                string? version = assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion
                    ?.Split('+')[0]
                    .Trim();
                if (string.IsNullOrWhiteSpace(version))
                    version = assembly.GetName().Version?.ToString(2) ?? "0.0";

                return $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) CS2TradeMonitor/{version}";
            }
        }

        private static readonly Lazy<Cs2UpdateReminderService> LazyInstance = new(() => new Cs2UpdateReminderService());
        public static Cs2UpdateReminderService Instance => LazyInstance.Value;

        private readonly HttpClient _fkbuffClient;
        private readonly HttpClient _steamDbClient;
        private readonly object _stateLock = new();
        private bool _checking;
        private DateTime _nextCheckAt = DateTime.MinValue;
        private Cs2UpdateCheckResult _lastResult = Cs2UpdateCheckResult.NotChecked();
        private List<Cs2UpdateLogItem> _recentItems = new();
        private long _lastRequestTimestampMs;

        private Cs2UpdateReminderService()
            : this(NotifyRuntimeServices.ResolveDomesticHttpFactory())
        {
        }

        internal Cs2UpdateReminderService(IDomesticHttpClientFactory httpFactory)
        {
            if (httpFactory == null) throw new ArgumentNullException(nameof(httpFactory));

            _fkbuffClient = httpFactory.Create(15);
            _fkbuffClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            _fkbuffClient.DefaultRequestHeaders.TryAddWithoutValidation("Origin", Cs2UpdateUrls.WebBase);
            _fkbuffClient.DefaultRequestHeaders.TryAddWithoutValidation("Referer", PageUrl);
            _fkbuffClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", ApplicationUserAgent);

            _steamDbClient = httpFactory.Create(15);
            _steamDbClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/rss+xml, application/xml, text/xml, */*");
            _steamDbClient.DefaultRequestHeaders.TryAddWithoutValidation("Referer", Cs2UpdateUrls.SteamDbPage);
            _steamDbClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", ApplicationUserAgent);
        }

        public event EventHandler<Cs2UpdateDetectedEventArgs>? UpdateDetected;

        public Cs2UpdateCheckResult LastResult
        {
            get { lock (_stateLock) return _lastResult; }
        }

        public IReadOnlyList<Cs2UpdateLogItem> RecentItems
        {
            get { lock (_stateLock) return _recentItems.ToList(); }
        }

        public void Tick(Settings cfg)
        {
            if (cfg == null || !cfg.Cs2UpdateReminderEnabled)
                return;

            if (_checking || DateTime.Now < _nextCheckAt)
                return;

            _ = CheckAsync(cfg, notify: true, resetBaseline: false);
        }

        public void ResetSchedule()
        {
            _nextCheckAt = DateTime.MinValue;
        }

        public Task<Cs2UpdateCheckResult> ManualCheckAsync(Settings cfg, bool resetBaseline = false)
        {
            _nextCheckAt = DateTime.MinValue;
            return CheckAsync(cfg, notify: !resetBaseline, resetBaseline: resetBaseline);
        }

        public async Task<Cs2UpdateCheckResult> CheckAsync(Settings cfg, bool notify, bool resetBaseline, CancellationToken cancellationToken = default)
        {
            if (cfg == null)
                return Cs2UpdateCheckResult.Fail("配置不可用");

            if (_checking)
                return Cs2UpdateCheckResult.Fail("正在检查，请稍后");

            _checking = true;
            try
            {
                var items = await FetchLatestAsync(cancellationToken).ConfigureAwait(false);
                var latest = items.FirstOrDefault();
                var now = DateTime.Now;
                cfg.Cs2UpdateLastCheckTime = new DateTimeOffset(now).ToUnixTimeMilliseconds();

                if (latest == null)
                {
                    cfg.Cs2UpdateLastStatus = "检查完成：暂无更新数据";
                    var empty = new Cs2UpdateCheckResult(true, false, "检查完成：暂无更新数据", null, 0, now);
                    StoreResult(empty, items);
                    cfg.Save();
                    ScheduleNext(cfg);
                    return empty;
                }

                if (resetBaseline || string.IsNullOrWhiteSpace(cfg.Cs2UpdateBaselineKey))
                {
                    ApplyBaseline(cfg, latest);
                    cfg.Cs2UpdateLastStatus = resetBaseline ? "已重置基准" : "已建立基准";
                    var baseline = new Cs2UpdateCheckResult(true, false, cfg.Cs2UpdateLastStatus, latest, 0, now);
                    StoreResult(baseline, items);
                    cfg.Save();
                    ScheduleNext(cfg);
                    return baseline;
                }

                if (IsBaselineLatest(latest, cfg.Cs2UpdateBaselineKey, cfg.Cs2UpdateBaselinePublishedAt))
                {
                    cfg.Cs2UpdateLastStatus = "没有新的 CS2 更新";
                    var unchanged = new Cs2UpdateCheckResult(true, false, cfg.Cs2UpdateLastStatus, latest, 0, now);
                    StoreResult(unchanged, items);
                    cfg.Save();
                    ScheduleNext(cfg);
                    return unchanged;
                }

                var newItems = CollectNewItems(items, cfg.Cs2UpdateBaselineKey, cfg.Cs2UpdateBaselinePublishedAt);
                if (newItems.Count == 0)
                    newItems.Add(latest);

                ApplyBaseline(cfg, latest);
                cfg.Cs2UpdateLastStatus = "CS2 已有最新更新";

                var changed = new Cs2UpdateCheckResult(true, true, cfg.Cs2UpdateLastStatus, latest, newItems.Count, now);
                StoreResult(changed, items);
                cfg.Save();
                ScheduleNext(cfg);

                if (notify)
                    DispatchUpdate(newItems);

                return changed;
            }
            catch (Exception ex)
            {
                bool sourceUnavailable = IsSourceUnavailable(ex);
                if (sourceUnavailable)
                {
                    DiagnosticsLogger.Info("CS2Update", $"CS2 update source unavailable: {ex.Message}");
                }
                else
                {
                    DiagnosticsLogger.Error("CS2Update", "CS2 update check failed.", ex);
                }

                var now = DateTime.Now;
                cfg.Cs2UpdateLastCheckTime = new DateTimeOffset(now).ToUnixTimeMilliseconds();
                cfg.Cs2UpdateLastStatus = sourceUnavailable ? ex.Message : "检查失败：网络或格式异常";
                var failed = Cs2UpdateCheckResult.Fail(cfg.Cs2UpdateLastStatus, now);
                StoreResult(failed, RecentItems);
                cfg.Save();
                ScheduleNext(cfg);
                return failed;
            }
            finally
            {
                _checking = false;
            }
        }

        private async Task<List<Cs2UpdateLogItem>> FetchLatestAsync(CancellationToken cancellationToken)
        {
            var allItems = new List<Cs2UpdateLogItem>();
            Exception? firstFailure = null;
            bool anySourceSucceeded = false;

            try
            {
                allItems.AddRange(await FetchFkbuffLatestAsync(cancellationToken).ConfigureAwait(false));
                anySourceSucceeded = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                firstFailure = ex;
                LogSourceFailure("FKBUFF", ex);
            }

            try
            {
                allItems.AddRange(await FetchSteamDbLatestAsync(cancellationToken).ConfigureAwait(false));
                anySourceSucceeded = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                firstFailure ??= ex;
                LogSourceFailure(SteamDbSource, ex);
            }

            if (!anySourceSucceeded && firstFailure != null)
                throw new InvalidOperationException("更新源暂时不可用：FKBUFF 与 SteamDB 均不可用", firstFailure);

            return NormalizeItems(allItems);
        }

        private async Task<List<Cs2UpdateLogItem>> FetchFkbuffLatestAsync(CancellationToken cancellationToken)
        {
            var allItems = new List<Cs2UpdateLogItem>();
            for (int page = 1; page <= MaxFetchPages; page++)
            {
                if (page > 1)
                    await Task.Delay(120, cancellationToken).ConfigureAwait(false);

                Cs2UpdateListData? data;
                try
                {
                    data = await FetchPageAsync(page, cancellationToken).ConfigureAwait(false);
                }
                catch when (allItems.Count > 0)
                {
                    break;
                }

                var pageItems = data?.List ?? new List<Cs2UpdateLogItem>();
                if (pageItems.Count == 0)
                    break;

                allItems.AddRange(pageItems);
                if (data?.More != true)
                    break;
            }

            return NormalizeItems(allItems);
        }

        private async Task<List<Cs2UpdateLogItem>> FetchSteamDbLatestAsync(CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, SteamDbRssUrl);
            using var response = await _steamDbClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            string xml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"SteamDB 更新源请求失败：HTTP {(int)response.StatusCode}");

            try
            {
                var document = XDocument.Parse(xml);
                return document
                    .Descendants("item")
                    .Select(ParseSteamDbItem)
                    .Where(item => item != null)
                    .Cast<Cs2UpdateLogItem>()
                    .ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException("SteamDB 更新源返回异常", ex);
            }
        }

        private async Task<Cs2UpdateListData?> FetchPageAsync(int page, CancellationToken cancellationToken)
        {
            string timestamp = NextRequestTimestamp();
            string auth = Md5Hex(Md5Hex(timestamp) + AuthSalt);
            string body = JsonSerializer.Serialize(new { page, pageSize = FetchPageSize });

            using var request = new HttpRequestMessage(HttpMethod.Post, ListUrl)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Timestamp", timestamp);
            request.Headers.TryAddWithoutValidation("Auth", auth);

            using var response = await _fkbuffClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                int statusCode = (int)response.StatusCode;
                if (statusCode >= 500)
                    throw new InvalidOperationException($"更新源暂时不可用：HTTP {statusCode}");

                throw new InvalidOperationException($"更新源请求失败：HTTP {statusCode}");
            }

            var parsed = JsonSerializer.Deserialize<Cs2UpdateListResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (parsed == null || parsed.Code != 0)
                throw new InvalidOperationException("更新源返回异常：" + (parsed?.Code.ToString(CultureInfo.InvariantCulture) ?? "null"));

            return parsed.Data;
        }

        private static Cs2UpdateLogItem? ParseSteamDbItem(XElement item)
        {
            string guid = (item.Element("guid")?.Value ?? "").Trim();
            string title = (item.Element("title")?.Value ?? "").Trim();
            string description = (item.Element("description")?.Value ?? "").Trim();
            string link = (item.Element("link")?.Value ?? "").Trim();
            string pubDate = (item.Element("pubDate")?.Value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(guid) && string.IsNullOrWhiteSpace(title))
                return null;

            long publishedAt = 0;
            if (DateTimeOffset.TryParse(
                pubDate,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedDate))
            {
                publishedAt = parsedDate.ToUnixTimeMilliseconds();
            }

            return new Cs2UpdateLogItem
            {
                Source = SteamDbSource,
                LogId = string.IsNullOrWhiteSpace(guid) ? title : guid,
                Title = string.IsNullOrWhiteSpace(title) ? "Counter-Strike 2 update" : title,
                Summary = description,
                Content = link,
                PublishedAt = publishedAt
            };
        }

        private static List<Cs2UpdateLogItem> NormalizeItems(IEnumerable<Cs2UpdateLogItem> items)
        {
            return items
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => NormalizeTimestamp(x.PublishedAt)).First())
                .OrderByDescending(x => NormalizeTimestamp(x.PublishedAt))
                .ThenByDescending(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void LogSourceFailure(string source, Exception ex)
        {
            DiagnosticsLogger.Info("CS2Update", $"{source} update source unavailable: {ex.Message}");
        }

        private static bool IsSourceUnavailable(Exception ex)
        {
            string message = ex.Message ?? string.Empty;
            return message.IndexOf("更新源暂时不可用", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("更新源请求失败", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("更新源返回异常", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("SteamDB 更新源请求失败", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("SteamDB 更新源返回异常", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string NextRequestTimestamp()
        {
            lock (_stateLock)
            {
                long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                if (now <= _lastRequestTimestampMs)
                    now = _lastRequestTimestampMs + 1;

                _lastRequestTimestampMs = now;
                return now.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static bool IsBaselineLatest(Cs2UpdateLogItem latest, string baselineKey, long baselinePublishedAt)
        {
            if (string.Equals(latest.Key, baselineKey, StringComparison.OrdinalIgnoreCase))
                return true;

            long baselineTime = NormalizeTimestamp(baselinePublishedAt);
            if (baselineTime <= 0)
                return false;

            return NormalizeTimestamp(latest.PublishedAt) <= baselineTime;
        }

        private static List<Cs2UpdateLogItem> CollectNewItems(List<Cs2UpdateLogItem> items, string baselineKey, long baselinePublishedAt)
        {
            var list = new List<Cs2UpdateLogItem>();
            foreach (var item in items)
            {
                if (string.Equals(item.Key, baselineKey, StringComparison.OrdinalIgnoreCase))
                    break;

                list.Add(item);
            }

            if (list.Count < items.Count)
                return list;

            long baselineTime = NormalizeTimestamp(baselinePublishedAt);
            if (baselineTime <= 0)
                return list;

            return items
                .Where(item => NormalizeTimestamp(item.PublishedAt) > baselineTime)
                .ToList();
        }

        private void DispatchUpdate(IReadOnlyList<Cs2UpdateLogItem> items)
        {
            if (items.Count == 0) return;

            string title = items.Count == 1 ? "CS2 更新提醒" : $"CS2 更新提醒（{items.Count} 条）";
            var latest = items[0];
            string message = latest.GetNotificationText();
            if (items.Count > 1)
                message += Environment.NewLine + $"另有 {items.Count - 1} 条更新。";

            UpdateDetected?.Invoke(this, new Cs2UpdateDetectedEventArgs(title, message, items));
        }

        private static void ApplyBaseline(Settings cfg, Cs2UpdateLogItem latest)
        {
            cfg.Cs2UpdateBaselineKey = latest.Key;
            cfg.Cs2UpdateBaselineTitle = latest.Title;
            cfg.Cs2UpdateBaselinePublishedAt = latest.PublishedAt;
        }

        private void StoreResult(Cs2UpdateCheckResult result, IReadOnlyList<Cs2UpdateLogItem> items)
        {
            lock (_stateLock)
            {
                _lastResult = result;
                _recentItems = items.Take(1).ToList();
            }
        }

        private void ScheduleNext(Settings cfg)
        {
            int seconds = Math.Clamp(cfg.Cs2UpdateReminderRefreshSec <= 0 ? 600 : cfg.Cs2UpdateReminderRefreshSec, 60, 86400);
            _nextCheckAt = DateTime.Now.AddSeconds(seconds);
        }

        private static string Md5Hex(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            byte[] hash = MD5.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private sealed class Cs2UpdateListResponse
        {
            [JsonPropertyName("code")] public int Code { get; set; }
            [JsonPropertyName("msg")] public string Msg { get; set; } = "";
            [JsonPropertyName("data")] public Cs2UpdateListData? Data { get; set; }
        }

        private sealed class Cs2UpdateListData
        {
            [JsonPropertyName("list")] public List<Cs2UpdateLogItem>? List { get; set; }
            [JsonPropertyName("more")] public bool More { get; set; }
        }

        public static string FormatTime(long timestamp)
        {
            if (timestamp <= 0) return "暂无";
            try
            {
                long ms = timestamp < 1_000_000_000_000L ? timestamp * 1000L : timestamp;
                return DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch
            {
                return "暂无";
            }
        }

        private static long NormalizeTimestamp(long timestamp)
        {
            if (timestamp <= 0) return 0;
            return timestamp < 1_000_000_000_000L ? timestamp * 1000L : timestamp;
        }

        public static string PageLink => PageUrl;
    }
}
