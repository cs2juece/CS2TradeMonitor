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
using CS2MarketData.Core;
using static CS2TradeMonitor.Application.Market.SteamDtItemJsonParser;


namespace CS2TradeMonitor.Application.Market
{
    public partial class SteamDtItemService
    {

        public async Task<bool> FetchItemPriceAsync(ItemMonitorConfig item)
        {
            await _fetchLock.WaitAsync();
            try
            {
                bool success = false;
                string errorMsg = "";
                double price = 0;
                double change = 0;
                double changeRatio = 0;
                long updateTime = 0;
                string source = "官方 API";
                bool hasChangeData = false;

                // 1. Try Official API if Key is present
                if (!string.IsNullOrWhiteSpace(_apiKey))
                {
                    if (!string.IsNullOrWhiteSpace(item.MarketHashName))
                    {
                        try
                        {
                            var request = new HttpRequestMessage(HttpMethod.Get, $"/open/cs2/v1/price/single?marketHashName={Uri.EscapeDataString(item.MarketHashName)}");
                            request.Headers.Add("Authorization", "Bearer " + _apiKey);

                            var response = await _http.SendAsync(request);
                            if (response.IsSuccessStatusCode)
                            {
                                var body = await response.Content.ReadAsStringAsync();
                                using var doc = JsonDocument.Parse(body);
                                var root = doc.RootElement;

                                bool apiSuccess = false;
                                if (root.TryGetProperty("success", out var successProp))
                                {
                                    apiSuccess = successProp.GetBoolean();
                                }

                                if (apiSuccess && root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
                                {
                                    JsonElement selectedElem = default;
                                    bool found = false;

                                    // 1. Try to match platformItemId
                                    if (!string.IsNullOrEmpty(item.PlatformItemId))
                                    {
                                        foreach (var elem in dataProp.EnumerateArray())
                                        {
                                            string pItemId = elem.TryGetProperty("platformItemId", out var pIdProp) ? pIdProp.GetString() ?? "" : "";
                                            if (string.Equals(pItemId, item.PlatformItemId, StringComparison.OrdinalIgnoreCase))
                                            {
                                                selectedElem = elem;
                                                found = true;
                                                break;
                                            }
                                        }
                                    }

                                    // 2. If not matched, try to find any platform with sellPrice > 0
                                    if (!found)
                                    {
                                        foreach (var elem in dataProp.EnumerateArray())
                                        {
                                            double sellPrice = GetDoubleProperty(elem, "sellPrice");
                                            if (sellPrice > 0)
                                            {
                                                selectedElem = elem;
                                                found = true;
                                                break;
                                            }
                                        }
                                    }

                                    // 3. If still not found, try to find any platform with biddingPrice > 0
                                    if (!found)
                                    {
                                        foreach (var elem in dataProp.EnumerateArray())
                                        {
                                            double biddingPrice = GetDoubleProperty(elem, "biddingPrice");
                                            if (biddingPrice > 0)
                                            {
                                                selectedElem = elem;
                                                found = true;
                                                break;
                                            }
                                        }
                                    }

                                    // 4. Fallback to first
                                    if (!found && dataProp.GetArrayLength() > 0)
                                    {
                                        selectedElem = dataProp[0];
                                        found = true;
                                    }

                                    if (found)
                                    {
                                        double sellPrice = GetDoubleProperty(selectedElem, "sellPrice");
                                        double biddingPrice = GetDoubleProperty(selectedElem, "biddingPrice");

                                        price = sellPrice > 0 ? sellPrice : (biddingPrice > 0 ? biddingPrice : 0);
                                        change = 0;
                                        changeRatio = 0;
                                        updateTime = GetLongProperty(selectedElem, "updateTime", "systemTime", "timestamp");

                                        source = "官方 API";
                                        success = true;
                                        hasChangeData = false;
                                    }
                                    else
                                    {
                                        errorMsg = "官方 API 返回价格列表为空";
                                    }
                                }
                                else
                                {
                                    string msg = root.TryGetProperty("errorMsg", out var msgProp) ? msgProp.GetString() ?? "" : "";
                                    string codeStr = root.TryGetProperty("errorCodeStr", out var codeStrProp) ? codeStrProp.GetString() ?? "" : "";
                                    errorMsg = $"官方 API 返回失败：{msg} ({codeStr})";
                                }
                            }
                            else
                            {
                                errorMsg = $"官方 API HTTP 错误：{(int)response.StatusCode} {response.ReasonPhrase}";
                            }
                        }
                        catch (Exception ex)
                        {
                            errorMsg = $"官方 API 异常：{ex.Message}";
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(item.ItemId))
                    {
                        try
                        {
                            var request = new HttpRequestMessage(HttpMethod.Get, $"{SteamDtUrls.OfficialItemPriceEndpoint}?itemId={Uri.EscapeDataString(item.ItemId)}");
                            request.Headers.Add("Authorization", "Bearer " + _apiKey);

                            var response = await _http.SendAsync(request);
                            if (response.IsSuccessStatusCode)
                            {
                                var body = await response.Content.ReadAsStringAsync();
                                using var doc = JsonDocument.Parse(body);
                                var root = doc.RootElement;

                                bool apiSuccess = false;
                                if (root.TryGetProperty("success", out var successProp))
                                {
                                    apiSuccess = successProp.GetBoolean();
                                }

                                if (apiSuccess && root.TryGetProperty("data", out var dataProp))
                                {
                                    price = GetDoubleProperty(dataProp, "price", "index", "lastPrice", "value");
                                    change = GetDoubleProperty(dataProp, "change", "diffYesterday", "riseFallDiff");
                                    changeRatio = GetDoubleProperty(dataProp, "changeRatio", "diffYesterdayRatio", "riseFallRate", "rate");
                                    updateTime = GetLongProperty(dataProp, "updateTime", "systemTime", "timestamp");

                                    success = true;
                                    hasChangeData = dataProp.TryGetProperty("change", out _) || dataProp.TryGetProperty("diffYesterday", out _) || dataProp.TryGetProperty("riseFallDiff", out _);
                                }
                                else
                                {
                                    string msg = root.TryGetProperty("errorMsg", out var msgProp) ? msgProp.GetString() ?? "" : "";
                                    string codeStr = root.TryGetProperty("errorCodeStr", out var codeStrProp) ? codeStrProp.GetString() ?? "" : "";
                                    errorMsg = $"官方 API 返回失败：{msg} ({codeStr})";
                                }
                            }
                            else
                            {
                                errorMsg = $"官方 API HTTP 错误：{(int)response.StatusCode} {response.ReasonPhrase}";
                            }
                        }
                        catch (Exception ex)
                        {
                            errorMsg = $"官方 API 异常：{ex.Message}";
                        }
                    }
                }

                if ((!success || !hasChangeData) && !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(item.MarketHashName))
                {
                    var klineData = await FetchOfficialKlineAsync(item.MarketHashName);
                    if (klineData != null)
                    {
                        if (!success || price <= 0)
                        {
                            price = klineData.Price;
                            source = klineData.Source;
                            success = price > 0;
                        }
                        else if (!hasChangeData)
                        {
                            source = $"{source}+K线";
                        }

                        if (klineData.HasChangeData)
                        {
                            change = klineData.Change;
                            changeRatio = klineData.ChangeRatio;
                            hasChangeData = true;
                        }

                        if (klineData.UpdateTime > 0)
                        {
                            updateTime = klineData.UpdateTime;
                        }
                    }
                }

                // 2. Use SteamDT public item detail as fallback/enrichment.
                // Official price/single does not expose 1-day change fields, while the public
                // detail page returns diff1Day and diff1DayPrice for the same marketHashName.
                if (!success || !hasChangeData)
                {
                    var publicData = await FetchPublicItemDetailAsync(item);
                    if (publicData != null)
                    {
                        if (!success || price <= 0)
                        {
                            price = publicData.Price;
                            source = publicData.Source;
                        }
                        else if (!hasChangeData)
                        {
                            source = $"{source}+公开涨跌";
                        }

                        if (publicData.HasChangeData)
                        {
                            change = publicData.Change;
                            changeRatio = publicData.ChangeRatio;
                            hasChangeData = true;
                        }

                        if (publicData.UpdateTime > 0)
                        {
                            updateTime = publicData.UpdateTime;
                        }

                        success = true;
                    }
                    else if (!success)
                    {
                        errorMsg = "公开接口未返回可用单品价格";
                    }
                }

                _lastFetchTime[item.ItemId] = DateTime.Now;

                if (success)
                {
                    var data = new SteamDtItemData
                    {
                        ItemId = item.ItemId,
                        Price = price,
                        Change = change,
                        ChangeRatio = changeRatio,
                        UpdateTime = updateTime,
                        RetrievedAt = DateTime.Now,
                        IsStale = false,
                        Source = source,
                        HasChangeData = hasChangeData
                    };
                    _cache[item.ItemId] = data;
                    if (!string.IsNullOrEmpty(item.MarketHashName))
                    {
                        _cache[item.MarketHashName] = data;
                    }
                    if (!string.IsNullOrEmpty(item.PlatformItemId))
                    {
                        _cache[item.PlatformItemId] = data;
                    }

                    item.LastPrice = price;
                    item.LastChange = change;
                    item.LastChangeRatio = changeRatio;
                    item.LastUpdateTime = updateTime;
                    item.LastStatus = "成功";
                    item.HasChangeData = hasChangeData;
                    ClearConfigErrorPause(item);
                    ClearFetchFailureLog(item);
                    EvaluateItemPriceAlert(item, data);

                    return true;
                }
                else
                {
                    bool configParameterError = IsConfigParameterError(errorMsg);
                    if (configParameterError)
                    {
                        MarkConfigErrorPaused(item, errorMsg);
                    }
                    else
                    {
                        item.LastStatus = $"失败 ({errorMsg})";
                    }

                    if (_cache.TryGetValue(item.ItemId, out var existing))
                    {
                        existing.IsStale = true;
                        existing.Source = "缓存";
                    }

                    if (!configParameterError)
                    {
                        LogFetchFailure(item, errorMsg);
                    }
                    return false;
                }
            }
            finally
            {
                _fetchLock.Release();
            }
        }


        private static void EvaluateItemPriceAlert(ItemMonitorConfig item, SteamDtItemData data)
        {
            if (data.Price <= 0)
                return;

            var now = DateTime.Now;
            long nowMs = new DateTimeOffset(now).ToUnixTimeMilliseconds();
            var defaults = MarketDataSourceRuntimeServices.Resolve().AppConfigState.ItemMonitor;
            int defaultWindowMinutes = defaults.DefaultAlertWindowMinutes > 0 ? defaults.DefaultAlertWindowMinutes : 10;
            int defaultCooldownMinutes = defaults.DefaultAlertCooldownMinutes > 0 ? defaults.DefaultAlertCooldownMinutes : 10;
            double defaultRisePercent = defaults.DefaultAlertRisePercent > 0 ? defaults.DefaultAlertRisePercent : 0;
            double defaultFallPercent = defaults.DefaultAlertFallPercent > 0 ? defaults.DefaultAlertFallPercent : 0;
            int windowMinutes = Math.Clamp(item.PriceAlertWindowMinutes > 0 ? item.PriceAlertWindowMinutes : defaultWindowMinutes, 1, 10080);
            int cooldownMinutes = Math.Clamp(item.PriceAlertCooldownMinutes > 0 ? item.PriceAlertCooldownMinutes : defaultCooldownMinutes, 1, 1440);
            double risePercentThreshold = item.PriceAlertRisePercent > 0 ? item.PriceAlertRisePercent : defaultRisePercent;
            double fallPercentThreshold = item.PriceAlertFallPercent > 0 ? item.PriceAlertFallPercent : defaultFallPercent;
            ItemPriceAlertTriggerMode triggerMode = ResolveItemTriggerMode(item);

            var baselineTime = UnixMsToLocalTime(item.PriceAlertBaselineTime);
            bool baselineMissing = item.PriceAlertBaselinePrice <= 0 || baselineTime == DateTime.MinValue;
            bool baselineExpired = !baselineMissing && now - baselineTime > TimeSpan.FromMinutes(windowMinutes);
            if (baselineMissing || baselineExpired)
            {
                item.PriceAlertBaselinePrice = data.Price;
                item.PriceAlertBaselineTime = nowMs;
            }

            if (!item.PriceAlertDesktopEnabled && !item.PriceAlertPhoneEnabled)
                return;

            var reasons = new List<string>();
            if (triggerMode == ItemPriceAlertTriggerMode.Breakthrough)
            {
                if (item.PriceAlertAbove > 0 && data.Price >= item.PriceAlertAbove)
                    reasons.Add($"高于 ¥{item.PriceAlertAbove:F2}");
                if (item.PriceAlertBelow > 0 && data.Price <= item.PriceAlertBelow)
                    reasons.Add($"低于 ¥{item.PriceAlertBelow:F2}");
            }

            if (triggerMode == ItemPriceAlertTriggerMode.Percent && item.PriceAlertBaselinePrice > 0)
            {
                double percent = (data.Price - item.PriceAlertBaselinePrice) / item.PriceAlertBaselinePrice * 100.0;
                if (risePercentThreshold > 0 && percent >= risePercentThreshold)
                    reasons.Add($"{windowMinutes} 分钟内上涨 {percent:F2}%");
                if (fallPercentThreshold > 0 && percent <= -fallPercentThreshold)
                    reasons.Add($"{windowMinutes} 分钟内下跌 {Math.Abs(percent):F2}%");
            }

            if (reasons.Count == 0)
                return;

            var lastTrigger = UnixMsToLocalTime(item.PriceAlertLastTriggerTime);
            if (lastTrigger != DateTime.MinValue && now - lastTrigger < TimeSpan.FromMinutes(cooldownMinutes))
                return;

            string reasonText = string.Join("；", reasons);
            item.PriceAlertLastTriggerTime = nowMs;
            item.PriceAlertLastMessage = $"{now:MM-dd HH:mm:ss} {reasonText}";
            item.PriceAlertBaselinePrice = data.Price;
            item.PriceAlertBaselineTime = nowMs;

            NotifyItemPriceAlert(item, data, reasonText);
        }


        private static ItemPriceAlertTriggerMode ResolveItemTriggerMode(ItemMonitorConfig item)
        {
            if (item.PriceAlertTriggerMode == ItemPriceAlertTriggerMode.Breakthrough ||
                item.PriceAlertTriggerMode == ItemPriceAlertTriggerMode.Percent)
            {
                return item.PriceAlertTriggerMode;
            }

            return item.PriceAlertAbove > 0 || item.PriceAlertBelow > 0
                ? ItemPriceAlertTriggerMode.Breakthrough
                : ItemPriceAlertTriggerMode.Percent;
        }


        private static DateTime UnixMsToLocalTime(long unixMs)
        {
            if (unixMs <= 0)
                return DateTime.MinValue;
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }


        private static void NotifyItemPriceAlert(ItemMonitorConfig item, SteamDtItemData data, string reason)
        {
            if (MarketDataSourceRuntimeServices.Resolve().AppConfigState.Notifications.DoNotDisturbEnabled)
                return;

            string title = "单品价格提醒";
            string name = string.IsNullOrWhiteSpace(item.Name) ? item.ItemId : item.Name;
            string message = $"{name}\n当前 ¥{data.Price:F2}，{reason}";
            AppNotificationHub.Instance.Request(
                title,
                message,
                AppNotificationSeverity.Warning,
                AppNotificationPlacement.BottomLeft,
                playSound: item.PriceAlertDesktopEnabled,
                showToast: item.PriceAlertDesktopEnabled,
                source: "单品监控",
                sendToPhone: item.PriceAlertPhoneEnabled);
        }


        private bool IsConfigErrorPaused(ItemMonitorConfig item)
        {
            string key = GetFetchFailureKey(item);
            return _configErrorPausedItems.ContainsKey(key)
                || item.LastStatus.StartsWith("配置错误", StringComparison.OrdinalIgnoreCase);
        }


        private void MarkConfigErrorPaused(ItemMonitorConfig item, string errorMsg)
        {
            string key = GetFetchFailureKey(item);
            _configErrorPausedItems[key] = 1;
            item.LastStatus = "配置错误（参数错误，已暂停自动刷新；修改单品或手动刷新后重试）";
            DiagnosticsLogger.Info("SteamDTItem", $"Fetch paused for item {key}: {SanitizeFetchError(errorMsg)}");
        }


        private void ClearConfigErrorPause(ItemMonitorConfig item)
        {
            string key = GetFetchFailureKey(item);
            _configErrorPausedItems.TryRemove(key, out _);
        }


        private static bool IsConfigParameterError(string errorMsg)
        {
            if (string.IsNullOrWhiteSpace(errorMsg)) return false;
            return errorMsg.IndexOf("参数错误", StringComparison.OrdinalIgnoreCase) >= 0
                || errorMsg.IndexOf("invalid parameter", StringComparison.OrdinalIgnoreCase) >= 0
                || errorMsg.IndexOf("parameter error", StringComparison.OrdinalIgnoreCase) >= 0;
        }


        private static string SanitizeFetchError(string errorMsg)
        {
            if (string.IsNullOrWhiteSpace(errorMsg)) return "unknown";
            return errorMsg.Length <= 160 ? errorMsg : errorMsg.Substring(0, 160) + "...";
        }


        private async Task<SteamDtItemData?> FetchOfficialKlineAsync(string marketHashName)
        {
            if (string.IsNullOrWhiteSpace(marketHashName) || string.IsNullOrWhiteSpace(_apiKey))
                return null;

            try
            {
                SteamDtKlineSeries series = await _klineClient.FetchDailyAsync(marketHashName, _apiKey);
                SteamDtItemData? data = SteamDtItemJsonParser.CreateOfficialKlineData(series.ClosingPrices);
                if (data is not null)
                {
                    data.Source = string.IsNullOrWhiteSpace(series.Platform)
                        ? "官方 K线"
                        : $"官方 K线/{series.Platform}";
                }
                return data;
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Info("SteamDTItem", $"Official kline exception for item hash: {ex.Message}");
                return null;
            }
        }

        private void LogFetchFailure(ItemMonitorConfig item, string errorMsg)
        {
            string key = GetFetchFailureKey(item);
            var now = DateTime.Now;

            if (_lastFailureLogTime.TryGetValue(key, out var lastTime)
                && _lastFailureLogMessage.TryGetValue(key, out var lastMessage)
                && string.Equals(lastMessage, errorMsg, StringComparison.Ordinal)
                && now - lastTime < FailureLogThrottle)
            {
                return;
            }

            _lastFailureLogTime[key] = now;
            _lastFailureLogMessage[key] = errorMsg;
            DiagnosticsLogger.Info("SteamDTItem", $"Fetch failed for item {key}: {errorMsg}");
        }


        private void ClearFetchFailureLog(ItemMonitorConfig item)
        {
            string key = GetFetchFailureKey(item);
            _lastFailureLogTime.TryRemove(key, out _);
            _lastFailureLogMessage.TryRemove(key, out _);
        }


        private static string GetFetchFailureKey(ItemMonitorConfig item)
        {
            if (!string.IsNullOrWhiteSpace(item.MarketHashName)) return item.MarketHashName;
            if (!string.IsNullOrWhiteSpace(item.ItemId)) return item.ItemId;
            if (!string.IsNullOrWhiteSpace(item.PlatformItemId)) return item.PlatformItemId;
            if (!string.IsNullOrWhiteSpace(item.Name)) return item.Name;
            return "unknown";
        }


        private async Task<SteamDtItemData?> FetchPublicItemDetailAsync(ItemMonitorConfig item)
        {
            string marketHashName = ResolveMarketHashName(item);
            if (string.IsNullOrWhiteSpace(marketHashName))
            {
                return null;
            }

            if (SteamDtPublicThrottle.IsCoolingDown(out _))
            {
                return null;
            }

            try
            {
                using SteamDtPublicLease? lease = await SteamDtPublicThrottle.TryAcquireAsync().ConfigureAwait(false);
                if (lease == null)
                {
                    return null;
                }

                string ts = CreateSteamDtTimestamp();
                var payload = new
                {
                    appId = 730,
                    marketHashName = marketHashName
                };

                var request = new HttpRequestMessage(HttpMethod.Post, SteamDtUrls.WithTimestamp(SteamDtUrls.PublicSkinItem, ts));
                request.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
                ApplyPublicSteamDtHeaders(request, SteamDtUrls.Cs2ItemReferer(marketHashName));

                var response = await _http.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    SteamDtPublicThrottle.ReportFailure(ClassifyPublicItemFailure(body, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".Trim()));
                    return null;
                }

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (!root.TryGetProperty("success", out var successProp) || !successProp.GetBoolean())
                {
                    SteamDtPublicThrottle.ReportFailure(ClassifyPublicItemFailure(body, "SteamDT 公开单品接口返回失败"));
                    return null;
                }

                if (!root.TryGetProperty("data", out var dataProp) || dataProp.ValueKind != JsonValueKind.Object)
                {
                    SteamDtPublicThrottle.ReportFailure(ClassifyPublicItemFailure(body, "SteamDT 公开单品接口返回数据为空"));
                    return null;
                }

                var selectedPrice = SelectPublicPlatformPrice(dataProp);
                double price = selectedPrice.Price;
                if (price <= 0)
                {
                    price = GetDoubleProperty(dataProp, "price", "lastPrice", "evaluatePrice", "increasePrice");
                }

                if (price <= 0)
                {
                    return null;
                }

                double change = GetDoubleProperty(dataProp, "diff1DayPrice", "increasePrice");
                double changeRatio = GetDoubleProperty(dataProp, "diff1Day");
                bool hasChangeData = HasProperty(dataProp, "diff1DayPrice") || HasProperty(dataProp, "diff1Day");

                long updateTime = selectedPrice.UpdateTime;
                if (updateTime <= 0)
                {
                    updateTime = GetLongProperty(dataProp, "updateTime", "systemTime", "timestamp");
                }

                string source = string.IsNullOrWhiteSpace(selectedPrice.PlatformName)
                    ? "公开页面接口"
                    : $"公开页面接口/{selectedPrice.PlatformName}";

                string returnedMarketHashName = dataProp.TryGetProperty("marketHashName", out var mhProp) ? mhProp.GetString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(returnedMarketHashName))
                {
                    item.MarketHashName = returnedMarketHashName;
                }

                SteamDtPublicThrottle.ReportSuccess();
                return new SteamDtItemData
                {
                    ItemId = item.ItemId,
                    Price = price,
                    Change = change,
                    ChangeRatio = changeRatio,
                    UpdateTime = updateTime,
                    RetrievedAt = DateTime.Now,
                    IsStale = false,
                    Source = source,
                    HasChangeData = hasChangeData
                };
            }
            catch (Exception ex)
            {
                SteamDtPublicThrottle.ReportFailure(ex.Message);
                DiagnosticsLogger.Info("SteamDTItem", $"Public item detail exception for item {item.ItemId}: {ex.Message}");
                return null;
            }
        }
        private string ResolveMarketHashName(ItemMonitorConfig item)
        {
            if (!string.IsNullOrWhiteSpace(item.MarketHashName))
            {
                return item.MarketHashName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(item.ItemId) && (item.ItemId.Contains('|') || item.ItemId.Contains('★')))
            {
                item.MarketHashName = item.ItemId.Trim();
                return item.MarketHashName;
            }

            LoadLocalItemsFile();
            SteamDtCatalogItem? match = _localItemCatalog?.FindExact(item.Name, item.PlatformItemId, item.ItemId);

            if (match != null && !string.IsNullOrWhiteSpace(match.MarketHashName))
            {
                item.MarketHashName = match.MarketHashName;
                if (string.IsNullOrWhiteSpace(item.PlatformItemId))
                {
                    item.PlatformItemId = match.ItemId;
                }
                return item.MarketHashName;
            }

            return "";
        }


        private static string CreateSteamDtTimestamp()
        {
            string msStr = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            if (msStr.Length < 12)
            {
                return msStr;
            }

            string first12 = msStr.Substring(0, 12);
            int sum = 0;
            foreach (char c in first12)
            {
                if (char.IsDigit(c))
                {
                    sum += c - '0';
                }
            }
            return first12 + (sum % 10).ToString();
        }


        private static void ApplyPublicSteamDtHeaders(HttpRequestMessage request, string referer)
        {
            request.Headers.TryAddWithoutValidation("x-device-id", "");
            request.Headers.TryAddWithoutValidation("x-device", "1");
            request.Headers.TryAddWithoutValidation("x-app-version", "1.0.0");
            request.Headers.TryAddWithoutValidation("access-token", "");
            request.Headers.TryAddWithoutValidation("x-currency", "CNY");
            request.Headers.TryAddWithoutValidation("language", "zh_CN");
            request.Headers.TryAddWithoutValidation("Referer", referer);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

    }
}
