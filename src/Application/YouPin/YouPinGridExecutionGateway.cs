using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.SystemServices;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using static CS2TradeMonitor.Application.YouPin.YouPinJsonElementReader;

namespace CS2TradeMonitor.Application.YouPin
{
    /// <summary>
    /// Owns the complete YouPin write contract for one grid action. The caller only sees
    /// revalidate, submit and reconcile; payment/order protocol details stay local here.
    /// </summary>
    internal sealed class YouPinGridExecutionGateway : IYouPinGridExecutionGateway, IDisposable
    {
        private const int CashierSubBusinessType = 20000;
        private const int CashierBusinessType = 1;
        private const int BalancePayWay = 7;
        private const int MaxPayStatusReads = 3;
        private static readonly TimeSpan PayStatusDelay = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan InventoryFreshness = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan MaximumFutureClockSkew = TimeSpan.FromSeconds(30);

        private readonly IYouPinAuthService _authService;
        private readonly IYouPinGridMarketGateway _marketGateway;
        private readonly IYouPinInventoryService _inventoryService;
        private readonly IYouPinLandlordGateway _landlordGateway;
        private readonly IAppDiagnostics _diagnostics;
        private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
        private readonly HttpClient _http;

        public YouPinGridExecutionGateway(
            IYouPinAuthService authService,
            IDomesticHttpClientFactory httpFactory,
            IYouPinGridMarketGateway marketGateway,
            IYouPinInventoryService inventoryService,
            IYouPinLandlordGateway landlordGateway,
            IAppDiagnostics diagnostics)
            : this(
                authService,
                httpFactory,
                marketGateway,
                inventoryService,
                landlordGateway,
                diagnostics,
                static (delay, cancellationToken) => Task.Delay(delay, cancellationToken))
        {
        }

        internal YouPinGridExecutionGateway(
            IYouPinAuthService authService,
            IDomesticHttpClientFactory httpFactory,
            IYouPinGridMarketGateway marketGateway,
            IYouPinInventoryService inventoryService,
            IYouPinLandlordGateway landlordGateway,
            IAppDiagnostics diagnostics,
            Func<TimeSpan, CancellationToken, Task> delayAsync)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _marketGateway = marketGateway ?? throw new ArgumentNullException(nameof(marketGateway));
            _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
            _landlordGateway = landlordGateway ?? throw new ArgumentNullException(nameof(landlordGateway));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _delayAsync = delayAsync ?? throw new ArgumentNullException(nameof(delayAsync));
            _http = (httpFactory ?? throw new ArgumentNullException(nameof(httpFactory))).Create(20);
        }

