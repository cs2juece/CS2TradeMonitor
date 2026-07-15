using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace CS2TradeMonitor.Application.YouPin
{
    internal static class YouPinInventoryComputationHelper
    {
        public static void MergeMissingPrices(List<YouPinInventoryItem> target, List<YouPinInventoryItem> priceSource)
        {
            if (target.Count == 0 || priceSource.Count == 0) return;

            var sourceByKey = priceSource
                .SelectMany(item => InventoryKeys(item).Select(key => new { key, item }))
                .GroupBy(x => x.key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().item, StringComparer.OrdinalIgnoreCase);

            foreach (var item in target.Where(x => x.Price <= 0))
            {
                foreach (var key in InventoryKeys(item))
                {
                    if (sourceByKey.TryGetValue(key, out var priced) && priced.Price > 0)
                    {
                        item.Price = priced.Price;
                        break;
                    }
                }
            }
        }

        public static void ApplyYouPinHeaders(HttpRequestMessage req, string token, string device, string uk)
        {
            YouPinMobileApiClient.ApplyHeaders(req, token, device, uk);
        }

        public static List<YouPinInventoryChange> CompareSnapshots(YouPinInventorySnapshot previous, YouPinInventorySnapshot current)
        {
            string Key(YouPinInventoryItem item) => !string.IsNullOrWhiteSpace(item.AssetId)
                ? item.AssetId
                : item.Name;

            var changes = new List<YouPinInventoryChange>();
            var prev = previous.Items.ToDictionary(Key, StringComparer.OrdinalIgnoreCase);
            var now = current.Items.ToDictionary(Key, StringComparer.OrdinalIgnoreCase);

            foreach (var item in now.Values)
            {
                if (!prev.TryGetValue(Key(item), out var old))
                {
                    changes.Add(new YouPinInventoryChange
                    {
                        Time = current.Time,
                        Type = "新增",
                        Name = item.Name,
                        AssetId = item.AssetId,
                        OldPrice = 0,
                        NewPrice = item.Price,
                        Delta = item.Price
                    });
                    continue;
                }

                if (Math.Abs(item.Price - old.Price) >= 0.01)
                {
                    changes.Add(new YouPinInventoryChange
                    {
                        Time = current.Time,
                        Type = "估值变化",
                        Name = item.Name,
                        AssetId = item.AssetId,
                        OldPrice = old.Price,
                        NewPrice = item.Price,
                        Delta = item.Price - old.Price
                    });
                }
            }

            foreach (var item in prev.Values)
            {
                if (now.ContainsKey(Key(item))) continue;
                changes.Add(new YouPinInventoryChange
                {
                    Time = current.Time,
                    Type = "移出",
                    Name = item.Name,
                    AssetId = item.AssetId,
                    OldPrice = item.Price,
                    NewPrice = 0,
                    Delta = -item.Price
                });
            }

            return changes;
        }

        public static List<YouPinInventoryTrendRow> BuildTrendRows(YouPinInventorySnapshot current, YouPinInventorySnapshot? previous)
        {
            var previousMap = (previous?.Items ?? new List<YouPinInventoryItem>())
                .GroupBy(TrendKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, AggregateTrendGroup, StringComparer.OrdinalIgnoreCase);

            var rows = new List<YouPinInventoryTrendRow>();
            foreach (var group in current.Items.GroupBy(TrendKey, StringComparer.OrdinalIgnoreCase))
            {
                var now = AggregateTrendGroup(group);
                previousMap.TryGetValue(group.Key, out var old);

                double delta = now.CurrentPrice > 0 && old?.CurrentPrice > 0
                    ? now.CurrentPrice - old.CurrentPrice
                    : 0;
                double percent = old?.CurrentPrice > 0 && now.CurrentPrice > 0
                    ? delta / old.CurrentPrice * 100.0
                    : 0;

                now.PreviousPrice = old?.CurrentPrice ?? 0;
                now.Delta = delta;
                now.Percent = percent;
                rows.Add(now);
            }

            return rows
                .OrderByDescending(x => x.CurrentPrice)
                .ThenBy(x => x.Name)
                .ToList();
        }

        public static YouPinInventoryTrendState CloneTrendState(YouPinInventoryTrendState state)
        {
            return new YouPinInventoryTrendState
            {
                LastStatus = state.LastStatus,
                LastError = state.LastError,
                LastFetch = state.LastFetch,
                TotalCount = state.TotalCount,
                TotalValue = state.TotalValue,
                TotalDelta = state.TotalDelta,
                TotalDeltaPercent = state.TotalDeltaPercent,
                PurchaseValue = state.PurchaseValue,
                MissingPurchaseCount = state.MissingPurchaseCount,
                HasOfficialProfitAndLoss = state.HasOfficialProfitAndLoss,
                Rows = state.Rows.Select(x => new YouPinInventoryTrendRow
                {
                    Name = x.Name,
                    TemplateId = x.TemplateId,
                    Quantity = x.Quantity,
                    CurrentPrice = x.CurrentPrice,
                    PreviousPrice = x.PreviousPrice,
                    Delta = x.Delta,
                    Percent = x.Percent,
                    PurchasePrice = x.PurchasePrice,
                    MissingEstimateCount = x.MissingEstimateCount,
                    MissingPurchaseCount = x.MissingPurchaseCount
                }).ToList()
            };
        }

        public static YouPinInventoryTrendRow ParseTrendApiRow(JsonElement item)
        {
            string name = FirstText(
                GetString(item, "commodityName", "CommodityName"),
                GetString(item, "templateHashName", "TemplateHashName"),
                GetString(item, "marketHashName", "MarketHashName"),
                "未命名饰品");
            int quantity = GetInt(item, "ownInventoryCount", "OwnInventoryCount")
                + GetInt(item, "rentedInventoryCount", "RentedInventoryCount");
            if (quantity <= 0)
                quantity = GetInt(item, "assetMergeCount", "AssetMergeCount");
            if (quantity <= 0)
                quantity = 1;

            double currentPrice = GetDouble(item, "marketPrice", "MarketPrice", "markPrice", "MarkPrice");
            double purchasePrice = GetDouble(item, "assetBuyPrice", "AssetBuyPrice", "buyPrice", "BuyPrice", "purchasePrice", "PurchasePrice");
            double delta = GetDouble(item, "profitAndLossPrice", "ProfitAndLossPrice");
            double percent = NormalizePercent(GetDouble(item, "profitAndLossRange", "ProfitAndLossRange"));
            double previousPrice = purchasePrice > 0
                ? purchasePrice
                : currentPrice > 0 && Math.Abs(delta) > 0.001 ? Math.Max(0, currentPrice - delta) : 0;

            return new YouPinInventoryTrendRow
            {
                Name = name,
                TemplateId = GetString(item, "templateId", "TemplateId") ?? "",
                Quantity = quantity,
                CurrentPrice = currentPrice,
                PreviousPrice = previousPrice,
                Delta = delta,
                Percent = percent,
                PurchasePrice = purchasePrice,
                MissingEstimateCount = currentPrice > 0 ? 0 : quantity,
                MissingPurchaseCount = purchasePrice > 0 ? 0 : quantity
            };
        }

        public static double NormalizePercent(double value)
        {
            return Math.Abs(value) > 0.0001 && Math.Abs(value) <= 1 ? value * 100.0 : value;
        }

        public static List<YouPinInventoryItem> BuildItemsFromTrendRows(YouPinInventoryTrendState trendState)
        {
            return trendState.Rows
                .Where(x => !string.IsNullOrWhiteSpace(x.Name) || !string.IsNullOrWhiteSpace(x.TemplateId))
                .Select(x => new YouPinInventoryItem
                {
                    AssetId = "trend:" + FirstText(x.TemplateId, x.Name),
                    TemplateId = x.TemplateId,
                    Name = x.Name,
                    Price = x.CurrentPrice,
                    PurchasePrice = x.PurchasePrice,
                    Quantity = Math.Max(1, x.Quantity),
                    RawStatus = "悠悠有品涨跌"
                })
                .Where(x => x.Price > 0)
                .ToList();
        }

        public static YouPinInventoryItem ParseInventoryItem(JsonElement item)
        {
            TryGetProperty(item, out var templateInfo, "TemplateInfo", "templateInfo");
            TryGetProperty(item, out var assetInfo, "AssetInfo", "assetInfo");
            TryGetProperty(item, out var commodityInfo, "CommodityInfo", "commodityInfo");
            TryGetProperty(item, out var productInfo, "ProductInfo", "productInfo");

            return new YouPinInventoryItem
            {
                AssetId = GetString(item, "steamAssetId", "SteamAssetId", "assetId", "AssetId", "id", "Id", "inventoryId", "InventoryId") ?? "",
                TemplateId = FirstText(
                    GetString(item, "templateId", "TemplateId", "commodityTemplateId", "CommodityTemplateId"),
                    GetString(item, "templateInfoId", "TemplateInfoId"),
                    GetString(templateInfo, "Id", "id", "TemplateId", "templateId", "CommodityTemplateId", "commodityTemplateId"),
                    GetString(assetInfo, "templateId", "TemplateId", "commodityTemplateId", "CommodityTemplateId"),
                    GetString(commodityInfo, "templateId", "TemplateId", "id", "Id"),
                    GetString(productInfo, "templateId", "TemplateId", "id", "Id")),
                Name = FirstText(
                    GetString(item, "commodityName", "CommodityName", "marketHashName", "MarketHashName", "goodsName", "GoodsName", "name", "Name", "itemName", "ItemName", "ShotName", "shotName", "ShortName", "shortName"),
                    GetString(templateInfo, "commodityName", "CommodityName", "marketHashName", "MarketHashName", "goodsName", "GoodsName", "name", "Name", "itemName", "ItemName", "ShotName", "shotName", "ShortName", "shortName"),
                    GetString(assetInfo, "commodityName", "CommodityName", "marketHashName", "MarketHashName", "goodsName", "GoodsName", "name", "Name", "itemName", "ItemName", "ShotName", "shotName", "ShortName", "shortName"),
                    GetString(commodityInfo, "commodityName", "CommodityName", "marketHashName", "MarketHashName", "goodsName", "GoodsName", "name", "Name", "itemName", "ItemName", "ShotName", "shotName", "ShortName", "shortName"),
                    GetString(productInfo, "commodityName", "CommodityName", "marketHashName", "MarketHashName", "goodsName", "GoodsName", "name", "Name", "itemName", "ItemName", "ShotName", "shotName", "ShortName", "shortName"),
                    "未命名单品"),
                Price = FirstPositive(
                    GetMarketEstimate(templateInfo),
                    GetMarketEstimate(assetInfo),
                    GetMarketEstimate(commodityInfo),
                    GetMarketEstimate(productInfo),
                    GetDouble(item, "MarkPrice", "markPrice", "price", "Price", "referencePrice", "ReferencePrice", "marketPrice", "MarketPrice", "suggestPrice", "SuggestPrice", "SteamPrice", "steamPrice", "CnyPrice", "cnyPrice", "CurrentPrice", "currentPrice", "EstimatePrice", "estimatePrice", "LowestPrice", "lowestPrice", "MinPrice", "minPrice", "SellPrice", "sellPrice", "NewPrice", "newPrice")),
                PurchasePrice = FirstPositive(
                    GetDouble(item, "AssetBuyPrice", "assetBuyPrice"),
                    GetPurchaseEstimate(item),
                    GetPurchaseEstimate(templateInfo),
                    GetPurchaseEstimate(assetInfo),
                    GetPurchaseEstimate(commodityInfo),
                    GetPurchaseEstimate(productInfo)),
                Quantity = Math.Max(1, GetInt(item, "assetMergeCount", "AssetMergeCount", "count", "Count", "quantity", "Quantity")),
                RawStatus = GetString(item, "AssetStatus", "assetStatus", "status", "Status", "state", "State") ?? ""
            };
        }

        public static YouPinInventoryItem NormalizeItem(YouPinInventoryItem item)
        {
            if (string.IsNullOrWhiteSpace(item.AssetId))
                item.AssetId = item.Name;
            item.Name = string.IsNullOrWhiteSpace(item.Name) ? item.AssetId : item.Name.Trim();
            item.AssetId = item.AssetId.Trim();
            item.TemplateId = item.TemplateId?.Trim() ?? "";
            item.RawStatus = item.RawStatus?.Trim() ?? "";
            item.Price = Math.Max(0, item.Price);
            item.PurchasePrice = Math.Max(0, item.PurchasePrice);
            item.Quantity = Math.Max(1, item.Quantity);
            return item;
        }

        public static double GetTotalEstimate(JsonElement element)
        {
            var textValue = ExtractMoneyFromText(GetString(element, "InventoryTotalInfo", "inventoryTotalInfo"));
            if (textValue > 0) return textValue;

            var direct = GetDouble(element,
                "TotalValue", "totalValue",
                "TotalPrice", "totalPrice",
                "TotalAmount", "totalAmount",
                "TotalMarketPrice", "totalMarketPrice",
                "TotalMarkPrice", "totalMarkPrice",
                "TotalAssetPrice", "totalAssetPrice",
                "TotalInventoryPrice", "totalInventoryPrice",
                "InventoryValue", "inventoryValue",
                "InventoryPrice", "inventoryPrice",
                "MarketValue", "marketValue",
                "AssetValue", "assetValue",
                "SumPrice", "sumPrice",
                "SumValue", "sumValue");
            if (direct > 0) return direct;

            if (element.ValueKind != JsonValueKind.Object) return 0;
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                textValue = ExtractMoneyFromText(GetString(prop.Value, "InventoryTotalInfo", "inventoryTotalInfo"));
                if (textValue > 0) return textValue;

                var nested = GetDouble(prop.Value,
                    "TotalValue", "totalValue",
                    "TotalPrice", "totalPrice",
                    "TotalAmount", "totalAmount",
                    "MarketValue", "marketValue",
                    "InventoryValue", "inventoryValue",
                    "SumPrice", "sumPrice");
                if (nested > 0) return nested;
            }

            return 0;
        }

        public static string FirstText(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        public static string FormatSigned(double value)
        {
            string sign = value > 0 ? "+" : "";
            return $"{sign}¥{value:F2}";
        }

        public static string FormatSignedPercent(double value)
        {
            string sign = value > 0 ? "+" : "";
            return $"{sign}{value:F2}%";
        }

        public static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
        {
            foreach (var name in names)
            {
                if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
                    return true;
            }

            value = default;
            return false;
        }

        public static string? GetString(JsonElement element, params string[] names)
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

        public static int GetInt(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out var prop, names)) return 0;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value)) return value;
            if (int.TryParse(prop.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return value;
            return 0;
        }

        public static bool GetBool(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out var prop, names)) return false;
            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var intValue)) return intValue != 0;
            if (bool.TryParse(prop.ToString(), out var boolValue)) return boolValue;
            if (int.TryParse(prop.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out intValue)) return intValue != 0;
            return false;
        }

        public static double GetDouble(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out var prop, names)) return 0;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var num)) return num;

            string text = prop.ToString()
                .Replace("¥", "", StringComparison.Ordinal)
                .Replace("￥", "", StringComparison.Ordinal)
                .Replace(",", "", StringComparison.Ordinal)
                .Trim();

            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)) return value;
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value)) return value;
            return 0;
        }

        public static string Sanitize(string message)
        {
            return YouPinMobileApiClient.Sanitize(message);
        }

        private static IEnumerable<string> InventoryKeys(YouPinInventoryItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.AssetId)) yield return "A:" + item.AssetId.Trim();
            if (!string.IsNullOrWhiteSpace(item.TemplateId)) yield return "T:" + item.TemplateId.Trim();
            if (!string.IsNullOrWhiteSpace(item.Name)) yield return "N:" + item.Name.Trim();
        }

        public static string TrendKey(YouPinInventoryItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.TemplateId))
                return "T:" + item.TemplateId.Trim();
            if (!string.IsNullOrWhiteSpace(item.Name))
                return "N:" + item.Name.Trim();
            return "A:" + item.AssetId.Trim();
        }

        private static YouPinInventoryTrendRow AggregateTrendGroup(IEnumerable<YouPinInventoryItem> items)
        {
            var list = items.ToList();
            var priced = list.Where(x => x.Price > 0).ToList();
            var purchased = list.Where(x => x.PurchasePrice > 0).ToList();
            var first = list.FirstOrDefault();

            return new YouPinInventoryTrendRow
            {
                Name = first?.Name ?? "",
                TemplateId = first?.TemplateId ?? "",
                Quantity = Math.Max(1, list.Sum(x => Math.Max(1, x.Quantity))),
                CurrentPrice = WeightedAverage(priced, x => x.Price),
                PurchasePrice = WeightedAverage(purchased, x => x.PurchasePrice),
                MissingEstimateCount = list.Count(x => x.Price <= 0),
                MissingPurchaseCount = list.Count(x => x.PurchasePrice <= 0)
            };
        }

        private static double WeightedAverage(List<YouPinInventoryItem> items, Func<YouPinInventoryItem, double> selector)
        {
            int quantity = items.Sum(x => Math.Max(1, x.Quantity));
            return quantity > 0
                ? items.Sum(x => selector(x) * Math.Max(1, x.Quantity)) / quantity
                : 0;
        }

        private static double GetMarketEstimate(JsonElement element)
        {
            return GetDouble(element,
                "MarkPrice", "markPrice",
                "ReferencePrice", "referencePrice",
                "MarketPrice", "marketPrice",
                "ShowMarkPrice", "showMarkPrice",
                "Price", "price",
                "SteamPrice", "steamPrice",
                "CnyPrice", "cnyPrice",
                "CurrentPrice", "currentPrice",
                "EstimatePrice", "estimatePrice",
                "EstimatedPrice", "estimatedPrice",
                "LowestPrice", "lowestPrice",
                "MinPrice", "minPrice",
                "SellPrice", "sellPrice",
                "SalePrice", "salePrice",
                "OrderPrice", "orderPrice",
                "UUPrice", "uuPrice");
        }

        private static double GetPurchaseEstimate(JsonElement element)
        {
            return GetDouble(element,
                "BuyPrice", "buyPrice",
                "PurchasePrice", "purchasePrice",
                "CostPrice", "costPrice",
                "Cost", "cost",
                "InPrice", "inPrice",
                "InputPrice", "inputPrice",
                "OriginPrice", "originPrice",
                "BoughtPrice", "boughtPrice",
                "PayPrice", "payPrice",
                "PayAmount", "payAmount",
                "BuyOrderPrice", "buyOrderPrice",
                "OrderPrice", "orderPrice",
                "StartPrice", "startPrice");
        }

        private static double ExtractMoneyFromText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;

            string value = text.Trim();
            int currencyIndex = Math.Max(value.LastIndexOf('¥'), value.LastIndexOf('￥'));
            if (currencyIndex >= 0 && currencyIndex + 1 < value.Length)
                return ParseFirstNumber(value[(currencyIndex + 1)..]);

            return ParseLastNumber(value);
        }

        private static double ParseFirstNumber(string text)
        {
            var buffer = new StringBuilder();
            bool started = false;
            foreach (char c in text)
            {
                if (char.IsDigit(c) || c == '.' || c == ',')
                {
                    buffer.Append(c);
                    started = true;
                    continue;
                }

                if (started) break;
            }

            return ParseMoneyNumber(buffer.ToString());
        }

        private static double ParseLastNumber(string text)
        {
            double result = 0;
            var buffer = new StringBuilder();
            foreach (char c in text)
            {
                if (char.IsDigit(c) || c == '.' || c == ',')
                {
                    buffer.Append(c);
                    continue;
                }

                if (buffer.Length > 0)
                {
                    double parsed = ParseMoneyNumber(buffer.ToString());
                    if (parsed > 0) result = parsed;
                    buffer.Clear();
                }
            }

            if (buffer.Length > 0)
            {
                double parsed = ParseMoneyNumber(buffer.ToString());
                if (parsed > 0) result = parsed;
            }

            return result;
        }

        private static double ParseMoneyNumber(string text)
        {
            string clean = (text ?? "").Replace(",", "", StringComparison.Ordinal).Trim();
            if (double.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)) return value;
            if (double.TryParse(clean, NumberStyles.Any, CultureInfo.CurrentCulture, out value)) return value;
            return 0;
        }

        private static double FirstPositive(params double[] values)
        {
            foreach (var value in values)
            {
                if (value > 0)
                    return value;
            }

            return 0;
        }
    }
}
