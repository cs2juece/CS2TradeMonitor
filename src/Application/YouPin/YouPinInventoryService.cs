using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using static CS2TradeMonitor.Application.YouPin.YouPinInventoryComputationHelper;

namespace CS2TradeMonitor.Application.YouPin
{
    public sealed class YouPinInventoryService : IYouPinInventoryService
    {
        private const string InventoryEndpoint = YouPinMobileApiClient.BaseUrl + "/api/youpin/commodity-agg/inventory/list/pull";
        private const string TrendEndpoint = YouPinMobileApiClient.BaseUrl + "/api/youpin/commodity-agg/inventory/trend/data";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        private static readonly object InstanceLock = new();
        private static YouPinInventoryService? _instance;
        public static YouPinInventoryService Instance
        {
            get
            {
                lock (InstanceLock)
                {
                    return _instance ??= new YouPinInventoryService();
                }
            }
        }

        private readonly IYouPinAuthService _authService;
        private readonly HttpClient _http;
        private readonly SemaphoreSlim _fetchLock = new(1, 1);
        private readonly object _stateLock = new();
        private readonly string _historyPath = RuntimeDataPaths.GetDataFilePath("youpin_inventory_history.json");
        private System.Threading.Timer? _timer;
        private Settings _settings = new();
        private YouPinInventoryHistory _history = new();
        private DateTime _lastFetch = DateTime.MinValue;
        private string _lastStatus = "未读取";
        private string _lastError = "";
        private YouPinInventoryTrendState? _latestRemoteTrendState;
        private int _mockSequence;

        public event Action? DataUpdated;

        private YouPinInventoryService()
            : this(YouPinServiceRuntimeServices.Resolve())
        {
        }

        internal YouPinInventoryService(YouPinServiceRuntimeServices services)
            : this(services.Auth, services.DomesticHttpFactory)
        {
        }

        internal YouPinInventoryService(IYouPinAuthService authService, IDomesticHttpClientFactory httpFactory)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _http = (httpFactory ?? throw new ArgumentNullException(nameof(httpFactory))).Create(20);
            _history = YouPinInventoryHistoryStore.Load(_historyPath, JsonOptions);
        }

        public void Configure(Settings settings)
        {
            _settings = settings ?? new Settings();
            _timer?.Dispose();
            _timer = null;

            if (!HasInventoryCredential())
            {
                lock (_stateLock)
                {
                    _latestRemoteTrendState = null;
                }
            }

            if (!_settings.YouPinInventoryEnabled && !_settings.YouPinStopProfitLossEnabled)
            {
                _lastStatus = "未启用";
                return;
            }

            _timer = new System.Threading.Timer(async _ => await FetchIfDueAsync(), null, 10000, 60000);
            _ = FetchIfDueAsync();
        }

        public YouPinInventoryState GetState()
        {
            if (!HasInventoryCredential())
                return CreateLockedInventoryState("未登录");

            lock (_stateLock)
            {
                var snapshot = _history.Snapshots.LastOrDefault();
                return new YouPinInventoryState
                {
                    Enabled = _settings.YouPinInventoryEnabled,
                    LastStatus = _lastStatus,
                    LastError = _lastError,
                    LastFetch = _lastFetch,
                    TotalCount = snapshot?.TotalCount ?? 0,
                    TotalValue = snapshot?.TotalValue ?? 0,
                    PreviousTotalValue = snapshot?.PreviousTotalValue ?? 0,
                    TotalDelta = snapshot?.TotalDelta ?? 0,
                    TotalDeltaPercent = snapshot?.TotalDeltaPercent ?? 0,
                    Items = snapshot?.Items.ToList() ?? new List<YouPinInventoryItem>(),
                    RecentChanges = _history.Changes.OrderByDescending(x => x.Time).Take(30).ToList(),
                    RecentValueAlerts = _history.ValueAlerts.OrderByDescending(x => x.Time).Take(30).ToList(),
                    RecentStopProfitLossAlerts = _history.StopProfitLossAlerts.OrderByDescending(x => x.Time).Take(30).ToList(),
                    DailyPoints = _history.Daily.OrderBy(x => x.Date).TakeLast(90).ToList()
                };
            }
        }

