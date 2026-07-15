using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.Application;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Notify;
using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using static CS2TradeMonitor.Application.YouPin.YouPinJsonElementReader;
using static CS2TradeMonitor.Application.YouPin.YouPinSaleActionResultHelper;
using static CS2TradeMonitor.Application.YouPin.YouPinSaleNotificationHelper;
using static CS2TradeMonitor.Application.YouPin.YouPinSaleOrderGroupHelper;
using static CS2TradeMonitor.Application.YouPin.YouPinSaleOrderParser;
using static CS2TradeMonitor.Application.YouPin.YouPinSaleReminderHttpHelper;
using static CS2TradeMonitor.Application.YouPin.YouPinSaleReminderHistoryHelper;

namespace CS2TradeMonitor.Application.YouPin
{
    public sealed class YouPinSaleReminderService : IYouPinSaleReminderService
    {
        private const string BaseUrl = YouPinMobileApiClient.BaseUrl;
        private const string LegacySendOfferEndpoint = "/api/youpin/bff/trade/v1/order/sell/delivery/send-offer";
        private const string LegacyOfferStatusEndpoint = "/api/youpin/bff/trade/v1/order/sell/delivery/get-offer-status";
        private const string SendOfferEndpoint = "/api/youpin/bff/trade/v1/order/sell/delivery/sendOfferV2";
        private const string OfferStatusEndpoint = "/api/youpin/bff/trade/v1/order/sell/delivery/queryOffersStatus";
        private const string LegacyConfirmOfferEndpoint = "/api/youpin/bff/trade/v1/order/sell/delivery/confirmOffer";
        private static readonly TimeSpan ProcessedActionTtl = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan LoginExpiredNotifyCooldown = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan OfferStatusPollDelay = TimeSpan.FromMilliseconds(1800);
        private const int OfferStatusPollAttempts = 4;
        private const int DeviceHeartbeatWarningThreshold = 3;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        private static YouPinSaleReminderService? _instance;
        public static YouPinSaleReminderService Instance => _instance ??= new YouPinSaleReminderService();

        private readonly IYouPinAuthService _authService;
        private readonly HttpClient _http;
        private readonly YouPinSaleReminderRemoteClient _remoteClient;
        private readonly YouPinRentalOfferConfirmationClient _rentalOfferConfirmationClient;
        private readonly YouPinSaleReminderHistoryStore _historyStore;
        private readonly SemaphoreSlim _fetchLock = new(1, 1);
        private readonly SemaphoreSlim _sendOfferLock = new(1, 1);
        private readonly object _stateLock = new();
        private readonly string _historyPath = RuntimeDataPaths.GetDataFilePath("youpin_sale_reminder_history.json");
        private System.Threading.Timer? _timer;
        private Settings _settings = new();
        private YouPinSaleReminderHistory _history = new();
        private DateTime _lastCheck = DateTime.MinValue;
        private string _lastStatus = "未检查";
        private string _lastError = "";
        private DateTime _lastMsgCenterCheck = DateTime.MinValue;
        private string _lastMsgCenterStatus = "未检查";
        private string _lastMsgCenterError = "";
        private DateTime _lastAutoDeliveryCheck = DateTime.MinValue;
        private string _lastAutoDeliveryStatus = "未检查";
        private string _lastAutoDeliveryError = "";
        private DateTime _lastLoginExpiredNotificationUtc = DateTime.MinValue;
        private int _deviceHeartbeatFailures;
        private int _mockSequence;
        private readonly ConcurrentDictionary<string, DateTime> _processedActions = new(StringComparer.OrdinalIgnoreCase);

        public event Action? DataUpdated;
        public event Action<IReadOnlyList<YouPinSaleOrder>>? NewWaitDeliverOrdersDetected;

        private YouPinSaleReminderService()
            : this(YouPinServiceRuntimeServices.Resolve())
        {
        }

        internal YouPinSaleReminderService(YouPinServiceRuntimeServices services)
            : this(services.Auth, services.DomesticHttpFactory)
        {
        }

        internal YouPinSaleReminderService(IYouPinAuthService authService, IDomesticHttpClientFactory httpFactory)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _http = (httpFactory ?? throw new ArgumentNullException(nameof(httpFactory))).Create(20);
            _remoteClient = new YouPinSaleReminderRemoteClient(_http);
            _rentalOfferConfirmationClient = new YouPinRentalOfferConfirmationClient(_http);
            _historyStore = new YouPinSaleReminderHistoryStore(_historyPath, JsonOptions);
            _history = _historyStore.Load();
        }

        public void Configure(Settings settings)
        {
            _settings = settings ?? new Settings();

            _timer?.Dispose();
            _timer = null;

            if (!_settings.YouPinSaleReminderEnabled
                && !_settings.YouPinQuoteAutoRefreshEnabled
                && !_settings.YouPinMsgCenterEnabled)
            {
                _lastStatus = "未启用";
                _lastAutoDeliveryStatus = "未启用";
                _lastMsgCenterStatus = "未启用";
                return;
            }

            var credential = _authService.GetCredential(_settings);
            if (credential == null || string.IsNullOrWhiteSpace(credential.Token))
            {
                _lastStatus = "未登录";
                _lastAutoDeliveryStatus = _settings.YouPinQuoteAutoRefreshEnabled ? "未登录" : "未启用";
                _lastMsgCenterStatus = _settings.YouPinMsgCenterEnabled ? "未登录" : "未启用";
                return;
            }

            _timer = new System.Threading.Timer(async _ => await CheckIfDueAsync(), null, 10000, 15000);
            _ = CheckIfDueAsync();
        }

        public YouPinSaleReminderState GetState()
        {
            var hasCredential = HasYouPinCredential();
            string accountKey = GetCurrentAccountKey();
            lock (_stateLock)
            {
                _history.RecentOrders ??= new List<YouPinSaleOrder>();
                _history.RecentMsgCenterOrders ??= new List<YouPinSaleOrder>();
                _history.RecentWaitDeliverOrders ??= new List<YouPinSaleOrder>();
                RemoveMockHistory(_history);
                if (!hasCredential)
                {
                    var state = YouPinSaleReminderStateFactory.CreateNoCredentialState(_settings);
                    state.HistoryPersistenceWarning = _historyStore.LastError;
                    return state;
                }

                return new YouPinSaleReminderState
                {
                    Enabled = _settings.YouPinSaleReminderEnabled,
                    LastCheck = _lastCheck,
                    LastStatus = _lastStatus,
                    LastError = _lastError,
                    RecentOrders = _history.RecentOrders
                        .Where(x => !IsMockOrder(x))
                        .Where(x => IsCurrentAccountOrder(x, accountKey))
                        .Where(ShouldIncludeTodo)
                        .OrderByDescending(x => x.DetectedAt)
                        .Take(30)
                        .ToList(),
                    MsgCenterEnabled = _settings.YouPinMsgCenterEnabled,
                    LastMsgCenterCheck = _lastMsgCenterCheck,
                    LastMsgCenterStatus = _lastMsgCenterStatus,
                    LastMsgCenterError = _lastMsgCenterError,
                    RecentMsgCenterOrders = _history.RecentMsgCenterOrders
                        .Where(x => !IsMockOrder(x))
                        .Where(x => IsCurrentAccountOrder(x, accountKey))
                        .OrderByDescending(x => x.DetectedAt)
                        .Take(30)
                        .ToList(),
                    QuoteAutoRefreshEnabled = _settings.YouPinQuoteAutoRefreshEnabled,
                    RecentWaitDeliverOrders = _history.RecentWaitDeliverOrders
                        .Where(x => !IsMockOrder(x))
                        .Where(x => IsCurrentAccountOrder(x, accountKey))
                        .OrderByDescending(x => x.DetectedAt)
                        .Take(30)
                        .ToList(),
                    LastAutoDeliveryCheck = _lastAutoDeliveryCheck,
                    LastAutoDeliveryStatus = _lastAutoDeliveryStatus,
                    LastAutoDeliveryError = _lastAutoDeliveryError,
                    HistoryPersistenceWarning = _historyStore.LastError
                };
            }
        }