        public async Task<YouPinGridExecutionRevalidation> RevalidateAsync(
            Settings settings,
            YouPinGridStrategy strategy,
            YouPinGridPlan plan,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(strategy);
            ArgumentNullException.ThrowIfNull(plan);

            YouPinInventoryState inventory = _inventoryService.GetState();
            if (!IsInventoryFresh(inventory, DateTime.Now))
            {
                return NotReady(
                    plan.Action,
                    0m,
                    "悠悠库存尚未完成最新回读，已停止本次真实买卖");
            }

            YouPinGridMarketQuote quote = await _marketGateway.ReadLowestValidListingAsync(
                settings,
                strategy,
                cancellationToken).ConfigureAwait(false);
            if (!quote.Available || quote.LowestPrice <= 0m)
            {
                return NotReady(
                    plan.Action,
                    quote.LowestPrice,
                    string.IsNullOrWhiteSpace(quote.Message) ? "悠悠同款最低有效在售价不可用" : quote.Message);
            }

            YouPinInventoryItem[] holdings = inventory.Items
                .Where(item => IsExactItem(item, strategy))
                .ToArray();
            int holdingCount = holdings.Sum(item => Math.Max(1, item.Quantity));
            decimal reservedCapital = holdings.Sum(item =>
                (decimal)Math.Max(0d, item.PurchasePrice > 0d ? item.PurchasePrice : item.Price)
                * Math.Max(1, item.Quantity));

            if (plan.Action == YouPinGridAction.Buy)
            {
                if (holdingCount + 1 > strategy.MaxHoldings)
                    return NotReady(plan.Action, quote.LowestPrice, "写入前复核发现已达到最大持有件数");
                if (!long.TryParse(quote.ListingId, NumberStyles.None, CultureInfo.InvariantCulture, out long listingId)
                    || listingId <= 0)
                {
                    return NotReady(plan.Action, quote.LowestPrice, "悠悠最低在售记录缺少有效商品编号");
                }

                YouPinGridExecutionRevalidation buyRevalidation = new(
                    true,
                    plan.Action,
                    1,
                    quote.LowestPrice,
                    reservedCapital,
                    listingId.ToString(CultureInfo.InvariantCulture),
                    "写入前已复核悠悠最低有效在售价");
                LogRevalidation(buyRevalidation);
                return buyRevalidation;
            }

            if (plan.Action == YouPinGridAction.Sell)
            {
                if (holdingCount - 1 < strategy.MinimumHoldings)
                    return NotReady(plan.Action, quote.LowestPrice, "写入前复核发现出售后会低于最低保留件数");
                IReadOnlyList<YouPinLandlordRemoteInventoryItem> remoteInventory =
                    await _landlordGateway.ReadInventoryAsync(
                        settings,
                        "grid-revalidate",
                        cancellationToken).ConfigureAwait(false);
                YouPinLandlordRemoteInventoryItem? asset = remoteInventory.FirstOrDefault(item =>
                    item.IsSaleEligible
                    && string.Equals(item.TemplateId, strategy.TemplateId, StringComparison.Ordinal)
                    && string.Equals(item.ItemName, strategy.ItemName, StringComparison.Ordinal));
                if (asset == null)
                    return NotReady(plan.Action, quote.LowestPrice, "悠悠库存中没有可上架的同款饰品");
                if (!long.TryParse(asset.AssetId, NumberStyles.None, CultureInfo.InvariantCulture, out long assetId)
                    || assetId <= 0)
                {
                    return NotReady(plan.Action, quote.LowestPrice, "悠悠库存返回的资产编号无效");
                }

                YouPinGridExecutionRevalidation saleRevalidation = new(
                    true,
                    plan.Action,
                    1,
                    quote.LowestPrice,
                    reservedCapital,
                    assetId.ToString(CultureInfo.InvariantCulture),
                    "写入前已复核悠悠库存资格与最低有效在售价");
                LogRevalidation(saleRevalidation);
                return saleRevalidation;
            }

            return NotReady(plan.Action, quote.LowestPrice, "当前没有可执行的网格方向");
        }