        public YouPinStopProfitLossState GetStopProfitLossState()
        {
            lock (_stateLock)
            {
                return new YouPinStopProfitLossState
                {
                    Enabled = _settings.YouPinStopProfitLossEnabled,
                    LastStatus = _lastStatus,
                    LastError = _lastError,
                    LastFetch = _lastFetch,
                    AlertCount = _history.StopProfitLossAlerts.Count,
                    RecentAlerts = _history.StopProfitLossAlerts.OrderByDescending(x => x.Time).Take(30).ToList()
                };
            }
        }

        public YouPinInventoryTrendState GetTrendState()
        {
            if (!HasInventoryCredential())
            {
                return new YouPinInventoryTrendState
                {
                    LastStatus = "未登录，请先登录后读取库存",
                    LastError = "",
                    LastFetch = DateTime.MinValue
                };
            }

            lock (_stateLock)
            {
                if (_latestRemoteTrendState != null)
                    return CloneTrendState(_latestRemoteTrendState);

                var snapshots = (_history.Snapshots ?? new List<YouPinInventorySnapshot>())
                    .OrderBy(x => x.Time)
                    .ToList();
                var current = snapshots.LastOrDefault();
                var previous = snapshots.Count >= 2 ? snapshots[^2] : null;

                var state = new YouPinInventoryTrendState
                {
                    LastStatus = _lastStatus,
                    LastError = _lastError,
                    LastFetch = _lastFetch
                };

                if (current == null)
                    return state;

                state.TotalCount = current.TotalCount;
                state.TotalValue = current.TotalValue;
                state.TotalDelta = current.TotalDelta;
                state.TotalDeltaPercent = current.TotalDeltaPercent;
                state.PurchaseValue = current.Items.Where(x => x.PurchasePrice > 0).Sum(x => x.PurchasePrice);
                state.MissingPurchaseCount = current.Items.Count(x => x.PurchasePrice <= 0);
                state.Rows = BuildTrendRows(current, previous);
                return state;
            }
        }

        public async Task<YouPinInventoryFetchResult> FetchNowAsync(bool useMock = false, CancellationToken cancellationToken = default)
        {
            return await FetchCoreAsync(force: true, useMock: useMock, cancellationToken);
        }

        private async Task FetchIfDueAsync()
        {
            await FetchCoreAsync(force: false, useMock: false, CancellationToken.None);
        }

        private async Task<YouPinInventoryFetchResult> FetchCoreAsync(bool force, bool useMock, CancellationToken cancellationToken)
        {
            if (!await _fetchLock.WaitAsync(0, cancellationToken))
                return YouPinInventoryFetchResult.Skip("已有库存刷新正在执行，本次请求已合并。");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!_settings.YouPinInventoryEnabled && !_settings.YouPinStopProfitLossEnabled && !useMock && !force)
                    return YouPinInventoryFetchResult.Skip("未启用");

                int refreshSec = Math.Max(300, _settings.YouPinInventoryRefreshSec <= 0 ? 1800 : _settings.YouPinInventoryRefreshSec);
                if (!force && !useMock && DateTime.Now - _lastFetch < TimeSpan.FromSeconds(refreshSec))
                    return YouPinInventoryFetchResult.Skip("未到抓取时间");