        public async Task<YouPinSaleReminderCheckResult> CheckTodoNowAsync(bool useMock = false, bool notify = true)
        {
            return await CheckTodoCoreAsync(force: true, useMock: useMock, notify: notify);
        }

        public async Task<YouPinSaleReminderCheckResult> CheckQuoteNowAsync(string trigger = "立即刷新")
        {
            return await CheckQuoteCoreAsync(force: true, trigger: string.IsNullOrWhiteSpace(trigger) ? "立即刷新" : trigger);
        }

        public async Task<YouPinSaleReminderCheckResult> CheckMsgCenterNowAsync(bool useMock = false, bool notify = true)
        {
            return await CheckMsgCenterCoreAsync(force: true, useMock: useMock, notify: notify);
        }

        public string EnsureQuoteLogFile()
        {
            return SteamOfferAuditLog.EnsureLogFile();
        }

        public async Task<YouPinSaleActionResult> SendOfferAsync(string orderNo, string trigger = SteamOfferAuditLog.TriggerUserManual)
        {
            var result = await SendOfferActionAsync(orderNo);
            AppendQuoteActionLog("发送报价", orderNo, result, trigger);
            return result;
        }

        private async Task<YouPinSaleActionResult> SendOfferActionAsync(string orderNo)
        {
            if (string.IsNullOrWhiteSpace(orderNo))
                return YouPinSaleActionResult.Failed("订单号为空，无法发送报价。");

            await _sendOfferLock.WaitAsync();
            try
            {
                var credential = GetRequiredCredential();
                string accountKey = GetCredentialAccountKey(credential);
                var localOrder = FindCurrentAccountWaitDeliverOrder(orderNo.Trim(), accountKey);
                if (localOrder == null)
                    return YouPinSaleActionResult.Failed("该订单不属于当前悠悠账号，或本地列表已过期。请先点击“立即刷新”同步当前账号的待发货/报价处理列表后再发送报价。");

                var actionOrderNos = ResolveActionOrderNos(localOrder);
                string processedKey = BuildProcessedActionKey(accountKey, "send", actionOrderNos, "");
                if (TryBuildProcessedSkip(processedKey, "发送报价", out var skipped))
                    return skipped;

                await TrySendDeviceHeartbeatAsync(credential).ConfigureAwait(false);
                await TradeWriteOperationGate.WaitAsync(BuildYouPinWriteGateKey(credential)).ConfigureAwait(false);

                string normalizedOrderNo = actionOrderNos[0];
                var h5Result = await SendOfferH5ForOrdersAsync(actionOrderNos, credential);
                if (h5Result.Ok)
                {
                    MarkProcessedAction(processedKey);
                    var enriched = await EnrichSendOfferResultAsync(actionOrderNos, credential, h5Result).ConfigureAwait(false);
                    ApplyOrderActionSnapshot(localOrder, enriched, YouPinSaleOrderActionKind.SendOffer);
                    return enriched;
                }

                if (IsOrderStateCannotSend(h5Result.Message) || actionOrderNos.Count > 1)
                    return h5Result;

                await TradeWriteOperationGate.WaitAsync(BuildYouPinWriteGateKey(credential)).ConfigureAwait(false);
                var legacyResult = await SendOfferLegacyAsync(normalizedOrderNo, credential);
                if (legacyResult.Ok)
                {
                    MarkProcessedAction(processedKey);
                    var enriched = await EnrichSendOfferResultAsync(actionOrderNos, credential, legacyResult).ConfigureAwait(false);
                    ApplyOrderActionSnapshot(localOrder, enriched, YouPinSaleOrderActionKind.SendOffer);
                    return enriched;
                }

                return MergeActionFailures("发送报价", h5Result, legacyResult);
            }
            catch (Exception ex)
            {
                var wrapped = YouPinMobileApiClient.WrapException(ex, "发送悠悠有品报价");
                if (IsYouPinLoginExpired(wrapped.Message))
                    PauseAfterLoginExpired("悠悠有品登录状态失效，请重新登录。");
                return YouPinSaleActionResult.Failed(Sanitize(wrapped.Message));
            }
            finally
            {
                _sendOfferLock.Release();
            }
        }

        public async Task<YouPinSaleActionResult> ConfirmOfferAsync(string orderNo, string tradeOfferId = "", string trigger = SteamOfferAuditLog.TriggerUserManual)
        {
            var result = await ConfirmOfferActionAsync(orderNo, tradeOfferId);
            AppendQuoteActionLog("确认报价", orderNo, result, trigger);
            return result;
        }

        private async Task<YouPinSaleActionResult> ConfirmOfferActionAsync(string orderNo, string tradeOfferId = "")
        {
            if (string.IsNullOrWhiteSpace(orderNo))
                return YouPinSaleActionResult.Failed("订单号为空，无法确认报价。");

            await _sendOfferLock.WaitAsync();
            try
            {
                var credential = GetRequiredCredential();
                string accountKey = GetCredentialAccountKey(credential);
                var localOrder = FindCurrentAccountWaitDeliverOrder(orderNo.Trim(), accountKey);
                if (localOrder == null)
                    return YouPinSaleActionResult.Failed("该订单不属于当前悠悠账号，或本地列表已过期。请先点击“立即刷新”同步当前账号的待发货/报价处理列表后再确认报价。");

                var actionOrderNos = ResolveActionOrderNos(localOrder);
                string normalizedOrderNo = actionOrderNos[0];
                string resolvedTradeOfferId = FirstText(tradeOfferId, localOrder.TradeOfferId);
                if (string.IsNullOrWhiteSpace(resolvedTradeOfferId))
                    resolvedTradeOfferId = await _remoteClient.TryFetchTradeOfferIdAsync(normalizedOrderNo, credential.Token, credential.DeviceToken, credential.Uk);

                if (string.IsNullOrWhiteSpace(resolvedTradeOfferId))
                    return YouPinSaleActionResult.Failed("缺少 Steam 报价号，请先点击“立即刷新”或在手机悠悠 APP 中确认报价。");

                string processedKey = BuildProcessedActionKey(accountKey, "confirm", actionOrderNos, resolvedTradeOfferId);
                if (TryBuildProcessedSkip(processedKey, "确认报价", out var skipped))
                    return skipped;

                await TrySendDeviceHeartbeatAsync(credential).ConfigureAwait(false);
                await TradeWriteOperationGate.WaitAsync(BuildYouPinWriteGateKey(credential)).ConfigureAwait(false);
                var result = await ConfirmOfferCoreAsync(actionOrderNos, resolvedTradeOfferId, localOrder.OrderType, credential);
                if (result.Ok)
                {
                    MarkProcessedAction(processedKey);
                    ApplyOrderActionSnapshot(localOrder, result, YouPinSaleOrderActionKind.ConfirmOffer);
                }
                return result;
            }
            catch (Exception ex)
            {
                var wrapped = YouPinMobileApiClient.WrapException(ex, "确认悠悠有品报价");
                if (IsYouPinLoginExpired(wrapped.Message))
                    PauseAfterLoginExpired("悠悠有品登录状态失效，请重新登录。");
                return YouPinSaleActionResult.Failed(Sanitize(wrapped.Message));
            }
            finally
            {
                _sendOfferLock.Release();
            }
        }

        public async Task<YouPinSaleActionResult> QueryOfferStatusAsync(string orderNo, string trigger = SteamOfferAuditLog.TriggerUserManual)
        {
            var result = await QueryOfferStatusActionAsync(orderNo);
            AppendQuoteActionLog("查询状态", orderNo, result, trigger);
            return result;
        }

