using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using static CS2TradeMonitor.Application.YouPin.YouPinJsonElementReader;

namespace CS2TradeMonitor.Application.YouPin
{
    internal static class YouPinSaleOrderGroupHelper
    {
        public static List<string> ResolveActionOrderNos(YouPinSaleOrder order)
        {
            var orderNos = NormalizeOrderNoList(order.OrderNos);
            if (orderNos.Count == 0 && !string.IsNullOrWhiteSpace(order.OrderNo))
                orderNos.Add(order.OrderNo.Trim());
            return orderNos;
        }

        public static List<string> NormalizeOrderNoList(IEnumerable<string?>? orderNos)
        {
            if (orderNos == null)
                return new List<string>();

            return orderNos
                .Select(x => x?.Trim() ?? "")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static YouPinSaleOrder NormalizeOrderGroupFields(YouPinSaleOrder order)
        {
            order.OrderNos = NormalizeOrderNoList(order.OrderNos);
            order.OrderItems ??= new List<YouPinSaleOrderItem>();
            if (order.IsOrderGroup || !string.IsNullOrWhiteSpace(order.OrderGroupId) || order.OrderNos.Count > 1)
            {
                order.IsOrderGroup = true;
                if (order.OrderNos.Count == 0 && !string.IsNullOrWhiteSpace(order.OrderNo))
                    order.OrderNos.Add(order.OrderNo.Trim());
                if (string.IsNullOrWhiteSpace(order.OrderGroupId))
                    order.OrderGroupId = BuildOrderGroupIdFromOrderNos(order.OrderNos);
                if (order.OrderItems.Count == 0)
                    order.OrderItems.Add(CreateOrderItem(order));
            }

            return order;
        }

        public static List<YouPinSaleOrder> GroupWaitDeliverOrders(List<YouPinSaleOrder> orders)
        {
            if (orders.Count == 0)
                return orders;

            var result = new List<YouPinSaleOrder>();
            foreach (var group in orders.GroupBy(GetOrderGroupKey, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(group.Key))
                {
                    result.AddRange(group);
                    continue;
                }

                var members = group.ToList();
                bool shouldGroup = members.Count > 1 || members.Any(x => (x.OrderNos?.Count ?? 0) > 1);
                if (!shouldGroup)
                {
                    result.AddRange(members);
                    continue;
                }

                result.Add(BuildOrderGroupWaitDeliverOrder(members, group.Key));
            }

            return result
                .OrderByDescending(x => x.DetectedAt)
                .ThenBy(x => x.OrderNo, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<YouPinSaleOrder> MergeQuoteDisplayOrders(
            IEnumerable<YouPinSaleOrder>? waitDeliverOrders,
            IEnumerable<YouPinSaleOrder>? pendingBuyOrders)
        {
            var result = new List<YouPinSaleOrder>();
            var knownOrderNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (YouPinSaleOrder order in (waitDeliverOrders ?? Array.Empty<YouPinSaleOrder>())
                         .Concat(pendingBuyOrders ?? Array.Empty<YouPinSaleOrder>()))
            {
                if (order == null)
                    continue;
                if (!YouPinSaleOrderActionResolver.IsPendingBuyQuote(order)
                    && !YouPinSaleOrderActionResolver.Resolve(order).CanRun)
                    continue;

                List<string> orderNos = ResolveActionOrderNos(order);
                if (orderNos.Count == 0 || orderNos.Any(knownOrderNos.Contains))
                    continue;

                result.Add(order);
                foreach (string orderNo in orderNos)
                    knownOrderNos.Add(orderNo);
            }

            return GroupWaitDeliverOrders(result);
        }

        public static string GetCredentialAccountKey(YouPinCredential? credential)
        {
            if (credential == null)
                return "";

            return FirstText(
                NormalizeAccountPart("uid", credential.UserId),
                NormalizeAccountPart("uk", credential.Uk),
                NormalizeAccountPart("nick", credential.NickName));
        }

        public static string BuildScopedOrderId(string accountKey, string orderNo)
        {
            return (accountKey ?? "").Trim() + "|" + (orderNo ?? "").Trim();
        }

        public static bool IsCurrentAccountOrder(YouPinSaleOrder? order, string accountKey)
        {
            if (order == null || string.IsNullOrWhiteSpace(accountKey))
                return false;

            return string.Equals(order.AccountKey, accountKey, StringComparison.OrdinalIgnoreCase);
        }

        public static string ExtractOrderGroupId(JsonElement element, IReadOnlyList<string> orderNos)
        {
            string explicitId = FindFirstStringByNames(
                element,
                "mergeId", "MergeId",
                "mergeNo", "MergeNo",
                "mergeOrderNo", "MergeOrderNo",
                "mergeOrderId", "MergeOrderId",
                "mergeTaskId", "MergeTaskId",
                "mergeGroupId", "MergeGroupId",
                "parentOrderNo", "ParentOrderNo",
                "parentOrderId", "ParentOrderId",
                "batchOrderNo", "BatchOrderNo",
                "batchNo", "BatchNo",
                "batchId", "BatchId",
                "deliveryBatchNo", "DeliveryBatchNo",
                "sendOrderNo", "SendOrderNo");
            return FirstText(explicitId, BuildOrderGroupIdFromOrderNos(orderNos));
        }

        public static bool IsOrderGroup(JsonElement element)
        {
            return FindOrderGroupFlag(
                element,
                "isMerge", "IsMerge",
                "isMerged", "IsMerged",
                "merge", "Merge",
                "mergeFlag", "MergeFlag",
                "isBatch", "IsBatch",
                "batch", "Batch",
                "batchDelivery", "BatchDelivery");
        }

        public static List<string> ExtractOrderNoList(JsonElement element)
        {
            var values = new List<string>();
            ExtractOrderNoListCore(element, values, depth: 0);
            return NormalizeOrderNoList(values);
        }

        internal static string BuildOrderGroupIdFromOrderNos(IEnumerable<string?> orderNos)
        {
            var list = NormalizeOrderNoList(orderNos)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return list.Count > 1 ? "orders:" + string.Join(",", list) : "";
        }

        private static string GetOrderGroupKey(YouPinSaleOrder order)
        {
            var orderNos = NormalizeOrderNoList(order.OrderNos);
            if (!order.IsOrderGroup && string.IsNullOrWhiteSpace(order.OrderGroupId) && orderNos.Count <= 1)
                return "";

            return FirstText(order.OrderGroupId, BuildOrderGroupIdFromOrderNos(orderNos));
        }

        private static YouPinSaleOrder BuildOrderGroupWaitDeliverOrder(List<YouPinSaleOrder> members, string orderGroupId)
        {
            var first = members
                .OrderByDescending(x => x.DetectedAt)
                .First();
            var items = members
                .Select(CreateOrderItem)
                .GroupBy(x => x.OrderNo, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();
            var rawOrderNos = new List<string?>();
            foreach (var member in members)
            {
                if (member.OrderNos != null && member.OrderNos.Count > 0)
                    rawOrderNos.AddRange(member.OrderNos);
                else
                    rawOrderNos.Add(member.OrderNo);
            }

            var orderNos = NormalizeOrderNoList(rawOrderNos);
            string names = string.Join("、", items.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)).Take(3));
            double total = items.Sum(x => Math.Max(0, x.Price));
            string summaryName = string.IsNullOrWhiteSpace(names)
                ? "悠悠有品订单"
                : names;

            return new YouPinSaleOrder
            {
                AccountKey = first.AccountKey,
                OrderNo = FirstText(first.OrderNo, orderNos.FirstOrDefault()),
                Name = summaryName,
                Message = FirstText(first.Message, "有买家购买，待发送报价"),
                Price = total,
                DetectedAt = members.Max(x => x.DetectedAt),
                Source = FirstText(first.Source, "待发货"),
                ImageUrl = FirstText(members.Select(x => x.ImageUrl).ToArray()),
                TradeOfferId = FirstText(members.Select(x => x.TradeOfferId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()),
                OrderType = first.OrderType,
                OrderStatus = first.OrderStatus,
                OrderSubStatus = first.OrderSubStatus,
                RealOrderSubStatus = first.RealOrderSubStatus,
                OrderStatusDesc = first.OrderStatusDesc,
                LeaseType = first.LeaseType,
                OfferId = first.OfferId,
                SteamPersonaName = first.SteamPersonaName,
                SteamAvatarUrl = first.SteamAvatarUrl,
                SteamPlayerLevel = first.SteamPlayerLevel,
                SteamGameTime = first.SteamGameTime,
                SteamJoinDate = first.SteamJoinDate,
                SteamCounterpartyStatus = first.SteamCounterpartyStatus,
                IsOrderGroup = true,
                OrderGroupId = orderGroupId,
                OrderNos = orderNos,
                OrderItems = items
            };
        }

        private static YouPinSaleOrderItem CreateOrderItem(YouPinSaleOrder order)
        {
            return new YouPinSaleOrderItem
            {
                OrderNo = order.OrderNo,
                Name = order.Name,
                Price = order.Price,
                ImageUrl = order.ImageUrl,
                TradeOfferId = order.TradeOfferId
            };
        }

        private static string NormalizeAccountPart(string prefix, string value)
        {
            string text = value?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(text))
                return "";

            return prefix + ":" + text;
        }

        private static void ExtractOrderNoListCore(JsonElement element, List<string> values, int depth)
        {
            if (depth > 6)
                return;

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (LooksLikeOrderNoListField(property.Name))
                        ExtractOrderNoValues(property.Value, values, depth + 1);
                    else if (property.Value.ValueKind == JsonValueKind.Object || property.Value.ValueKind == JsonValueKind.Array)
                        ExtractOrderNoListCore(property.Value, values, depth + 1);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray())
                    ExtractOrderNoListCore(child, values, depth + 1);
            }
        }