                List<YouPinInventoryItem> items;
                double totalValueOverride = 0;
                YouPinInventoryTrendState? remoteTrendState = null;
                string source;
                if (useMock)
                {
                    items = CreateMockItems();
                    source = "模拟数据";
                }
                else
                {
                    var credential = _authService.GetCredential(_settings);
                    if (credential == null || string.IsNullOrWhiteSpace(credential.Token))
                        throw new InvalidOperationException("请先在悠悠有品登录区完成登录，或使用“模拟数据测试”。");

                    var remote = await FetchRemoteInventoryAsync(credential.Token, credential.DeviceToken, credential.Uk, cancellationToken);
                    items = remote.Items;
                    totalValueOverride = remote.TotalValue;
                    remoteTrendState = remote.TrendState;
                    source = "悠悠有品";
                }

                var snapshot = RecordSnapshot(items, source, totalValueOverride, remoteTrendState);
                _lastFetch = snapshot.Time;
                _lastStatus = remoteTrendState != null && remoteTrendState.Rows.Count > 0
                    ? $"悠悠有品涨跌读取成功：{snapshot.TotalCount} 件，市场价 ¥{snapshot.TotalValue:F2}"
                    : $"{source}读取成功：{snapshot.TotalCount} 件，估值 ¥{snapshot.TotalValue:F2}";
                _lastError = "";
                lock (_stateLock)
                {
                    if (remoteTrendState != null)
                    {
                        remoteTrendState.LastFetch = snapshot.Time;
                        remoteTrendState.LastError = "";
                        remoteTrendState.LastStatus = _lastStatus;
                        _latestRemoteTrendState = remoteTrendState;
                    }
                    else if (useMock)
                    {
                        _latestRemoteTrendState = null;
                    }
                }
                SaveHistory();
                DataUpdated?.Invoke();
                return YouPinInventoryFetchResult.Success(_lastStatus);
            }
            catch (OperationCanceledException)
            {
                return YouPinInventoryFetchResult.Skip("刷新已取消");
            }
            catch (Exception ex)
            {
                _lastError = Sanitize(ex.Message);
                _lastStatus = "读取失败";
                DataUpdated?.Invoke();
                return YouPinInventoryFetchResult.Failed(_lastError);
            }
            finally
            {
                _fetchLock.Release();
            }
        }

        private bool HasInventoryCredential()
        {
            var credential = _authService.GetCredential(_settings);
            return credential != null && !string.IsNullOrWhiteSpace(credential.Token);
        }

        private YouPinInventoryState CreateLockedInventoryState(string status)
        {
            return new YouPinInventoryState
            {
                Enabled = _settings.YouPinInventoryEnabled,
                LastStatus = status,
                LastError = "",
                LastFetch = DateTime.MinValue,
                Items = new List<YouPinInventoryItem>(),
                RecentChanges = new List<YouPinInventoryChange>(),
                RecentValueAlerts = new List<YouPinInventoryValueAlert>(),
                RecentStopProfitLossAlerts = new List<YouPinStopProfitLossAlert>(),
                DailyPoints = new List<YouPinDailyPnl>()
            };
        }

        private async Task<RemoteInventoryResult> FetchRemoteInventoryAsync(string token, string deviceToken, string uk, CancellationToken cancellationToken)
        {
            string device = string.IsNullOrWhiteSpace(deviceToken) ? YouPinMobileApiClient.GetDeviceToken() : deviceToken.Trim();
            var list = new List<YouPinInventoryItem>();
            double firstPageTotalEstimate = 0;
            int pageIndex = 1;
            bool hasNext = true;

            while (hasNext && pageIndex <= 50)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var req = new HttpRequestMessage(HttpMethod.Post, InventoryEndpoint);
                req.Content = YouPinMobileApiClient.JsonContent(new
                {
                    AppType = "4",
                    GameID = 730,
                    IsMerge = 0,
                    IsRefresh = pageIndex == 1,
                    PageIndex = pageIndex,
                    RefreshType = 2,
                    Sessionid = device
                });
                ApplyYouPinHeaders(req, token.Trim(), device, uk);

                using var resp = await YouPinMobileApiClient.SendAsync(_http, req, "读取悠悠有品库存", cancellationToken);
                if (!resp.IsSuccessStatusCode)
                    throw new InvalidOperationException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");

                using var doc = await YouPinMobileApiClient.ReadJsonDocumentAsync(resp, "读取悠悠有品库存");
                var root = doc.RootElement;
                int code = GetInt(root, "Code", "code");
                if (code != 0)
                {
                    string msg = GetString(root, "Msg", "msg", "Message", "message", "Tip") ?? "";
                    if (YouPinMobileApiClient.IsLoginExpired(code, msg))
                        throw new InvalidOperationException("悠悠有品登录状态失效，请重新登录。");
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(msg) ? $"悠悠有品返回 Code={code}" : msg);
                }

                if (!TryGetProperty(root, out var data, "Data", "data"))
                    break;

                if (pageIndex == 1)
                    firstPageTotalEstimate = GetTotalEstimate(data);

                int currentCount = 0;
                if (TryGetProperty(data, out var itemArray, "ItemsInfos", "itemsInfos", "items", "Items") && itemArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in itemArray.EnumerateArray())
                    {
                        var parsed = ParseInventoryItem(item);
                        if (!string.IsNullOrWhiteSpace(parsed.AssetId) || !string.IsNullOrWhiteSpace(parsed.Name))
                        {
                            list.Add(parsed);
                            currentCount++;
                        }
                    }
                }

                hasNext = GetBool(data, "hasNext", "HasNext") && currentCount > 0;
                pageIndex++;
                if (hasNext)
                    await Task.Delay(150, cancellationToken);
            }

            var trendState = await TryFetchRemoteTrendStateAsync(token.Trim(), device, uk, cancellationToken);

            // 悠悠有品接口返回的数据中可能包含钱包余额等无关字段（例如 7.02），
            // 若先用 GetTotalEstimate 递归匹配，会错误地将钱包余额当作库存估值。
            // 因此必须优先使用 trend/data 的官方 marketPriceTotal 或完整列表的 market price 求和。
            double totalValue = 0;
            if (trendState != null && trendState.TotalValue > 0)
            {
                totalValue = trendState.TotalValue;
            }
            else
            {
                double sumPrice = list.Sum(x => x.Price);
                if (sumPrice > 0)
                {
                    totalValue = sumPrice;
                }
                else
                {
                    totalValue = firstPageTotalEstimate;
                }
            }

            return new RemoteInventoryResult
            {
                Items = list,
                TotalValue = totalValue,
                TrendState = trendState
            };
        }

        private async Task<YouPinInventoryTrendState?> TryFetchRemoteTrendStateAsync(string token, string device, string uk, CancellationToken cancellationToken)
        {
            try
            {
                var state = new YouPinInventoryTrendState();
                int pageIndex = 1;
                int totalPages = 1;
                const int pageSize = 100;

                while (pageIndex <= Math.Min(50, totalPages))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using var req = new HttpRequestMessage(HttpMethod.Post, TrendEndpoint);
                    req.Content = YouPinMobileApiClient.JsonContent(new
                    {
                        forceRefresh = pageIndex == 1,
                        pageIndex,
                        pageSize,
                        queryType = 1
                    });
                    ApplyYouPinHeaders(req, token, device, uk);

                    using var resp = await YouPinMobileApiClient.SendAsync(_http, req, "读取悠悠有品库存涨跌", cancellationToken);
                    if (!resp.IsSuccessStatusCode) return pageIndex == 1 ? null : state;

                    using var doc = await YouPinMobileApiClient.ReadJsonDocumentAsync(resp, "读取悠悠有品库存涨跌");
                    var root = doc.RootElement;
                    int code = GetInt(root, "code", "Code");
                    if (code != 0) return pageIndex == 1 ? null : state;
                    if (!TryGetProperty(root, out var data, "data", "Data")) return pageIndex == 1 ? null : state;

                    if (pageIndex == 1)
                    {
                        state.TotalCount = GetInt(data, "totalCount", "TotalCount");
                        state.TotalValue = GetDouble(data, "marketPriceTotal", "MarketPriceTotal");
                        state.PurchaseValue = GetDouble(data, "buyPriceTotal", "BuyPriceTotal");
                        state.TotalDelta = GetDouble(data, "profitAndLossTotal", "ProfitAndLossTotal");
                        state.TotalDeltaPercent = NormalizePercent(GetDouble(data, "profitAndLossRangeTotal", "ProfitAndLossRangeTotal"));
                        state.HasOfficialProfitAndLoss = true;
                        state.MissingPurchaseCount = GetInt(data, "ownNoneBuyPriceCount", "OwnNoneBuyPriceCount")
                            + GetInt(data, "rentedNoneBuyPriceCount", "RentedNoneBuyPriceCount");
                        totalPages = Math.Max(1, GetInt(data, "totalPages", "TotalPages"));
                    }

                    if (TryGetProperty(data, out var itemArray, "itemsInfos", "ItemsInfos") && itemArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in itemArray.EnumerateArray())
                            state.Rows.Add(ParseTrendApiRow(item));
                    }

                    pageIndex++;
                    if (pageIndex <= totalPages)
                        await Task.Delay(150, cancellationToken);
                }

                if (state.Rows.Count == 0 && state.TotalValue <= 0)
                    return null;

                state.Rows = state.Rows
                    .OrderByDescending(x => x.CurrentPrice)
                    .ThenBy(x => x.Name)
                    .ToList();
                return state;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        private YouPinInventorySnapshot RecordSnapshot(
            List<YouPinInventoryItem> items,
            string source,
            double totalValueOverride = 0,
            YouPinInventoryTrendState? trendState = null)
        {
            var now = DateTime.Now;
            bool useTrendSnapshot = trendState != null && trendState.Rows.Count > 0;
            var sourceItems = useTrendSnapshot ? BuildItemsFromTrendRows(trendState!) : items;
            var normalized = sourceItems
                .Select(NormalizeItem)
                .Where(x => !string.IsNullOrWhiteSpace(x.AssetId) || !string.IsNullOrWhiteSpace(x.Name))
                .OrderBy(x => x.Name)
                .ThenBy(x => x.AssetId)
                .ToList();

            var snapshot = new YouPinInventorySnapshot
            {
                Time = now,
                Source = useTrendSnapshot ? "悠悠有品涨跌" : source,
                TotalCount = useTrendSnapshot && trendState!.TotalCount > 0
                    ? trendState.TotalCount
                    : normalized.Sum(x => Math.Max(1, x.Quantity)),
                TotalValue = useTrendSnapshot && trendState!.TotalValue > 0
                    ? trendState.TotalValue
                    : totalValueOverride > 0 ? totalValueOverride : normalized.Sum(x => x.Price * Math.Max(1, x.Quantity)),
                Items = normalized
            };

            YouPinInventoryValueAlert? alertToNotify = null;
            List<YouPinStopProfitLossAlert> stopAlertsToNotify = new();
            lock (_stateLock)
            {
                _history.Snapshots ??= new List<YouPinInventorySnapshot>();
                _history.Changes ??= new List<YouPinInventoryChange>();
                _history.Daily ??= new List<YouPinDailyPnl>();
                _history.ValueAlerts ??= new List<YouPinInventoryValueAlert>();
                _history.StopProfitLossAlerts ??= new List<YouPinStopProfitLossAlert>();
                _history.LastStopProfitLossAlertTimes ??= new Dictionary<string, DateTime>();

                var previous = _history.Snapshots.LastOrDefault();
                if (previous != null)
                {
                    snapshot.PreviousTotalValue = previous.TotalValue;
                    snapshot.TotalDelta = snapshot.TotalValue - previous.TotalValue;
                    snapshot.TotalDeltaPercent = previous.TotalValue > 0
                        ? snapshot.TotalDelta / previous.TotalValue * 100.0
                        : 0;

                    foreach (var change in CompareSnapshots(previous, snapshot))
                        _history.Changes.Add(change);

                    alertToNotify = TryCreateValueAlert(previous, snapshot, source);
                    if (alertToNotify != null)
                    {
                        _history.LastValueAlertTime = alertToNotify.Time;
                        _history.ValueAlerts.Add(alertToNotify);
                    }
                }

                stopAlertsToNotify = YouPinStopProfitLossAlertEvaluator.CreateAlerts(_history, snapshot, _settings);
                if (stopAlertsToNotify.Count > 0)
                {
                    foreach (var alert in stopAlertsToNotify)
                    {
                        _history.LastStopProfitLossAlertTimes[alert.DedupeKey] = alert.Time;
                        _history.StopProfitLossAlerts.Add(alert);
                    }
                }

                UpdateDaily(snapshot, trendState);
                _history.Snapshots.Add(snapshot);
                YouPinInventoryHistoryStore.Prune(_history);
            }

            if (alertToNotify != null)
                NotifyValueAlert(alertToNotify);
            if (stopAlertsToNotify.Count > 0)
                NotifyStopProfitLossAlerts(stopAlertsToNotify);

            return snapshot;
        }

        private YouPinInventoryValueAlert? TryCreateValueAlert(YouPinInventorySnapshot previous, YouPinInventorySnapshot current, string source)
        {
            if (!_settings.YouPinInventoryChangeAlertEnabled) return null;
            if (previous.TotalValue <= 0 || current.TotalValue <= 0) return null;
            if (Math.Abs(current.TotalDelta) < 0.01) return null;

            double absPercent = Math.Abs(current.TotalDeltaPercent);
            double absAmount = Math.Abs(current.TotalDelta);
            double amountThreshold = Math.Max(0, _settings.YouPinInventoryChangeAmountThreshold);
            if (amountThreshold > 0 && absAmount < amountThreshold) return null;

            bool rising = current.TotalDelta > 0;
            double threshold = rising
                ? Math.Max(0.01, _settings.YouPinInventoryRisePercentThreshold)
                : Math.Max(0.01, _settings.YouPinInventoryFallPercentThreshold);
            if (absPercent < threshold) return null;

            int cooldown = Math.Clamp(_settings.YouPinInventoryChangeAlertCooldownMinutes <= 0 ? 30 : _settings.YouPinInventoryChangeAlertCooldownMinutes, 1, 1440);
            if (_history.LastValueAlertTime != DateTime.MinValue
                && current.Time - _history.LastValueAlertTime < TimeSpan.FromMinutes(cooldown))
            {
                return null;
            }

            return new YouPinInventoryValueAlert
            {
                Time = current.Time,
                Direction = rising ? "上涨" : "下跌",
                OldValue = previous.TotalValue,
                NewValue = current.TotalValue,
                Delta = current.TotalDelta,
                Percent = current.TotalDeltaPercent,
                Source = source,
                Message = $"库存估值{(rising ? "上涨" : "下跌")} {FormatSigned(current.TotalDelta)} ({FormatSignedPercent(current.TotalDeltaPercent)})"
            };
        }

        private void NotifyValueAlert(YouPinInventoryValueAlert alert)
        {
            if (_settings.DoNotDisturbEnabled) return;

            string title = "悠悠有品库存涨跌提醒";
            string message = $"{alert.Message}\n¥{alert.OldValue:F2} -> ¥{alert.NewValue:F2}";
            var mode = _settings.YouPinInventoryChangeAlertNotificationMode;
            bool showBubble = mode == YouPinSaleReminderNotificationMode.Bubble || mode == YouPinSaleReminderNotificationMode.BubbleAndSound;
            bool playSound = mode == YouPinSaleReminderNotificationMode.Sound || mode == YouPinSaleReminderNotificationMode.BubbleAndSound;
            if (!showBubble && !playSound)
                return;

            AppNotificationHub.Instance.Request(
                title,
                message,
                AppNotificationSeverity.Info,
                AppNotificationPlacement.BottomLeft,
                playSound,
                showToast: showBubble);
        }

        private void NotifyStopProfitLossAlerts(List<YouPinStopProfitLossAlert> alerts)
        {
            if (_settings.DoNotDisturbEnabled) return;
            if (alerts.Count == 0) return;

            string title = alerts.Count == 1 ? "库存止盈/损报警" : $"库存止盈/损报警（{alerts.Count} 条）";
            var preview = alerts.Take(3).Select(x => x.Message);
            string message = string.Join(Environment.NewLine, preview);
            if (alerts.Count > 3)
                message += Environment.NewLine + $"另有 {alerts.Count - 3} 条达到阈值。";

            var mode = _settings.YouPinStopProfitLossNotificationMode;
            bool showBubble = mode == YouPinSaleReminderNotificationMode.Bubble || mode == YouPinSaleReminderNotificationMode.BubbleAndSound;
            bool playSound = mode == YouPinSaleReminderNotificationMode.Sound || mode == YouPinSaleReminderNotificationMode.BubbleAndSound;
            if (!showBubble && !playSound)
                return;

            AppNotificationHub.Instance.Request(
                title,
                message,
                AppNotificationSeverity.Warning,
                AppNotificationPlacement.BottomLeft,
                playSound,
                showToast: showBubble);
        }

        private void UpdateDaily(YouPinInventorySnapshot snapshot, YouPinInventoryTrendState? trendState = null)
        {
            string date = snapshot.Time.ToString("yyyy-MM-dd");
            var existing = _history.Daily.FirstOrDefault(x => x.Date == date);
            if (existing == null)
            {
                existing = new YouPinDailyPnl { Date = date };
                _history.Daily.Add(existing);
            }

            existing.EndValue = snapshot.TotalValue;
            existing.Count = snapshot.TotalCount;
            existing.LastUpdate = snapshot.Time;

            var previousDay = _history.Daily
                .Where(x => string.CompareOrdinal(x.Date, date) < 0)
                .OrderBy(x => x.Date)
                .LastOrDefault();

            existing.Pnl = previousDay == null ? 0 : snapshot.TotalValue - previousDay.EndValue;
            if (trendState != null && trendState.TotalValue > 0 && trendState.HasOfficialProfitAndLoss)
            {
                existing.ProfitAndLoss = trendState.TotalDelta;
                existing.ProfitAndLossPercent = trendState.TotalDeltaPercent;
                existing.HasProfitAndLoss = true;
            }
            _history.Daily = _history.Daily.OrderBy(x => x.Date).TakeLast(180).ToList();
        }

        private void SaveHistory()
        {
            try
            {
                lock (_stateLock)
                {
                    YouPinInventoryHistoryStore.Prune(_history);
                }

                YouPinInventoryHistoryStore.Save(_historyPath, _history, JsonOptions);
            }
            catch
            {
                // History persistence must not block monitoring.
            }
        }

        private List<YouPinInventoryItem> CreateMockItems()
        {
            int tick = Interlocked.Increment(ref _mockSequence);
            double drift = tick % 2 == 0 ? 18.5 : -12.8;
            return new List<YouPinInventoryItem>
            {
                new() { AssetId = "mock-001", TemplateId = "1001", Name = "驾驶手套（★） | 月色织物 (久经沙场)", Price = 2680 + drift, PurchasePrice = 2550 },
                new() { AssetId = "mock-002", TemplateId = "1002", Name = "AK-47 | 红线 (久经沙场)", Price = 188.6, PurchasePrice = 205 },
                new() { AssetId = "mock-003", TemplateId = "1003", Name = "M4A1-S | 黑莲花 (略有磨损)", Price = 96.4 }
            };
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _http.Dispose();
            _fetchLock.Dispose();
        }
    }

}
