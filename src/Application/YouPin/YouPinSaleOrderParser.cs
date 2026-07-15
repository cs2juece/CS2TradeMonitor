using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using static CS2TradeMonitor.Application.YouPin.YouPinJsonElementReader;
using static CS2TradeMonitor.Application.YouPin.YouPinSaleOrderGroupHelper;

namespace CS2TradeMonitor.Application.YouPin
{
    internal static class YouPinSaleOrderParser
    {
        public static List<JsonElement> ExtractOrderArray(JsonElement root)
        {
            var elements = new List<JsonElement>();
            if (TryGetProperty(root, out var data, "data", "Data"))
            {
                if (data.ValueKind == JsonValueKind.Array)
                {
                    elements.AddRange(data.EnumerateArray());
                }
                else if (data.ValueKind == JsonValueKind.Object)
                {
                    if (TryGetProperty(data, out var topList, "topList", "TopList") && topList.ValueKind == JsonValueKind.Array)
                        elements.AddRange(topList.EnumerateArray());

                    if (TryGetProperty(data, out var list, "list", "List", "orderList", "OrderList", "items", "Items") && list.ValueKind == JsonValueKind.Array)
                        elements.AddRange(list.EnumerateArray());
                }
            }

            return elements;
        }

        public static List<JsonElement> ExtractWaitDeliverArray(JsonElement root)
        {
            if (TryGetProperty(root, out var data, "data", "Data"))
            {
                if (TryGetProperty(data, out var list, "waitDeliverList", "WaitDeliverList", "list", "List", "items", "Items") && list.ValueKind == JsonValueKind.Array)
                    return list.EnumerateArray().ToList();
                if (data.ValueKind == JsonValueKind.Array)
                    return data.EnumerateArray().ToList();
            }

            return new List<JsonElement>();
        }

        public static List<JsonElement> ExtractPendingBuyArray(JsonElement root)
        {
            if (TryGetProperty(root, out var data, "data", "Data")
                && TryGetProperty(data, out var list, "orderList", "OrderList")
                && list.ValueKind == JsonValueKind.Array)
            {
                return list.EnumerateArray().ToList();
            }

            return new List<JsonElement>();
        }

        public static YouPinSaleOrder ParseOrder(JsonElement item)
        {
            TryGetProperty(item, out var commodityInfo, "commodityInfoVO", "CommodityInfoVO", "commodityInfo", "CommodityInfo", "goodsInfo", "GoodsInfo");

            string orderNo = FirstText(
                GetString(item, "orderNo", "OrderNo", "orderId", "OrderId", "id", "Id", "todoId", "TodoId", "businessId", "BusinessId", "businessNo", "BusinessNo"),
                GetString(item, "tradeOfferId", "TradeOfferId", "orderSn", "OrderSn", "sn", "Sn"));
            string name = FirstText(
                GetString(item, "commodityName", "CommodityName", "itemName", "ItemName", "goodsName", "GoodsName", "name", "Name"),
                GetString(item, "marketHashName", "MarketHashName", "shortName", "ShortName"));
            string message = FirstText(
                GetString(item, "message", "Message", "todoMessage", "TodoMessage", "title", "Title", "content", "Content"),
                GetString(item, "desc", "Desc", "description", "Description", "todoDesc", "TodoDesc", "actionDesc", "ActionDesc"));
            double price = GetDouble(item, "price", "Price", "salePrice", "SalePrice", "orderPrice", "OrderPrice", "payAmount", "PayAmount", "amount", "Amount");
            string imageUrl = FirstText(
                GetImageUrl(commodityInfo),
                GetImageUrl(item),
                FindImageUrl(item));
            string tradeOfferId = FirstText(
                GetString(item, "tradeOfferId", "TradeOfferId", "offerId", "OfferId"),
                FindTradeOfferId(item));
            string offerId = FirstText(GetString(item, "offerId", "OfferId"), tradeOfferId);
            int orderType = GetInt(item, "orderType", "OrderType");
            int orderStatus = GetInt(item, "orderStatus", "OrderStatus", "status", "Status");
            int orderSubStatus = GetInt(item, "orderSubStatus", "OrderSubStatus", "subStatus", "SubStatus");
            int realOrderSubStatus = GetInt(item, "realOrderSubStatus", "RealOrderSubStatus");
            string orderStatusDesc = CleanStatusText(FirstText(
                GetString(item, "orderStatusDesc", "OrderStatusDesc", "statusDesc", "StatusDesc", "desc", "Desc"),
                message));
            string leaseType = FirstText(
                GetString(item, "leaseType", "LeaseType"),
                GetString(item, "orderLeaseType", "OrderLeaseType"));
            var groupOrderNos = ExtractOrderNoList(item);
            string orderGroupId = ExtractOrderGroupId(item, groupOrderNos);
            bool isOrderGroup = IsOrderGroup(item) || groupOrderNos.Count > 1 || !string.IsNullOrWhiteSpace(orderGroupId);
            if (string.IsNullOrWhiteSpace(orderNo))
            {
                string stableTime = GetString(item, "createTime", "CreateTime", "createdAt", "CreatedAt", "time", "Time") ?? "todo";
                orderNo = stableTime + ":" + name + ":" + message + ":" + price.ToString("0.##", CultureInfo.InvariantCulture);
            }

            var order = new YouPinSaleOrder
            {
                OrderNo = orderNo,
                Name = name,
                Message = message,
                Price = price,
                DetectedAt = DateTime.Now,
                ImageUrl = imageUrl,
                TradeOfferId = tradeOfferId,
                OrderType = orderType,
                OrderStatus = orderStatus,
                OrderSubStatus = orderSubStatus,
                RealOrderSubStatus = realOrderSubStatus,
                OrderStatusDesc = orderStatusDesc,
                LeaseType = leaseType,
                OfferId = offerId,
                IsOrderGroup = isOrderGroup,
                OrderGroupId = orderGroupId,
                OrderNos = groupOrderNos
            };
            return NormalizeOrderGroupFields(order);
        }

