using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static CS2TradeMonitor.Application.YouPin.YouPinJsonElementReader;
using static CS2TradeMonitor.Application.YouPin.YouPinSaleNotificationHelper;
using static CS2TradeMonitor.Application.YouPin.YouPinSaleOrderGroupHelper;
using static CS2TradeMonitor.Application.YouPin.YouPinSaleOrderParser;
using static CS2TradeMonitor.Application.YouPin.YouPinSaleReminderHttpHelper;

namespace CS2TradeMonitor.Application.YouPin
{
    internal sealed class YouPinSaleReminderRemoteClient
    {
        private const string BaseUrl = YouPinMobileApiClient.BaseUrl;
        private const string UserInfoEndpoint = "/api/youpin/bff/user/Account/getUserInfoForApp";
        private const string LegacyUserInfoEndpoint = "/api/user/Account/getUserInfo";
        private const string TodoEndpoint = "/api/youpin/bff/trade/todo/v1/orderTodo/topList";
        private const string LegacyTodoEndpoint = "/api/youpin/bff/trade/todo/v1/orderTodo/list";
        private const string WaitDeliverEndpoint = "/api/youpin/bff/trade/sell/page/v1/waitDeliver/waitDeliverList";
        private const string PendingBuyEndpoint = "/api/youpin/bff/trade/sale/v1/buy/list";
        private const string OrderDetailEndpoint = "/api/youpin/bff/order/v2/detail";
        private const string QueryOrderDetailEndpoint = "/api/youpin/bff/trade/v1/order/query/detail";
        private const string SaleSellListEndpoint = "/api/youpin/bff/trade/sale/v1/sell/list";
        private const string TokenConfirmInfoEndpoint = "/api/youpin/bff/order/offer/common/query/token/confirm/info";

        private readonly HttpClient _http;

        public YouPinSaleReminderRemoteClient(HttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        public async Task<List<YouPinSaleOrder>> FetchRemoteTodoOrdersAsync(string token, string deviceToken, string uk)
        {
            string device = string.IsNullOrWhiteSpace(deviceToken) ? YouPinMobileApiClient.GetDeviceToken() : deviceToken.Trim();
            string userId = await FetchUserIdAsync(token.Trim(), device, uk);
            var orders = await FetchTodoOrdersAsync(token.Trim(), device, userId, uk);

            return orders
                .Where(ShouldIncludeTodo)
                .GroupBy(x => x.OrderNo, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .Select(NormalizeOrderGroupFields)
                .ToList();
        }

        public async Task<List<YouPinSaleOrder>> FetchWaitDeliverOrdersAsync(string token, string device, string uk)
        {
            var result = new List<YouPinSaleOrder>();
            int pageIndex = 1;
            const int pageSize = 20;

            try
            {
                while (pageIndex <= 20)
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + WaitDeliverEndpoint);
                    req.Content = YouPinMobileApiClient.JsonContent(new
                    {
                        gameId = 730,
                        pageIndex,
                        pageSize,
                        Sessionid = device
                    });
                    ApplyYouPinHeaders(req, token, device, uk);

                    using var resp = await YouPinMobileApiClient.SendAsync(_http, req, "获取悠悠有品待发货列表");
                    if (!resp.IsSuccessStatusCode)
                        throw new InvalidOperationException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");

                    using var doc = await YouPinMobileApiClient.ReadJsonDocumentAsync(resp, "检查悠悠有品待发货提醒");
                    var root = doc.RootElement;
                    int code = GetInt(root, "code", "Code");
                    if (code != 0)
                    {
                        string msg = GetString(root, "msg", "Msg", "message", "Message") ?? "";
                        if (YouPinMobileApiClient.IsLoginExpired(code, msg))
                            throw new InvalidOperationException("悠悠有品登录状态失效，请重新登录。");
                        throw new InvalidOperationException(string.IsNullOrWhiteSpace(msg) ? $"悠悠有品返回 code={code}" : msg);
                    }

                    var current = ExtractWaitDeliverArray(root);
                    foreach (var item in current)
                    {
                        var order = ParseWaitDeliverOrder(item);
                        order.Source = "待发货";
                        if (!string.IsNullOrWhiteSpace(order.OrderNo) && !result.Any(x => string.Equals(x.OrderNo, order.OrderNo, StringComparison.OrdinalIgnoreCase)))
                        {
                            result.Add(order);
                        }
                    }

                    if (current.Count < pageSize) break;
                    pageIndex++;
                    await Task.Delay(250);
                }
            }
            catch (Exception ex)
            {
                throw YouPinMobileApiClient.WrapException(ex, "获取悠悠有品待发货列表");
            }

            return result;
        }

