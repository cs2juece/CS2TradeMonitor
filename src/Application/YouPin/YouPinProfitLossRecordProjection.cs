using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace CS2TradeMonitor.Application.YouPin
{
    internal enum YouPinOrderEndpointKind
    {
        Generic,
        SaleBuyList,
        SaleSellList
    }

    internal static class YouPinProfitLossRecordProjection
    {
        public static List<YouPinProfitLossRow> BuildRows(List<YouPinProfitLossRecord> records)
        {
            return records
                .Where(x => x.Amount > 0)
                .GroupBy(BuildAggregateKey, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var items = group.ToList();
                    var first = items.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Name)) ?? items[0];
                    int buyCount = items.Where(x => x.Direction == YouPinProfitLossDirection.Buy).Sum(x => Math.Max(1, x.Quantity));
                    int sellCount = items.Where(x => x.Direction == YouPinProfitLossDirection.Sell).Sum(x => Math.Max(1, x.Quantity));
                    double buyAmount = items.Where(x => x.Direction == YouPinProfitLossDirection.Buy).Sum(x => x.Amount);
                    double sellAmount = items.Where(x => x.Direction == YouPinProfitLossDirection.Sell).Sum(x => x.Amount);
                    double pnl = sellAmount - buyAmount;
                    return new YouPinProfitLossRow
                    {
                        Key = group.Key,
                        Name = string.IsNullOrWhiteSpace(first.Name) ? group.Key : first.Name,
                        TemplateId = first.TemplateId,
                        BuyCount = buyCount,
                        SellCount = sellCount,
                        BuyAmount = buyAmount,
                        SellAmount = sellAmount,
                        NetProfit = pnl,
                        NetRate = buyAmount > 0 ? pnl / buyAmount * 100.0 : 0,
                        LastTradeTime = items
                            .Where(x => x.Time != DateTime.MinValue)
                            .Select(x => x.Time)
                            .DefaultIfEmpty(DateTime.MinValue)
                            .Max()
                    };
                })
                .OrderByDescending(x => Math.Abs(x.NetProfit))
                .ThenBy(x => x.Name)
                .ToList();
        }

        public static List<YouPinProfitLossRecord> ParseRecords(
            JsonElement item,
            YouPinProfitLossDirection direction,
            YouPinOrderEndpointKind endpointKind)
        {
            TryGetProperty(item, out var orderInfo, "orderInfoVO", "OrderInfoVO", "orderInfo", "OrderInfo", "order", "Order");
            if (!ShouldIncludeOrder(item, orderInfo, endpointKind))
                return new List<YouPinProfitLossRecord>();

            bool moneyInCents = endpointKind is YouPinOrderEndpointKind.SaleBuyList or YouPinOrderEndpointKind.SaleSellList;
            var details = ExtractProductDetails(item);
            if (details.Count == 0)
            {
                var fallback = ParseRecord(item, default, direction, moneyInCents, 0, 0);
                return fallback == null ? new List<YouPinProfitLossRecord>() : new List<YouPinProfitLossRecord> { fallback };
            }

            double orderTotalRaw = FirstPositive(
                GetDouble(item, "paymentAmount", "PaymentAmount", "totalAmount", "TotalAmount", "commodityAmount", "CommodityAmount", "price", "Price"));
            var detailRawAmounts = details
                .Select(detail => FirstPositive(
                    GetDouble(detail, "paymentAmount", "PaymentAmount", "totalAmount", "TotalAmount", "commodityAmount", "CommodityAmount", "price", "Price")))
                .ToList();
            double detailRawTotal = detailRawAmounts.Sum();
            bool detailAmountsMatchTotal = orderTotalRaw <= 0
                || detailRawTotal <= 0
                || Math.Abs(detailRawTotal - orderTotalRaw) <= Math.Max(1.0, orderTotalRaw * 0.05);

            var records = new List<YouPinProfitLossRecord>();
            for (int i = 0; i < details.Count; i++)
            {
                double allocatedRawAmount = 0;
                if (!detailAmountsMatchTotal && orderTotalRaw > 0)
                {
                    if (detailRawTotal > 0)
                        allocatedRawAmount = orderTotalRaw * detailRawAmounts[i] / detailRawTotal;
                    else
                        allocatedRawAmount = orderTotalRaw / details.Count;
                }

                var record = ParseRecord(item, details[i], direction, moneyInCents, i, allocatedRawAmount);
                if (record != null)
                    records.Add(record);
            }

            return records;
        }

        public static List<JsonElement> ExtractOrderArray(JsonElement root)
        {
            var elements = new List<JsonElement>();
            if (!TryGetProperty(root, out var data, "data", "Data"))
                return elements;

            AddArrays(data, elements, 0);
            return elements;
        }

        public static string BuildRecordDedupeKey(YouPinProfitLossRecord record)
        {
            if (!string.IsNullOrWhiteSpace(record.OrderNo))
            {
                string detail = FirstText(record.DetailNo, record.AssetId, record.CommodityId, record.TemplateId, record.Name);
                return $"{record.Direction}:O:{record.OrderNo.Trim()}:{detail.Trim()}:{record.Amount:0.####}";
            }

            return $"{record.Direction}:{record.Name}:{record.Amount:0.####}:{record.Time:yyyyMMddHHmmss}";
        }

        public static string BuildEstimatedMatchKey(YouPinProfitLossRecord record)
        {
            string template = NormalizeIdentityPart(record.TemplateId);
            string name = NormalizeIdentityPart(FirstText(record.CommodityHashName, record.Name));
            string abrade = NormalizeAbrade(record.Abrade);

            // 悠悠买卖记录不稳定提供同一件实物的全链路唯一标识，只能按模板、名称和磨损做饰品记录级估算匹配。
            if (!string.IsNullOrWhiteSpace(template) && !string.IsNullOrWhiteSpace(name))
                return JoinText("|", "T:" + template, "N:" + name, string.IsNullOrWhiteSpace(abrade) ? "" : "W:" + abrade);
            if (!string.IsNullOrWhiteSpace(name))
                return JoinText("|", "N:" + name, string.IsNullOrWhiteSpace(abrade) ? "" : "W:" + abrade);

            return "";
        }

        private static List<JsonElement> ExtractProductDetails(JsonElement item)
        {
            var details = new List<JsonElement>();
            foreach (string name in new[] { "productDetailList", "ProductDetailList", "commodityList", "CommodityList", "items", "Items" })
            {
                if (TryGetProperty(item, out var array, name) && array.ValueKind == JsonValueKind.Array)
                {
                    details.AddRange(array.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object));
                    if (details.Count > 0)
                        return details;
                }
            }

            foreach (string name in new[] { "productDetail", "ProductDetail", "commodityInfoVO", "CommodityInfoVO", "commodityInfo", "CommodityInfo" })
            {
                if (TryGetProperty(item, out var detail, name) && detail.ValueKind == JsonValueKind.Object)
                {
                    details.Add(detail);
                    return details;
                }
            }

            return details;
        }

        private static YouPinProfitLossRecord? ParseRecord(
            JsonElement item,
            JsonElement detail,
            YouPinProfitLossDirection direction,
            bool moneyInCents,
            int detailIndex,
            double allocatedRawAmount)
        {
            TryGetProperty(item, out var commodityInfo, "commodityInfoVO", "CommodityInfoVO", "commodityInfo", "CommodityInfo", "goodsInfo", "GoodsInfo", "templateInfo", "TemplateInfo");
            TryGetProperty(item, out var orderInfo, "orderInfoVO", "OrderInfoVO", "orderInfo", "OrderInfo", "order", "Order");

            string statusText = JoinText(" ",
                GetString(item, "statusName", "StatusName", "orderStatusName", "OrderStatusName", "orderSubStatusName", "OrderSubStatusName", "message", "Message"),
                GetString(orderInfo, "statusName", "StatusName", "orderStatusName", "OrderStatusName", "orderSubStatusName", "OrderSubStatusName"),
                GetString(detail, "statusName", "StatusName", "orderStatusName", "OrderStatusName", "orderSubStatusName", "OrderSubStatusName", "splitStatusMsg", "SplitStatusMsg"));
            if (LooksCanceledOrOpen(statusText))
                return null;

            string name = FirstText(
                GetString(detail, "commodityName", "CommodityName", "templateName", "TemplateName", "itemName", "ItemName", "goodsName", "GoodsName", "marketHashName", "MarketHashName", "commodityHashName", "CommodityHashName", "templateHashName", "TemplateHashName", "name", "Name"),
                GetString(item, "commodityName", "CommodityName", "itemName", "ItemName", "goodsName", "GoodsName", "marketHashName", "MarketHashName", "name", "Name"),
                GetString(commodityInfo, "commodityName", "CommodityName", "itemName", "ItemName", "goodsName", "GoodsName", "marketHashName", "MarketHashName", "name", "Name"));
            string orderNo = FirstText(
                GetString(item, "orderNo", "OrderNo", "orderId", "OrderId", "id", "Id", "orderSn", "OrderSn", "businessNo", "BusinessNo"),
                GetString(orderInfo, "orderNo", "OrderNo", "orderId", "OrderId", "id", "Id", "orderSn", "OrderSn", "businessNo", "BusinessNo"));
            string detailNo = FirstText(
                GetString(detail, "orderDetailNo", "OrderDetailNo", "detailNo", "DetailNo", "id", "Id", "commodityId", "CommodityId"),
                detailIndex.ToString(CultureInfo.InvariantCulture));
            string commodityId = FirstText(
                GetString(detail, "commodityId", "CommodityId"),
                GetString(item, "commodityId", "CommodityId"),
                GetString(commodityInfo, "commodityId", "CommodityId", "id", "Id"));
            string commodityHashName = FirstText(
                GetString(detail, "commodityHashName", "CommodityHashName", "marketHashName", "MarketHashName", "templateHashName", "TemplateHashName"),
                GetString(item, "commodityHashName", "CommodityHashName", "marketHashName", "MarketHashName", "templateHashName", "TemplateHashName"),
                GetString(commodityInfo, "commodityHashName", "CommodityHashName", "marketHashName", "MarketHashName", "templateHashName", "TemplateHashName"));
            string templateId = FirstText(
                GetString(detail, "templateId", "TemplateId", "commodityTemplateId", "CommodityTemplateId", "templateHashName", "TemplateHashName"),
                GetString(item, "templateId", "TemplateId", "commodityTemplateId", "CommodityTemplateId"),
                GetString(commodityInfo, "templateId", "TemplateId", "commodityTemplateId", "CommodityTemplateId", "id", "Id"));
            string abrade = FirstText(
                GetString(detail, "abrade", "Abrade", "wear", "Wear", "floatValue", "FloatValue"),
                GetString(item, "abrade", "Abrade", "wear", "Wear", "floatValue", "FloatValue"),
                GetString(commodityInfo, "abrade", "Abrade", "wear", "Wear", "floatValue", "FloatValue"));
            string assetId = FirstText(
                GetString(detail, "assetId", "AssetId", "assertId", "AssertId", "steamAssetId", "SteamAssetId", "inventoryId", "InventoryId"),
                GetString(item, "assetId", "AssetId", "assertId", "AssertId", "steamAssetId", "SteamAssetId", "inventoryId", "InventoryId"),
                GetString(orderInfo, "assetId", "AssetId", "steamAssetId", "SteamAssetId", "inventoryId", "InventoryId"));
            double rawAmount = allocatedRawAmount > 0 ? allocatedRawAmount : FirstPositive(
                GetDouble(detail, "paymentAmount", "PaymentAmount", "totalAmount", "TotalAmount", "commodityAmount", "CommodityAmount", "payAmount", "PayAmount", "sellAmount", "SellAmount", "incomeAmount", "IncomeAmount", "actualAmount", "ActualAmount", "amount", "Amount", "price", "Price", "orderPrice", "OrderPrice"),
                GetDouble(item, "payAmount", "PayAmount", "sellAmount", "SellAmount", "incomeAmount", "IncomeAmount", "actualAmount", "ActualAmount", "amount", "Amount", "price", "Price", "orderPrice", "OrderPrice"),
                GetDouble(orderInfo, "payAmount", "PayAmount", "sellAmount", "SellAmount", "incomeAmount", "IncomeAmount", "actualAmount", "ActualAmount", "amount", "Amount", "price", "Price", "orderPrice", "OrderPrice"),
                GetDouble(commodityInfo, "payAmount", "PayAmount", "sellAmount", "SellAmount", "price", "Price", "purchasePrice", "PurchasePrice"));
            double amount = NormalizeMoney(rawAmount, moneyInCents);
            int quantity = Math.Max(1, FirstPositiveInt(
                GetInt(detail, "count", "Count", "quantity", "Quantity", "num", "Num", "assetMergeCount", "AssetMergeCount"),
                GetInt(item, "count", "Count", "quantity", "Quantity", "assetMergeCount", "AssetMergeCount"),
                GetInt(orderInfo, "count", "Count", "quantity", "Quantity", "assetMergeCount", "AssetMergeCount"),
                GetInt(commodityInfo, "count", "Count", "quantity", "Quantity", "assetMergeCount", "AssetMergeCount")));
            DateTime time = FirstDate(
                GetString(detail, "finishOrderTime", "FinishOrderTime", "sendOfferSuccessTime", "SendOfferSuccessTime", "paySuccessTime", "PaySuccessTime", "createOrderTime", "CreateOrderTime", "createTime", "CreateTime", "createdAt", "CreatedAt", "finishTime", "FinishTime", "completeTime", "CompleteTime", "time", "Time"),
                GetString(item, "createTime", "CreateTime", "createdAt", "CreatedAt", "finishTime", "FinishTime", "completeTime", "CompleteTime", "time", "Time"),
                GetString(orderInfo, "createTime", "CreateTime", "createdAt", "CreatedAt", "finishTime", "FinishTime", "completeTime", "CompleteTime", "time", "Time"));

            if (string.IsNullOrWhiteSpace(orderNo))
                orderNo = $"{direction}:{templateId}:{assetId}:{name}:{amount.ToString("0.##", CultureInfo.InvariantCulture)}:{time:yyyyMMddHHmmss}:{detailIndex}";

            if (string.IsNullOrWhiteSpace(name) || amount <= 0)
                return null;

            return new YouPinProfitLossRecord
            {
                Direction = direction,
                OrderNo = orderNo,
                DetailNo = detailNo,
                Name = name,
                TemplateId = templateId,
                CommodityId = commodityId,
                CommodityHashName = commodityHashName,
                Abrade = abrade,
                AssetId = assetId,
                Amount = amount,
                Quantity = quantity,
                Time = time == DateTime.MinValue ? DateTime.Now : time,
                Status = statusText
            };
        }

        private static bool ShouldIncludeOrder(JsonElement item, JsonElement orderInfo, YouPinOrderEndpointKind endpointKind)
        {
            string statusText = JoinText(" ",
                GetString(item, "statusName", "StatusName", "orderStatusName", "OrderStatusName", "orderSubStatusName", "OrderSubStatusName", "cancelReason", "CancelReason", "message", "Message"),
                GetString(orderInfo, "statusName", "StatusName", "orderStatusName", "OrderStatusName", "orderSubStatusName", "OrderSubStatusName", "cancelReason", "CancelReason"));
            if (LooksCanceledOrOpen(statusText))
                return false;

            if (endpointKind is YouPinOrderEndpointKind.SaleBuyList or YouPinOrderEndpointKind.SaleSellList)
            {
                int orderStatus = FirstPositiveInt(
                    GetInt(item, "orderStatus", "OrderStatus"),
                    GetInt(orderInfo, "orderStatus", "OrderStatus"));
                string orderStatusName = JoinText(" ",
                    GetString(item, "orderStatusName", "OrderStatusName"),
                    GetString(orderInfo, "orderStatusName", "OrderStatusName"));

                // 吃米/亏米只统计已完成成交。抓包里同一列表请求可能返回取消历史，必须用响应字段二次收口。
                return orderStatus == 340
                    && orderStatusName.Contains("已完成", StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        private static void AddArrays(JsonElement element, List<JsonElement> target, int depth)
        {
            if (depth > 4) return;
            if (element.ValueKind == JsonValueKind.Array)
            {
                target.AddRange(element.EnumerateArray());
                return;
            }

            if (element.ValueKind != JsonValueKind.Object)
                return;

            foreach (var name in new[] { "list", "List", "items", "Items", "orderList", "OrderList", "records", "Records", "rows", "Rows", "dataList", "DataList" })
            {
                if (element.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array)
                {
                    target.AddRange(array.EnumerateArray());
                    return;
                }
            }

            foreach (var property in element.EnumerateObject())
                AddArrays(property.Value, target, depth + 1);
        }

        private static string BuildAggregateKey(YouPinProfitLossRecord record)
        {
            string matchKey = BuildEstimatedMatchKey(record);
            if (!string.IsNullOrWhiteSpace(matchKey))
                return matchKey;
            if (!string.IsNullOrWhiteSpace(record.Name))
                return "N:" + record.Name.Trim();
            return "O:" + record.OrderNo.Trim();
        }

        private static bool LooksCanceledOrOpen(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string[] blocked =
            {
                "取消", "关闭", "失败", "退款", "超时", "待支付", "支付中", "待发货", "待报价",
                "待您发货", "待您发送报价", "待处理", "处理中", "申诉", "异常"
            };
            return blocked.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        private static double NormalizeMoney(double value, bool cents)
        {
            if (value <= 0)
                return 0;

            return cents ? Math.Round(value / 100.0, 2, MidpointRounding.AwayFromZero) : value;
        }

        private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
        {
            foreach (string name in names)
            {
                if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
                    return true;
            }

            value = default;
            return false;
        }

        private static string? GetString(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out var prop, names)) return null;
            return prop.ValueKind switch
            {
                JsonValueKind.String => prop.GetString(),
                JsonValueKind.Number => prop.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => prop.ToString()
            };
        }

        private static int GetInt(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out var prop, names)) return 0;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int value)) return value;
            if (int.TryParse(prop.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return value;
            return 0;
        }

        private static double GetDouble(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out var prop, names)) return 0;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out double num)) return num;

            string text = prop.ToString()
                .Replace("¥", "", StringComparison.Ordinal)
                .Replace("￥", "", StringComparison.Ordinal)
                .Replace(",", "", StringComparison.Ordinal)
                .Trim();
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double value)) return value;
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value)) return value;
            return 0;
        }

        private static string FirstText(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        private static string JoinText(string separator, params string?[] values)
        {
            return string.Join(separator, values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
        }

        private static string NormalizeIdentityPart(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? ""
                : value.Trim().ToUpperInvariant();
        }

        private static string NormalizeAbrade(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string text = value.Trim();
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double numeric))
                return numeric.ToString("0.0000", CultureInfo.InvariantCulture);
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out numeric))
                return numeric.ToString("0.0000", CultureInfo.InvariantCulture);
            return text.ToUpperInvariant();
        }

        private static double FirstPositive(params double[] values)
        {
            foreach (double value in values)
            {
                if (value > 0)
                    return value;
            }

            return 0;
        }

        private static int FirstPositiveInt(params int[] values)
        {
            foreach (int value in values)
            {
                if (value > 0)
                    return value;
            }

            return 1;
        }

        private static DateTime FirstDate(params string?[] values)
        {
            foreach (string? value in values)
            {
                DateTime parsed = ParseDate(value);
                if (parsed != DateTime.MinValue)
                    return parsed;
            }

            return DateTime.MinValue;
        }

        private static DateTime ParseDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return DateTime.MinValue;

            string text = value.Trim();
            if (long.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out long numeric))
            {
                try
                {
                    if (numeric > 1000000000000)
                        return DateTimeOffset.FromUnixTimeMilliseconds(numeric).LocalDateTime;
                    if (numeric > 1000000000)
                        return DateTimeOffset.FromUnixTimeSeconds(numeric).LocalDateTime;
                }
                catch
                {
                    return DateTime.MinValue;
                }
            }

            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out DateTime parsed))
                return parsed;
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed))
                return parsed;
            return DateTime.MinValue;
        }
    }
}