        public Task<YouPinGridRemoteMutationResult> SubmitAsync(
            Settings settings,
            YouPinGridExecutionRecord prepared,
            YouPinGridExecutionRevalidation revalidation,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(prepared);
            ArgumentNullException.ThrowIfNull(revalidation);
            if (prepared.Action != revalidation.Action
                || !string.Equals(prepared.TargetReference, revalidation.TargetReference, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(prepared.TargetReference))
            {
                return Task.FromResult(Rejected("执行目标与写入前核验不一致，已停止远端写入"));
            }

            return prepared.Action == YouPinGridAction.Buy
                ? SubmitBuyAsync(settings, prepared, cancellationToken)
                : prepared.Action == YouPinGridAction.Sell
                    ? SubmitSaleAsync(settings, prepared, cancellationToken)
                    : Task.FromResult(Rejected("当前没有可提交的网格方向"));
        }

        public Task<YouPinGridRemoteSettlementResult> ReconcileAsync(
            Settings settings,
            YouPinGridExecutionRecord active,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(active);
            return active.Action == YouPinGridAction.Buy
                ? ReadBatchBuySettlementAsync(settings, active.RemoteReference, active.Quantity, active.UnitPrice, cancellationToken)
                : active.Action == YouPinGridAction.Sell
                    ? ReadSaleSettlementAsync(settings, active.TargetReference, active.UnitPrice, cancellationToken)
                    : Task.FromResult(new YouPinGridRemoteSettlementResult(
                        YouPinGridExecutionStage.Failed,
                        active.UnitPrice,
                        "执行记录缺少有效方向"));
        }

        private async Task<YouPinGridRemoteMutationResult> SubmitBuyAsync(
            Settings settings,
            YouPinGridExecutionRecord prepared,
            CancellationToken cancellationToken)
        {
            YouPinCredential credential = GetRequiredCredential(settings);
            string device = ResolveDeviceToken(credential, settings);
            if (!long.TryParse(prepared.TargetReference, NumberStyles.None, CultureInfo.InvariantCulture, out long listingId)
                || listingId <= 0)
                return Rejected("悠悠商品编号无效，已停止批量购买");

            if (!int.TryParse(prepared.TemplateId, NumberStyles.None, CultureInfo.InvariantCulture, out int templateId)
                || templateId <= 0)
                return Rejected("悠悠模板编号无效，已停止批量购买");

            JsonElement precheck = await PostAsync(
                YouPinUrls.GridBatchBuyPrecheck,
                new
                {
                    userId = credential.UserId ?? string.Empty,
                    batchPurQuantity = prepared.Quantity,
                    refreshInvFlag = true
                },
                credential,
                device,
                "校验悠悠批量购买资格",
                cancellationToken).ConfigureAwait(false);
            if (!TryEnsureApiSuccess(precheck, out string precheckMessage))
                return Rejected("悠悠购买资格未通过：" + precheckMessage);

            string price = FormatAmount(prepared.UnitPrice);
            JsonElement create = await PostAsync(
                YouPinUrls.GridBatchBuyCreate,
                new
                {
                    buyNumber = prepared.Quantity,
                    commodityList = new[]
                    {
                        new
                        {
                            commodityId = listingId,
                            commodityIdPrice = price,
                            templateId,
                            reductionRequest = (object?)null
                        }
                    },
                    paySellAmount = FormatAmount(prepared.UnitPrice * prepared.Quantity),
                    unitPrice = price,
                    templateId,
                    orderSubType = 0,
                    pageSourceCode = string.Empty
                },
                credential,
                device,
                "创建悠悠网格买入订单",
                cancellationToken).ConfigureAwait(false);
            if (!TryEnsureApiSuccess(create, out string createMessage))
                return Rejected("悠悠创建买入订单失败：" + createMessage);

            if (!TryGetProperty(create, out JsonElement createData, "data", "Data"))
                return Unknown(string.Empty, "悠悠已受理买入，但响应缺少订单信息");
            string orderNo = GetString(createData, "orderNo", "OrderNo") ?? string.Empty;
            if (orderNo.Length == 0)
                return Unknown(string.Empty, "悠悠已受理买入，但未返回订单号");

            string payOrderNo = GetString(createData, "payOrderNo", "PayOrderNo") ?? string.Empty;
            string notifyUrl = GetString(createData, "notifyUrl", "NotifyUrl") ?? string.Empty;
            string waitPaymentDataNo = GetString(
                createData,
                "waitPaymentDataNo",
                "WaitPaymentDataNo") ?? string.Empty;

            try
            {
                decimal total = prepared.UnitPrice * prepared.Quantity;
                JsonElement cashier = await PostAsync(
                    YouPinUrls.GridCashierList,
                    new
                    {
                        subBusType = CashierSubBusinessType,
                        businessType = CashierBusinessType,
                        paymentAmount = FormatAmount(total),
                        orderNo,
                        extend = (string?)null,
                        extendParam = (object?)null,
                        userId = (string?)null,
                        waitPaymentDataNo
                    },
                    credential,
                    device,
                    "读取悠悠余额支付通道",
                    cancellationToken).ConfigureAwait(false);
                if (!TryEnsureApiSuccess(cashier, out string cashierMessage))
                {
                    return Unknown(orderNo, "悠悠收银台拒绝订单：" + cashierMessage);
                }
                if (!TrySelectBalanceChannel(
                        cashier,
                        total,
                        out BalanceChannel channel,
                        out string channelMessage))
                    return Unknown(orderNo, channelMessage);

                string extend = channel.AvailableBalanceShow.Length == 0
                    ? string.Empty
                    : JsonSerializer.Serialize(new { availableBalanceShow = channel.AvailableBalanceShow });
                JsonElement pay = await PostAsync(
                    YouPinUrls.GridPayConfirm,
                    new
                    {
                        paymentAmount = channel.PaymentAmount,
                        orderNo,
                        businessType = CashierBusinessType.ToString(CultureInfo.InvariantCulture),
                        subBusType = CashierSubBusinessType.ToString(CultureInfo.InvariantCulture),
                        userId = (string?)null,
                        channelCode = channel.ChannelCode,
                        channelId = channel.ChannelId,
                        extend,
                        payWay = BalancePayWay,
                        outTradeNo = payOrderNo,
                        notifyUrl,
                        waitPaymentDataNo,
                        contractNo = channel.ContractNo,
                        mixPayDetails = (object?)null
                    },
                    credential,
                    device,
                    "确认悠悠余额支付",
                    cancellationToken).ConfigureAwait(false);
                if (!TryEnsureApiSuccess(pay, out string payMessage))
                    return Unknown(orderNo, "悠悠余额支付结果需核对：" + payMessage);

                if (TryGetProperty(pay, out JsonElement payData, "data", "Data"))
                    payOrderNo = FirstText(GetString(payData, "payOrderNo", "PayOrderNo"), payOrderNo);

                bool paid = await WaitForPaymentAsync(
                    credential,
                    device,
                    orderNo,
                    payOrderNo,
                    waitPaymentDataNo,
                    cancellationToken).ConfigureAwait(false);
                if (!paid)
                {
                    return new YouPinGridRemoteMutationResult(
                        true,
                        false,
                        true,
                        orderNo,
                        "悠悠买入订单已创建，等待余额支付状态回读");
                }

                YouPinGridRemoteSettlementResult settlement = await ReadBatchBuySettlementAsync(
                    settings,
                    orderNo,
                    prepared.Quantity,
                    prepared.UnitPrice,
                    cancellationToken).ConfigureAwait(false);
                return new YouPinGridRemoteMutationResult(
                    true,
                    settlement.Stage == YouPinGridExecutionStage.Completed,
                    true,
                    orderNo,
                    settlement.Message);
            }
            catch (Exception ex) when (ex is not StackOverflowException and not OutOfMemoryException)
            {
                return Unknown(
                    orderNo,
                    ex is OperationCanceledException
                        ? "悠悠买入订单已创建，但后续请求被取消，禁止自动重试"
                        : "悠悠买入订单已创建，但后续结果未知：" + Sanitize(ex.Message));
            }
        }

        private async Task<YouPinGridRemoteMutationResult> SubmitSaleAsync(
            Settings settings,
            YouPinGridExecutionRecord prepared,
            CancellationToken cancellationToken)
        {
            try
            {
                YouPinLandlordInventoryWriteResult result = await _landlordGateway.ListInventoryAsync(
                    settings,
                    new YouPinLandlordInventoryListCommand(
                        prepared.TargetReference,
                        0m,
                        0m,
                        0m,
                        0,
                        prepared.UnitPrice,
                        0,
                        0m,
                        0m,
                        0,
                        IsCanLease: false,
                        IsCanSold: true),
                    "grid-sale",
                    prepared.Id,
                    cancellationToken).ConfigureAwait(false);
                return result.Success
                    ? new YouPinGridRemoteMutationResult(
                        true,
                        false,
                        true,
                        result.ListingId,
                        "悠悠出售已上架，等待成交状态回读")
                    : Unknown(result.ListingId, result.Message);
            }
            catch (Exception ex) when (ex is not StackOverflowException and not OutOfMemoryException)
            {
                return Unknown(
                    string.Empty,
                    ex is OperationCanceledException
                        ? "悠悠出售请求被取消，结果未知，禁止自动重试"
                        : "悠悠出售结果未知：" + Sanitize(ex.Message));
            }
        }

        private async Task<bool> WaitForPaymentAsync(
            YouPinCredential credential,
            string device,
            string orderNo,
            string payOrderNo,
            string waitPaymentDataNo,
            CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < MaxPayStatusReads; attempt++)
            {
                if (attempt > 0)
                    await _delayAsync(PayStatusDelay, cancellationToken).ConfigureAwait(false);
                JsonElement status = await PostAsync(
                    YouPinUrls.GridPayStatus,
                    new
                    {
                        subBusType = CashierSubBusinessType,
                        businessType = CashierBusinessType,
                        payWay = BalancePayWay,
                        orderNo,
                        payOrderNo,
                        waitPaymentDataNo
                    },
                    credential,
                    device,
                    "回读悠悠余额支付状态",
                    cancellationToken).ConfigureAwait(false);
                if (!TryEnsureApiSuccess(status, out _))
                    continue;
                JsonElement data = TryGetProperty(status, out JsonElement value, "data", "Data")
                    ? value
                    : status;
                if (ReadBool(data, "paySuccess", "PaySuccess"))
                    return true;
                int payStatus = GetInt(data, "payStatus", "PayStatus", "payState", "PayState");
                if (payStatus is 1 or 6001)
                    return true;
            }

            return false;
        }

