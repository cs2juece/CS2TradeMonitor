using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.Core.Refresh;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.Market;

namespace CS2TradeMonitor.Application.Market
{

    // Keep this file focused on service lifetime and orchestration.
    // Cache, fetch, and search details live in the matching partial files.
    public partial class SteamDtItemService : ISteamDtItemService
    {
        private static SteamDtItemService? _instance;
        public static SteamDtItemService Instance => _instance ??= new SteamDtItemService();

        private readonly HttpClient _http;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly ConcurrentDictionary<string, SteamDtItemData> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, DateTime> _lastFetchTime = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, DateTime> _lastFailureLogTime = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _lastFailureLogMessage = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, byte> _configErrorPausedItems = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _fetchLock = new(1, 1);
        private readonly RefreshPipeline _refreshPipeline = new("SteamDT 单品批量刷新");
        private static readonly TimeSpan FailureLogThrottle = TimeSpan.FromMinutes(30);
        private System.Threading.Timer? _timer;
        private string _apiKey = "";
        private bool _configured;

        // Base cache variables
        private List<SteamDtBaseItem> _baseItemsCache = new();
        private DateTime _cacheLastUpdated = DateTime.MinValue;
        private string? _cacheFilePath;

        // Local items database cache
        private List<SteamDtSearchCandidate>? _localItemsCache;
        private readonly object _localCacheLock = new();

        public event Action? DataUpdated;
        internal int ConfigureRestartCount { get; private set; }

        public bool IsLocalItemDatabaseAvailable
        {
            get
            {
                lock (_localCacheLock)
                {
                    if (_localItemsCache != null)
                        return _localItemsCache.Count > 0;
                }

                return !string.IsNullOrWhiteSpace(GetLocalItemsFilePath());
            }
        }

        private SteamDtItemService()
            : this(MarketServiceRuntimeServices.Resolve().DomesticHttpFactory)
        {
        }

        internal SteamDtItemService(IDomesticHttpClientFactory httpFactory)
        {
            if (httpFactory == null) throw new ArgumentNullException(nameof(httpFactory));

            _http = httpFactory.Create(15, new Uri(SteamDtUrls.OpenApiBase));
            _http.DefaultRequestHeaders.Add("User-Agent", "CS2TradeMonitor/1.0 (Windows; .NET)");
            _jsonOptions = ServiceInfra.DefaultJsonOptions;

            // Load base cache from local file
            using (AppPerformanceProfiler.Measure("SteamDtItemService.Ctor", "LoadBaseCache=True; LoadLocalItems=False", thresholdMs: 1))
            {
                LoadBaseCache();
            }
        }

        public void Configure(string apiKey)
        {
            apiKey ??= "";
            if (_configured && string.Equals(_apiKey, apiKey, StringComparison.Ordinal))
                return;

            _apiKey = apiKey;
            _configured = true;
            ConfigureRestartCount++;

            _timer?.Dispose();
            // Check every 30 seconds for items that need refreshing
            _timer = new System.Threading.Timer(async _ => await FetchAllDueItemsAsync(), null, 30000, 30000);

            // Trigger initial check in background after a short jitter to avoid cold-start request bursts.
            _ = RunInitialDueItemsAsync();
        }

        private async Task RunInitialDueItemsAsync()
        {
            try
            {
                await Task.Delay(Random.Shared.Next(2500, 6000)).ConfigureAwait(false);
                await FetchAllDueItemsAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Info("SteamDTItem", "Initial due-item refresh failed: " + ex.Message);
            }
        }

        public SteamDtItemData? GetItemData(string itemId)
        {
            if (_cache.TryGetValue(itemId, out var data))
            {
                return data;
            }
            try
            {
                var config = MarketDataSourceRuntimeServices.Resolve().AppConfigState.ItemMonitor.Items.FirstOrDefault(x => x.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase));
                if (config != null)
                {
                    if (!string.IsNullOrEmpty(config.MarketHashName) && _cache.TryGetValue(config.MarketHashName, out data))
                    {
                        return data;
                    }
                    if (!string.IsNullOrEmpty(config.PlatformItemId) && _cache.TryGetValue(config.PlatformItemId, out data))
                    {
                        return data;
                    }
                }
            }
            catch
            {
                // 快照别名读取失败时只影响缓存兜底命中，不能阻断主刷新链路。
            }
            return null;
        }

        public async Task FetchAllEnabledItemsAsync()
        {
            await _refreshPipeline.RunAsync("手动刷新单品", FetchAllItemsCoreAsyncFactory(force: true));
        }

        private async Task FetchAllDueItemsAsync()
        {
            await _refreshPipeline.RunAsync("自动刷新单品", FetchAllItemsCoreAsyncFactory(force: false));
        }

        private Func<long, CancellationToken, Task> FetchAllItemsCoreAsyncFactory(bool force)
        {
            return (version, cancellationToken) => FetchAllItemsCoreAsync(force, version, cancellationToken);
        }

        private async Task FetchAllItemsCoreAsync(bool force, long version, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 单品监控要读取完整 ItemConfigs 并可能更新其运行状态，当前仍以持久化 Settings 为权威来源。
            var settings = Settings.Load();
            if (settings.ItemConfigs == null || settings.ItemConfigs.Count == 0) return;
            int defaultIntervalSec = Math.Max(60, settings.DefaultItemRefreshIntervalSec <= 0 ? 600 : settings.DefaultItemRefreshIntervalSec);

            var itemsToFetch = new List<ItemMonitorConfig>();
            foreach (var item in settings.ItemConfigs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!item.Enabled || string.IsNullOrEmpty(item.ItemId)) continue;
                if (!force && IsConfigErrorPaused(item)) continue;

                if (force)
                {
                    itemsToFetch.Add(item);
                }
                else
                {
                    _lastFetchTime.TryGetValue(item.ItemId, out var lastTime);
                    int intervalSec = Math.Max(60, item.RefreshIntervalSec <= 0 ? defaultIntervalSec : item.RefreshIntervalSec);
                    if (DateTime.Now - lastTime >= TimeSpan.FromSeconds(intervalSec))
                    {
                        itemsToFetch.Add(item);
                    }
                }
            }

            if (itemsToFetch.Count == 0) return;

            bool anyUpdated = false;
            foreach (var item in itemsToFetch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool success = await FetchItemPriceAsync(item, persistSettings: false);
                if (success)
                {
                    anyUpdated = true;
                }
                await Task.Delay(100, cancellationToken);
            }

            if (anyUpdated && _refreshPipeline.IsLatest(version))
            {
                settings.Save();
                NotifyDataUpdated();
            }
        }

        private void NotifyDataUpdated()
        {
            try
            {
                DataUpdated?.Invoke();
            }
            catch
            {
                // UI 订阅者异常不能反向打断单品价格刷新流程。
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _http.Dispose();
        }
    }
}
