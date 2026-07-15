using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.Core.Refresh;
using CS2TradeMonitor.Domain.Market;
using static CS2TradeMonitor.Application.Market.SteamDtItemJsonParser;


namespace CS2TradeMonitor.Application.Market
{
    public partial class SteamDtItemService
    {

        public async Task<List<SteamDtSearchCandidate>> SearchItemsAsync(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return new List<SteamDtSearchCandidate>();
            keyword = keyword.Trim();

            var results = new List<SteamDtSearchCandidate>();
            var seenHashNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Try Official API if API key is present
            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                try
                {
                    await EnsureBaseCacheAsync();
                    if (_baseItemsCache != null && _baseItemsCache.Count > 0)
                    {
                        foreach (var item in _baseItemsCache)
                        {
                            if (string.IsNullOrWhiteSpace(item.MarketHashName)) continue;
                            if (IsMatch(item.Name, item.MarketHashName, keyword))
                            {
                                if (seenHashNames.Add(item.MarketHashName))
                                {
                                    string platformItemId = "";
                                    if (item.PlatformList != null && item.PlatformList.Count > 0)
                                    {
                                        var buffPlatform = item.PlatformList.FirstOrDefault(p => p.Name != null && p.Name.Contains("BUFF", StringComparison.OrdinalIgnoreCase));
                                        platformItemId = buffPlatform?.ItemId ?? item.PlatformList[0].ItemId ?? "";
                                    }

                                    string itemId = string.IsNullOrWhiteSpace(platformItemId) ? item.MarketHashName : platformItemId;

                                    results.Add(new SteamDtSearchCandidate
                                    {
                                        ItemId = itemId,
                                        Name = item.Name,
                                        Price = 0,
                                        Source = "官方 API",
                                        MarketHashName = item.MarketHashName,
                                        PlatformItemId = platformItemId
                                    });
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Info("SteamDTItem", $"Official base cache search exception: {ex.Message}");
                }
            }

            // 2. Always supplement with the local full item database.
            // The SteamDT base endpoint is quota-limited and may be absent; the user-provided
            // 25k item JSON is the stable search source for "all items can be searched".
            await Task.Run(LoadLocalItemsFile);
            if (_localItemsCache != null && _localItemsCache.Count > 0)
            {
                foreach (var item in _localItemsCache)
                {
                    if (IsMatch(item.Name, item.MarketHashName, keyword))
                    {
                        string key = !string.IsNullOrWhiteSpace(item.MarketHashName) ? item.MarketHashName : item.ItemId;
                        if (seenHashNames.Add(key))
                        {
                            results.Add(item);
                        }
                    }
                }
            }

            // 3. Fallback to Public suggest API
            if (results.Count == 0)
            {
                try
                {
                    long currentMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    string ts = currentMs.ToString();
                    if (ts.Length >= 12)
                    {
                        string first12 = ts.Substring(0, 12);
                        int sum = 0;
                        foreach (char c in first12)
                        {
                            if (char.IsDigit(c)) sum += (c - '0');
                        }
                        ts = first12 + (sum % 10).ToString();
                    }

                    var suggestPayload = new { keyword = keyword };
                    var content = new StringContent(JsonSerializer.Serialize(suggestPayload), System.Text.Encoding.UTF8, "application/json");

                    var request = new HttpRequestMessage(HttpMethod.Post, SteamDtUrls.WithTimestamp(SteamDtUrls.PublicBlockSuggest, ts));
                    request.Content = content;
                    request.Headers.TryAddWithoutValidation("x-device-id", "");
                    request.Headers.Add("x-device", "1");
                    request.Headers.Add("x-app-version", "1.0.0");
                    request.Headers.TryAddWithoutValidation("access-token", "");
                    request.Headers.Add("x-currency", "CNY");
                    request.Headers.Add("language", "zh_CN");
                    request.Headers.TryAddWithoutValidation("Referer", SteamDtUrls.ItemReferer);
                    request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                    var response = await _http.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync();
                        var parsed = ParseSearchJson(body, "公开接口");
                        if (parsed.Count > 0)
                        {
                            results.AddRange(parsed);
                        }
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Info("SteamDTItem", $"Public search exception: {ex.Message}");
                }
            }

            return FinalizeSearchResults(results, keyword);
        }


        private static List<SteamDtSearchCandidate> FinalizeSearchResults(List<SteamDtSearchCandidate> results, string keyword)
        {
            var distinct = new List<SteamDtSearchCandidate>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in results)
            {
                string key = !string.IsNullOrWhiteSpace(item.MarketHashName)
                    ? item.MarketHashName.Trim()
                    : (!string.IsNullOrWhiteSpace(item.ItemId) ? item.ItemId.Trim() : item.Name.Trim());
                if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
                    continue;
                distinct.Add(item);
            }

            string normalizedKeyword = NormalizeForSearch(keyword);
            int Score(SteamDtSearchCandidate item)
            {
                string name = NormalizeForSearch(item.Name);
                string hash = NormalizeForSearch(item.MarketHashName);
                if (name.Equals(normalizedKeyword, StringComparison.OrdinalIgnoreCase) ||
                    hash.Equals(normalizedKeyword, StringComparison.OrdinalIgnoreCase))
                    return 0;
                if (name.StartsWith(normalizedKeyword, StringComparison.OrdinalIgnoreCase))
                    return 1;
                if (hash.StartsWith(normalizedKeyword, StringComparison.OrdinalIgnoreCase))
                    return 2;
                if (name.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase))
                    return 3;
                if (hash.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase))
                    return 4;
                return 9;
            }

            return distinct
                .OrderBy(Score)
                .ThenBy(x => x.Name.Length)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(300)
                .ToList();
        }