        private async Task<YouPinGridRemoteSettlementResult> ReadBatchBuySettlementAsync(
            Settings settings,
            string orderNo,
            int expectedQuantity,
            decimal unitPrice,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(orderNo))
            {
                return new YouPinGridRemoteSettlementResult(
                    YouPinGridExecutionStage.RequiresManualReview,
                    unitPrice,
                    "悠悠买入记录缺少订单号，请人工核对");
            }

            YouPinCredential credential = GetRequiredCredential(settings);
            string device = ResolveDeviceToken(credential, settings);
            JsonElement response = await PostAsync(
                YouPinUrls.GridBatchBuyStatus,
                new
                {
                    orderNo,
                    orderNoList = new[] { orderNo },
                    version = "2"
                },
                credential,
                device,
                "回读悠悠批量买入订单",
                cancellationToken).ConfigureAwait(false);
            if (!TryEnsureApiSuccess(response, out string message))
            {
                return new YouPinGridRemoteSettlementResult(
                    YouPinGridExecutionStage.AwaitingSettlement,
                    unitPrice,
                    "悠悠买入状态暂不可用：" + message);
            }

            JsonElement data = TryGetProperty(response, out JsonElement value, "data", "Data")
                ? value
                : response;
            int total = GetInt(data, "totalNumber", "TotalNumber");
            int success = GetInt(data, "successNumber", "SuccessNumber");
            int failed = GetInt(data, "failNumber", "FailNumber");
            int failStatus = GetInt(data, "failStatus", "FailStatus");
            int required = Math.Max(1, expectedQuantity);
            if (success >= required)
            {
                return new YouPinGridRemoteSettlementResult(
                    YouPinGridExecutionStage.Completed,
                    unitPrice,
                    $"悠悠买入已完成 {success}/{Math.Max(total, required)} 件");
            }
            if ((total > 0 && failed >= total) || failStatus > 0)
            {
                return new YouPinGridRemoteSettlementResult(
                    YouPinGridExecutionStage.Failed,
                    unitPrice,
                    "悠悠买入订单已失败");
            }