        private static void ExtractOrderNoValues(JsonElement element, List<string> values, int depth)
        {
            if (depth > 7)
                return;

            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                case JsonValueKind.Number:
                    values.Add(element.ToString());
                    break;
                case JsonValueKind.Object:
                    values.Add(FirstText(
                        GetString(element, "orderNo", "OrderNo", "orderId", "OrderId", "businessNo", "BusinessNo"),
                        GetString(element, "sendOrderNo", "SendOrderNo")));
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.Object || property.Value.ValueKind == JsonValueKind.Array)
                            ExtractOrderNoValues(property.Value, values, depth + 1);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var child in element.EnumerateArray())
                        ExtractOrderNoValues(child, values, depth + 1);
                    break;
            }
        }

        private static bool LooksLikeOrderNoListField(string name)
        {
            return name.Equals("sendOrderNoList", StringComparison.OrdinalIgnoreCase)
                || name.Equals("sendOrderNos", StringComparison.OrdinalIgnoreCase)
                || name.Equals("orderNoList", StringComparison.OrdinalIgnoreCase)
                || name.Equals("orderNos", StringComparison.OrdinalIgnoreCase)
                || name.Equals("orderIdList", StringComparison.OrdinalIgnoreCase)
                || name.Equals("orderIds", StringComparison.OrdinalIgnoreCase)
                || name.Equals("mergeOrderNoList", StringComparison.OrdinalIgnoreCase)
                || name.Equals("sellOrderNoList", StringComparison.OrdinalIgnoreCase)
                || name.Equals("businessNoList", StringComparison.OrdinalIgnoreCase);
        }

        private static string FindFirstStringByNames(JsonElement element, params string[] names)
        {
            return FindFirstStringByNames(element, names, depth: 0);
        }

        private static string FindFirstStringByNames(JsonElement element, string[] names, int depth)
        {
            if (depth > 6)
                return "";

            if (element.ValueKind == JsonValueKind.Object)
            {
                if (TryGetProperty(element, out var value, names))
                {
                    string text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
                    if (!string.IsNullOrWhiteSpace(text) && !text.Equals("0", StringComparison.OrdinalIgnoreCase) && !text.Equals("false", StringComparison.OrdinalIgnoreCase))
                        return text.Trim();
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.Object && property.Value.ValueKind != JsonValueKind.Array)
                        continue;

                    string nested = FindFirstStringByNames(property.Value, names, depth + 1);
                    if (!string.IsNullOrWhiteSpace(nested))
                        return nested;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray())
                {
                    string nested = FindFirstStringByNames(child, names, depth + 1);
                    if (!string.IsNullOrWhiteSpace(nested))
                        return nested;
                }
            }

            return "";
        }

        private static bool FindOrderGroupFlag(JsonElement element, params string[] names)
        {
            return FindOrderGroupFlag(element, names, depth: 0);
        }

        private static bool FindOrderGroupFlag(JsonElement element, string[] names, int depth)
        {
            if (depth > 6)
                return false;

            if (element.ValueKind == JsonValueKind.Object)
            {
                if (TryGetProperty(element, out var value, names) && IsTruthyJsonValue(value))
                    return true;

                foreach (var property in element.EnumerateObject())
                {
                    if ((property.Value.ValueKind == JsonValueKind.Object || property.Value.ValueKind == JsonValueKind.Array)
                        && FindOrderGroupFlag(property.Value, names, depth + 1))
                        return true;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray())
                {
                    if (FindOrderGroupFlag(child, names, depth + 1))
                        return true;
                }
            }

            return false;
        }

        private static bool IsTruthyJsonValue(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.Number => value.TryGetInt32(out int number) && number != 0,
                JsonValueKind.String => IsTruthyText(value.GetString() ?? ""),
                _ => false
            };
        }

        private static bool IsTruthyText(string text)
        {
            text = (text ?? "").Trim();
            return text.Equals("true", StringComparison.OrdinalIgnoreCase)
                || text.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || text.Equals("1", StringComparison.OrdinalIgnoreCase)
                || text.Equals("Y", StringComparison.OrdinalIgnoreCase)
                || text.Equals("是", StringComparison.Ordinal);
        }
    }
}