        public static YouPinSaleOrder ParseWaitDeliverOrder(JsonElement item)
        {
            TryGetProperty(item, out var commodityInfo, "commodityInfoVO", "CommodityInfoVO", "commodityInfo", "CommodityInfo");
            TryGetProperty(item, out var orderInfo, "orderInfoVO", "OrderInfoVO", "orderInfo", "OrderInfo");

            string orderNo = FirstText(
                GetString(orderInfo, "orderNo", "OrderNo", "orderId", "OrderId", "id", "Id", "businessNo", "BusinessNo"),
                GetString(item, "orderNo", "OrderNo", "orderId", "OrderId", "id", "Id", "businessNo", "BusinessNo"),
                GetString(item, "tradeOfferId", "TradeOfferId", "orderSn", "OrderSn", "sn", "Sn"));
            string name = FirstText(
                GetString(commodityInfo, "commodityName", "CommodityName", "marketHashName", "MarketHashName", "name", "Name"),
                GetString(item, "commodityName", "CommodityName", "marketHashName", "MarketHashName", "name", "Name"),
                "悠悠有品待发货订单");
            string status = FirstText(
                JoinText(" / ",
                    GetString(orderInfo, "orderStatusName", "OrderStatusName"),
                    GetString(orderInfo, "orderSubStatusName", "OrderSubStatusName")),
                GetString(orderInfo, "orderStatusDesc", "OrderStatusDesc"),
                GetString(orderInfo, "message", "Message", "statusName", "StatusName", "desc", "Desc"),
                GetString(item, "message", "Message", "statusName", "StatusName", "desc", "Desc"),
                "等待处理");
            int orderType = FirstPositiveInt(
                GetInt(orderInfo, "orderType", "OrderType"),
                GetInt(item, "orderType", "OrderType"));
            int orderStatus = FirstPositiveInt(
                GetInt(orderInfo, "orderStatus", "OrderStatus", "status", "Status"),
                GetInt(item, "orderStatus", "OrderStatus", "status", "Status"));
            int orderSubStatus = FirstPositiveInt(
                GetInt(orderInfo, "orderSubStatus", "OrderSubStatus", "subStatus", "SubStatus"),
                GetInt(item, "orderSubStatus", "OrderSubStatus", "subStatus", "SubStatus"));
            int realOrderSubStatus = FirstPositiveInt(
                GetInt(orderInfo, "realOrderSubStatus", "RealOrderSubStatus"),
                GetInt(item, "realOrderSubStatus", "RealOrderSubStatus"));
            string orderStatusDesc = CleanStatusText(FirstText(
                GetString(orderInfo, "orderStatusDesc", "OrderStatusDesc"),
                GetString(item, "orderStatusDesc", "OrderStatusDesc"),
                status));
            status = CleanStatusText(status);
            if (IsWaitingCounterpartyConfirm(orderStatusDesc))
                status = orderStatusDesc;
            string leaseType = FirstText(
                GetString(orderInfo, "leaseType", "LeaseType"),
                GetString(item, "leaseType", "LeaseType"));
            double price = FirstPositive(
                GetDouble(orderInfo, "sellAmount", "SellAmount", "price", "Price", "amount", "Amount"),
                GetDouble(commodityInfo, "purchasePrice", "PurchasePrice", "price", "Price"));
            string imageUrl = FirstText(
                GetImageUrl(commodityInfo),
                GetImageUrl(orderInfo),
                GetImageUrl(item),
                FindImageUrl(commodityInfo),
                FindImageUrl(item));
            string tradeOfferId = FirstText(
                GetString(orderInfo, "tradeOfferId", "TradeOfferId", "offerId", "OfferId"),
                GetString(item, "tradeOfferId", "TradeOfferId", "offerId", "OfferId"),
                FindTradeOfferId(item));
            string offerId = FirstText(
                GetString(orderInfo, "offerId", "OfferId"),
                GetString(item, "offerId", "OfferId"),
                tradeOfferId);
            var groupOrderNos = NormalizeOrderNoList(ExtractOrderNoList(orderInfo).Concat(ExtractOrderNoList(item)));
            string orderGroupId = FirstText(
                ExtractOrderGroupId(orderInfo, groupOrderNos),
                ExtractOrderGroupId(item, groupOrderNos));
            bool isOrderGroup = IsOrderGroup(orderInfo) || IsOrderGroup(item) || groupOrderNos.Count > 1 || !string.IsNullOrWhiteSpace(orderGroupId);

            if (string.IsNullOrWhiteSpace(orderNo))
                orderNo = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + ":" + name + ":" + status + ":" + price.ToString("0.##", CultureInfo.InvariantCulture);

            var order = new YouPinSaleOrder
            {
                OrderNo = orderNo,
                Name = name,
                Message = $"{(orderType == 2 ? "出租转交" : "有买家购买")}，{status}",
                Price = price,
                DetectedAt = DateTime.Now,
                ImageUrl = imageUrl,
                TradeOfferId = tradeOfferId,
                OrderType = orderType,
                OrderStatus = orderStatus,
                OrderSubStatus = orderSubStatus,
                RealOrderSubStatus = realOrderSubStatus,
                OrderStatusDesc = orderStatusDesc,
                LeaseType = leaseType,
                OfferId = offerId,
                IsOrderGroup = isOrderGroup,
                OrderGroupId = orderGroupId,
                OrderNos = groupOrderNos
            };
            return NormalizeOrderGroupFields(order);
        }