            return new YouPinGridRemoteSettlementResult(
                YouPinGridExecutionStage.AwaitingSettlement,
                unitPrice,
                "悠悠买入订单等待成交回读");
        }

        private async Task<YouPinGridRemoteSettlementResult> ReadSaleSettlementAsync(
            Settings settings,
            string assetId,
            decimal unitPrice,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(assetId))
            {
                return new YouPinGridRemoteSettlementResult(
                    YouPinGridExecutionStage.RequiresManualReview,
                    unitPrice,
                    "悠悠出售记录缺少资产编号，请人工核对");
            }

            YouPinCredential credential = GetRequiredCredential(settings);
            string device = ResolveDeviceToken(credential, settings);
            JsonElement response = await PostAsync(
                YouPinUrls.GridSaleOrderList,
                new
                {
                    pageIndex = 1,
                    pageSize = 50,
                    orderStatus = "0",
                    keys = assetId
                },
                credential,
                device,
                "回读悠悠出售订单",
                cancellationToken).ConfigureAwait(false);
            if (!TryEnsureApiSuccess(response, out string message))
            {
                return new YouPinGridRemoteSettlementResult(
                    YouPinGridExecutionStage.AwaitingSettlement,
                    unitPrice,
                    "悠悠出售状态暂不可用：" + message);
            }

            JsonElement data = TryGetProperty(response, out JsonElement value, "data", "Data")
                ? value
                : response;
            if (!TryGetProperty(data, out JsonElement orders, "orderList", "OrderList")
                || orders.ValueKind != JsonValueKind.Array)
            {
                return new YouPinGridRemoteSettlementResult(
                    YouPinGridExecutionStage.AwaitingSettlement,
                    unitPrice,
                    "悠悠出售仍在等待买家成交");
            }

            foreach (JsonElement order in orders.EnumerateArray())
            {
                if (!ContainsAsset(order, assetId))
                    continue;
                string status = FirstText(
                    GetString(order, "orderStatusDesc", "OrderStatusDesc"),
                    GetString(order, "orderStatusName", "OrderStatusName"),
                    GetString(order, "orderStatus", "OrderStatus"));
                decimal settledPrice = ReadPositiveDecimal(order, "price", "Price", "totalAmount", "TotalAmount");
                if (ContainsFailureStatus(status))
                {
                    return new YouPinGridRemoteSettlementResult(
                        YouPinGridExecutionStage.Failed,
                        settledPrice > 0m ? settledPrice : unitPrice,
                        "悠悠出售订单已关闭：" + status);
                }
                if (!ContainsSuccessStatus(status))
                {
                    return new YouPinGridRemoteSettlementResult(
                        YouPinGridExecutionStage.AwaitingSettlement,
                        settledPrice > 0m ? settledPrice : unitPrice,
                        string.IsNullOrWhiteSpace(status)
                            ? "悠悠出售订单等待状态更新"
                            : "悠悠出售订单处理中：" + status);
                }

                return new YouPinGridRemoteSettlementResult(
                    YouPinGridExecutionStage.Completed,
                    settledPrice > 0m ? settledPrice : unitPrice,
                    string.IsNullOrWhiteSpace(status)
                        ? "悠悠出售已匹配买家订单"
                        : "悠悠出售已匹配买家订单：" + status);
            }

            return new YouPinGridRemoteSettlementResult(
                YouPinGridExecutionStage.AwaitingSettlement,
                unitPrice,
                "悠悠出售仍在等待买家成交");
        }

        private async Task<JsonElement> PostAsync(
            string path,
            object body,
            YouPinCredential credential,
            string device,
            string operation,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, YouPinUrls.ApiBase + path)
            {
                Content = YouPinMobileApiClient.JsonContent(body)
            };
            YouPinMobileApiClient.ApplyHeaders(request, credential.Token, device, credential.Uk);
            using HttpResponseMessage response = await YouPinMobileApiClient.SendAsync(
                _http,
                request,
                operation,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"{operation}失败：HTTP {(int)response.StatusCode}");
            using JsonDocument document = await YouPinMobileApiClient.ReadJsonDocumentAsync(
                response,
                operation).ConfigureAwait(false);
            return document.RootElement.Clone();
        }

        private static bool TrySelectBalanceChannel(
            JsonElement root,
            decimal requiredAmount,
            out BalanceChannel channel,
            out string message)
        {
            channel = default;
            message = "悠悠收银台没有可用的余额支付通道";
            if (!TryGetProperty(root, out JsonElement data, "data", "Data"))
                return false;
            string rootConfirmUrl = GetString(data, "confirmBtnUrl", "ConfirmBtnUrl") ?? string.Empty;
            if (rootConfirmUrl.Length > 0)
            {
                message = "悠悠余额支付需要跳转页面确认，已停止自动支付";
                return false;
            }
            if (!TryGetProperty(data, out JsonElement payList, "payList", "PayList")
                || payList.ValueKind != JsonValueKind.Array)
                return false;

            string paymentAmount = FirstText(
                GetString(data, "amount", "Amount"),
                FormatAmount(requiredAmount));
            if (!TryParseAmount(paymentAmount, out decimal cashierAmount)
                || cashierAmount <= 0m
                || cashierAmount > requiredAmount)
            {
                message = "悠悠收银台金额与写入前复核金额不一致，已停止自动支付";
                return false;
            }
            foreach (JsonElement item in payList.EnumerateArray())
            {
                if (GetInt(item, "payWay", "PayWay") != BalancePayWay
                    || GetInt(item, "showGray", "ShowGray") != 0)
                    continue;
                if (FirstText(
                        GetString(item, "confirmBtnUrl", "ConfirmBtnUrl"),
                        GetString(item, "jumpUrl", "JumpUrl"),
                        GetString(item, "schemeJumpUrl", "SchemeJumpUrl")).Length > 0)
                {
                    continue;
                }

                string balanceText = FirstText(
                    GetString(item, "balance", "Balance"),
                    GetString(item, "availableBalance", "AvailableBalance"),
                    GetString(item, "availableBalanceShow", "AvailableBalanceShow"));
                if (!TryParseAmount(balanceText, out decimal balance) || balance < requiredAmount)
                {
                    message = "悠悠余额不足，已停止自动支付";
                    continue;
                }

                channel = new BalanceChannel(
                    paymentAmount,
                    GetString(item, "channelCode", "ChannelCode") ?? string.Empty,
                    GetString(item, "channelId", "ChannelId") ?? string.Empty,
                    GetString(item, "contractNo", "ContractNo") ?? string.Empty,
                    GetString(item, "availableBalanceShow", "AvailableBalanceShow") ?? string.Empty);
                return true;
            }

            return false;
        }

        private static bool TryEnsureApiSuccess(JsonElement root, out string message)
        {
            if (!TryGetProperty(root, out JsonElement codeElement, "code", "Code"))
            {
                message = "响应缺少状态码";
                return false;
            }
            int code = codeElement.ValueKind == JsonValueKind.Number && codeElement.TryGetInt32(out int numericCode)
                ? numericCode
                : int.TryParse(codeElement.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int textCode)
                    ? textCode
                    : int.MinValue;
            message = FirstText(
                GetString(root, "msg", "Msg", "message", "Message"),
                code == 0 ? "成功" : $"code={code}");
            return code == 0;
        }

        private static bool ContainsAsset(JsonElement order, string assetId)
        {
            if (TryGetProperty(order, out JsonElement product, "productDetail", "ProductDetail")
                && string.Equals(GetString(product, "assertId", "assetId", "AssetId"), assetId, StringComparison.Ordinal))
            {
                return true;
            }
            if (!TryGetProperty(order, out JsonElement products, "productDetailList", "ProductDetailList")
                || products.ValueKind != JsonValueKind.Array)
                return false;
            return products.EnumerateArray().Any(item => string.Equals(
                GetString(item, "assertId", "assetId", "AssetId"),
                assetId,
                StringComparison.Ordinal));
        }

        private static bool ContainsFailureStatus(string value)
        {
            return value.Contains("取消", StringComparison.OrdinalIgnoreCase)
                || value.Contains("关闭", StringComparison.OrdinalIgnoreCase)
                || value.Contains("失败", StringComparison.OrdinalIgnoreCase)
                || value.Contains("cancel", StringComparison.OrdinalIgnoreCase)
                || value.Contains("close", StringComparison.OrdinalIgnoreCase)
                || value.Contains("fail", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsSuccessStatus(string value)
        {
            return value.Contains("已完成", StringComparison.OrdinalIgnoreCase)
                || value.Contains("交易完成", StringComparison.OrdinalIgnoreCase)
                || value.Contains("已收货", StringComparison.OrdinalIgnoreCase)
                || value.Contains("成功", StringComparison.OrdinalIgnoreCase)
                || value.Contains("complete", StringComparison.OrdinalIgnoreCase)
                || value.Contains("success", StringComparison.OrdinalIgnoreCase);
        }

        private static decimal ReadPositiveDecimal(JsonElement element, params string[] names)
        {
            foreach (string name in names)
            {
                string? text = GetString(element, name);
                if (TryParseAmount(text, out decimal value) && value > 0m)
                    return value;
            }
            return 0m;
        }

        private static bool TryParseAmount(string? value, out decimal amount)
        {
            string normalized = (value ?? string.Empty)
                .Replace("¥", string.Empty, StringComparison.Ordinal)
                .Replace("￥", string.Empty, StringComparison.Ordinal)
                .Replace(",", string.Empty, StringComparison.Ordinal)
                .Trim();
            return decimal.TryParse(
                normalized,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out amount);
        }

        private static string FormatAmount(decimal value) =>
            value.ToString("0.##", CultureInfo.InvariantCulture);

        private static bool IsExactItem(YouPinInventoryItem item, YouPinGridStrategy strategy)
        {
            return string.Equals(item.TemplateId, strategy.TemplateId, StringComparison.Ordinal)
                && string.Equals(item.Name, strategy.ItemName, StringComparison.Ordinal);
        }

        private static bool IsInventoryFresh(YouPinInventoryState inventory, DateTime now)
        {
            TimeSpan age = now - inventory.LastFetch;
            return inventory.LastFetch != DateTime.MinValue
                && string.IsNullOrWhiteSpace(inventory.LastError)
                && age >= -MaximumFutureClockSkew
                && age <= InventoryFreshness;
        }

        private static bool ReadBool(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out JsonElement value, names))
                return false;
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return value.GetBoolean();
            return bool.TryParse(value.ToString(), out bool parsed) && parsed;
        }

        private YouPinCredential GetRequiredCredential(Settings settings)
        {
            YouPinCredential? credential = _authService.GetCredential(settings);
            if (credential == null || string.IsNullOrWhiteSpace(credential.Token))
                throw new InvalidOperationException("请先登录悠悠有品后再执行交易网格。");
            return credential;
        }

        private string ResolveDeviceToken(YouPinCredential credential, Settings settings)
        {
            return string.IsNullOrWhiteSpace(credential.DeviceToken)
                ? _authService.EnsureDeviceToken(settings)
                : credential.DeviceToken.Trim();
        }

        private static YouPinGridExecutionRevalidation NotReady(
            YouPinGridAction action,
            decimal price,
            string message)
        {
            return new YouPinGridExecutionRevalidation(false, action, 0, price, 0m, string.Empty, message);
        }

        private static YouPinGridRemoteMutationResult Rejected(string message)
        {
            return new YouPinGridRemoteMutationResult(false, false, false, string.Empty, message);
        }

        private static YouPinGridRemoteMutationResult Unknown(string remoteReference, string message)
        {
            return new YouPinGridRemoteMutationResult(false, false, true, remoteReference, message);
        }

        private static string Sanitize(string? value)
        {
            string text = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return text.Length <= 160 ? text : text[..160];
        }

        private void LogRevalidation(YouPinGridExecutionRevalidation revalidation)
        {
            _diagnostics.Info(
                "YouPinGrid",
                $"Execution revalidated. Action={revalidation.Action}; "
                + $"Ready={revalidation.Ready}; Quantity={revalidation.Quantity}");
        }

        public void Dispose() => _http.Dispose();

        private readonly record struct BalanceChannel(
            string PaymentAmount,
            string ChannelCode,
            string ChannelId,
            string ContractNo,
            string AvailableBalanceShow);
    }
}
