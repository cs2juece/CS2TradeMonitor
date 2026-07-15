using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Market;
using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.Application.Steam.Auth;
using CS2TradeMonitor.Domain.Steam;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;

namespace CS2TradeMonitor.Application.Steam
{
    public sealed class SteamTradeOfferClient : ISteamTradeOfferClient
    {
        private readonly HttpClient? _http;
        private readonly ISteamRoutedHttpClientFactory _httpFactory;
        private readonly Func<string, string> _localItemNameResolver;

        public SteamTradeOfferClient(HttpClient? http = null)
            : this(SteamServiceRuntimeServices.ResolveRoutedHttpFactory(), http)
        {
        }

        internal SteamTradeOfferClient(
            ISteamRoutedHttpClientFactory httpFactory,
            HttpClient? http = null,
            Func<string, string>? localItemNameResolver = null)
        {
            _httpFactory = httpFactory ?? throw new ArgumentNullException(nameof(httpFactory));
            _http = http;
            _localItemNameResolver = localItemNameResolver ?? SteamDtLocalItemNameResolver.ResolveNameByMarketHashName;
            if (_http != null)
                ApplyDefaultHeaders(_http);
        }

        public Task<TradeOffersResult> GetTradeOffersAsync(SteamAuthCredential credential)
        {
            return GetTradeOffersAsync(credential, activeOnly: true, historicalOnly: false, historicalCutoffUnix: null);
        }

        public Task<TradeOffersResult> GetTradeOffersForDetailLookupAsync(SteamAuthCredential credential)
        {
            return GetTradeOffersAsync(credential, activeOnly: false, historicalOnly: false, historicalCutoffUnix: null);
        }

        public Task<TradeOffersResult> GetHistoricalTradeOffersForDetailLookupAsync(SteamAuthCredential credential, TimeSpan maxAge)
        {
            long cutoff = DateTimeOffset.UtcNow.Subtract(maxAge).ToUnixTimeSeconds();
            return GetTradeOffersAsync(credential, activeOnly: false, historicalOnly: true, historicalCutoffUnix: cutoff);
        }

        private async Task<TradeOffersResult> GetTradeOffersAsync(
            SteamAuthCredential credential,
            bool activeOnly,
            bool historicalOnly,
            long? historicalCutoffUnix)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            EnsureApiKey(credential);

            var query = new List<string>
            {
                "key=" + Uri.EscapeDataString(credential.ApiKey.Trim()),
                "get_sent_offers=1",
                "get_received_offers=1",
                "get_descriptions=1",
                "language=zh_CN"
            };
            if (historicalOnly)
            {
                query.Add("historical_only=1");
                if (historicalCutoffUnix.HasValue)
                    query.Add("time_historical_cutoff=" + historicalCutoffUnix.Value.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                query.Add("active_only=" + (activeOnly ? "1" : "0"));
            }

            string url = SteamUrls.EconServiceApiBase + "/GetTradeOffers/v1/?" + string.Join("&", query);

            using var owned = await CreateOwnedClientIfNeededAsync();
            var http = _http ?? owned!;
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            ApplySteamCookies(req, credential);
            using var resp = await http.SendAsync(req);
            string text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                throw BuildHttpException(resp.StatusCode, text, "Steam 报价接口");

            using var doc = JsonDocument.Parse(text);
            if (!TryGetProperty(doc.RootElement, out var response, "response"))
                throw new InvalidOperationException("Steam 报价接口响应格式异常。");

            return ParseOffersResponse(response);
        }

        public async Task<TradeOffersResult> GetTradeOffersFromWebSessionAsync(SteamAuthCredential credential)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            if (string.IsNullOrWhiteSpace(credential.SessionId) || string.IsNullOrWhiteSpace(credential.SteamLoginSecure))
                throw new SteamAuthExpiredException("Steam 登录状态未保存，无法读取网页报价列表。");

            using var owned = await CreateOwnedClientIfNeededAsync(allowAutoRedirect: false);
            var http = _http ?? owned!;
            var redirectCookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var result = new TradeOffersResult();

            foreach (string pageUrl in BuildWebTradeOffersUrls(credential))
            {
                var pageResult = await GetTradeOffersFromWebSessionPageAsync(http, credential, new Uri(pageUrl), redirectCookies);
                MergeTradeOffersResult(result, pageResult);
            }

            FillLocalWebAssetNames(result);
            await FillMissingWebAssetNamesFromClassInfoAsync(http, credential, result);
            FillLocalWebAssetNames(result);
            return result;
        }