        public static YouPinSaleOrder ParsePendingBuyOrder(JsonElement item)
        {
            var productDetails = new List<JsonElement>();
            if (TryGetProperty(item, out var detailList, "productDetailList", "ProductDetailList")
                && detailList.ValueKind == JsonValueKind.Array)
            {
                productDetails.AddRange(detailList.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object));
            }
            if (productDetails.Count == 0
                && TryGetProperty(item, out var singleDetail, "productDetail", "ProductDetail")
                && singleDetail.ValueKind == JsonValueKind.Object)
            {
                productDetails.Add(singleDetail);
            }

            JsonElement firstDetail = productDetails.FirstOrDefault();
            string orderNo = FirstText(
                GetString(item, "orderNo", "OrderNo", "orderId", "OrderId", "businessNo", "BusinessNo", "id", "Id"));
            int orderStatus = GetInt(item, "orderStatus", "OrderStatus", "status", "Status");
            int orderSubStatus = GetInt(item, "orderSubStatus", "OrderSubStatus", "subStatus", "SubStatus");
            string statusName = FirstText(
                GetString(item, "orderStatusName", "OrderStatusName"),
                GetString(item, "orderSubStatusName", "OrderSubStatusName"),
                "待收货");
            string statusDesc = CleanStatusText(FirstText(
                GetString(item, "orderStatusDesc", "OrderStatusDesc"),
                statusName));
            string message = orderStatus == 140
                ? "悠悠购买待收货，待您发送报价"
                : $"悠悠购买，{statusName}";
            string tradeOfferId = FirstText(
                GetString(item, "tradeOfferId", "TradeOfferId", "offerId", "OfferId"),
                FindTradeOfferId(item));
            string name = FirstText(
                GetString(firstDetail, "commodityName", "CommodityName", "marketHashName", "MarketHashName", "name", "Name"),
                GetString(item, "commodityName", "CommodityName", "marketHashName", "MarketHashName", "name", "Name"),
                "悠悠购买待收货订单");
            double price = NormalizeCents(FirstPositive(
                GetDouble(item, "paymentAmount", "PaymentAmount", "totalAmount", "TotalAmount", "commodityAmount", "CommodityAmount", "price", "Price")));
            string imageUrl = FirstText(
                GetImageUrl(firstDetail),
                GetImageUrl(item),
                FindImageUrl(firstDetail),
                FindImageUrl(item));

