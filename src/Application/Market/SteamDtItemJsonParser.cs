using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CS2MarketData.Core;

namespace CS2TradeMonitor.Application.Market
{
    internal static class SteamDtItemJsonParser
    {
        internal static SteamDtItemData? ParseOfficialKlineData(string body)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("success", out JsonElement success)
                    && success.ValueKind == JsonValueKind.False)
                    return null;
                return CreateOfficialKlineData(SteamDtKlinePayloadParser.ParseClosingPrices(document.RootElement));
            }
            catch
            {
                return null;
            }
        }

        internal static SteamDtItemData? CreateOfficialKlineData(IReadOnlyList<double> prices)
        {
            if (prices.Count == 0)
                return null;
            double last = prices[^1];
            double previous = prices.Count >= 2 ? prices[^2] : 0;
            double change = previous > 0 ? last - previous : 0;
            return new SteamDtItemData
            {
                Price = last,
                Change = change,
                ChangeRatio = previous > 0 ? change / previous * 100.0 : 0,
                UpdateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                RetrievedAt = DateTime.Now,
                IsStale = false,
                HasChangeData = previous > 0
            };
        }

        internal static string ClassifyPublicItemFailure(string? body, string fallback)
        {
            if (string.IsNullOrWhiteSpace(body))
                return fallback;

            if (body.Contains("访问速度太快", StringComparison.OrdinalIgnoreCase)
                || body.Contains("请求太快", StringComparison.OrdinalIgnoreCase)
                || body.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
                || body.Contains("HTTP 429", StringComparison.OrdinalIgnoreCase))
            {
                return "请求过于频繁";
            }

            if (body.Contains("今日访问次数超限", StringComparison.OrdinalIgnoreCase)
                || body.Contains("访问次数超限", StringComparison.OrdinalIgnoreCase)
                || body.Contains("明日再试", StringComparison.OrdinalIgnoreCase))
            {
                return "今日访问次数超限";
            }

            return fallback;
        }

        internal static (double Price, long UpdateTime, string PlatformName) SelectPublicPlatformPrice(JsonElement dataProp)
        {
            if (!dataProp.TryGetProperty("sellingPriceList", out var listProp) || listProp.ValueKind != JsonValueKind.Array)
            {
                return (0, 0, "");
            }

            double bestPrice = 0;
            long bestUpdateTime = 0;
            string bestPlatformName = "";

            foreach (var elem in listProp.EnumerateArray())
            {
                if (elem.ValueKind != JsonValueKind.Object) continue;

                string platform = elem.TryGetProperty("platform", out var platformProp) ? platformProp.GetString() ?? "" : "";
                double currentPrice = GetDoubleProperty(elem, "price", "sellPrice");
                if (currentPrice <= 0) continue;

                bool isSteam = platform.Equals("steam", StringComparison.OrdinalIgnoreCase);
                if (isSteam && bestPrice > 0)
                {
                    continue;
                }

                if (bestPrice <= 0 || (!isSteam && currentPrice < bestPrice))
                {
                    bestPrice = currentPrice;
                    bestUpdateTime = GetLongProperty(elem, "updateTime", "timestamp", "systemTime");
                    bestPlatformName = elem.TryGetProperty("platformName", out var nameProp) ? nameProp.GetString() ?? "" : platform;
                }
            }

            return (bestPrice, bestUpdateTime, bestPlatformName);
        }

        internal static (double Price, long UpdateTime) SelectYouPinBidPrice(JsonElement payload)
        {
            double bestPrice = 0;
            long bestUpdateTime = 0;

            foreach (JsonElement element in EnumeratePlatformRows(payload))
            {
                if (element.ValueKind != JsonValueKind.Object || !IsYouPinPlatform(element))
                    continue;

                double price = GetDoubleProperty(
                    element,
                    "biddingPrice",
                    "bidPrice",
                    "buyPrice",
                    "purchasePrice");
                if (price <= 0 || price < bestPrice)
                    continue;

                long updateTime = GetLongProperty(element, "updateTime", "timestamp", "systemTime");
                if (price > bestPrice || updateTime >= bestUpdateTime)
                {
                    bestPrice = price;
                    bestUpdateTime = updateTime;
                }
            }

            return (bestPrice, bestUpdateTime);
        }

        internal static bool HasProperty(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out _);
        }

        internal static double GetDoubleProperty(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.Number) return prop.GetDouble();
                    if (prop.ValueKind == JsonValueKind.String && double.TryParse(prop.GetString(), out double val)) return val;
                }
            }
            return 0;
        }

        internal static long GetLongProperty(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.Number) return prop.GetInt64();
                    if (prop.ValueKind == JsonValueKind.String && long.TryParse(prop.GetString(), out long val)) return val;
                }
            }
            return 0;
        }

        private static IEnumerable<JsonElement> EnumeratePlatformRows(JsonElement payload)
        {
            if (payload.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement element in payload.EnumerateArray())
                    yield return element;
                yield break;
            }

            if (payload.ValueKind != JsonValueKind.Object)
                yield break;

            foreach (string listName in new[] { "sellingPriceList", "priceList", "platformList" })
            {
                if (!payload.TryGetProperty(listName, out JsonElement list)
                    || list.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement element in list.EnumerateArray())
                    yield return element;
            }
        }

        private static bool IsYouPinPlatform(JsonElement element)
        {
            foreach (string propertyName in new[] { "platform", "platformCode", "platformName", "name" })
            {
                if (!element.TryGetProperty(propertyName, out JsonElement property)
                    || property.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string value = property.GetString() ?? "";
                if (value.Contains("悠悠", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("youpin", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("yp", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

    }
}