        private static async Task<TradeOffersResult> GetTradeOffersFromWebSessionPageAsync(
            HttpClient http,
            SteamAuthCredential credential,
            Uri url,
            Dictionary<string, string> redirectCookies)
        {
            for (int redirect = 0; redirect < 10; redirect++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                ApplySteamCookies(req, credential, extraCookies: redirectCookies);
                req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                using var resp = await http.SendAsync(req);
                CaptureSetCookies(resp, redirectCookies);
                if (IsRedirect(resp.StatusCode))
                {
                    string location = resp.Headers.Location?.ToString() ?? "";
                    if (IsLoginRedirect(location))
                    {
                        LogWebLoginPageDiagnostic("Steam 网页报价列表重定向到登录页", "", resp.StatusCode, location);
                        throw new SteamAuthExpiredException($"Steam 网页报价列表返回 HTTP {(int)resp.StatusCode} 并重定向到登录页，Steam 登录状态已失效。", (int)resp.StatusCode);
                    }

                    Uri? next = ResolveSteamCommunityRedirect(url, resp.Headers.Location);
                    if (next == null)
                        throw new SteamTransientSteamException($"Steam 网页报价列表返回 HTTP {(int)resp.StatusCode} 非预期重定向，暂时无法读取报价。", (int)resp.StatusCode, "steam-web-tradeoffers-redirect");

                    url = next;
                    continue;
                }

                string html = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                    throw BuildHttpException(resp.StatusCode, html, "Steam 网页报价列表");
                if (LooksLikeLoginPage(html))
                {
                    LogWebLoginPageDiagnostic("Steam 网页报价列表返回登录页", html, resp.StatusCode, "");
                    throw new SteamAuthExpiredException("Steam 网页报价列表返回登录页，Steam 登录状态已失效。");
                }

                bool isSentPage = IsSentTradeOffersPage(url);
                var htmlOffers = SteamTradeOfferWebHtmlParser.Parse(html, isSentPage);
                LogWebTradeOffersParseDiagnostic(url, html, htmlOffers, isSentPage);
                if (htmlOffers.SentOffers.Count > 0 || htmlOffers.ReceivedOffers.Count > 0)
                    return htmlOffers;

                string json = FirstText(
                    ExtractJavascriptAssignmentJson(html, "g_rgCurrentTradeOffers"),
                    ExtractJavascriptAssignmentJson(html, "g_rgTradeOffers"));
                if (string.IsNullOrWhiteSpace(json))
                    return new TradeOffersResult();

                using var doc = JsonDocument.Parse(json);
                return ParseWebOffersRoot(doc.RootElement);
            }

            throw new SteamTransientSteamException("Steam 网页报价列表重定向次数过多，暂时无法读取报价。", null, "steam-web-tradeoffers-too-many-redirects");
        }

        public async Task<TradeOfferDetail> GetTradeOfferFromWebSessionAsync(SteamAuthCredential credential, string tradeOfferId)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            if (string.IsNullOrWhiteSpace(credential.SessionId) || string.IsNullOrWhiteSpace(credential.SteamLoginSecure))
                throw new SteamAuthExpiredException("Steam 登录状态未保存，无法读取网页报价详情。");

            tradeOfferId = (tradeOfferId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(tradeOfferId))
                throw new ArgumentException("Steam 报价号不能为空。", nameof(tradeOfferId));

            using var owned = await CreateOwnedClientIfNeededAsync(allowAutoRedirect: false).ConfigureAwait(false);
            var http = _http ?? owned!;
            var redirectCookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Uri url = new(BuildTradeOfferDetailUrl(tradeOfferId));

            for (int redirect = 0; redirect < 10; redirect++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                ApplySteamCookies(req, credential, BuildTradeOfferReferer(tradeOfferId), redirectCookies);
                req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                using var resp = await http.SendAsync(req).ConfigureAwait(false);
                CaptureSetCookies(resp, redirectCookies);
                if (IsRedirect(resp.StatusCode))
                {
                    string location = resp.Headers.Location?.ToString() ?? "";
                    if (IsLoginRedirect(location))
                        throw new SteamAuthExpiredException($"Steam 报价详情返回 HTTP {(int)resp.StatusCode} 并重定向到登录页，Steam 登录状态已失效。", (int)resp.StatusCode);

                    Uri? next = ResolveSteamCommunityRedirect(url, resp.Headers.Location);
                    if (next == null)
                        throw new SteamTransientSteamException($"Steam 报价详情返回 HTTP {(int)resp.StatusCode} 非预期重定向，无法读取报价明细。", (int)resp.StatusCode, "steam-tradeoffer-detail-redirect");

                    url = next;
                    continue;
                }

                string html = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    throw BuildHttpException(resp.StatusCode, html, "Steam 报价详情");
                if (LooksLikeLoginPage(html))
                {
                    LogWebLoginPageDiagnostic("Steam 报价详情返回登录页", html, resp.StatusCode, "");
                    throw new SteamAuthExpiredException("Steam 报价详情返回登录页，Steam 登录状态已失效。");
                }

                var detail = SteamTradeOfferWebHtmlParser.ParseTradeOfferDetailPage(html, tradeOfferId, forceSentOffer: true)
                    ?? throw new InvalidOperationException("Steam 网页报价详情未返回饰品明细。");
                var result = new TradeOffersResult();
                result.SentOffers.Add(detail);
                FillLocalWebAssetNames(result);
                await FillMissingWebAssetNamesFromClassInfoAsync(http, credential, result).ConfigureAwait(false);
                FillLocalWebAssetNames(result);
                return detail;
            }