            var orderItems = productDetails.Select(detail => new YouPinSaleOrderItem
            {
                OrderNo = orderNo,
                Name = FirstText(
                    GetString(detail, "commodityName", "CommodityName", "marketHashName", "MarketHashName", "name", "Name"),
                    name),
                Price = NormalizeCents(GetDouble(detail, "price", "Price", "paymentAmount", "PaymentAmount")),
                ImageUrl = FirstText(GetImageUrl(detail), FindImageUrl(detail)),
                TradeOfferId = tradeOfferId
            }).ToList();

            if (string.IsNullOrWhiteSpace(orderNo))
            {
                string stableTime = GetString(item, "createOrderTime", "CreateOrderTime", "createTime", "CreateTime") ?? "pending-buy";
                orderNo = stableTime + ":" + name + ":" + price.ToString("0.##", CultureInfo.InvariantCulture);
                foreach (YouPinSaleOrderItem orderItem in orderItems)
                    orderItem.OrderNo = orderNo;
            }

            bool isOrderGroup = productDetails.Count > 1;
            var order = new YouPinSaleOrder
            {
                OrderNo = orderNo,
                Name = name,
                Message = message,
                Price = price,
                DetectedAt = ParseOrderTime(item),
                Source = "悠悠购买待收货",
                ImageUrl = imageUrl,
                TradeOfferId = tradeOfferId,
                OrderType = GetInt(item, "orderType", "OrderType"),
                OrderStatus = orderStatus,
                OrderSubStatus = orderSubStatus,
                RealOrderSubStatus = GetInt(item, "realOrderSubStatus", "RealOrderSubStatus"),
                OrderStatusDesc = statusDesc,
                OfferId = FirstText(GetString(item, "offerId", "OfferId"), tradeOfferId),
                IsOrderGroup = isOrderGroup,
                OrderGroupId = isOrderGroup ? FirstText(GetString(item, "groupNo", "GroupNo", "parentOrderNo", "ParentOrderNo"), orderNo) : "",
                OrderNos = string.IsNullOrWhiteSpace(orderNo) ? new List<string>() : new List<string> { orderNo },
                OrderItems = orderItems
            };
            return NormalizeOrderGroupFields(order);
        }

        public static string FindTradeOfferId(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Name.Equals("tradeOfferId", StringComparison.OrdinalIgnoreCase)
                        || prop.Name.Equals("offerId", StringComparison.OrdinalIgnoreCase)
                        || prop.Name.Equals("TradeOfferId", StringComparison.Ordinal)
                        || prop.Name.Equals("OfferId", StringComparison.Ordinal))
                    {
                        string value = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? "" : prop.Value.ToString();
                        if (!string.IsNullOrWhiteSpace(value) && value != "0" && !value.Equals("null", StringComparison.OrdinalIgnoreCase))
                            return value.Trim();
                    }

                    string nested = FindTradeOfferId(prop.Value);
                    if (!string.IsNullOrWhiteSpace(nested)) return nested;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    string nested = FindTradeOfferId(item);
                    if (!string.IsNullOrWhiteSpace(nested)) return nested;
                }
            }

            return "";
        }

        private static int FirstPositiveInt(params int[] values)
        {
            foreach (int value in values)
            {
                if (value > 0)
                    return value;
            }

            return 0;
        }

        private static double NormalizeCents(double value)
        {
            return value <= 0 ? 0 : Math.Round(value / 100.0, 2, MidpointRounding.AwayFromZero);
        }

        private static DateTime ParseOrderTime(JsonElement item)
        {
            string text = FirstText(
                GetString(item, "createOrderTime", "CreateOrderTime"),
                GetString(item, "recordTime", "RecordTime"),
                GetString(item, "createTime", "CreateTime"));
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
            {
                try
                {
                    return value > 10_000_000_000L
                        ? DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime
                        : DateTimeOffset.FromUnixTimeSeconds(value).LocalDateTime;
                }
                catch (ArgumentOutOfRangeException)
                {
                    // 平台偶发返回异常时间戳时保留当前检测时间，不能让订单读取整体失败。
                }
            }

            return DateTime.Now;
        }

        private static string CleanStatusText(string text)
        {
            text = (text ?? string.Empty).Trim();
            if (text.EndsWith(" -s", StringComparison.OrdinalIgnoreCase))
                return text[..^3].TrimEnd();
            if (text.EndsWith("-s", StringComparison.OrdinalIgnoreCase))
                return text[..^2].TrimEnd();
            return text;
        }

        private static bool IsWaitingCounterpartyConfirm(string text)
        {
            return !string.IsNullOrWhiteSpace(text)
                && (text.Contains("待对方确认报价", StringComparison.Ordinal)
                    || text.Contains("对方确认报价", StringComparison.Ordinal));
        }
    }
}
