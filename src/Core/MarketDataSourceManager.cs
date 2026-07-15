using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Market;
using CS2TradeMonitor.Domain.Market;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CS2TradeMonitor.src.Core.Refresh;
using CS2TradeMonitor.src.Core.State;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.src.Core
{
    public sealed class MarketDataSourceState
    {
        public string Id { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string TypeDescription { get; init; } = "";
        public string ConfigState { get; init; } = "";
        public string Status { get; init; } = "未获取";
        public string LastRefresh { get; init; } = "";
        public string LastError { get; init; } = "";
    }

    public sealed class MarketDisplaySnapshot
    {
        public string Key { get; init; } = "";
        public string Label { get; init; } = "";
        public bool HasData { get; init; }
        public bool IsStale { get; init; }
        public double Index { get; init; }
        public double Change { get; init; }
        public double Percent { get; init; }
        public DateTime RetrievedAt { get; init; }
        public string PlaceholderText { get; init; } = "";
        public bool HasChangeData { get; init; }
    }

    public static class MarketDataSourceManager
    {
        public const string QaqId = "CSQAQ";
        public const string SteamDtId = "STEAMDT";
        public const string QaqDisplayKey = "CSQAQ.Display";
        public const string SteamDtDisplayKey = "STEAMDT.Display";

        public static event Action? DataUpdated;

        private static readonly RefreshPipeline MarketRefreshPipeline = new("大盘数据源刷新");
        private static readonly object TimerGate = new();
        private static readonly TimeSpan PassiveRefreshCoalesceWindow = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan StartupDisplayRefreshSuppressWindow = TimeSpan.FromSeconds(30);
        private static System.Threading.Timer? _marketRefreshTimer;
        private static long _lastPassiveRefreshRequestUtcTicks;
        private static long _displayRefreshSuppressedUntilUtcTicks;
        private static int _startupRefreshInFlight;
        private static int _displayRefreshRequestIssued;
        private static int _displayRefreshRequestInFlight;
        private static MarketDataSourceRuntimeServices? _services;
        private static MarketSourceStateProjection? _sourceStateProjection;
        private static MarketDataSourceRuntimeServices Services => _services ??= MarketDataSourceRuntimeServices.Resolve();
        private static ISteamDtService SteamDt => Services.SteamDt;
        private static ICsqaqService Csqaq => Services.Csqaq;
        private static ISteamDtItemService SteamDtItems => Services.SteamDtItems;
        private static IRuntimeAppState RuntimeState => Services.RuntimeState;
        private static IAppConfigState AppConfigState => Services.AppConfigState;
        private static MarketSourceStateProjection SourceStateProjection =>
            _sourceStateProjection ??= new MarketSourceStateProjection(
                new SteamDtMarketSourceStateAdapter(SteamDt),
                new CsqaqMarketSourceStateAdapter(Csqaq));

        static MarketDataSourceManager()
        {
            SteamDt.DataUpdated -= OnSourceDataUpdated;
            SteamDt.DataUpdated += OnSourceDataUpdated;

            Csqaq.DataUpdated -= OnSourceDataUpdated;
            Csqaq.DataUpdated += OnSourceDataUpdated;

            SteamDtItems.DataUpdated -= OnSourceDataUpdated;
            SteamDtItems.DataUpdated += OnSourceDataUpdated;
        }

        private static void OnSourceDataUpdated()
        {
            try
            {
                PublishRuntimeMarketSnapshot("MarketDataSourceUpdated");
                PublishRuntimeItemSnapshots("MarketDataSourceUpdated");
                DataUpdated?.Invoke();
            }
            catch
            {
                // 数据源事件来自后台服务，快照发布失败不能反向打断原始刷新流程。
            }
        }

        public static void Configure(Settings cfg, bool requestInitialRefresh = true)
        {
            SteamDt.Configure(cfg.SteamDtApiKey, cfg.SteamDtRefreshSec);
            SteamDtItems.Configure(cfg.SteamDtApiKey);
            Csqaq.Configure(cfg.CsqaqApiToken);
            ConfigureMarketRefreshTimer(cfg);
            Interlocked.Exchange(ref _displayRefreshRequestIssued, 0);
            PublishRuntimeMarketSnapshot("MarketDataSourceConfigured");
            PublishRuntimeItemSnapshots("MarketDataSourceConfigured");
            if (requestInitialRefresh)
                _ = RequestPassiveMarketIndexRefreshAsync("配置后刷新");
        }

        public static void UpdateMarketRefreshIntervals(int steamDtRefreshSec, int qaqRefreshSec)
        {
            ConfigureMarketRefreshTimer(steamDtRefreshSec, qaqRefreshSec);
        }

        public static async Task RefreshAllAsync(bool waitForSteamDtLock = false)
        {
            await RefreshMarketIndexesAsync(waitForSteamDtLock ? "手动刷新" : "刷新", waitForSteamDtLock);
            await SteamDtItems.FetchAllEnabledItemsAsync();
        }

        public static Task RefreshMarketIndexesAsync(string reason = "刷新", bool waitForSteamDtLock = false)
        {
            MarketRefreshTrigger trigger = ResolveLegacyTrigger(reason, waitForSteamDtLock);
            MarketRefreshRequest request = MarketRefreshRequest.For(trigger, reason) with
            {
                WaitForSteamDtLock = waitForSteamDtLock || trigger is MarketRefreshTrigger.Manual or MarketRefreshTrigger.Startup
            };
            return RefreshMarketIndexesAsync(request);
        }

        public static Task RefreshMarketIndexesAsync(MarketRefreshRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!request.WaitForSteamDtLock && IsMarketIndexRefreshInFlight())
                return Task.CompletedTask;

            return RefreshMarketIndexesCoreAsync(request);
        }

        public static Task RequestPassiveMarketIndexRefreshAsync(string reason, bool waitForSteamDtLock = false)
        {
            if (IsDisplayTriggeredReason(reason) && IsDisplayRefreshSuppressed())
                return Task.CompletedTask;

            if (IsMarketIndexRefreshInFlight())
                return Task.CompletedTask;

            return TryClaimPassiveRefreshRequest()
                ? RefreshMarketIndexesAsync(reason, waitForSteamDtLock)
                : Task.CompletedTask;
        }

        public static Task RequestStartupMarketIndexRefreshAsync()
        {
            if (Interlocked.CompareExchange(ref _startupRefreshInFlight, 1, 0) != 0)
                return Task.CompletedTask;

            return RunStartupMarketIndexRefreshAsync();
        }

        private static async Task RunStartupMarketIndexRefreshAsync()
        {
            try
            {
                await Task.Delay(Random.Shared.Next(2500, 6000)).ConfigureAwait(false);
                await RefreshMarketIndexesAsync(MarketRefreshRequest.For(MarketRefreshTrigger.Startup)).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(
                    ref _displayRefreshSuppressedUntilUtcTicks,
                    DateTime.UtcNow.Add(StartupDisplayRefreshSuppressWindow).Ticks);
                Interlocked.Exchange(ref _displayRefreshRequestIssued, 1);
                Interlocked.Exchange(ref _startupRefreshInFlight, 0);
            }
        }

        private static bool IsMarketIndexRefreshInFlight()
        {
            return Volatile.Read(ref _startupRefreshInFlight) != 0
                || MarketRefreshPipeline.IsRunning;
        }

        private static bool IsDisplayRefreshSuppressed()
        {
            long suppressedUntil = Interlocked.Read(ref _displayRefreshSuppressedUntilUtcTicks);
            return suppressedUntil > DateTime.UtcNow.Ticks;
        }

        private static bool IsDisplayTriggeredReason(string reason)
        {
            return reason.IndexOf("显示触发", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static async Task RefreshMarketIndexesCoreAsync(MarketRefreshRequest request)
        {
            var result = await MarketRefreshPipeline.RunAsync(request.Reason, async (version, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await SteamDt.FetchAsync(waitForLock: request.WaitForSteamDtLock);
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(Random.Shared.Next(1000, 3500), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await Csqaq.FetchAsync(force: request.ForceSources);

                if (MarketRefreshPipeline.IsLatest(version))
                {
                    PublishRuntimeMarketSnapshot("MarketRefreshPipeline:" + request.Reason);
                    DataUpdated?.Invoke();
                }
            }, waitForTurn: request.WaitForSteamDtLock);

            if (!result.Success && !result.Coalesced && !result.Canceled)
            {
                DiagnosticsLogger.Info("RefreshPipeline", $"{result.Pipeline} {result.Message}");
            }
        }

        private static void ConfigureMarketRefreshTimer(Settings cfg)
        {
            ConfigureMarketRefreshTimer(cfg.SteamDtRefreshSec, cfg.CsqaqRefreshSec);
        }

        private static void ConfigureMarketRefreshTimer(int steamDtRefreshSec, int qaqRefreshSec)
        {
            var steamDtSec = NormalizeRefreshInterval(steamDtRefreshSec);
            var qaqSec = NormalizeRefreshInterval(qaqRefreshSec);
            var intervalSec = Math.Max(Settings.DefaultMarketRefreshSec, Math.Min(steamDtSec, qaqSec));

            lock (TimerGate)
            {
                _marketRefreshTimer?.Dispose();
                _marketRefreshTimer = new System.Threading.Timer(
                    _ => _ = RefreshMarketIndexesAsync("自动刷新"),
                    null,
                    intervalSec * 1000,
                    intervalSec * 1000);
            }
        }

        private static int NormalizeRefreshInterval(int seconds)
        {
            return seconds <= 0 ? Settings.DefaultMarketRefreshSec : Math.Max(Settings.DefaultMarketRefreshSec, seconds);
        }

        private static MarketRefreshTrigger ResolveLegacyTrigger(string reason, bool waitForSteamDtLock)
        {
            if (waitForSteamDtLock || reason.Contains("手动", StringComparison.OrdinalIgnoreCase))
                return MarketRefreshTrigger.Manual;
            if (reason.Contains("启动", StringComparison.OrdinalIgnoreCase))
                return MarketRefreshTrigger.Startup;
            if (reason.Contains("显示", StringComparison.OrdinalIgnoreCase))
                return MarketRefreshTrigger.Display;
            if (reason.Contains("路由", StringComparison.OrdinalIgnoreCase) || reason.Contains("网络恢复", StringComparison.OrdinalIgnoreCase))
                return MarketRefreshTrigger.RouteRecovered;
            return MarketRefreshTrigger.Automatic;
        }

        internal static bool HasRetryableNetworkFailure()
        {
            return IsRetryableNetworkFailure(SteamDt.LastError)
                || IsRetryableNetworkFailure(Csqaq.LastError);
        }

        internal static bool IsRetryableNetworkFailure(string? message)
        {
            string text = message ?? "";
            return text.Contains("网络", StringComparison.OrdinalIgnoreCase)
                || text.Contains("代理", StringComparison.OrdinalIgnoreCase)
                || text.Contains("连接", StringComparison.OrdinalIgnoreCase)
                || text.Contains("超时", StringComparison.OrdinalIgnoreCase)
                || text.Contains("connection refused", StringComparison.OrdinalIgnoreCase)
                || text.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                || text.Contains("name resolution", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryClaimPassiveRefreshRequest()
        {
            long nowTicks = DateTime.UtcNow.Ticks;
            while (true)
            {
                long previousTicks = Interlocked.Read(ref _lastPassiveRefreshRequestUtcTicks);
                if (previousTicks > 0 && nowTicks - previousTicks < PassiveRefreshCoalesceWindow.Ticks)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _lastPassiveRefreshRequestUtcTicks, nowTicks, previousTicks) == previousTicks)
                {
                    return true;
                }
            }
        }

        public static IReadOnlyList<MarketDataSourceState> GetStates(Settings cfg)
        {
            return GetSourceStates(cfg).All
                .Select(ToLegacyState)
                .ToArray();
        }

        private static MarketDataSourceState ToLegacyState(MarketSourceStateSnapshot state)
        {
            return new MarketDataSourceState
            {
                Id = state.Id,
                DisplayName = state.DisplayName,
                TypeDescription = state.TypeDescription,
                ConfigState = state.ConfigState,
                Status = state.Status,
                LastRefresh = state.LastRefresh,
                LastError = state.LastError
            };
        }

        internal static MarketSourceStatesSnapshot GetSourceStates(Settings cfg)
        {
            ArgumentNullException.ThrowIfNull(cfg);

            return SourceStateProjection.Capture(
                new MarketSourceStateConfig(
                    !string.IsNullOrWhiteSpace(cfg.SteamDtApiKey),
                    cfg.SteamDtRefreshSec),
                new MarketSourceStateConfig(
                    !string.IsNullOrWhiteSpace(cfg.CsqaqApiToken),
                    cfg.CsqaqRefreshSec));
        }

        internal static MarketSourceStateSnapshot GetSourceState(
            string sourceId,
            bool hasCredential,
            int refreshIntervalSeconds)
        {
            return SourceStateProjection.Capture(
                sourceId,
                new MarketSourceStateConfig(hasCredential, refreshIntervalSeconds));
        }

        private static MarketSourceStatesSnapshot GetSourceStates(MarketDisplayConfigSnapshot cfg)
        {
            return SourceStateProjection.Capture(
                new MarketSourceStateConfig(cfg.HasSteamDtApiKey, cfg.SteamDtRefreshSec),
                new MarketSourceStateConfig(cfg.HasCsqaqApiToken, cfg.CsqaqRefreshSec));
        }

        private static void PublishRuntimeMarketSnapshot(string reason)
        {
            try
            {
                var cfg = AppConfigState.MarketDisplay;
                MarketSourceStatesSnapshot sourceStates = GetSourceStates(cfg);
                MarketIndexSnapshot steamDt = BuildRuntimeSnapshot(sourceStates.SteamDt);
                MarketIndexSnapshot qaq = BuildRuntimeSnapshot(sourceStates.Csqaq);
                var sources = new Dictionary<string, MarketIndexSnapshot>(StringComparer.OrdinalIgnoreCase);

                sources[SteamDtId] = steamDt;
                sources[QaqId] = qaq;

                RuntimeState.UpdateMarket(
                    new MarketSnapshot(steamDt, qaq, sources, DateTime.Now),
                    reason);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Info("RuntimeState", "Failed to publish market snapshot: " + ex.Message);
            }
        }

        private static MarketIndexSnapshot BuildRuntimeSnapshot(MarketSourceStateSnapshot state)
        {
            return new MarketIndexSnapshot(
                state.Id,
                state.DisplayName,
                state.TypeDescription,
                state.Status,
                state.LastRefresh,
                state.LastError,
                state.Index,
                state.Change,
                state.Percent,
                state.HasData ? state.Source : state.TypeDescription,
                state.RetrievedAt,
                state.HasData,
                state.IsStale);
        }

        private static void PublishRuntimeItemSnapshots(string reason)
        {
            try
            {
                var items = AppConfigState.ItemMonitor.Items;
                var snapshots = new List<MonitoredItemSnapshot>(items.Count);

                foreach (var item in items)
                {
                    snapshots.Add(BuildRuntimeItemSnapshot(item));
                }

                RuntimeState.UpdateItems(snapshots, reason);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Info("RuntimeState", "Failed to publish item snapshots: " + ex.Message);
            }
        }

        private static MonitoredItemSnapshot BuildRuntimeItemSnapshot(ItemConfigSummary item)
        {
            var latest = SteamDtItems.GetItemData(item.ItemId);
            if (latest == null && !string.IsNullOrWhiteSpace(item.MarketHashName))
            {
                latest = SteamDtItems.GetItemData(item.MarketHashName);
            }

            if (latest != null)
            {
                return new MonitoredItemSnapshot(
                    string.IsNullOrWhiteSpace(item.ItemKey) ? "ITEM." + item.ItemId : item.ItemKey,
                    item.ItemId ?? "",
                    item.Name ?? "",
                    item.ShortName ?? "",
                    latest.Price,
                    latest.Change,
                    latest.ChangeRatio,
                    latest.RetrievedAt,
                    latest.Source ?? "已获取",
                    true,
                    latest.IsStale);
            }

            if (item.LastPrice > 0)
            {
                var retrievedAt = item.LastUpdateTime > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(item.LastUpdateTime).LocalDateTime
                    : DateTime.MinValue;

                return new MonitoredItemSnapshot(
                    string.IsNullOrWhiteSpace(item.ItemKey) ? "ITEM." + item.ItemId : item.ItemKey,
                    item.ItemId ?? "",
                    item.Name ?? "",
                    item.ShortName ?? "",
                    item.LastPrice,
                    Math.Abs(item.LastChange) > 0.000001 ? item.LastChange : item.LastPrice * item.LastChangeRatio / 100.0,
                    item.LastChangeRatio,
                    retrievedAt,
                    string.IsNullOrWhiteSpace(item.LastStatus) ? "缓存" : item.LastStatus,
                    true,
                    true);
            }

            return new MonitoredItemSnapshot(
                string.IsNullOrWhiteSpace(item.ItemKey) ? "ITEM." + item.ItemId : item.ItemKey,
                item.ItemId ?? "",
                item.Name ?? "",
                item.ShortName ?? "",
                0,
                0,
                0,
                DateTime.MinValue,
                string.IsNullOrWhiteSpace(item.LastStatus) ? "未获取" : item.LastStatus,
                false,
                false);
        }

        private static MonitoredItemSnapshot BuildRuntimeItemSnapshot(ItemMonitorConfig item)
        {
            var latest = SteamDtItems.GetItemData(item.ItemId);
            if (latest == null && !string.IsNullOrWhiteSpace(item.MarketHashName))
            {
                latest = SteamDtItems.GetItemData(item.MarketHashName);
            }

            if (latest != null)
            {
                return new MonitoredItemSnapshot(
                    string.IsNullOrWhiteSpace(item.ItemKey) ? "ITEM." + item.ItemId : item.ItemKey,
                    item.ItemId ?? "",
                    item.Name ?? "",
                    item.ShortName ?? "",
                    latest.Price,
                    latest.Change,
                    latest.ChangeRatio,
                    latest.RetrievedAt,
                    latest.Source ?? "已获取",
                    true,
                    latest.IsStale);
            }

            if (item.LastPrice > 0)
            {
                var retrievedAt = item.LastUpdateTime > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(item.LastUpdateTime).LocalDateTime
                    : DateTime.MinValue;

                return new MonitoredItemSnapshot(
                    string.IsNullOrWhiteSpace(item.ItemKey) ? "ITEM." + item.ItemId : item.ItemKey,
                    item.ItemId ?? "",
                    item.Name ?? "",
                    item.ShortName ?? "",
                    item.LastPrice,
                    Math.Abs(item.LastChange) > 0.000001 ? item.LastChange : item.LastPrice * item.LastChangeRatio / 100.0,
                    item.LastChangeRatio,
                    retrievedAt,
                    string.IsNullOrWhiteSpace(item.LastStatus) ? "缓存" : item.LastStatus,
                    true,
                    true);
            }

            return new MonitoredItemSnapshot(
                string.IsNullOrWhiteSpace(item.ItemKey) ? "ITEM." + item.ItemId : item.ItemKey,
                item.ItemId ?? "",
                item.Name ?? "",
                item.ShortName ?? "",
                0,
                0,
                0,
                DateTime.MinValue,
                string.IsNullOrWhiteSpace(item.LastStatus) ? "未获取" : item.LastStatus,
                false,
                false);
        }

        public static bool IsDisplayKey(string? key)
        {
            return key != null &&
                (key.Equals(QaqDisplayKey, StringComparison.OrdinalIgnoreCase) ||
                 key.Equals(SteamDtDisplayKey, StringComparison.OrdinalIgnoreCase) ||
                 key.StartsWith("ITEM.", StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsMarketKey(string? key)
        {
            return key != null &&
                (key.StartsWith("STEAMDT.", StringComparison.OrdinalIgnoreCase) ||
                 key.StartsWith("CSQAQ.", StringComparison.OrdinalIgnoreCase) ||
                 key.StartsWith("ITEM.", StringComparison.OrdinalIgnoreCase));
        }

        public static int GetDisplayOrder(string key)
        {
            if (key.Equals(SteamDtDisplayKey, StringComparison.OrdinalIgnoreCase)) return 0;
            if (key.Equals(QaqDisplayKey, StringComparison.OrdinalIgnoreCase)) return 1;
            if (key.StartsWith("ITEM.", StringComparison.OrdinalIgnoreCase)) return 2;
            return 10;
        }

        public static MarketDisplaySnapshot GetDisplaySnapshot(string key, bool triggerFetch = false)
        {
            if (key.Equals(SteamDtDisplayKey, StringComparison.OrdinalIgnoreCase))
            {
                if (triggerFetch)
                {
                    RequestDisplayRefreshIfNeeded(SteamDtDisplayKey);
                }

                var stateSnapshot = RuntimeState.Market.SteamDt;
                if (stateSnapshot?.HasData == true)
                {
                    return new MarketDisplaySnapshot
                    {
                        Key = SteamDtDisplayKey,
                        Label = "DT指数",
                        HasData = true,
                        IsStale = stateSnapshot.IsStale,
                        Index = stateSnapshot.Index,
                        Change = stateSnapshot.Change,
                        Percent = stateSnapshot.Percent,
                        RetrievedAt = stateSnapshot.RetrievedAt
                    };
                }

                var latest = SteamDt.Latest;
                if (latest == null)
                {
                    return new MarketDisplaySnapshot
                    {
                        Key = SteamDtDisplayKey,
                        Label = "DT指数",
                        PlaceholderText = GetNoDataPlaceholder(SteamDt.LastError)
                    };
                }

                return new MarketDisplaySnapshot
                {
                    Key = SteamDtDisplayKey,
                    Label = "DT指数",
                    HasData = true,
                    IsStale = latest.IsStale,
                    Index = latest.Index,
                    Change = latest.DiffYesterday,
                    Percent = latest.DiffYesterdayRatio,
                    RetrievedAt = latest.RetrievedAt
                };
            }

            if (key.Equals(QaqDisplayKey, StringComparison.OrdinalIgnoreCase))
            {
                if (triggerFetch)
                {
                    RequestDisplayRefreshIfNeeded(QaqDisplayKey);
                }

                var stateSnapshot = RuntimeState.Market.Qaq;
                if (stateSnapshot?.HasData == true)
                {
                    return new MarketDisplaySnapshot
                    {
                        Key = QaqDisplayKey,
                        Label = "QAQ指数",
                        HasData = true,
                        IsStale = stateSnapshot.IsStale,
                        Index = stateSnapshot.Index,
                        Change = stateSnapshot.Change,
                        Percent = stateSnapshot.Percent,
                        RetrievedAt = stateSnapshot.RetrievedAt
                    };
                }

                var latest = Csqaq.Latest;
                if (latest == null)
                {
                    return new MarketDisplaySnapshot
                    {
                        Key = QaqDisplayKey,
                        Label = "QAQ指数",
                        PlaceholderText = GetNoDataPlaceholder(Csqaq.LastError)
                    };
                }

                return new MarketDisplaySnapshot
                {
                    Key = QaqDisplayKey,
                    Label = "QAQ指数",
                    HasData = true,
                    IsStale = latest.IsStale,
                    Index = latest.MarketIndex,
                    Change = latest.Change,
                    Percent = latest.ChangeRate,
                    RetrievedAt = latest.RetrievedAt
                };
            }

            if (key.StartsWith("ITEM.", StringComparison.OrdinalIgnoreCase))
            {
                string itemId = key.Substring(5);
                var itemConfig = Services.AppConfigState.ItemMonitor.Items.FirstOrDefault(x =>
                    string.Equals(x.ItemKey, key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.ItemId, itemId, StringComparison.OrdinalIgnoreCase));
                var runtimeItem = RuntimeState.Items.FirstOrDefault(x =>
                    string.Equals(x.ItemKey, key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.ItemId, itemId, StringComparison.OrdinalIgnoreCase));

                if (itemConfig == null && runtimeItem == null)
                {
                    return new MarketDisplaySnapshot
                    {
                        Key = key,
                        Label = "未知饰品",
                        PlaceholderText = "未配置"
                    };
                }

                if (runtimeItem?.HasData == true)
                {
                    return new MarketDisplaySnapshot
                    {
                        Key = key,
                        Label = ResolveItemSnapshotLabel(itemConfig, runtimeItem, "饰品价格"),
                        HasData = true,
                        IsStale = runtimeItem.IsStale,
                        Index = runtimeItem.Price,
                        Change = runtimeItem.Change,
                        Percent = runtimeItem.ChangePercent,
                        RetrievedAt = runtimeItem.RetrievedAt,
                        HasChangeData = Math.Abs(runtimeItem.Change) > 0.000001 || Math.Abs(runtimeItem.ChangePercent) > 0.000001
                    };
                }

                // Check in-memory cache first
                string lookupItemId = !string.IsNullOrWhiteSpace(itemConfig?.ItemId)
                    ? itemConfig.ItemId
                    : runtimeItem?.ItemId ?? itemId;
                var latest = SteamDtItems.GetItemData(lookupItemId);
                if (latest == null && !string.IsNullOrWhiteSpace(itemConfig?.MarketHashName))
                {
                    latest = SteamDtItems.GetItemData(itemConfig.MarketHashName);
                }

                if (latest != null)
                {
                    return new MarketDisplaySnapshot
                    {
                        Key = key,
                        Label = ResolveItemSnapshotLabel(itemConfig, runtimeItem, "饰品价格"),
                        HasData = true,
                        IsStale = latest.IsStale,
                        Index = latest.Price,
                        Change = latest.Change,
                        Percent = latest.ChangeRatio,
                        RetrievedAt = latest.RetrievedAt,
                        HasChangeData = latest.HasChangeData
                    };
                }

                // Fallback to settings persistent cache published in AppConfigState.
                if (itemConfig?.LastPrice > 0)
                {
                    return new MarketDisplaySnapshot
                    {
                        Key = key,
                        Label = ResolveItemSnapshotLabel(itemConfig, runtimeItem, "饰品价格"),
                        HasData = true,
                        IsStale = true,
                        Index = itemConfig.LastPrice,
                        Change = Math.Abs(itemConfig.LastChange) > 0.000001
                            ? itemConfig.LastChange
                            : itemConfig.LastPrice * itemConfig.LastChangeRatio / 100.0,
                        Percent = itemConfig.LastChangeRatio,
                        RetrievedAt = itemConfig.LastUpdateTime > 0
                            ? DateTimeOffset.FromUnixTimeMilliseconds(itemConfig.LastUpdateTime).LocalDateTime
                            : DateTime.Now,
                        HasChangeData = itemConfig.HasChangeData
                    };
                }

                return new MarketDisplaySnapshot
                {
                    Key = key,
                    Label = ResolveItemSnapshotLabel(itemConfig, runtimeItem, "加载中"),
                    PlaceholderText = "加载中..."
                };
            }

            return new MarketDisplaySnapshot { Key = key };
        }

        private static string ResolveItemSnapshotLabel(
            ItemConfigSummary? itemConfig,
            MonitoredItemSnapshot? runtimeItem,
            string fallback)
        {
            if (!string.IsNullOrWhiteSpace(itemConfig?.Name))
                return itemConfig.Name;

            if (!string.IsNullOrWhiteSpace(runtimeItem?.Name))
                return runtimeItem.Name;

            return fallback;
        }

        private static void RequestDisplayRefreshIfNeeded(string key)
        {
            if (HasDisplayData(key))
                return;

            if (Volatile.Read(ref _startupRefreshInFlight) != 0)
                return;

            if (IsDisplayRefreshSuppressed())
                return;

            if (MarketRefreshPipeline.IsRunning)
                return;

            if (Interlocked.Exchange(ref _displayRefreshRequestIssued, 1) != 0)
                return;

            if (Interlocked.CompareExchange(ref _displayRefreshRequestInFlight, 1, 0) != 0)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await RequestPassiveMarketIndexRefreshAsync("显示触发刷新");
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Error("MarketData", "Display-triggered market index refresh failed.", ex);
                }
                finally
                {
                    Interlocked.Exchange(ref _displayRefreshRequestInFlight, 0);
                }
            });
        }

        private static bool HasDisplayData(string key)
        {
            if (key.Equals(SteamDtDisplayKey, StringComparison.OrdinalIgnoreCase))
            {
                return RuntimeState.Market.SteamDt?.HasData == true
                    || SteamDt.Latest != null;
            }

            if (key.Equals(QaqDisplayKey, StringComparison.OrdinalIgnoreCase))
            {
                return RuntimeState.Market.Qaq?.HasData == true
                    || Csqaq.Latest != null;
            }

            return true;
        }

        private static string GetNoDataPlaceholder(string lastError)
        {
            if (MarketRefreshPipeline.IsRunning || Volatile.Read(ref _displayRefreshRequestInFlight) != 0)
            {
                return "加载中";
            }

            if (string.IsNullOrWhiteSpace(lastError))
                return "加载中";

            if (lastError.Contains("今日访问次数超限", StringComparison.OrdinalIgnoreCase)
                || lastError.Contains("明日再试", StringComparison.OrdinalIgnoreCase))
                return "今日限额";

            if (lastError.Contains("SteamDT 服务", StringComparison.OrdinalIgnoreCase)
                || lastError.Contains("HTTP 500", StringComparison.OrdinalIgnoreCase)
                || lastError.Contains("Internal Server Error", StringComparison.OrdinalIgnoreCase))
                return "服务异常";

            return "刷新失败";
        }
    }
}