            throw new SteamTransientSteamException("Steam 报价详情重定向次数过多，无法读取报价明细。", null, "steam-tradeoffer-detail-too-many-redirects");
        }

        private static async Task FillMissingWebAssetNamesFromClassInfoAsync(
            HttpClient http,
            SteamAuthCredential credential,
            TradeOffersResult result)
        {
            if (string.IsNullOrWhiteSpace(credential.ApiKey))
                return;

            var assets = result.SentOffers
                .Concat(result.ReceivedOffers)
                .SelectMany(offer => offer.ItemsToGive.Concat(offer.ItemsToReceive))
                .Where(NeedsClassInfoLookup)
                .Where(asset => asset.AppId > 0 && !string.IsNullOrWhiteSpace(asset.ClassId))
                .GroupBy(asset => new AssetClassLookupKey(asset.AppId, asset.ClassId.Trim(), string.IsNullOrWhiteSpace(asset.InstanceId) ? "0" : asset.InstanceId.Trim()))
                .Select(group => group.Key)
                .Take(50)
                .ToList();
            if (assets.Count == 0)
                return;

            try
            {
                foreach (var appGroup in assets.GroupBy(asset => asset.AppId))
                {
                    var keys = appGroup.Take(50).ToList();
                    var query = new List<string>
                    {
                        "key=" + Uri.EscapeDataString(credential.ApiKey.Trim()),
                        "appid=" + appGroup.Key.ToString(CultureInfo.InvariantCulture),
                        "language=zh_CN",
                        "class_count=" + keys.Count.ToString(CultureInfo.InvariantCulture)
                    };
                    for (int i = 0; i < keys.Count; i++)
                    {
                        query.Add("classid" + i.ToString(CultureInfo.InvariantCulture) + "=" + Uri.EscapeDataString(keys[i].ClassId));
                        query.Add("instanceid" + i.ToString(CultureInfo.InvariantCulture) + "=" + Uri.EscapeDataString(keys[i].InstanceId));
                    }

                    string url = SteamUrls.EconomyApiBase + "/GetAssetClassInfo/v1/?" + string.Join("&", query);
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    ApplySteamCookies(req, credential);
                    using var resp = await http.SendAsync(req).ConfigureAwait(false);
                    string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                        continue;

                    using var doc = JsonDocument.Parse(text);
                    var descriptions = ParseClassInfoDescriptions(doc.RootElement);
                    foreach (var description in descriptions)
                        result.Descriptions[description.Key] = description.Value;
                }

                FillClassInfoDescriptions(result);
            }
            catch (Exception ex)
            {
                SteamOfferAuditLog.InfoThrottled(
                    "steam-web-classinfo-enrich-failed",
                    "Steam web asset class info enrichment skipped. Reason=" + SteamOfferAuditLog.RedactSecrets(ex.Message),
                    TimeSpan.FromMinutes(5));
            }
        }

        public async Task EnrichTradeOfferDetailAssetsAsync(SteamAuthCredential credential, TradeOfferDetail detail)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            if (detail == null) throw new ArgumentNullException(nameof(detail));
            if (string.IsNullOrWhiteSpace(credential.ApiKey))
                return;
            if (detail.ItemsToGive.Count == 0 && detail.ItemsToReceive.Count == 0)
                return;

            var result = new TradeOffersResult();
            if (detail.IsOurOffer)
                result.SentOffers.Add(detail);
            else
                result.ReceivedOffers.Add(detail);

            using var owned = await CreateOwnedClientIfNeededAsync().ConfigureAwait(false);
            var http = _http ?? owned!;
            FillLocalWebAssetNames(result);
            await FillMissingWebAssetNamesFromClassInfoAsync(http, credential, result).ConfigureAwait(false);
            FillLocalWebAssetNames(result);
        }

        public async Task<TradeOfferDetail> GetTradeOfferAsync(SteamAuthCredential credential, string tradeOfferId)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            EnsureApiKey(credential);
            tradeOfferId = (tradeOfferId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(tradeOfferId))
                throw new ArgumentException("Steam 报价号不能为空。", nameof(tradeOfferId));

            string url = SteamUrls.EconServiceApiBase + "/GetTradeOffer/v1/?"
                + "key=" + Uri.EscapeDataString(credential.ApiKey.Trim())
                + "&tradeofferid=" + Uri.EscapeDataString(tradeOfferId)
                + "&get_descriptions=1"
                + "&language=zh_CN";

            using var owned = await CreateOwnedClientIfNeededAsync();
            var http = _http ?? owned!;
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            ApplySteamCookies(req, credential);
            using var resp = await http.SendAsync(req);
            string text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                throw BuildHttpException(resp.StatusCode, text, "Steam 报价详情接口");

            using var doc = JsonDocument.Parse(text);
            if (!TryGetProperty(doc.RootElement, out var response, "response"))
                throw new InvalidOperationException("Steam 报价详情接口响应格式异常。");

            var descriptions = ParseDescriptions(response);
            if (!TryGetProperty(response, out var offerElement, "offer", "trade_offer"))
                throw new InvalidOperationException("Steam 报价详情为空。");

            return ParseOffer(offerElement, descriptions, isOurOfferFallback: false);
        }

        public async Task<bool> AcknowledgeNewTradeAsync(SteamAuthCredential credential, string tradeOfferId)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            if (string.IsNullOrWhiteSpace(credential.SessionId) || string.IsNullOrWhiteSpace(credential.SteamLoginSecure))
                throw new SteamAuthExpiredException("Steam 登录状态未保存，无法确认新报价提示。");

            string cleanTradeOfferId = (tradeOfferId ?? "").Trim();
            using var owned = await CreateOwnedClientIfNeededAsync();
            var http = _http ?? owned!;
            using var req = new HttpRequestMessage(HttpMethod.Post, SteamUrls.TradeNewAcknowledge);
            ApplySteamCookies(req, credential, BuildTradeOfferReferer(cleanTradeOfferId));
            req.Headers.TryAddWithoutValidation("Origin", SteamUrls.CommunityBase);
            req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
            req.Headers.TryAddWithoutValidation("Accept", "application/json, text/javascript, */*; q=0.01");
            req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["sessionid"] = credential.SessionId.Trim(),
                ["message"] = "1"
            });

            using var resp = await http.SendAsync(req);
            string text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                throw BuildHttpException(resp.StatusCode, text, "Steam 新报价提示确认");
            if (LooksLikeLoginPage(text))
            {
                LogWebLoginPageDiagnostic("Steam 新报价提示确认返回登录页", text, resp.StatusCode, "");
                throw new SteamAuthExpiredException("Steam 新报价提示确认返回登录页，Steam 登录状态已失效。");
            }
            if (string.IsNullOrWhiteSpace(text))
                return true;

            try
            {
                using var doc = JsonDocument.Parse(text);
                return !TryGetProperty(doc.RootElement, out _, "success") || GetBool(doc.RootElement, "success");
            }
            catch (JsonException)
            {
                return true;
            }
        }

        public async Task<SteamTradeOfferAcceptResult> AcceptTradeOfferAsync(SteamAuthCredential credential, string tradeOfferId)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            if (string.IsNullOrWhiteSpace(credential.SessionId) || string.IsNullOrWhiteSpace(credential.SteamLoginSecure))
                throw new SteamAuthExpiredException("Steam 登录状态未保存，无法同意报价。");

            string cleanTradeOfferId = (tradeOfferId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(cleanTradeOfferId))
                throw new ArgumentException("Steam 报价号不能为空。", nameof(tradeOfferId));

            string partnerSteamId = await FetchTradePartnerIdAsync(credential, cleanTradeOfferId).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(partnerSteamId))
                return SteamTradeOfferAcceptResult.Failed("未能读取 Steam 报价对方 ID，无法调用同意接口。");

            using var owned = await CreateOwnedClientIfNeededAsync(allowAutoRedirect: false).ConfigureAwait(false);
            var http = _http ?? owned!;
            string acceptUrl = SteamUrls.TradeOfferAccept(cleanTradeOfferId);
            using var req = new HttpRequestMessage(HttpMethod.Post, acceptUrl);
            ApplySteamCookies(req, credential, BuildTradeOfferReferer(cleanTradeOfferId));
            req.Headers.TryAddWithoutValidation("Origin", SteamUrls.CommunityBase);
            req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
            req.Headers.TryAddWithoutValidation("Accept", "application/json, text/javascript, */*; q=0.01");
            req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["sessionid"] = credential.SessionId.Trim(),
                ["tradeofferid"] = cleanTradeOfferId,
                ["serverid"] = "1",
                ["partner"] = partnerSteamId.Trim(),
                ["captcha"] = ""
            });

            using var resp = await http.SendAsync(req).ConfigureAwait(false);
            string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (IsRedirect(resp.StatusCode) && IsLoginRedirect(resp.Headers.Location?.ToString() ?? ""))
                throw new SteamAuthExpiredException($"Steam 同意报价返回 HTTP {(int)resp.StatusCode} 并重定向到登录页，Steam 登录状态已失效。", (int)resp.StatusCode);
            if (!resp.IsSuccessStatusCode)
                throw BuildHttpException(resp.StatusCode, text, "Steam 同意报价");
            if (LooksLikeLoginPage(text))
            {
                LogWebLoginPageDiagnostic("Steam 同意报价返回登录页", text, resp.StatusCode, "");
                throw new SteamAuthExpiredException("Steam 同意报价返回登录页，Steam 登录状态已失效。");
            }

            if (string.IsNullOrWhiteSpace(text))
                return SteamTradeOfferAcceptResult.Success("Steam 已接受报价。");

            if (LooksLikeAlreadyHandledTradeOffer(text))
                return SteamTradeOfferAcceptResult.Handled("Steam 报价已被处理或失效，已跳过。");

            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (GetBool(root, "needs_mobile_confirmation", "needsMobileConfirmation"))
                    return SteamTradeOfferAcceptResult.NeedsConfirmation("Steam 要求手机确认。");
                if (TryGetProperty(root, out _, "needs_mobile_confirmation", "needsMobileConfirmation"))
                    return SteamTradeOfferAcceptResult.Success("Steam 已接受报价。");
                if (GetBool(root, "success"))
                    return SteamTradeOfferAcceptResult.Success("Steam 已接受报价。");
                if (TryGetProperty(root, out _, "tradeid", "trade_id"))
                    return SteamTradeOfferAcceptResult.Success("Steam 已接受报价。");

                string message = FirstText(
                    GetString(root, "strError", "message", "msg", "error"),
                    "Steam 未返回成功状态。");
                if (LooksLikeAlreadyHandledTradeOffer(message))
                    return SteamTradeOfferAcceptResult.Handled("Steam 报价已被处理或失效，已跳过。");

                return SteamTradeOfferAcceptResult.Failed(SteamOfferAuditLog.RedactSecrets(message));
            }
            catch (JsonException)
            {
                string summary = BuildBodySummary(text);
                return SteamTradeOfferAcceptResult.Failed("Steam 同意报价响应格式异常：" + summary);
            }
        }

        private async Task<string> FetchTradePartnerIdAsync(SteamAuthCredential credential, string tradeOfferId)
        {
            Uri url = new(BuildTradeOfferReferer(tradeOfferId));
            using var owned = await CreateOwnedClientIfNeededAsync(allowAutoRedirect: false).ConfigureAwait(false);
            var http = _http ?? owned!;
            var redirectCookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int redirect = 0; redirect < 10; redirect++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                ApplySteamCookies(req, credential, BuildTradeOfferReferer(tradeOfferId), redirectCookies);
                req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                using var resp = await http.SendAsync(req).ConfigureAwait(false);
                CaptureSetCookies(resp, redirectCookies);
                if (IsRedirect(resp.StatusCode))
                {
                    string location = resp.Headers.Location?.ToString() ?? "";
                    if (IsLoginRedirect(location))
                        throw new SteamAuthExpiredException($"Steam 报价详情返回 HTTP {(int)resp.StatusCode} 并重定向到登录页，Steam 登录状态已失效。", (int)resp.StatusCode);

                    Uri? next = ResolveSteamCommunityRedirect(url, resp.Headers.Location);
                    if (next == null)
                        throw new SteamTransientSteamException($"Steam 报价详情返回 HTTP {(int)resp.StatusCode} 非预期重定向，无法读取对方 ID。", (int)resp.StatusCode, "steam-tradeoffer-detail-redirect");

                    url = next;
                    continue;
                }

                string html = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    throw BuildHttpException(resp.StatusCode, html, "Steam 报价详情");
                if (LooksLikeLoginPage(html))
                {
                    LogWebLoginPageDiagnostic("Steam 报价详情返回登录页", html, resp.StatusCode, "");
                    throw new SteamAuthExpiredException("Steam 报价详情返回登录页，Steam 登录状态已失效。");
                }
                if (html.Contains("You have logged in from a new device. In order to protect the items", StringComparison.OrdinalIgnoreCase))
                    throw new SteamTransientSteamException("Steam 新设备登录保护中，暂时不能交易。", null, "steam-trade-seven-day-hold");

                return ExtractTradePartnerSteamId(html);
            }

            throw new SteamTransientSteamException("Steam 报价详情重定向次数过多，无法读取对方 ID。", null, "steam-tradeoffer-detail-too-many-redirects");
        }

        private async Task<HttpClient?> CreateOwnedClientIfNeededAsync(bool allowAutoRedirect = true)
        {
            if (_http != null)
                return null;

            var http = await _httpFactory.CreateResolvedAsync(
                8,
                decompression: DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                useCookies: false,
                allowAutoRedirect: allowAutoRedirect).ConfigureAwait(false);
            ApplyDefaultHeaders(http);
            return http;
        }

        private static void ApplyDefaultHeaders(HttpClient http)
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) CS2TradeMonitor/1.0");
        }

        private static TradeOffersResult ParseOffersResponse(JsonElement response)
        {
            var descriptions = ParseDescriptions(response);
            var result = new TradeOffersResult
            {
                Descriptions = descriptions
            };

            if (TryGetProperty(response, out var sent, "trade_offers_sent") && sent.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in sent.EnumerateArray())
                    result.SentOffers.Add(ParseOffer(item, descriptions, isOurOfferFallback: true));
            }

            if (TryGetProperty(response, out var received, "trade_offers_received") && received.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in received.EnumerateArray())
                    result.ReceivedOffers.Add(ParseOffer(item, descriptions, isOurOfferFallback: false));
            }

            return result;
        }

        private static Dictionary<string, TradeItemDescription> ParseDescriptions(JsonElement response)
        {
            var descriptions = new Dictionary<string, TradeItemDescription>(StringComparer.OrdinalIgnoreCase);
            if (!TryGetProperty(response, out var array, "descriptions") || array.ValueKind != JsonValueKind.Array)
                return descriptions;

            foreach (var item in array.EnumerateArray())
            {
                string classId = GetString(item, "classid");
                string instanceId = GetString(item, "instanceid");
                if (string.IsNullOrWhiteSpace(classId)) continue;
                string key = BuildDescriptionKey(classId, instanceId);
                descriptions[key] = new TradeItemDescription
                {
                    MarketHashName = GetString(item, "name", "market_name", "market_hash_name"),
                    IconUrl = BuildIconUrl(GetString(item, "icon_url")),
                    Type = GetString(item, "type")
                };
            }

            return descriptions;
        }

        private static Dictionary<string, TradeItemDescription> ParseClassInfoDescriptions(JsonElement root)
        {
            var descriptions = new Dictionary<string, TradeItemDescription>(StringComparer.OrdinalIgnoreCase);
            JsonElement effective = root;
            if (TryGetProperty(root, out var result, "result"))
                effective = result;

            CollectClassInfoDescriptions(effective, descriptions);
            return descriptions;
        }

        private static void CollectClassInfoDescriptions(JsonElement element, Dictionary<string, TradeItemDescription> descriptions)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                string classId = GetString(element, "classid", "class_id");
                string instanceId = GetString(element, "instanceid", "instance_id");
                string name = GetString(element, "name", "market_name", "market_hash_name");
                string icon = GetString(element, "icon_url", "icon_url_large", "icon");
                if (!string.IsNullOrWhiteSpace(classId)
                    && (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(icon)))
                {
                    descriptions[BuildDescriptionKey(classId, instanceId)] = new TradeItemDescription
                    {
                        MarketHashName = name,
                        IconUrl = BuildIconUrl(icon),
                        Type = GetString(element, "type")
                    };
                }

                foreach (var property in element.EnumerateObject())
                    CollectClassInfoDescriptions(property.Value, descriptions);
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                    CollectClassInfoDescriptions(item, descriptions);
            }
        }

        private static TradeOfferDetail ParseOffer(JsonElement item, IReadOnlyDictionary<string, TradeItemDescription> descriptions, bool isOurOfferFallback)
        {
            string tradeOfferId = GetString(item, "tradeofferid", "trade_offer_id");
            var offer = new TradeOfferDetail
            {
                TradeOfferId = tradeOfferId,
                PartnerSteamId = AccountIdToSteamId64(GetString(item, "accountid_other")),
                Message = GetString(item, "message"),
                TradeOfferState = GetInt(item, "trade_offer_state", "tradeofferstate"),
                IsOurOffer = GetBool(item, "is_our_offer") || isOurOfferFallback,
                TimeCreated = FromUnix(GetLong(item, "time_created")),
                TimeUpdated = FromUnix(GetLong(item, "time_updated")),
                ExpirationTime = FromUnix(GetLong(item, "expiration_time"))
            };

            if (TryGetProperty(item, out var give, "items_to_give") && give.ValueKind == JsonValueKind.Array)
                offer.ItemsToGive.AddRange(ParseAssets(give, descriptions));
            if (TryGetProperty(item, out var receive, "items_to_receive") && receive.ValueKind == JsonValueKind.Array)
                offer.ItemsToReceive.AddRange(ParseAssets(receive, descriptions));

            return offer;
        }

        private static IEnumerable<TradeAsset> ParseAssets(JsonElement array, IReadOnlyDictionary<string, TradeItemDescription> descriptions)
        {
            foreach (var item in array.EnumerateArray())
            {
                string classId = GetString(item, "classid");
                string instanceId = GetString(item, "instanceid");
                descriptions.TryGetValue(BuildDescriptionKey(classId, instanceId), out var desc);

                yield return new TradeAsset
                {
                    AppId = GetLong(item, "appid"),
                    ContextId = GetLong(item, "contextid"),
                    AssetId = GetString(item, "assetid", "id"),
                    ClassId = classId,
                    InstanceId = instanceId,
                    Amount = Math.Max(1, GetInt(item, "amount")),
                    MarketHashName = desc?.MarketHashName ?? "",
                    IconUrl = desc?.IconUrl ?? ""
                };
            }
        }

        private static void ApplySteamCookies(
            HttpRequestMessage req,
            SteamAuthCredential credential,
            string referer = SteamUrls.CommunityBase + "/tradeoffer/",
            IReadOnlyDictionary<string, string>? extraCookies = null)
        {
            // Do not log cookies or credentials.
            var webCookies = new SteamWebCookies
            {
                SessionId = credential.SessionId,
                SteamLoginSecure = credential.SteamLoginSecure,
                SteamLogin = credential.SteamLogin
            };
            string cookieHeader = SteamLoginCookieHelper.BuildCookieHeader(webCookies);
            if (extraCookies != null)
            {
                var parts = new List<string> { cookieHeader };
                foreach (var cookie in extraCookies)
                {
                    if (!string.IsNullOrWhiteSpace(cookie.Key) && !string.IsNullOrWhiteSpace(cookie.Value))
                        parts.Add(cookie.Key + "=" + SteamCookieValue.Encode(cookie.Value));
                }
                cookieHeader = string.Join("; ", parts);
            }

            if (!string.IsNullOrWhiteSpace(cookieHeader))
                req.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            req.Headers.TryAddWithoutValidation("Referer", referer);
        }

        private static void CaptureSetCookies(HttpResponseMessage response, Dictionary<string, string> cookies)
        {
            if (!response.Headers.TryGetValues("Set-Cookie", out var values))
                return;

            foreach (string value in values)
            {
                string pair = (value ?? "").Split(';', 2)[0].Trim();
                int equals = pair.IndexOf('=');
                if (equals <= 0)
                    continue;

                string name = pair[..equals].Trim();
                string cookieValue = pair[(equals + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (string.IsNullOrEmpty(cookieValue))
                    cookies.Remove(name);
                else
                    cookies[name] = cookieValue;
            }
        }

        private void FillLocalWebAssetNames(TradeOffersResult result)
        {
            foreach (var asset in result.SentOffers
                         .Concat(result.ReceivedOffers)
                         .SelectMany(offer => offer.ItemsToGive.Concat(offer.ItemsToReceive)))
            {
                string current = (asset.MarketHashName ?? "").Trim();
                if (string.IsNullOrWhiteSpace(current) || SteamAssetNameCompletionHelper.IsPlaceholderName(current))
                    continue;

                string localName = _localItemNameResolver(current);
                if (!string.IsNullOrWhiteSpace(localName))
                    asset.MarketHashName = localName;
            }
        }

        private static void FillClassInfoDescriptions(TradeOffersResult result)
        {
            foreach (var asset in result.SentOffers
                         .Concat(result.ReceivedOffers)
                         .SelectMany(offer => offer.ItemsToGive.Concat(offer.ItemsToReceive)))
            {
                if (!NeedsClassInfoLookup(asset))
                    continue;
                if (!TryGetDescription(result.Descriptions, asset.ClassId, asset.InstanceId, out var desc))
                    continue;

                if (SteamAssetNameCompletionHelper.ShouldReplaceWithDescription(asset.MarketHashName, desc.MarketHashName))
                    asset.MarketHashName = desc.MarketHashName;
                if ((string.IsNullOrWhiteSpace(asset.IconUrl) || IsPlaceholderIconUrl(asset.IconUrl))
                    && !string.IsNullOrWhiteSpace(desc.IconUrl))
                    asset.IconUrl = desc.IconUrl;
            }
        }

        private static bool TryGetDescription(
            IReadOnlyDictionary<string, TradeItemDescription> descriptions,
            string classId,
            string instanceId,
            out TradeItemDescription description)
        {
            if (descriptions.TryGetValue(BuildDescriptionKey(classId, instanceId), out description!))
                return true;
            if (descriptions.TryGetValue(BuildDescriptionKey(classId, "0"), out description!))
                return true;
            return false;
        }

        private static bool NeedsClassInfoLookup(TradeAsset asset)
        {
            return SteamAssetNameCompletionHelper.NeedsExternalLookup(asset);
        }

        private static bool IsPlaceholderIconUrl(string iconUrl)
        {
            return (iconUrl ?? "").Contains("/trans.gif", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildTradeOfferReferer(string tradeOfferId)
        {
            return SteamUrls.TradeOfferReferer(tradeOfferId);
        }

        private static string BuildTradeOfferDetailUrl(string tradeOfferId)
        {
            return BuildTradeOfferReferer(tradeOfferId) + "?l=schinese";
        }

        private static string ExtractTradePartnerSteamId(string html)
        {
            var match = Regex.Match(
                html ?? "",
                @"\bg_ulTradePartnerSteamID\s*=\s*['""]?(?<id>\d{16,20})['""]?",
                RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(200));
            return match.Success ? match.Groups["id"].Value.Trim() : "";
        }

        private static bool LooksLikeAlreadyHandledTradeOffer(string text)
        {
            return ContainsAny(
                text,
                "Invalid trade offer state",
                "Trade offer does not exist",
                "already been accepted",
                "already accepted",
                "already been canceled",
                "already been cancelled",
                "expired",
                "canceled",
                "cancelled",
                "declined",
                "报价已失效",
                "报价不存在",
                "已经接受",
                "已接受",
                "已取消",
                "已拒绝");
        }

        private static bool ContainsAny(string text, params string[] needles)
        {
            string value = text ?? "";
            return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        private static void EnsureApiKey(SteamAuthCredential credential)
        {
            if (string.IsNullOrWhiteSpace(credential.ApiKey))
                throw new InvalidOperationException("未配置 Steam Web API Key，无法读取完整报价详情。");
        }

        private static string AccountIdToSteamId64(string accountIdText)
        {
            if (!ulong.TryParse(accountIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong accountId))
                return "";
            const ulong steamIdBase = 76561197960265728UL;
            return (steamIdBase + accountId).ToString(CultureInfo.InvariantCulture);
        }

        private static string BuildDescriptionKey(string classId, string instanceId)
        {
            return (classId ?? "").Trim() + "_" + (string.IsNullOrWhiteSpace(instanceId) ? "0" : instanceId.Trim());
        }

        private static string BuildIconUrl(string iconPath)
        {
            iconPath = (iconPath ?? "").Trim();
            if (string.IsNullOrWhiteSpace(iconPath)) return "";
            if (iconPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || iconPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return iconPath;
            return SteamUrls.EconomyImage(iconPath);
        }

        private static DateTime FromUnix(long unixSeconds)
        {
            if (unixSeconds <= 0) return DateTime.MinValue;
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
        {
            value = default;
            if (element.ValueKind != JsonValueKind.Object) return false;
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out value))
                    return true;
            }
            foreach (var prop in element.EnumerateObject())
            {
                if (names.Any(name => prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    value = prop.Value;
                    return true;
                }
            }
            return false;
        }

        private static string GetString(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out var value, names)) return "";
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
        }

        private static int GetInt(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out var value, names)) return 0;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)) return number;
            return int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ? number : 0;
        }

        private static long GetLong(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out var value, names)) return 0;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number)) return number;
            return long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ? number : 0;
        }

        private static bool GetBool(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out var value, names)) return false;
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)) return number != 0;
            return bool.TryParse(value.ToString(), out bool result) && result;
        }

        private static IEnumerable<string> BuildWebTradeOffersUrls(SteamAuthCredential credential)
        {
            string steamId = (credential.SteamId ?? "").Trim();
            string baseUrl = string.IsNullOrWhiteSpace(steamId)
                ? SteamUrls.MyTradeOffers
                : SteamUrls.ProfileTradeOffers(steamId);

            yield return baseUrl + "/?l=schinese";
            yield return baseUrl + "/sent/?l=schinese";
        }

        private static bool IsSentTradeOffersPage(Uri url)
        {
            return (url?.AbsolutePath ?? "").Contains("/tradeoffers/sent", StringComparison.OrdinalIgnoreCase);
        }

        private static void LogWebTradeOffersParseDiagnostic(Uri url, string html, TradeOffersResult result, bool isSentPage)
        {
            string page = isSentPage ? "sent" : "active";
            string ids = string.Join(
                ",",
                result.SentOffers.Concat(result.ReceivedOffers)
                    .Select(x => (x.TradeOfferId ?? "").Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Take(8));
            SteamOfferAuditLog.InfoThrottled(
                "steam-web-tradeoffers-parse:" + page,
                $"Steam web tradeoffers parsed. Page={page}; Sent={result.SentOffers.Count}; Received={result.ReceivedOffers.Count}; HasTradeOfferSignal={ContainsAny(html, "tradeoffer", "trade_item")}; HasEconomyItem={ContainsAny(html, "data-economy-item")}; Ids={ids}",
                TimeSpan.FromMinutes(2));
        }

        private static void MergeTradeOffersResult(TradeOffersResult target, TradeOffersResult source)
        {
            foreach (var description in source.Descriptions)
                target.Descriptions[description.Key] = description.Value;
            AddUniqueTradeOfferDetails(target.SentOffers, source.SentOffers);
            AddUniqueTradeOfferDetails(target.ReceivedOffers, source.ReceivedOffers);
        }

        private static void AddUniqueTradeOfferDetails(List<TradeOfferDetail> target, IEnumerable<TradeOfferDetail> source)
        {
            var existing = new HashSet<string>(
                target.Select(x => (x.TradeOfferId ?? "").Trim()).Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var offer in source)
            {
                string id = (offer.TradeOfferId ?? "").Trim();
                if (string.IsNullOrWhiteSpace(id))
                {
                    target.Add(offer);
                    continue;
                }

                if (!existing.Add(id))
                    continue;
                target.Add(offer);
            }
        }

        private static TradeOffersResult ParseWebOffersRoot(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (TryGetProperty(root, out var response, "response") && response.ValueKind == JsonValueKind.Object)
                    return ParseOffersResponse(response);
                if (TryGetProperty(root, out _, "trade_offers_sent", "trade_offers_received"))
                    return ParseOffersResponse(root);
            }

            throw new SteamTransientSteamException("Steam 网页报价数据格式暂不支持解析。", null, "web-tradeoffers-unparsed");
        }

        private static string ExtractJavascriptAssignmentJson(string html, string variableName)
        {
            string text = html ?? "";
            int index = text.IndexOf(variableName, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                int equals = text.IndexOf('=', index + variableName.Length);
                if (equals < 0)
                    return "";

                int start = -1;
                for (int i = equals + 1; i < text.Length; i++)
                {
                    if (text[i] == '{' || text[i] == '[')
                    {
                        start = i;
                        break;
                    }
                    if (text[i] == ';')
                        break;
                }

                string json = start >= 0 ? ReadBalancedJson(text, start) : "";
                if (!string.IsNullOrWhiteSpace(json))
                    return json;

                index = text.IndexOf(variableName, index + variableName.Length, StringComparison.OrdinalIgnoreCase);
            }

            return "";
        }

        private static string ReadBalancedJson(string text, int start)
        {
            char open = text[start];
            char close = open == '{' ? '}' : ']';
            int depth = 0;
            bool inString = false;
            bool escaped = false;
            char quote = '\0';
            for (int i = start; i < text.Length; i++)
            {
                char ch = text[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }
                    if (ch == '\\')
                    {
                        escaped = true;
                        continue;
                    }
                    if (ch == quote)
                        inString = false;
                    continue;
                }

                if (ch == '"' || ch == '\'')
                {
                    inString = true;
                    quote = ch;
                    continue;
                }
                if (ch == open)
                    depth++;
                else if (ch == close)
                {
                    depth--;
                    if (depth == 0)
                        return text[start..(i + 1)];
                }
            }

            return "";
        }

        private static bool LooksLikeLoginPage(string html)
        {
            string text = html ?? "";
            string steamId = ExtractSteamIdMarker(text);
            if (!string.IsNullOrWhiteSpace(steamId))
                return IsMissingSteamIdMarker(steamId);

            if (HasTradeOffersPageSignal(text))
                return false;

            return HasStrongLoginPageSignal(text);
        }

        private static bool IsRedirect(HttpStatusCode statusCode)
        {
            int code = (int)statusCode;
            return code >= 300 && code < 400;
        }

        private static bool IsLoginRedirect(string location)
        {
            if (!TryBuildRedirectUri(location, out Uri? uri) || uri == null)
                return false;

            if (string.Equals(uri.Host, "login.steampowered.com", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.Equals(uri.Host, "steamcommunity.com", StringComparison.OrdinalIgnoreCase))
                return false;

            string path = uri.AbsolutePath.TrimEnd('/');
            return path.Equals("/login", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/login/", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/openid/login", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractSteamIdMarker(string html)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                html ?? "",
                @"\bg_steamid\s*=\s*(?<value>[^;\r\n]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(200));
            if (!match.Success)
                return "";

            return match.Groups["value"].Value.Trim().Trim('"', '\'');
        }

        private static bool IsMissingSteamIdMarker(string value)
        {
            string text = (value ?? "").Trim();
            return text.Length == 0
                || text.Equals("false", StringComparison.OrdinalIgnoreCase)
                || text.Equals("null", StringComparison.OrdinalIgnoreCase)
                || text.Equals("undefined", StringComparison.OrdinalIgnoreCase)
                || text.Equals("0", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasTradeOffersPageSignal(string html)
        {
            string text = html ?? "";
            return text.Contains("g_rgCurrentTradeOffers", StringComparison.OrdinalIgnoreCase)
                || text.Contains("g_rgTradeOffers", StringComparison.OrdinalIgnoreCase)
                || text.Contains("class=\"tradeoffer", StringComparison.OrdinalIgnoreCase)
                || text.Contains("class='tradeoffer", StringComparison.OrdinalIgnoreCase)
                || text.Contains("data-tradeofferid", StringComparison.OrdinalIgnoreCase)
                || text.Contains("data-economy-item", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasStrongLoginPageSignal(string html)
        {
            string text = html ?? "";
            bool hasLoginHost = text.Contains("steamcommunity.com/login", StringComparison.OrdinalIgnoreCase)
                || text.Contains("login.steampowered.com", StringComparison.OrdinalIgnoreCase);
            bool hasLoginForm = text.Contains("loginForm", StringComparison.OrdinalIgnoreCase)
                || text.Contains("openid/login", StringComparison.OrdinalIgnoreCase)
                || text.Contains("name=\"password\"", StringComparison.OrdinalIgnoreCase)
                || text.Contains("type=\"password\"", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Sign In", StringComparison.OrdinalIgnoreCase);
            return hasLoginHost && hasLoginForm;
        }

        private static bool TryBuildRedirectUri(string location, out Uri? uri)
        {
            uri = null;
            string text = (location ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
                return false;
            if (Uri.TryCreate(text, UriKind.Absolute, out uri))
                return true;
            if (text.StartsWith("/", StringComparison.Ordinal) && Uri.TryCreate(new Uri(SteamUrls.CommunityBase), text, out uri))
                return true;
            return false;
        }

        private static void LogWebLoginPageDiagnostic(string reason, string html, HttpStatusCode statusCode, string location)
        {
            string steamId = ExtractSteamIdMarker(html);
            string marker = string.IsNullOrWhiteSpace(steamId)
                ? "missing"
                : IsMissingSteamIdMarker(steamId) ? "not-logged-in" : "present";
            bool hasTradeOffersSignal = HasTradeOffersPageSignal(html);
            string cleanLocation = SanitizeRedirectLocation(location);
            SteamOfferAuditLog.InfoThrottled(
                "steam-web-login-page-diagnostic:" + reason,
                $"{reason}. HttpStatus={(int)statusCode}; Location={cleanLocation}; GSteamId={marker}; HasTradeOffersSignal={hasTradeOffersSignal}",
                TimeSpan.FromMinutes(5));
        }

        private static string SanitizeRedirectLocation(string location)
        {
            if (!TryBuildRedirectUri(location, out Uri? uri) || uri == null)
                return string.IsNullOrWhiteSpace(location) ? "" : "unparsed";
            return uri.Host + uri.AbsolutePath;
        }

        private static Uri? ResolveSteamCommunityRedirect(Uri currentUrl, Uri? location)
        {
            if (location == null)
                return null;

            Uri next = location.IsAbsoluteUri ? location : new Uri(currentUrl, location);
            return string.Equals(next.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && string.Equals(next.Host, "steamcommunity.com", StringComparison.OrdinalIgnoreCase)
                ? next
                : null;
        }

        private static string FirstText(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        private static Exception BuildHttpException(HttpStatusCode statusCode, string body, string step)
        {
            string summary = BuildBodySummary(body);
            int code = (int)statusCode;
            if (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden)
                return new SteamAuthExpiredException($"{step}返回 HTTP {code}，Steam 登录状态已失效。", code);
            if (statusCode == HttpStatusCode.TooManyRequests)
                return new SteamTransientSteamException($"{step}被 Steam 限流：HTTP 429，请稍后再试。响应摘要：{summary}", code, "steam-rate-limited");
            return new SteamTransientSteamException($"{step}暂时不可用：HTTP {code}，响应摘要：{summary}", code);
        }

        private static string BuildBodySummary(string body)
        {
            string clean = SteamOfferAuditLog.RedactSecrets(body ?? "");
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+", " ").Trim();
            return clean.Length <= 180 ? clean : clean[..180] + "...";
        }

        private sealed record AssetClassLookupKey(long AppId, string ClassId, string InstanceId);
    }

}
