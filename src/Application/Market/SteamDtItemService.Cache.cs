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


namespace CS2TradeMonitor.Application.Market
{
    public partial class SteamDtItemService
    {

        private string GetCacheFilePath()
        {
            return _cacheFilePath ??= RuntimeDataPaths.GetCacheFilePath("steamdt_base_cache.json");
        }


        private void LoadBaseCache()
        {
            try
            {
                string path = GetCacheFilePath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var container = JsonSerializer.Deserialize<SteamDtBaseCacheContainer>(json, _jsonOptions);
                    if (container != null)
                    {
                        _baseItemsCache = container.Items ?? new List<SteamDtBaseItem>();
                        _cacheLastUpdated = container.LastUpdated;
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Info("SteamDTItem", $"Failed to load base cache: {ex.Message}");
            }
        }


        private void SaveBaseCache()
        {
            try
            {
                string path = GetCacheFilePath();
                var container = new SteamDtBaseCacheContainer
                {
                    LastUpdated = _cacheLastUpdated,
                    Items = _baseItemsCache
                };
                string json = JsonSerializer.Serialize(container, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Info("SteamDTItem", $"Failed to save base cache: {ex.Message}");
            }
        }


        private async Task EnsureBaseCacheAsync()
        {
            if (_baseItemsCache.Count == 0)
            {
                LoadBaseCache();
            }

            bool isExpired = DateTime.Now - _cacheLastUpdated >= TimeSpan.FromHours(24) || _baseItemsCache.Count == 0;
            if (isExpired && !string.IsNullOrWhiteSpace(_apiKey))
            {
                await RefreshBaseCacheAsync();
            }
        }


        private async Task RefreshBaseCacheAsync()
        {
            var (success, message, count, time) = await ForceRefreshBaseCacheAsync();
            if (!success)
            {
                DiagnosticsLogger.Info("SteamDTItem", $"Refresh base cache failed: {message}");
            }
        }


        public async Task<(bool Success, string Message, int Count, DateTime RefreshTime)> ForceRefreshBaseCacheAsync()
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                return (false, "未配置 API Key，无法刷新基础信息缓存。", 0, DateTime.MinValue);
            }

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "/open/cs2/v1/base");
                request.Headers.Add("Authorization", "Bearer " + _apiKey);

                var response = await _http.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;

                    bool success = false;
                    if (root.TryGetProperty("success", out var successProp))
                    {
                        success = successProp.GetBoolean();
                    }

                    if (success && root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
                    {
                        var items = new List<SteamDtBaseItem>();
                        foreach (var elem in dataProp.EnumerateArray())
                        {
                            string name = elem.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                            string marketHashName = elem.TryGetProperty("marketHashName", out var mhnProp) ? mhnProp.GetString() ?? "" : "";
                            var platformList = new List<SteamDtPlatformItem>();

                            if (elem.TryGetProperty("platformList", out var plProp) && plProp.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var pElem in plProp.EnumerateArray())
                                {
                                    string pName = pElem.TryGetProperty("name", out var pNameProp) ? pNameProp.GetString() ?? "" : "";
                                    string pItemId = pElem.TryGetProperty("itemId", out var pItemIdProp) ? pItemIdProp.GetString() ?? "" : "";
                                    platformList.Add(new SteamDtPlatformItem { Name = pName, ItemId = pItemId });
                                }
                            }

                            items.Add(new SteamDtBaseItem
                            {
                                Name = name,
                                MarketHashName = marketHashName,
                                PlatformList = platformList
                            });
                        }

                        _baseItemsCache = items;
                        _cacheLastUpdated = DateTime.Now;
                        SaveBaseCache();
                        return (true, "刷新成功", _baseItemsCache.Count, _cacheLastUpdated);
                    }
                    else
                    {
                        string msg = root.TryGetProperty("errorMsg", out var msgProp) ? msgProp.GetString() ?? "" : "";
                        string code = root.TryGetProperty("errorCodeStr", out var codeProp) ? codeProp.GetString() ?? "" : "";
                        string detail = string.IsNullOrWhiteSpace(code) ? msg : $"{msg} ({code})";
                        return (false, $"接口返回失败: {detail}", 0, DateTime.MinValue);
                    }
                }
                else
                {
                    return (false, $"HTTP 请求失败: {(int)response.StatusCode} {response.ReasonPhrase}", 0, DateTime.MinValue);
                }
            }
            catch (Exception ex)
            {
                return (false, $"刷新异常: {ex.Message}", 0, DateTime.MinValue);
            }
        }

    }
}