        private List<SteamDtSearchCandidate> ParseSearchJson(string json, string sourceName)
        {
            var list = new List<SteamDtSearchCandidate>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Find any array property or if the root is an array
                if (root.ValueKind == JsonValueKind.Array)
                {
                    ExtractCandidatesFromArray(root, list, sourceName);
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    // Scan properties
                    foreach (var prop in root.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            ExtractCandidatesFromArray(prop.Value, list, sourceName);
                        }
                        else if (prop.Value.ValueKind == JsonValueKind.Object)
                        {
                            // Sub-object (e.g. "data" object which contains a list)
                            foreach (var subProp in prop.Value.EnumerateObject())
                            {
                                if (subProp.Value.ValueKind == JsonValueKind.Array)
                                {
                                    ExtractCandidatesFromArray(subProp.Value, list, sourceName);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Resilient to parsing errors
            }
            return list;
        }


        private void ExtractCandidatesFromArray(JsonElement array, List<SteamDtSearchCandidate> list, string sourceName)
        {
            foreach (var elem in array.EnumerateArray())
            {
                if (elem.ValueKind == JsonValueKind.Object)
                {
                    string id = "";
                    string name = "";
                    double price = 0;
                    string marketHashName = "";

                    // Search for ID fields
                    if (elem.TryGetProperty("itemId", out var idProp) ||
                        elem.TryGetProperty("id", out idProp) ||
                        elem.TryGetProperty("goodsId", out idProp) ||
                        elem.TryGetProperty("goods_id", out idProp) ||
                        elem.TryGetProperty("item_id", out idProp))
                    {
                        if (idProp.ValueKind == JsonValueKind.String) id = idProp.GetString() ?? "";
                        else if (idProp.ValueKind == JsonValueKind.Number) id = idProp.ToString();
                    }

                    // Search for Name fields
                    if (elem.TryGetProperty("name", out var nameProp) ||
                        elem.TryGetProperty("goodsName", out nameProp) ||
                        elem.TryGetProperty("itemName", out nameProp) ||
                        elem.TryGetProperty("nameCn", out nameProp) ||
                        elem.TryGetProperty("name_cn", out nameProp))
                    {
                        name = nameProp.GetString() ?? "";
                    }

                    // Search for MarketHashName fields
                    if (elem.TryGetProperty("marketHashName", out var mhnProp) ||
                        elem.TryGetProperty("market_hash_name", out mhnProp))
                    {
                        marketHashName = mhnProp.GetString() ?? "";
                    }

                    // Search for Price fields
                    price = GetDoubleProperty(elem, "price", "lastPrice", "index", "value");

                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                    {
                        list.Add(new SteamDtSearchCandidate
                        {
                            ItemId = id,
                            Name = name,
                            Price = price,
                            Source = sourceName,
                            MarketHashName = string.IsNullOrWhiteSpace(marketHashName) ? name : marketHashName,
                            PlatformItemId = id
                        });
                    }
                }
            }
        }


        private void LoadLocalItemsFile()
        {
            if (_localItemsCache != null) return;

            using (AppPerformanceProfiler.Measure("SteamDtItemService.LoadLocalItemsFile", "File=steamdt_items.json.gz", thresholdMs: 1))
            {
                lock (_localCacheLock)
                {
                    if (_localItemsCache != null) return;

                    var list = new List<SteamDtSearchCandidate>();
                    try
                    {
                        string? path = GetLocalItemsFilePath();
                        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                        {
                            string json = ReadLocalItemsFileText(path);
                            using var doc = JsonDocument.Parse(json);
                            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var elem in doc.RootElement.EnumerateArray())
                                {
                                    string id = "";
                                    if (elem.TryGetProperty("id", out var idProp))
                                    {
                                        id = idProp.ValueKind == JsonValueKind.Number ? idProp.GetInt64().ToString() : idProp.GetString() ?? "";
                                    }
                                    string name = elem.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                                    string marketHashName = elem.TryGetProperty("market_hash_name", out var mhnProp) ? mhnProp.GetString() ?? "" : "";

                                    if (!string.IsNullOrEmpty(name))
                                    {
                                        list.Add(new SteamDtSearchCandidate
                                        {
                                            ItemId = !string.IsNullOrEmpty(marketHashName) ? marketHashName : id,
                                            Name = name,
                                            Price = 0,
                                            Source = "本地库",
                                            MarketHashName = marketHashName,
                                            PlatformItemId = id
                                        });
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DiagnosticsLogger.Info("SteamDTItem", $"加载本地饰品文件异常: {ex.Message}");
                    }
                    _localItemsCache = list;
                }
            }
        }


        private static string? GetLocalItemsFilePath()
        {
            string fileName = "steamdt_items.json";
            string gzFileName = "steamdt_items.json.gz";
            var candidates = new List<string>
            {
                Path.Combine(global::CS2TradeMonitor.src.SystemServices.InstallationPaths.ResourcesDirectory, gzFileName),
                Path.Combine(global::CS2TradeMonitor.src.SystemServices.InstallationPaths.InstallDirectory, gzFileName),
                Path.Combine(Environment.CurrentDirectory, "resources", gzFileName),
                Path.Combine(global::CS2TradeMonitor.src.SystemServices.InstallationPaths.ResourcesDirectory, fileName),
                Path.Combine(global::CS2TradeMonitor.src.SystemServices.InstallationPaths.InstallDirectory, fileName),
                Path.Combine(global::CS2TradeMonitor.src.SystemServices.InstallationPaths.ResourcesDirectory, "饰品id_20260423（后缀更新为json）.txt"),
                Path.Combine(Environment.CurrentDirectory, "resources", fileName),
                Path.Combine(Environment.CurrentDirectory, "resources", "饰品id_20260423（后缀更新为json）.txt"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "饰品id_20260423（后缀更新为json）.txt")
            };

            return candidates.FirstOrDefault(File.Exists);
        }


        private static string ReadLocalItemsFileText(string path)
        {
            if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                using var file = File.OpenRead(path);
                using var gzip = new GZipStream(file, CompressionMode.Decompress);
                using var reader = new StreamReader(gzip, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }

            return File.ReadAllText(path, Encoding.UTF8);
        }


        private static bool IsMatch(string name, string marketHashName, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return false;

            var parts = keyword.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;

            string normCn = NormalizeForSearch(name);
            string normEn = NormalizeForSearch(marketHashName);

            foreach (var part in parts)
            {
                string normPart = NormalizeForSearch(part);
                if (string.IsNullOrEmpty(normPart)) continue;

                bool matchCn = normCn.Contains(normPart);
                bool matchEn = normEn.Contains(normPart);

                if (!matchCn && !matchEn)
                {
                    return false;
                }
            }

            return true;
        }


        private static string NormalizeForSearch(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var sb = new System.Text.StringBuilder();
            foreach (char c in text)
            {
                if (!char.IsWhiteSpace(c) && c != '(' && c != ')' && c != '[' && c != ']' && c != '（' && c != '）' && c != '|' && c != '-')
                {
                    char target = c == '*' ? '★' : char.ToLowerInvariant(c);
                    sb.Append(target);
                }
            }
            return sb.ToString();
        }

    }
}