        private async Task<YouPinSaleActionResult> QueryOfferStatusActionAsync(string orderNo)
        {
            if (string.IsNullOrWhiteSpace(orderNo))
                return YouPinSaleActionResult.Failed("订单号为空，无法查询报价状态。");

            try
            {
                var credential = GetRequiredCredential();
                string accountKey = GetCredentialAccountKey(credential);
                var localOrder = FindCurrentAccountWaitDeliverOrder(orderNo.Trim(), accountKey);
                var actionOrderNos = localOrder == null
                    ? new List<string> { orderNo.Trim() }
                    : ResolveActionOrderNos(localOrder);
                if (actionOrderNos.Count > 1)
                {
                    var batchResult = await QueryOfferStatusH5Async(actionOrderNos, credential);
                    if (batchResult.Ok && localOrder != null)
                        ApplyOrderActionSnapshot(localOrder, batchResult, YouPinSaleOrderActionKind.QueryStatus);
                    return batchResult.Ok
                        ? batchResult
                        : YouPinSaleActionResult.Failed("报价状态查询失败：" + batchResult.Message);
                }

                string normalizedOrderNo = actionOrderNos[0];
                var legacyResult = await QueryOfferStatusLegacyAsync(normalizedOrderNo, credential);
                if (legacyResult.Ok)
                {
                    if (localOrder != null)
                        ApplyOrderActionSnapshot(localOrder, legacyResult, YouPinSaleOrderActionKind.QueryStatus);
                    return legacyResult;
                }

                var h5Result = await QueryOfferStatusH5Async(normalizedOrderNo, credential);
                if (h5Result.Ok && localOrder != null)
                    ApplyOrderActionSnapshot(localOrder, h5Result, YouPinSaleOrderActionKind.QueryStatus);
                return h5Result.Ok ? h5Result : MergeActionFailures("查询报价状态", legacyResult, h5Result);
            }
            catch (Exception ex)
            {
                var wrapped = YouPinMobileApiClient.WrapException(ex, "查询悠悠有品报价状态");
                return YouPinSaleActionResult.Failed(Sanitize(wrapped.Message));
            }
        }

