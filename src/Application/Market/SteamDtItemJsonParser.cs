using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CS2TradeMonitor.Application.Market
{
    internal static class SteamDtItemJsonParser
    {
        internal static SteamDtItemData? ParseOfficialKlineData(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("success", out var successProp)
                    && successProp.ValueKind == JsonValueKind.False)
                {
                    return null;
                }

                JsonElement data = root;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var dataProp))
                {
                    data = dataProp;
                }

                var prices = new List<double>();
                CollectKlinePrices(data, prices);
                prices = prices.Where(x => x > 0).ToList();
                if (prices.Count == 0)
                    return null;

                double last = prices[^1];
                double previous = prices.Count >= 2 ? prices[^2] : 0;
                double change = previous > 0 ? last - previous : 0;
                double ratio = previous > 0 ? change / previous * 100.0 : 0;

                return new SteamDtItemData
                {
                    Price = last,
                    Change = change,
                    ChangeRatio = ratio,
                    UpdateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    RetrievedAt = DateTime.Now,
                    IsStale = false,
                    HasChangeData = previous > 0
                };
            }
            catch
            {
                return null;
            }
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

        private static void CollectKlinePrices(JsonElement element, List<double> prices)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray())
                {
                    if (TryExtractKlinePoint(child, out double price))
                    {
                        prices.Add(price);
                    }
                    else
                    {
                        CollectKlinePrices(child, prices);
                    }
                }
                return;
            }

            if (element.ValueKind == JsonValueKind.Object)
            {
                if (TryExtractKlinePoint(element, out double price))
                {
                    prices.Add(price);
                    return;
                }

                foreach (var prop in element.EnumerateObject())
                {
                    CollectKlinePrices(prop.Value, prices);
                }
            }
        }

        private static bool TryExtractKlinePoint(JsonElement element, out double price)
        {
            price = 0;
            if (element.ValueKind == JsonValueKind.Object)
            {
                price = GetDoubleProperty(element,
                    "close",
                    "Close",
                    "endPrice",
                    "EndPrice",
                    "price",
                    "Price",
                    "avgPrice",
                    "AvgPrice",
                    "value",
                    "Value",
                    "index",
                    "Index");
                return price > 0;
            }

            if (element.ValueKind == JsonValueKind.Array)
            {
                var numbers = new List<double>();
                foreach (var child in element.EnumerateArray())
                {
                    if (child.ValueKind == JsonValueKind.Number)
                    {
                        double value = child.GetDouble();
                        if (value > 0 && value < 100000000)
                        {
                            numbers.Add(value);
                        }
                    }
                    else if (child.ValueKind == JsonValueKind.String && double.TryParse(child.GetString(), out double parsed))
                    {
                        if (parsed > 0 && parsed < 100000000)
                        {
                            numbers.Add(parsed);
                        }
                    }
                }

                if (numbers.Count > 0)
                {
                    price = numbers[^1];
                    return true;
                }
            }

            return false;
        }
    }
}