        public async Task<List<YouPinSaleOrder>> FetchPendingBuyQuoteOrdersAsync(string token, string device, string uk)
        {
            var result = new List<YouPinSaleOrder>();
            int pageIndex = 1;
            const int pageSize = 20;

            try
            {
                while (pageIndex <= 20)
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + PendingBuyEndpoint);
                    req.Content = YouPinMobileApiClient.JsonContent(new
                    {
                        keys = "",
                        orderStatus = 140,
                        pageIndex,
                        pageSize,
                        presenterId = 0,
                        sceneType = 0,
                        Sessionid = device
                    });
                    ApplyYouPinHeaders(req, token, device, uk);

                    using var resp = await YouPinMobileApiClient.SendAsync(_http, req, "获取悠悠购买待收货列表");
                    if (!resp.IsSuccessStatusCode)
                        throw new InvalidOperationException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");

                    using var doc = await YouPinMobileApiClient.ReadJsonDocumentAsync(resp, "检查悠悠购买待收货报价");
                    JsonElement root = doc.RootElement;
                    int code = GetInt(root, "code", "Code");
                    if (code != 0)
                    {
                        string msg = GetString(root, "msg", "Msg", "message", "Message") ?? "";
                        if (YouPinMobileApiClient.IsLoginExpired(code, msg))
                            throw new InvalidOperationException("悠悠有品登录状态失效，请重新登录。");
                        throw new InvalidOperationException(string.IsNullOrWhiteSpace(msg) ? $"悠悠有品返回 code={code}" : msg);
                    }

                    List<JsonElement> current = ExtractPendingBuyArray(root);
                    foreach (JsonElement item in current)
                    {
                        YouPinSaleOrder order = ParsePendingBuyOrder(item);
                        if (order.OrderStatus != 140 || string.IsNullOrWhiteSpace(order.OrderNo))
                            continue;
                        if (!result.Any(x => string.Equals(x.OrderNo, order.OrderNo, StringComparison.OrdinalIgnoreCase)))
                            result.Add(order);
                    }

                    if (current.Count < pageSize)
                        break;
                    pageIndex++;
                    await Task.Delay(250);
                }
            }
            catch (Exception ex)
            {
                throw YouPinMobileApiClient.WrapException(ex, "获取悠悠购买待收货列表");
            }