        private async Task<YouPinSaleActionResult> SendOfferLegacyAsync(string orderNo, YouPinCredential credential)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Put, BaseUrl + LegacySendOfferEndpoint);
                req.Content = YouPinMobileApiClient.JsonContent(new
                {
                    orderNo,
                    Sessionid = credential.DeviceToken
                });
                ApplyYouPinLegacyAndroidHeaders(req, credential.Token, credential.DeviceToken, credential.Uk);

                using var resp = await YouPinMobileApiClient.SendAsync(_http, req, "通过兼容接口发送悠悠有品报价");
                if (!resp.IsSuccessStatusCode)
                    return YouPinSaleActionResult.Failed("兼容接口：" + BuildFriendlyHttpError(resp));

                using var doc = await YouPinMobileApiClient.ReadJsonDocumentAsync(resp, "通过兼容接口发送悠悠有品报价");
                var root = doc.RootElement;
                int code = GetInt(root, "code", "Code");
                string msg = GetString(root, "msg", "Msg", "message", "Message") ?? "";
                if (code != 0)
                    return BuildFailedActionResult("兼容接口", code, msg, "发送报价失败");

                string tradeOfferId = FindTradeOfferId(root);
                return YouPinSaleActionResult.Success(
                    BuildActionSuccessMessage(
                        "发送报价",
                        "兼容旧接口",
                        orderNo,
                        tradeOfferId,
                        root,
                        "如列表仍显示待确认，请点击“确认报价”或“立即刷新”。"),
                    tradeOfferId,
                    status: 3);
            }
            catch (Exception ex)
            {
                return YouPinSaleActionResult.Failed("兼容接口：" + BuildFriendlyExceptionMessage(ex, "发送报价"));
            }
        }

        private async Task<YouPinSaleActionResult> EnrichSendOfferResultAsync(
            IReadOnlyList<string> orderNos,
            YouPinCredential credential,
            YouPinSaleActionResult sendResult)
        {
            var statusResult = await PollOfferStatusAfterSendAsync(orderNos, credential).ConfigureAwait(false);
            if (statusResult == null)
            {
                return YouPinSaleActionResult.Success(
                    "发送报价成功，待您令牌验证。",
                    sendResult.TradeOfferId,
                    sendResult.Status);
            }

            string tradeOfferId = FirstText(statusResult.TradeOfferId, sendResult.TradeOfferId);
            int status = statusResult.Status != 0 ? statusResult.Status : sendResult.Status;
            if (status == 4)
                return YouPinSaleActionResult.Failed("发送报价失败，请刷新后重试。");

            return YouPinSaleActionResult.Success(
                "发送报价成功，待您令牌验证。",
                tradeOfferId,
                status);
        }

        private async Task<YouPinSaleActionResult?> PollOfferStatusAfterSendAsync(
            IReadOnlyList<string> orderNos,
            YouPinCredential credential)
        {
            YouPinSaleActionResult? lastOk = null;
            for (int attempt = 0; attempt < OfferStatusPollAttempts; attempt++)
            {
                await Task.Delay(OfferStatusPollDelay).ConfigureAwait(false);
                var result = await QueryOfferStatusH5Async(orderNos, credential).ConfigureAwait(false);
                if (!result.Ok)
                    continue;

                lastOk = result;
                if (!string.IsNullOrWhiteSpace(result.TradeOfferId))
                    return result;
            }

            return lastOk;
        }

        private void ApplyOrderActionSnapshot(
            YouPinSaleOrder order,
            YouPinSaleActionResult result,
            YouPinSaleOrderActionKind actionKind)
        {
            if (order == null || result == null || !result.Ok)
                return;

            lock (_stateLock)
            {
                if (!string.IsNullOrWhiteSpace(result.TradeOfferId))
                    order.TradeOfferId = result.TradeOfferId.Trim();

                if (actionKind == YouPinSaleOrderActionKind.ConfirmOffer)
                {
                    YouPinQuoteLocalState.MarkConfirmSubmitted(order, result.TradeOfferId);
                }
                else if (YouPinQuoteLocalState.IsConfirmSubmitted(order))
                {
                    if (YouPinQuoteLocalState.ShouldClearConfirmSubmitted(result))
                    {
                        YouPinQuoteLocalState.Clear(order);
                        order.Message = "待您确认报价";
                        order.OrderStatusDesc = "待您确认报价";
                    }
                    else
                    {
                        YouPinQuoteLocalState.MarkConfirmSubmitted(order, result.TradeOfferId, order.LocalQuoteStateAt);
                    }
                }
                else if (actionKind == YouPinSaleOrderActionKind.SendOffer
                    || result.Status == 3
                    || result.Message.Contains("令牌", StringComparison.Ordinal))
                {
                    order.Message = "已发送报价，待您令牌验证";
                    order.OrderStatusDesc = "待您令牌验证";
                }
            }

            SaveHistory();
            RaiseDataUpdated();
        }

        private static void ApplySteamCounterpartyInfo(YouPinSaleOrder order, YouPinSteamCounterpartyFetchResult info)
        {
            if (info.Ok)
            {
                order.SteamPersonaName = info.PersonaName;
                order.SteamAvatarUrl = info.AvatarUrl;
                order.SteamPlayerLevel = info.PlayerLevel;
                order.SteamGameTime = info.GameTime;
                order.SteamJoinDate = info.JoinDate;
            }

            order.SteamCounterpartyStatus = info.Status;
        }

        private string BuildYouPinWriteGateKey(YouPinCredential credential)
        {
            return "YouPin:" + FirstText(credential.UserId, credential.Uk, credential.DeviceToken, "unknown");
        }

        private static string BuildProcessedActionKey(
            string accountKey,
            string action,
            IEnumerable<string> orderNos,
            string tradeOfferId)
        {
            var normalized = NormalizeOrderNoList(orderNos)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
            return string.Join("|", new[]
            {
                (accountKey ?? "").Trim(),
                (action ?? "").Trim(),
                string.Join(",", normalized),
                (tradeOfferId ?? "").Trim()
            });
        }

        private bool TryBuildProcessedSkip(string key, string actionName, out YouPinSaleActionResult result)
        {
            PruneProcessedActions();
            if (_processedActions.TryGetValue(key, out var processedAt)
                && DateTime.UtcNow - processedAt <= ProcessedActionTtl)
            {
                result = YouPinSaleActionResult.Skip($"{actionName}已处理过，请刷新状态确认。");
                return true;
            }

            result = YouPinSaleActionResult.Skip("");
            return false;
        }

        private void MarkProcessedAction(string key)
        {
            PruneProcessedActions();
            _processedActions[key] = DateTime.UtcNow;
        }

        private void PruneProcessedActions()
        {
            DateTime cutoff = DateTime.UtcNow - ProcessedActionTtl;
            foreach (var pair in _processedActions)
            {
                if (pair.Value < cutoff)
                    _processedActions.TryRemove(pair.Key, out _);
            }
        }

        private async Task TrySendDeviceHeartbeatAsync(YouPinCredential credential)
        {
            try
            {
                await TradeWriteOperationGate.WaitAsync(BuildYouPinWriteGateKey(credential)).ConfigureAwait(false);
                YouPinDeviceHeartbeatResult result = await _remoteClient.SendDeviceHeartbeatAsync(
                    credential.Token,
                    credential.DeviceToken,
                    credential.Uk).ConfigureAwait(false);
                if (result.Success)
                {
                    _deviceHeartbeatFailures = 0;
                    return;
                }

                if (result.Kind == YouPinDeviceHeartbeatErrorKind.LoginExpired)
                {
                    RecordDeviceHeartbeatFailure(result);
                    PauseAfterLoginExpired("悠悠有品登录状态失效，请重新登录。");
                    return;
                }

                RecordDeviceHeartbeatFailure(result);
            }
            catch (Exception ex)
            {
                RecordDeviceHeartbeatFailure(YouPinDeviceHeartbeatResult.FromException(YouPinDeviceHeartbeatErrorKind.Unknown, ex));
            }
        }

        private void RecordDeviceHeartbeatFailure(YouPinDeviceHeartbeatResult result)
        {
            int failures = Interlocked.Increment(ref _deviceHeartbeatFailures);
            string diagnostic = result.ToDiagnosticText();
            DiagnosticsLogger.InfoThrottled(
                "YouPin",
                "device-heartbeat-" + result.Kind.ToString().ToLowerInvariant(),
                $"悠悠有品设备心跳失败：{diagnostic}; ConsecutiveFailures={failures}",
                TimeSpan.FromMinutes(5));

            if (failures < DeviceHeartbeatWarningThreshold)
                return;

            _lastAutoDeliveryError = "设备心跳失败，可能影响接口稳定性：" + diagnostic;
        }

        private static bool IsYouPinLoginExpired(string message)
        {
            return YouPinMobileApiClient.IsLoginExpired(0, message);
        }

        private void PauseAfterLoginExpired(string message)
        {
            _timer?.Dispose();
            _timer = null;
            _lastStatus = "已暂停，需重新登录";
            _lastAutoDeliveryStatus = "已暂停，需重新登录";
            _lastError = Sanitize(message);
            _lastAutoDeliveryError = _lastError;
            NotifyLoginExpiredOnce("悠悠有品登录失效", "自动发货/报价已暂停，请重新登录悠悠有品后再继续。");
        }

        private void NotifyLoginExpiredOnce(string title, string message)
        {
            DateTime now = DateTime.UtcNow;
            if (now - _lastLoginExpiredNotificationUtc < LoginExpiredNotifyCooldown)
                return;

            _lastLoginExpiredNotificationUtc = now;
            try
            {
                if (!_settings.DoNotDisturbEnabled)
                {
                    AppNotificationHub.Instance.Request(
                        title,
                        message,
                        AppNotificationSeverity.Warning,
                        AppNotificationPlacement.Desktop);
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored("YouPin", "ShowLoginExpiredToast", ex, retryable: false, category: "Notify");
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    if (PhoneAlertDispatchService.IsConfigured(_settings))
                        await PhoneAlertDispatchService.Instance.SendConfiguredAsync(_settings, title, message).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Ignored("YouPin", "SendLoginExpiredPhoneAlert", ex, retryable: true, category: "Notify");
                }
            });
        }

        private static bool IsOrderStateCannotSend(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            return message.Contains("状态不能发送报价", StringComparison.Ordinal)
                || message.Contains("不能发送报价", StringComparison.Ordinal)
                || message.Contains("不是待发送状态", StringComparison.Ordinal);
        }

        private async Task<YouPinSaleActionResult> SendOfferH5ForOrdersAsync(IReadOnlyList<string> orderNos, YouPinCredential credential)
        {
            try
            {
                var cleanOrderNos = NormalizeOrderNoList(orderNos);
                if (cleanOrderNos.Count == 0)
                    return YouPinSaleActionResult.Failed("订单号为空，无法发送报价。");

                using var req = new HttpRequestMessage(HttpMethod.Put, BaseUrl + SendOfferEndpoint);
                object payload = cleanOrderNos.Count == 1
                    ? new
                    {
                        orderNo = cleanOrderNos[0]
                    }
                    : new
                    {
                        orderNo = cleanOrderNos[0],
                        sendOrderNoList = cleanOrderNos,
                        gameId = 730,
                        isMerge = true
                    };
                req.Content = YouPinMobileApiClient.JsonContent(payload);
                ApplyYouPinHeaders(req, credential.Token, credential.DeviceToken, credential.Uk);
                YouPinMobileApiClient.ApplyH5WebViewHeaders(req, credential.Uk, credential.UserId, credential.DeviceToken);

                using var resp = await YouPinMobileApiClient.SendAsync(_http, req, "通过新版接口发送悠悠有品报价");
                if (!resp.IsSuccessStatusCode)
                    return YouPinSaleActionResult.Failed("新版接口：" + BuildFriendlyHttpError(resp));

                using var doc = await YouPinMobileApiClient.ReadJsonDocumentAsync(resp, "通过新版接口发送悠悠有品报价");
                var root = doc.RootElement;
                int code = GetInt(root, "code", "Code");
                string msg = GetString(root, "msg", "Msg", "message", "Message") ?? "";
                if (code != 0)
                    return BuildFailedActionResult("新版接口", code, msg, "发送报价失败");

                string tradeOfferId = FindTradeOfferId(root);
                return YouPinSaleActionResult.Success(
                    BuildActionSuccessMessage(
                        "发送报价",
                        "新版 H5",
                        cleanOrderNos[0],
                        tradeOfferId,
                        root,
                        "已提交订单，请刷新列表确认状态。"),
                    tradeOfferId,
                    status: 3);
            }
            catch (Exception ex)
            {
                return YouPinSaleActionResult.Failed("新版接口：" + BuildFriendlyExceptionMessage(ex, "发送报价"));
            }
        }

        private async Task<YouPinSaleActionResult> ConfirmOfferCoreAsync(
            IReadOnlyList<string> orderNos,
            string tradeOfferId,
            int orderType,
            YouPinCredential credential)
        {
            try
            {
                var cleanOrderNos = NormalizeOrderNoList(orderNos);
                if (cleanOrderNos.Count == 0)
                    return YouPinSaleActionResult.Failed("订单号为空，无法确认报价。");

                if (orderType == 2 && cleanOrderNos.Count == 1)
                {
                    return await _rentalOfferConfirmationClient
                        .ConfirmAsync(cleanOrderNos[0], tradeOfferId, credential)
                        .ConfigureAwait(false);
                }

                using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + LegacyConfirmOfferEndpoint);
                object confirmOrder = cleanOrderNos.Count == 1
                    ? new
                    {
                        orderNo = cleanOrderNos[0],
                        tradeOfferId
                    }
                    : new
                    {
                        orderNo = cleanOrderNos[0],
                        sendOrderNoList = cleanOrderNos,
                        tradeOfferId,
                        gameId = 730,
                        isMerge = true
                    };
                req.Content = YouPinMobileApiClient.JsonContent(new
                {
                    confirmOrder,
                    Sessionid = credential.DeviceToken
                });
                ApplyYouPinHeaders(req, credential.Token, credential.DeviceToken, credential.Uk);

                using var resp = await YouPinMobileApiClient.SendAsync(_http, req, "确认悠悠有品报价");
                if (!resp.IsSuccessStatusCode)
                    return YouPinSaleActionResult.Failed("确认报价：" + BuildFriendlyHttpError(resp));

                using var doc = await YouPinMobileApiClient.ReadJsonDocumentAsync(resp, "确认悠悠有品报价");
                var root = doc.RootElement;
                int code = GetInt(root, "code", "Code");
                string msg = GetString(root, "msg", "Msg", "message", "Message") ?? "";
                if (code != 0)
                    return BuildFailedActionResult("确认报价", code, msg, "确认报价失败");

                bool success = true;
                if (TryGetProperty(root, out var data, "data", "Data")
                    && TryGetProperty(data, out var successValue, "success", "Success")
                    && successValue.ValueKind is JsonValueKind.False)
                {
                    success = false;
                }

                return success
                    ? YouPinSaleActionResult.Success(
                        BuildActionSuccessMessage(
                            "确认报价",
                            "confirmOffer",
                            cleanOrderNos[0],
                            tradeOfferId,
                            root,
                            "请点击“立即刷新”确认订单是否已从待处理列表移除。"),
                        tradeOfferId,
                        status: 3)
                    : YouPinSaleActionResult.Failed("确认报价：悠悠有品返回未成功，请刷新订单状态或在手机端确认。");
            }
            catch (Exception ex)
            {
                return YouPinSaleActionResult.Failed("确认报价：" + BuildFriendlyExceptionMessage(ex, "确认报价"));
            }
        }

        private async Task<YouPinSaleActionResult> QueryOfferStatusLegacyAsync(string orderNo, YouPinCredential credential)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + LegacyOfferStatusEndpoint);
                req.Content = YouPinMobileApiClient.JsonContent(new
                {
                    orderNo,
                    Sessionid = credential.DeviceToken
                });
                ApplyYouPinLegacyAndroidHeaders(req, credential.Token, credential.DeviceToken, credential.Uk);

                using var resp = await YouPinMobileApiClient.SendAsync(_http, req, "通过兼容接口查询悠悠有品报价状态");
                if (!resp.IsSuccessStatusCode)
                    return YouPinSaleActionResult.Failed("兼容接口：" + BuildFriendlyHttpError(resp));

                using var doc = await YouPinMobileApiClient.ReadJsonDocumentAsync(resp, "通过兼容接口查询悠悠有品报价状态");
                return await BuildOfferStatusResultAsync(doc.RootElement, orderNo, credential, "兼容接口");
            }
            catch (Exception ex)
            {
                return YouPinSaleActionResult.Failed("兼容接口：" + BuildFriendlyExceptionMessage(ex, "查询报价状态"));
            }
        }

        private async Task<YouPinSaleActionResult> QueryOfferStatusH5Async(string orderNo, YouPinCredential credential)
        {
            return await QueryOfferStatusH5Async(new[] { orderNo }, credential);
        }

        private async Task<YouPinSaleActionResult> QueryOfferStatusH5Async(IReadOnlyList<string> orderNos, YouPinCredential credential)
        {
            try
            {
                var cleanOrderNos = NormalizeOrderNoList(orderNos);
                if (cleanOrderNos.Count == 0)
                    return YouPinSaleActionResult.Failed("订单号为空，无法查询报价状态。");

                using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + OfferStatusEndpoint);
                req.Content = YouPinMobileApiClient.JsonContent(new
                {
                    sendOrderNoList = cleanOrderNos
                });
                ApplyYouPinHeaders(req, credential.Token, credential.DeviceToken, credential.Uk);
                YouPinMobileApiClient.ApplyH5WebViewHeaders(req, credential.Uk, credential.UserId, credential.DeviceToken);

                using var resp = await YouPinMobileApiClient.SendAsync(_http, req, "通过新版接口查询悠悠有品报价状态");
                if (!resp.IsSuccessStatusCode)
                    return YouPinSaleActionResult.Failed("新版接口：" + BuildFriendlyHttpError(resp));

                using var doc = await YouPinMobileApiClient.ReadJsonDocumentAsync(resp, "通过新版接口查询悠悠有品报价状态");
                return await BuildOfferStatusResultAsync(doc.RootElement, cleanOrderNos[0], credential, "新版接口");
            }
            catch (Exception ex)
            {
                return YouPinSaleActionResult.Failed("新版接口：" + BuildFriendlyExceptionMessage(ex, "查询报价状态"));
            }
        }

        private async Task<YouPinSaleActionResult> BuildOfferStatusResultAsync(JsonElement root, string orderNo, YouPinCredential credential, string source)
        {
            int code = GetInt(root, "code", "Code");
            string msg = GetString(root, "msg", "Msg", "message", "Message") ?? "";
            if (code != 0)
                return BuildFailedActionResult(source, code, msg, "查询报价状态失败");

            TryGetProperty(root, out var data, "data", "Data");
            int status = ResolveOfferStatus(data);
            string statusText = FirstText(
                GetString(data, "statusText", "StatusText", "statusName", "StatusName", "message", "Message", "msg", "Msg"),
                BuildOfferStatusSummary(data),
                msg,
                BuildOfferStatusText(status));
            YouPinSaleActionResult detailResult = await _remoteClient.QueryOrderDetailOfferStatusAsync(
                orderNo,
                credential.Token,
                credential.DeviceToken,
                credential.Uk);
            YouPinSaleActionResult saleResult = YouPinSaleActionResult.Failed("");
            if (detailResult.Ok && IsMeaningfulOrderDetailStatus(detailResult.Message))
            {
                statusText = detailResult.Message;
                if (detailResult.Status > 0)
                    status = detailResult.Status;
            }
            else
            {
                saleResult = await _remoteClient.QuerySaleOrderStatusAsync(
                    orderNo,
                    credential.Token,
                    credential.DeviceToken,
                    credential.Uk);
                if (saleResult.Ok && IsMeaningfulOrderDetailStatus(saleResult.Message))
                {
                    statusText = saleResult.Message;
                    if (saleResult.Status > 0)
                        status = saleResult.Status;
                }
            }

            string tradeOfferId = FirstText(
                FindTradeOfferId(root),
                detailResult.TradeOfferId,
                saleResult.TradeOfferId,
                await _remoteClient.TryFetchTradeOfferIdAsync(orderNo, credential.Token, credential.DeviceToken, credential.Uk));

            string message = string.IsNullOrWhiteSpace(statusText)
                ? "已查询报价状态。"
                : statusText;

            return YouPinSaleActionResult.Success(message, tradeOfferId, status);
        }

        private static bool IsMeaningfulOrderDetailStatus(string? message)
        {
            string text = message?.Trim() ?? "";
            return !string.IsNullOrWhiteSpace(text)
                && !string.Equals(text, "成功", StringComparison.Ordinal)
                && !string.Equals(text, "OK", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<YouPinSaleActionResult> QueryTradeOfferIdAsync(string orderNo)
        {
            if (string.IsNullOrWhiteSpace(orderNo))
                return YouPinSaleActionResult.Failed("订单号为空，无法获取 Steam 报价号。");

            try
            {
                var credential = GetRequiredCredential();
                string normalizedOrderNo = orderNo.Trim();
                string accountKey = GetCredentialAccountKey(credential);
                string tradeOfferId = await _remoteClient.TryFetchTradeOfferIdAsync(normalizedOrderNo, credential.Token, credential.DeviceToken, credential.Uk);
                if (string.IsNullOrWhiteSpace(tradeOfferId))
                    return YouPinSaleActionResult.Failed("暂未获取到 Steam 报价号，可能报价仍在发送中或该待办不是 Steam 报价订单。");

                var result = YouPinSaleActionResult.Success("已获取匹配的 Steam 报价号。", tradeOfferId);
                YouPinSaleOrder? localOrder = FindCurrentAccountWaitDeliverOrder(normalizedOrderNo, accountKey);
                if (localOrder != null)
                    ApplyOrderActionSnapshot(localOrder, result, YouPinSaleOrderActionKind.QueryStatus);
                return result;
            }
            catch (Exception ex)
            {
                var wrapped = YouPinMobileApiClient.WrapException(ex, "获取 Steam 报价号");
                return YouPinSaleActionResult.Failed(Sanitize(wrapped.Message));
            }
        }

        public async Task<YouPinSaleOrder> EnrichOrderDetailAsync(YouPinSaleOrder order)
        {
            ArgumentNullException.ThrowIfNull(order);

            if (!string.IsNullOrWhiteSpace(order.SteamPersonaName)
                || order.SteamCounterpartyStatus.StartsWith("未获取", StringComparison.Ordinal)
                || string.Equals(order.SteamCounterpartyStatus, "已获取", StringComparison.Ordinal))
            {
                return order;
            }

            try
            {
                var credential = GetRequiredCredential();
                var info = await _remoteClient.TryFetchSteamCounterpartyInfoAsync(
                    order,
                    credential.Token,
                    credential.DeviceToken,
                    credential.Uk,
                    credential.UserId).ConfigureAwait(false);

                ApplySteamCounterpartyInfo(order, info);
                SaveHistory();
                return order;
            }
            catch
            {
                order.SteamCounterpartyStatus = "未获取";
                SaveHistory();
                return order;
            }
        }

        private async Task CheckIfDueAsync()
        {
            if (_settings.YouPinSaleReminderEnabled)
            {
                int refreshSec = Math.Max(30, _settings.YouPinSaleReminderRefreshSec <= 0 ? 180 : _settings.YouPinSaleReminderRefreshSec);
                if (DateTime.Now - _lastCheck >= TimeSpan.FromSeconds(refreshSec))
                {
                    await CheckTodoCoreAsync(force: false, useMock: false, notify: true);
                }
            }
            if (_settings.YouPinQuoteAutoRefreshEnabled)
            {
                int refreshSec = Math.Max(30, _settings.YouPinQuoteAutoRefreshSec <= 0 ? 180 : _settings.YouPinQuoteAutoRefreshSec);
                if (DateTime.Now - _lastAutoDeliveryCheck >= TimeSpan.FromSeconds(refreshSec))
                {
                    await CheckQuoteCoreAsync(force: false, trigger: "自动刷新");
                }
            }
            if (_settings.YouPinMsgCenterEnabled)
            {
                int refreshSec = Math.Max(30, _settings.YouPinMsgCenterRefreshSec <= 0 ? 60 : _settings.YouPinMsgCenterRefreshSec);
                if (DateTime.Now - _lastMsgCenterCheck >= TimeSpan.FromSeconds(refreshSec))
                {
                    await CheckMsgCenterCoreAsync(force: false, useMock: false, notify: true);
                }
            }
        }

        private async Task<YouPinSaleReminderCheckResult> CheckQuoteCoreAsync(bool force, string trigger)
        {
            await _fetchLock.WaitAsync();
            try
            {
                if (!_settings.YouPinQuoteAutoRefreshEnabled && !force)
                    return YouPinSaleReminderCheckResult.Skip("未启用");

                int refreshSec = Math.Max(30, _settings.YouPinQuoteAutoRefreshSec <= 0 ? 180 : _settings.YouPinQuoteAutoRefreshSec);
                if (!force && DateTime.Now - _lastAutoDeliveryCheck < TimeSpan.FromSeconds(refreshSec))
                    return YouPinSaleReminderCheckResult.Skip("未到检查时间");

                var credential = GetRequiredCredential();
                await TrySendDeviceHeartbeatAsync(credential).ConfigureAwait(false);

                string accountKey = GetCredentialAccountKey(credential);
                string device = string.IsNullOrWhiteSpace(credential.DeviceToken)
                    ? YouPinMobileApiClient.GetDeviceToken()
                    : credential.DeviceToken.Trim();
                var waitDeliverOrders = await _remoteClient.FetchWaitDeliverOrdersAsync(credential.Token.Trim(), device, credential.Uk);
                try
                {
                    List<YouPinSaleOrder> pendingBuyOrders = await _remoteClient.FetchPendingBuyQuoteOrdersAsync(
                        credential.Token.Trim(),
                        device,
                        credential.Uk).ConfigureAwait(false);
                    waitDeliverOrders = MergeQuoteDisplayOrders(waitDeliverOrders, pendingBuyOrders);
                }
                catch (Exception pendingBuyEx)
                {
                    DiagnosticsLogger.Info(
                        "YouPinQuote",
                        "Pending-buy read failed; keeping wait-deliver results. " + Sanitize(pendingBuyEx.Message));
                }
                await ReconcileLocalConfirmSubmittedOrdersAsync(waitDeliverOrders, accountKey, credential).ConfigureAwait(false);
                var newWaitDeliverOrders = RecordWaitDeliverOrders(waitDeliverOrders, "报价处理", accountKey);

                _lastAutoDeliveryCheck = DateTime.Now;
                _lastAutoDeliveryStatus = waitDeliverOrders.Count == 0
                    ? "报价列表刷新完成：暂无待处理订单。"
                    : $"报价列表刷新完成：{waitDeliverOrders.Count} 条待处理。";
                _lastAutoDeliveryError = "";

                SaveHistory();
                RaiseDataUpdated();
                if (newWaitDeliverOrders.Count > 0)
                    RaiseNewWaitDeliverOrdersDetected(newWaitDeliverOrders);
                AppendQuoteLog("读取订单", true, "", "", $"{_lastAutoDeliveryStatus} 新增 {newWaitDeliverOrders.Count} 条", NormalizeQuoteCheckTrigger(trigger));
                return YouPinSaleReminderCheckResult.Success(_lastAutoDeliveryStatus, newWaitDeliverOrders.Count);
            }
            catch (Exception ex)
            {
                _lastAutoDeliveryCheck = DateTime.Now;
                _lastAutoDeliveryStatus = "报价列表刷新失败";
                _lastAutoDeliveryError = Sanitize(ex.Message);
                if (IsYouPinLoginExpired(_lastAutoDeliveryError))
                    PauseAfterLoginExpired("悠悠有品登录状态失效，请重新登录。");

                RaiseDataUpdated();
                AppendQuoteLog("读取订单", false, "", "", _lastAutoDeliveryError, NormalizeQuoteCheckTrigger(trigger));
                return YouPinSaleReminderCheckResult.Failed(_lastAutoDeliveryError);
            }
            finally
            {
                _fetchLock.Release();
            }
        }

        private async Task<YouPinSaleReminderCheckResult> CheckTodoCoreAsync(bool force, bool useMock, bool notify)
        {
            await _fetchLock.WaitAsync();
            try
            {
                if (!_settings.YouPinSaleReminderEnabled && !useMock && !force)
                    return YouPinSaleReminderCheckResult.Skip("未启用");

                int refreshSec = Math.Max(30, _settings.YouPinSaleReminderRefreshSec <= 0 ? 180 : _settings.YouPinSaleReminderRefreshSec);
                if (!force && !useMock && DateTime.Now - _lastCheck < TimeSpan.FromSeconds(refreshSec))
                    return YouPinSaleReminderCheckResult.Skip("未到检查时间");

                List<YouPinSaleOrder> orders;
                List<YouPinSaleOrder> waitDeliverOrders = new();
                bool waitDeliverLoaded = false;
                string source;
                string accountKey = "";
                if (useMock)
                {
                    orders = YouPinSaleReminderMockOrderFactory.CreateTodoOrders(
                        Interlocked.Increment(ref _mockSequence),
                        DateTime.Now);
                    orders = orders.Where(ShouldIncludeTodo).ToList();
                    waitDeliverOrders = YouPinSaleReminderMockOrderFactory.CreateWaitDeliverOrders(
                        Interlocked.Increment(ref _mockSequence),
                        DateTime.Now);
                    source = "模拟待办";

                    _lastCheck = DateTime.Now;
                    _lastError = "";
                    _lastStatus = "待办测试提醒完成：仅用于测试提醒，不写入最近待办列表";

                    if (notify)
                    {
                        foreach (var order in orders)
                            Notify(order, isMsgCenter: false);
                    }

                    RaiseDataUpdated();
                    return YouPinSaleReminderCheckResult.Success(_lastStatus, orders.Count);
                }
                else
                {
                    lock (_stateLock)
                    {
                        RemoveMockHistory(_history);
                    }

                    var credential = _authService.GetCredential(_settings);
                    if (credential == null || string.IsNullOrWhiteSpace(credential.Token))
                        throw new InvalidOperationException("请先在悠悠有品登录区完成登录。");

                    credential.Token = credential.Token.Trim();
                    credential.DeviceToken = string.IsNullOrWhiteSpace(credential.DeviceToken)
                        ? YouPinMobileApiClient.GetDeviceToken()
                        : credential.DeviceToken.Trim();
                    await TrySendDeviceHeartbeatAsync(credential).ConfigureAwait(false);

                    orders = await _remoteClient.FetchRemoteTodoOrdersAsync(credential.Token, credential.DeviceToken, credential.Uk);
                    accountKey = GetCredentialAccountKey(credential);

                    string device = string.IsNullOrWhiteSpace(credential.DeviceToken) ? YouPinMobileApiClient.GetDeviceToken() : credential.DeviceToken.Trim();
                    try
                    {
                        waitDeliverOrders = await _remoteClient.FetchWaitDeliverOrdersAsync(credential.Token.Trim(), device, credential.Uk);
                        try
                        {
                            List<YouPinSaleOrder> pendingBuyOrders = await _remoteClient.FetchPendingBuyQuoteOrdersAsync(
                                credential.Token.Trim(),
                                device,
                                credential.Uk).ConfigureAwait(false);
                            waitDeliverOrders = MergeQuoteDisplayOrders(waitDeliverOrders, pendingBuyOrders);
                        }
                        catch (Exception pendingBuyEx)
                        {
                            DiagnosticsLogger.Info(
                                "YouPinQuote",
                                "Pending-buy read failed; keeping wait-deliver results. " + Sanitize(pendingBuyEx.Message));
                        }
                        await ReconcileLocalConfirmSubmittedOrdersAsync(waitDeliverOrders, accountKey, credential).ConfigureAwait(false);
                        waitDeliverLoaded = true;
                        _lastAutoDeliveryCheck = DateTime.Now;
                        _lastAutoDeliveryStatus = waitDeliverOrders.Count == 0
                            ? "已读取待发货/报价处理列表：暂无待处理订单。自动发货开关请以手机端为准，本软件不修改配置。"
                            : $"已读取待发货/报价处理列表：{waitDeliverOrders.Count} 条待处理。自动发货开关请以手机端为准，本软件不修改配置。";
                        _lastAutoDeliveryError = "";
                        AppendQuoteLog("读取订单", true, "", "", _lastAutoDeliveryStatus, SteamOfferAuditLog.TriggerBackgroundAuto);
                    }
                    catch (Exception waitEx)
                    {
                        waitDeliverOrders = new List<YouPinSaleOrder>();
                        _lastAutoDeliveryCheck = DateTime.Now;
                        _lastAutoDeliveryStatus = "待发货/自动发货诊断读取失败";
                        _lastAutoDeliveryError = Sanitize(waitEx.Message);
                        AppendQuoteLog("读取订单", false, "", "", _lastAutoDeliveryError, SteamOfferAuditLog.TriggerBackgroundAuto);
                    }
                    source = "悠悠有品待办";
                }

                var newOrders = RecordOrders(orders, source, accountKey);
                var newWaitDeliverOrders = waitDeliverLoaded
                    ? RecordWaitDeliverOrders(waitDeliverOrders, "报价处理", accountKey)
                    : new List<YouPinSaleOrder>();
                _lastCheck = DateTime.Now;
                _lastError = "";
                _lastStatus = $"{source}检查完成：待处理 {orders.Count} 条，待发货 {waitDeliverOrders.Count} 条，新增待办 {newOrders.Count} 条";

                if (notify)
                {
                    foreach (var order in newOrders)
                        Notify(order, isMsgCenter: false);
                }

                SaveHistory();
                RaiseDataUpdated();
                if (newWaitDeliverOrders.Count > 0)
                    RaiseNewWaitDeliverOrdersDetected(newWaitDeliverOrders);
                return YouPinSaleReminderCheckResult.Success(_lastStatus, newOrders.Count);
            }
            catch (Exception ex)
            {
                _lastError = Sanitize(ex.Message);
                if (IsYouPinLoginExpired(_lastError))
                {
                    PauseAfterLoginExpired("悠悠有品登录状态失效，请重新登录。");
                    RaiseDataUpdated();
                    return YouPinSaleReminderCheckResult.Failed(_lastError);
                }

                _lastStatus = "待办检查失败";
                _lastAutoDeliveryCheck = DateTime.Now;
                _lastAutoDeliveryStatus = "未完成诊断";
                _lastAutoDeliveryError = _lastError;
                RaiseDataUpdated();
                return YouPinSaleReminderCheckResult.Failed(_lastError);
            }
            finally
            {
                _fetchLock.Release();
            }
        }

        private async Task<YouPinSaleReminderCheckResult> CheckMsgCenterCoreAsync(bool force, bool useMock, bool notify)
        {
            await _fetchLock.WaitAsync();
            try
            {
                if (!_settings.YouPinMsgCenterEnabled && !useMock && !force)
                    return YouPinSaleReminderCheckResult.Skip("未启用");

                int refreshSec = Math.Max(30, _settings.YouPinMsgCenterRefreshSec <= 0 ? 60 : _settings.YouPinMsgCenterRefreshSec);
                if (!force && !useMock && DateTime.Now - _lastMsgCenterCheck < TimeSpan.FromSeconds(refreshSec))
                    return YouPinSaleReminderCheckResult.Skip("未到检查时间");

                List<YouPinSaleOrder> notices;
                if (useMock)
                {
                    notices = YouPinSaleReminderMockOrderFactory.CreateMsgCenterNotices(
                        Interlocked.Increment(ref _mockSequence),
                        DateTime.Now);

                    _lastMsgCenterCheck = DateTime.Now;
                    _lastMsgCenterError = "";
                    _lastMsgCenterStatus = "提醒设置测试完成：仅用于测试提醒，不写入最近提醒列表";

                    if (notify)
                    {
                        foreach (var notice in notices)
                            Notify(notice, isMsgCenter: true);
                    }

                    RaiseDataUpdated();
                    return YouPinSaleReminderCheckResult.Success(_lastMsgCenterStatus, notices.Count);
                }
                else
                {
                    throw new NotSupportedException("提醒设置暂无真实接口实现，请使用“测试提醒”验证通知通道。");
                }
            }
            catch (Exception ex)
            {
                _lastMsgCenterError = Sanitize(ex.Message);
                _lastMsgCenterStatus = "提醒设置检查失败";
                RaiseDataUpdated();
                return YouPinSaleReminderCheckResult.Failed(_lastMsgCenterError);
            }
            finally
            {
                _fetchLock.Release();
            }
        }

        private YouPinCredential GetRequiredCredential()
        {
            var credential = _authService.GetCredential(_settings);
            if (credential == null || string.IsNullOrWhiteSpace(credential.Token))
                throw new InvalidOperationException("请先在悠悠有品登录区完成登录。");

            credential.Token = credential.Token.Trim();
            credential.DeviceToken = string.IsNullOrWhiteSpace(credential.DeviceToken)
                ? YouPinMobileApiClient.GetDeviceToken()
                : credential.DeviceToken.Trim();
            return credential;
        }

        private List<YouPinSaleOrder> RecordOrders(List<YouPinSaleOrder> orders, string source, string accountKey)
        {
            lock (_stateLock)
            {
                return YouPinSaleReminderHistoryHelper.RecordTodoOrders(_history, orders, source, accountKey);
            }
        }

        private List<YouPinSaleOrder> RecordWaitDeliverOrders(List<YouPinSaleOrder> orders, string source, string accountKey)
        {
            lock (_stateLock)
            {
                return YouPinSaleReminderHistoryHelper.RecordWaitDeliverOrders(_history, orders, source, accountKey);
            }
        }

        private async Task ReconcileLocalConfirmSubmittedOrdersAsync(
            List<YouPinSaleOrder> waitDeliverOrders,
            string accountKey,
            YouPinCredential credential)
        {
            if (waitDeliverOrders.Count == 0 || string.IsNullOrWhiteSpace(accountKey))
                return;

            List<YouPinLocalQuoteStateSnapshot> snapshots;
            lock (_stateLock)
            {
                snapshots = YouPinSaleReminderHistoryHelper
                    .GetLocalQuoteStateSnapshots(_history.RecentWaitDeliverOrders, accountKey)
                    .Where(snapshot => string.Equals(snapshot.State, YouPinQuoteLocalState.ConfirmSubmitted, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (snapshots.Count == 0)
                return;

            foreach (YouPinSaleOrder order in waitDeliverOrders)
            {
                YouPinLocalQuoteStateSnapshot? snapshot = snapshots.FirstOrDefault(candidate => YouPinQuoteLocalState.Matches(order, candidate));
                if (snapshot == null || !snapshot.HasValue)
                    continue;

                bool clearLocalState = await ShouldClearConfirmSubmittedAsync(order, credential).ConfigureAwait(false);
                if (clearLocalState)
                {
                    lock (_stateLock)
                    {
                        YouPinSaleReminderHistoryHelper.ClearMatchingLocalQuoteStates(_history, accountKey, snapshot);
                    }
                    YouPinQuoteLocalState.Clear(order);
                    continue;
                }

                YouPinQuoteLocalState.ApplySnapshot(order, snapshot);
            }
        }

        private async Task<bool> ShouldClearConfirmSubmittedAsync(YouPinSaleOrder order, YouPinCredential credential)
        {
            try
            {
                var orderNos = ResolveActionOrderNos(order);
                var result = await QueryOfferStatusH5Async(orderNos, credential).ConfigureAwait(false);
                return YouPinQuoteLocalState.ShouldClearConfirmSubmitted(result);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored("YouPinQuote", "VerifyConfirmSubmitted", ex, retryable: true, category: "Refresh");
                return false;
            }
        }

        private YouPinSaleOrder? FindCurrentAccountWaitDeliverOrder(string orderNo, string accountKey)
        {
            if (string.IsNullOrWhiteSpace(orderNo) || string.IsNullOrWhiteSpace(accountKey))
                return null;

            lock (_stateLock)
            {
                _history.RecentWaitDeliverOrders ??= new List<YouPinSaleOrder>();
                return _history.RecentWaitDeliverOrders.FirstOrDefault(x =>
                    (string.Equals(x.OrderNo, orderNo, StringComparison.OrdinalIgnoreCase)
                        || (x.OrderNos?.Any(child => string.Equals(child, orderNo, StringComparison.OrdinalIgnoreCase)) ?? false))
                    && IsCurrentAccountOrder(x, accountKey));
            }
        }

        private string GetCurrentAccountKey()
        {
            return GetCredentialAccountKey(_authService.GetCredential(_settings));
        }

        public void Notify(YouPinSaleOrder order)
        {
            Notify(order, isMsgCenter: false);
        }

        public void Notify(YouPinSaleOrder order, bool isMsgCenter)
        {
            if (_settings.DoNotDisturbEnabled) return;

            string title = isMsgCenter ? "悠悠有品提醒设置" : "悠悠有品待办提醒";
            string message = BuildNotificationMessage(order, isMsgCenter);
            var mode = isMsgCenter ? _settings.YouPinMsgCenterNotificationMode : _settings.YouPinSaleReminderNotificationMode;
            bool showBubble = mode == YouPinSaleReminderNotificationMode.Bubble || mode == YouPinSaleReminderNotificationMode.BubbleAndSound;
            bool playSound = mode == YouPinSaleReminderNotificationMode.Sound || mode == YouPinSaleReminderNotificationMode.BubbleAndSound;
            if (!showBubble && !playSound)
                return;

            AppNotificationHub.Instance.Request(
                title,
                message,
                AppNotificationSeverity.Info,
                AppNotificationPlacement.Desktop,
                playSound,
                showToast: showBubble);
        }

        private void RaiseDataUpdated()
        {
            try { DataUpdated?.Invoke(); } catch (System.Exception ex) { CS2TradeMonitor.src.SystemServices.DiagnosticsLogger.Ignored(ex); }
        }

        private void RaiseNewWaitDeliverOrdersDetected(IReadOnlyList<YouPinSaleOrder> orders)
        {
            try { NewWaitDeliverOrdersDetected?.Invoke(orders); }
            catch (Exception ex) { DiagnosticsLogger.Ignored("YouPinQuote", "NewWaitDeliverOrdersDetected", ex, retryable: true, category: "Automation"); }
        }

        private void AppendQuoteActionLog(string action, string orderNo, YouPinSaleActionResult result, string trigger)
        {
            if (result == null)
                return;

            AppendQuoteLog(action, result.Ok, orderNo, result.TradeOfferId, BuildYouPinActionLogMessage(action, orderNo, result), trigger);
        }

        private void AppendQuoteLog(string action, bool ok, string orderNo, string tradeOfferId, string message, string trigger)
        {
            try
            {
                SteamOfferAuditLog.LogTradeAction(
                    SteamOfferAuditLog.SystemYouPin,
                    NormalizeQuoteTrigger(trigger),
                    action,
                    BuildYouPinLogResult(action, ok, message),
                    orderNo,
                    tradeOfferId,
                    Sanitize(message ?? string.Empty));
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored("YouPinQuote", "AppendQuoteLog", ex, retryable: true, category: "Log");
            }
        }

        private string BuildYouPinActionLogMessage(string action, string orderNo, YouPinSaleActionResult result)
        {
            string scenario = DescribeYouPinOrder(orderNo);
            string message = Sanitize(result.Message ?? string.Empty);
            if (result.Ok)
            {
                if (action == "发送报价")
                    return $"{scenario}：本软件调用悠悠接口发送报价成功，等待Steam手机确认。";
                if (action == "确认报价")
                    return $"{scenario}：本软件调用悠悠接口确认报价成功。";
                if (action == "查询状态")
                    return $"{scenario}：本软件调用悠悠接口查询报价状态成功。{message}";
            }

            return $"{scenario}：本软件调用悠悠接口{action}失败。原因={message}";
        }

        private string DescribeYouPinOrder(string orderNo)
        {
            try
            {
                string accountKey = GetCurrentAccountKey();
                YouPinSaleOrder? order = FindCurrentAccountWaitDeliverOrder(orderNo, accountKey);
                if (order != null && IsLikelyRentalOrder(order))
                    return "悠悠出租";
            }
            catch
            {
                // Logging should never affect trading operations.
            }

            return "悠悠出售";
        }

        private static bool IsLikelyRentalOrder(YouPinSaleOrder order)
        {
            string text = string.Join(" ", new[]
            {
                order.Message,
                order.Source,
                order.OrderStatusDesc,
                order.LeaseType
            });
            return order.OrderType == 2
                || text.Contains("出租", StringComparison.Ordinal)
                || text.Contains("租赁", StringComparison.Ordinal)
                || text.Contains("租借", StringComparison.Ordinal);
        }

        private static string BuildYouPinLogResult(string action, bool ok, string message)
        {
            if (!ok)
                return "失败";
            if (action == "发送报价" && LooksLikeWaitingSteamConfirmation(message))
                return "待确认";
            return "成功";
        }

        private static bool LooksLikeWaitingSteamConfirmation(string message)
        {
            string text = message ?? string.Empty;
            return text.Contains("令牌", StringComparison.Ordinal)
                || text.Contains("待", StringComparison.Ordinal)
                || text.Contains("确认", StringComparison.Ordinal);
        }

        private static string NormalizeQuoteCheckTrigger(string trigger)
        {
            string text = trigger ?? string.Empty;
            return text.Contains("立即", StringComparison.Ordinal)
                ? SteamOfferAuditLog.TriggerUserCheckNow
                : SteamOfferAuditLog.TriggerBackgroundAuto;
        }

        private static string NormalizeQuoteTrigger(string trigger)
        {
            if (string.IsNullOrWhiteSpace(trigger))
                return SteamOfferAuditLog.TriggerUserManual;

            return trigger.Trim();
        }


        private void SaveHistory()
        {
            lock (_stateLock)
            {
                RemoveMockHistory(_history);
                PruneHistory(_history);
            }

            _historyStore.Save(_history);
        }

        private bool HasYouPinCredential()
        {
            var credential = _authService.GetCredential(_settings);
            return credential != null && !string.IsNullOrWhiteSpace(credential.Token);
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _http.Dispose();
            _fetchLock.Dispose();
        }
    }
}