            return result;
        }

        public async Task<YouPinDeviceHeartbeatResult> SendDeviceHeartbeatAsync(string token, string device, string uk)
        {
            string normalizedDevice = string.IsNullOrWhiteSpace(device)
                ? YouPinMobileApiClient.GetDeviceToken()
                : device.Trim();

            var preferred = await SendDeviceHeartbeatToEndpointAsync(
                UserInfoEndpoint,
                useLegacyHeaders: false,
                token,
                normalizedDevice,
                uk).ConfigureAwait(false);
            if (preferred.Success || preferred.Kind == YouPinDeviceHeartbeatErrorKind.LoginExpired)
                return preferred;

            return await SendDeviceHeartbeatToEndpointAsync(
                LegacyUserInfoEndpoint,
                useLegacyHeaders: true,
                token,
                normalizedDevice,
                uk).ConfigureAwait(false);
        }

        private async Task<YouPinDeviceHeartbeatResult> SendDeviceHeartbeatToEndpointAsync(
            string endpoint,
            bool useLegacyHeaders,
            string token,
            string device,
            string uk)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl + endpoint);
                if (useLegacyHeaders)
                    ApplyYouPinLegacyAndroidHeaders(req, token.Trim(), device, uk);
                else
                    ApplyYouPinHeaders(req, token.Trim(), device, uk);

                using var resp = await YouPinMobileApiClient.SendAsync(_http, req, "同步悠悠有品设备状态");
                if (!resp.IsSuccessStatusCode)
                {
                    return YouPinDeviceHeartbeatResult.Fail(
                        YouPinDeviceHeartbeatErrorKind.HttpError,
                        httpStatusCode: (int)resp.StatusCode,
                        safeMessage: string.IsNullOrWhiteSpace(resp.ReasonPhrase)
                            ? "HTTP 请求未成功。"
                            : resp.ReasonPhrase);
                }

                using var doc = await YouPinMobileApiClient.ReadJsonDocumentAsync(resp, "同步悠悠有品设备状态");
                var root = doc.RootElement;
                bool hasCode = TryGetProperty(root, out _, "Code", "code");
                int code = GetInt(root, "Code", "code");
                string msg = GetString(root, "Msg", "msg", "Message", "message") ?? "";
                if (YouPinMobileApiClient.IsLoginExpired(code, msg))
                {
                    return YouPinDeviceHeartbeatResult.Fail(
                        YouPinDeviceHeartbeatErrorKind.LoginExpired,
                        httpStatusCode: (int)resp.StatusCode,
                        apiCode: code,
                        apiMessage: msg,
                        safeMessage: string.IsNullOrWhiteSpace(msg) ? "悠悠有品登录状态失效，请重新登录。" : msg);
                }

                if (!hasCode)
                {
                    return YouPinDeviceHeartbeatResult.Fail(
                        YouPinDeviceHeartbeatErrorKind.ApiRejected,
                        httpStatusCode: (int)resp.StatusCode,
                        safeMessage: "响应缺少 code 字段。");
                }

                if (code == 0)
                    return YouPinDeviceHeartbeatResult.Ok((int)resp.StatusCode, code, msg);

                return YouPinDeviceHeartbeatResult.Fail(
                    YouPinDeviceHeartbeatErrorKind.ApiRejected,
                    httpStatusCode: (int)resp.StatusCode,
                    apiCode: code,
                    apiMessage: msg,
                    safeMessage: string.IsNullOrWhiteSpace(msg) ? "接口返回非成功 code。" : msg);
            }
            catch (HttpRequestException ex)
            {
                return YouPinDeviceHeartbeatResult.FromException(YouPinDeviceHeartbeatErrorKind.NetworkError, ex);
            }
            catch (TaskCanceledException ex)
            {
                return YouPinDeviceHeartbeatResult.FromException(YouPinDeviceHeartbeatErrorKind.NetworkError, ex);
            }
            catch (InvalidOperationException ex) when (ex.InnerException is JsonException
                || ex.Message.Contains("返回内容格式异常", StringComparison.Ordinal)
                || ex.Message.Contains("返回空响应", StringComparison.Ordinal))
            {
                return YouPinDeviceHeartbeatResult.FromException(YouPinDeviceHeartbeatErrorKind.ParseError, ex);
            }
            catch (JsonException ex)
            {
                return YouPinDeviceHeartbeatResult.FromException(YouPinDeviceHeartbeatErrorKind.ParseError, ex);
            }
            catch (Exception ex)
            {
                return YouPinDeviceHeartbeatResult.FromException(YouPinDeviceHeartbeatErrorKind.Unknown, ex);
            }
        }

        public async Task<string> TryFetchTradeOfferIdAsync(string orderNo, string token, string device, string uk)
        {
            string offerId = await TryFetchTradeOfferIdFromOrderDetailAsync(orderNo, token, device, uk);
            if (!string.IsNullOrWhiteSpace(offerId)) return offerId;

            return await TryFetchTradeOfferIdFromQueryDetailAsync(orderNo, token, device, uk);
        }

        public async Task<YouPinSaleActionResult> QueryOrderDetailOfferStatusAsync(
            string orderNo,
            string token,
            string device,
            string uk)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + QueryOrderDetailEndpoint);
            req.Content = YouPinMobileApiClient.JsonContent(new
            {
                orderNo,
                Sessionid = device
            });
            ApplyYouPinHeaders(req, token, device, uk);

            using var resp = await YouPinMobileApiClient.SendAsync(_http, req, "查询悠悠有品订单真实状态");
            if (!resp.IsSuccessStatusCode)
                return YouPinSaleActionResult.Failed($"订单详情：HTTP {(int)resp.StatusCode}");

            using var doc = await YouPinMobileApiClient.ReadJsonDocumentAsync(resp, "查询悠悠有品订单真实状态");
            JsonElement root = doc.RootElement;
            int code = GetInt(root, "code", "Code");
            string msg = GetString(root, "msg", "Msg", "message", "Message") ?? "";
            if (code != 0)
                return YouPinSaleActionResult.Failed(string.IsNullOrWhiteSpace(msg) ? $"订单详情：Code={code}" : "订单详情：" + msg);

            TryGetProperty(root, out JsonElement data, "data", "Data");
            string statusText = FirstNonEmpty(
                GetString(data, "orderStatusDesc", "OrderStatusDesc"),
                GetString(data, "subStatusName", "SubStatusName", "orderSubStatusName", "OrderSubStatusName"),
                GetString(data, "statusName", "StatusName", "orderStatusName", "OrderStatusName"),
                msg);
            int status = GetInt(data, "subStatus", "SubStatus", "orderSubStatus", "OrderSubStatus", "status", "Status");
            return YouPinSaleActionResult.Success(statusText, FindTradeOfferId(root), status);
        }

        public async Task<YouPinSaleActionResult> QuerySaleOrderStatusAsync(
            string orderNo,
            string token,
            string device,
            string uk)
        {
            foreach (string orderStatus in new[] { "140", "340", "280", "" })
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + SaleSellListEndpoint);
                req.Content = YouPinMobileApiClient.JsonContent(new
                {
                    keys = orderNo,
                    orderStatus,
                    pageIndex = 1,
                    pageSize = 20,
                    Sessionid = device
                });
                ApplyYouPinHeaders(req, token, device, uk);

                using var resp = await YouPinMobileApiClient.SendAsync(_http, req, "查询悠悠有品出售记录状态");
                if (!resp.IsSuccessStatusCode)
                    continue;

                using var doc = await YouPinMobileApiClient.ReadJsonDocumentAsync(resp, "查询悠悠有品出售记录状态");
                JsonElement root = doc.RootElement;
                if (GetInt(root, "code", "Code") != 0 || !TryFindOrder(root, orderNo, out JsonElement order))
                    continue;

                string statusText = FirstNonEmpty(
                    GetString(order, "orderStatusDesc", "OrderStatusDesc"),
                    GetString(order, "orderSubStatusName", "OrderSubStatusName", "subStatusName", "SubStatusName"),
                    GetString(order, "orderStatusName", "OrderStatusName", "statusName", "StatusName"));
                int status = GetInt(order, "orderSubStatus", "OrderSubStatus", "subStatus", "SubStatus");
                if (status <= 0)
                    status = GetInt(order, "orderStatus", "OrderStatus", "status", "Status");
                return YouPinSaleActionResult.Success(statusText, FindTradeOfferId(order), status);
            }

            return YouPinSaleActionResult.Failed("悠悠出售记录中暂未找到该订单。");
        }

        public async Task<YouPinSteamCounterpartyFetchResult> TryFetchSteamCounterpartyInfoAsync(
            YouPinSaleOrder order,
            string token,
            string device,
            string uk,
            string userId)
        {
            if (order == null || string.IsNullOrWhiteSpace(order.OrderNo))
                return YouPinSteamCounterpartyFetchResult.Unavailable("未获取");

            string normalizedDevice = string.IsNullOrWhiteSpace(device)
                ? YouPinMobileApiClient.GetDeviceToken()
                : device.Trim();
            string lastError = "未获取";

            foreach (int orderType in BuildTokenInfoOrderTypeCandidates(order.OrderType))
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + TokenConfirmInfoEndpoint);
                    req.Content = YouPinMobileApiClient.JsonContent(new
                    {
                        orderNo = order.OrderNo.Trim(),
                        orderType,
                        forceRefresh = false
                    });
                    ApplyYouPinHeaders(req, token.Trim(), normalizedDevice, uk);
                    YouPinMobileApiClient.ApplyH5WebViewHeaders(req, uk, userId, normalizedDevice);

                    using var resp = await YouPinMobileApiClient.SendAsync(_http, req, "获取悠悠有品 Steam 对方信息");
                    if (!resp.IsSuccessStatusCode)
                    {
                        lastError = "未获取";
                        continue;
                    }

                    using var doc = await YouPinMobileApiClient.ReadJsonDocumentAsync(resp, "获取悠悠有品 Steam 对方信息");
                    var root = doc.RootElement;
                    int code = GetInt(root, "code", "Code");
                    string msg = GetString(root, "msg", "Msg", "message", "Message") ?? "";
                    if (code != 0)
                    {
                        lastError = "未获取";
                        continue;
                    }

                    if (!TryGetProperty(root, out var data, "data", "Data"))
                        return YouPinSteamCounterpartyFetchResult.Unavailable("未获取");

                    string personaName = GetString(data, "personaName", "PersonaName") ?? "";
                    string avatarUrl = GetString(data, "avatarfull", "avatarFull", "AvatarFull", "avatar", "Avatar") ?? "";
                    int level = GetInt(data, "playerLevel", "PlayerLevel");
                    int gameTime = GetInt(data, "gameTime", "GameTime");
                    string joinDate = GetString(data, "joinSteamDate", "JoinSteamDate", "timecreated", "Timecreated") ?? "";
                    int status = GetInt(data, "status", "Status");

                    if (string.IsNullOrWhiteSpace(personaName))
                        return YouPinSteamCounterpartyFetchResult.Unavailable(status > 0 ? "未获取昵称" : "未获取");

                    return YouPinSteamCounterpartyFetchResult.Success(
                        personaName.Trim(),
                        avatarUrl.Trim(),
                        level,
                        gameTime,
                        joinDate.Trim());
                }
                catch
                {
                    lastError = "未获取";
                }
            }

            return YouPinSteamCounterpartyFetchResult.Unavailable(lastError);
        }

        private async Task<List<YouPinSaleOrder>> FetchTodoOrdersAsync(string token, string device, string userId, string uk)
        {
            List<YouPinSaleOrder> topListOrders = new();
            Exception? topListError = null;

            try
            {
                topListOrders = await FetchTopTodoOrdersAsync(token, device, userId, uk).ConfigureAwait(false);
                if (topListOrders.Count > 0)
                    return GroupWaitDeliverOrders(topListOrders);
            }
            catch (Exception ex)
            {
                topListError = ex;
            }

            try
            {
                List<YouPinSaleOrder> legacyOrders = await FetchLegacyTodoOrdersAsync(token, device, userId, uk).ConfigureAwait(false);
                return GroupWaitDeliverOrders(legacyOrders);
            }
            catch (Exception legacyError)
            {
                if (topListError == null)
                    return GroupWaitDeliverOrders(topListOrders);

                throw YouPinMobileApiClient.WrapException(
                    new AggregateException(topListError, legacyError),
                    "获取悠悠有品待办");
            }
        }

        private async Task<List<YouPinSaleOrder>> FetchTopTodoOrdersAsync(string token, string device, string userId, string uk)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + TodoEndpoint);
                req.Content = YouPinMobileApiClient.JsonContent(new
                {
                    userId,
                    Sessionid = device
                });
                ApplyYouPinHeaders(req, token.Trim(), device, uk);

                using var resp = await YouPinMobileApiClient.SendAsync(_http, req, "获取悠悠有品顶部待办").ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    throw new InvalidOperationException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");

                using var doc = await YouPinMobileApiClient.ReadJsonDocumentAsync(resp, "检查悠悠有品顶部待办").ConfigureAwait(false);
                return ParseTodoOrders(doc.RootElement);
            }
            catch (Exception ex)
            {
                throw YouPinMobileApiClient.WrapException(ex, "获取悠悠有品顶部待办");
            }
        }

        private async Task<List<YouPinSaleOrder>> FetchLegacyTodoOrdersAsync(string token, string device, string userId, string uk)
        {
            var result = new List<YouPinSaleOrder>();
            int pageIndex = 1;
            const int pageSize = 20;

            try
            {
                while (pageIndex <= 20)
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + LegacyTodoEndpoint);
                    req.Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        userId,
                        pageIndex,
                        pageSize,
                        Sessionid = device
                    }), Encoding.UTF8, "application/json");
                    ApplyYouPinHeaders(req, token.Trim(), device, uk);

                    using var resp = await YouPinMobileApiClient.SendAsync(_http, req, "获取悠悠有品待办");
                    string body = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode)
                        throw new InvalidOperationException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");

                    using var doc = YouPinMobileApiClient.ParseJson(body, "检查悠悠有品待办提醒");
                    var root = doc.RootElement;
                    int code = GetInt(root, "code", "Code");
                    if (code != 0)
                    {
                        string msg = GetString(root, "msg", "Msg", "message", "Message") ?? "";
                        if (code == 401 || code == 403 || msg.Contains("登录", StringComparison.Ordinal) || msg.Contains("token", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("悠悠有品登录状态失效，请重新登录。");
                        throw new InvalidOperationException(string.IsNullOrWhiteSpace(msg) ? $"悠悠有品返回 code={code}" : msg);
                    }

                    var current = ExtractOrderArray(root);
                    AddTodoOrders(result, current);

                    if (current.Count < pageSize) break;
                    pageIndex++;
                    await Task.Delay(250);
                }
            }
            catch (Exception ex)
            {
                throw YouPinMobileApiClient.WrapException(ex, "获取悠悠有品待办");
            }

            return result;
        }

        private static List<YouPinSaleOrder> ParseTodoOrders(JsonElement root)
        {
            int code = GetInt(root, "code", "Code");
            if (code != 0)
            {
                string msg = GetString(root, "msg", "Msg", "message", "Message") ?? "";
                if (YouPinMobileApiClient.IsLoginExpired(code, msg))
                    throw new InvalidOperationException("悠悠有品登录状态失效，请重新登录。");

                throw new InvalidOperationException(string.IsNullOrWhiteSpace(msg) ? $"悠悠有品返回 code={code}" : msg);
            }

            var result = new List<YouPinSaleOrder>();
            AddTodoOrders(result, ExtractOrderArray(root));
            return result;
        }

        private static void AddTodoOrders(List<YouPinSaleOrder> result, IReadOnlyList<JsonElement> items)
        {
            foreach (JsonElement item in items)
            {
                YouPinSaleOrder order = ParseOrder(item);
                order.Source = "待办";
                if (!string.IsNullOrWhiteSpace(order.OrderNo)
                    && !result.Any(x => string.Equals(x.OrderNo, order.OrderNo, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(order);
                }
            }
        }

        private async Task<string> TryFetchTradeOfferIdFromOrderDetailAsync(string orderNo, string token, string device, string uk)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + OrderDetailEndpoint);
            req.Content = YouPinMobileApiClient.JsonContent(new
            {
                orderId = orderNo,
                Sessionid = device
            });
            ApplyYouPinHeaders(req, token, device, uk);

            using var resp = await YouPinMobileApiClient.SendAsync(_http, req, "获取悠悠有品订单详情");
            if (!resp.IsSuccessStatusCode) return "";

            using var doc = await YouPinMobileApiClient.ReadJsonDocumentAsync(resp, "获取悠悠有品订单详情");
            var root = doc.RootElement;
            int code = GetInt(root, "code", "Code");
            if (code != 0) return "";
            return FindTradeOfferId(root);
        }

        private async Task<string> TryFetchTradeOfferIdFromQueryDetailAsync(string orderNo, string token, string device, string uk)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + QueryOrderDetailEndpoint);
            req.Content = YouPinMobileApiClient.JsonContent(new
            {
                orderNo,
                Sessionid = device
            });
            ApplyYouPinHeaders(req, token, device, uk);

            using var resp = await YouPinMobileApiClient.SendAsync(_http, req, "获取悠悠有品订单报价号");
            if (!resp.IsSuccessStatusCode) return "";

            using var doc = await YouPinMobileApiClient.ReadJsonDocumentAsync(resp, "获取悠悠有品订单报价号");
            var root = doc.RootElement;
            int code = GetInt(root, "code", "Code");
            if (code != 0) return "";
            return FindTradeOfferId(root);
        }

        private async Task<string> FetchUserIdAsync(string token, string device, string uk)
        {
            string firstError = "";
            foreach (var endpoint in new[] { UserInfoEndpoint, LegacyUserInfoEndpoint })
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl + endpoint);
                    ApplyYouPinHeaders(req, token, device, uk);
                    using var resp = await YouPinMobileApiClient.SendAsync(_http, req, "获取悠悠有品用户信息");
                    if (!resp.IsSuccessStatusCode)
                        throw new InvalidOperationException($"获取用户信息失败：HTTP {(int)resp.StatusCode}");

                    using var doc = await YouPinMobileApiClient.ReadJsonDocumentAsync(resp, "获取悠悠有品用户信息");
                    var root = doc.RootElement;
                    int code = GetInt(root, "Code", "code");
                    if (code != 0)
                    {
                        string msg = GetString(root, "Msg", "msg", "Message", "message") ?? "";
                        if (YouPinMobileApiClient.IsLoginExpired(code, msg))
                            throw new InvalidOperationException("悠悠有品登录状态失效，请重新登录。");
                        throw new InvalidOperationException(string.IsNullOrWhiteSpace(msg) ? $"获取用户信息失败：Code={code}" : msg);
                    }

                    if (!TryGetProperty(root, out var data, "Data", "data"))
                        throw new InvalidOperationException("获取用户信息失败：返回缺少 Data");

                    string userId = GetString(data, "UserId", "userId", "id", "Id") ?? "";
                    if (string.IsNullOrWhiteSpace(userId))
                        throw new InvalidOperationException("获取用户信息失败：缺少 UserId");

                    return userId;
                }
                catch (Exception ex)
                {
                    firstError = string.IsNullOrWhiteSpace(firstError) ? ex.Message : firstError;
                }
            }

            throw new InvalidOperationException(YouPinMobileApiClient.Sanitize(firstError));
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
        }

        private static bool TryFindOrder(JsonElement element, string orderNo, out JsonElement order)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                string currentOrderNo = GetString(element, "orderNo", "OrderNo") ?? "";
                if (string.Equals(currentOrderNo.Trim(), orderNo.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    order = element;
                    return true;
                }

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (TryFindOrder(property.Value, orderNo, out order))
                        return true;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (TryFindOrder(item, orderNo, out order))
                        return true;
                }
            }

            order = default;
            return false;
        }

        private static IReadOnlyList<int> BuildTokenInfoOrderTypeCandidates(int orderType)
        {
            var values = new List<int> { 1 };
            if (orderType > 0 && orderType != 1)
                values.Add(orderType);
            if (!values.Contains(2))
                values.Add(2);
            return values;
        }
    }

    internal sealed class YouPinSteamCounterpartyFetchResult
    {
        public bool Ok { get; private init; }
        public string Status { get; private init; } = "";
        public string PersonaName { get; private init; } = "";
        public string AvatarUrl { get; private init; } = "";
        public int PlayerLevel { get; private init; }
        public int GameTime { get; private init; }
        public string JoinDate { get; private init; } = "";

        public static YouPinSteamCounterpartyFetchResult Success(
            string personaName,
            string avatarUrl,
            int playerLevel,
            int gameTime,
            string joinDate)
            => new()
            {
                Ok = true,
                Status = "已获取",
                PersonaName = personaName,
                AvatarUrl = avatarUrl,
                PlayerLevel = playerLevel,
                GameTime = gameTime,
                JoinDate = joinDate
            };

        public static YouPinSteamCounterpartyFetchResult Unavailable(string status)
            => new()
            {
                Ok = false,
                Status = string.IsNullOrWhiteSpace(status) ? "未获取" : status.Trim()
            };
    }
}
