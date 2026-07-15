using CS2TradeMonitor.Domain.Steam;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CS2TradeMonitor.Application.Steam
{
    internal static class SteamTradeOfferWebHtmlParser
    {
        public static TradeOffersResult Parse(string html, bool forceSentOffers = false)
        {
            var result = new TradeOffersResult();
            if (string.IsNullOrWhiteSpace(html))
                return result;

            foreach (var description in ExtractWebDescriptions(html))
                result.Descriptions[description.Key] = description.Value;

            foreach (string offerHtml in ExtractWebOfferBlocks(html))
            {
                var detail = ParseWebOfferHtml(offerHtml, forceSentOffers);
                if (detail == null)
                    continue;
                if (string.IsNullOrWhiteSpace(detail.TradeOfferId)
                    && (!forceSentOffers || (detail.ItemsToGive.Count == 0 && detail.ItemsToReceive.Count == 0)))
                    continue;

                if (detail.IsOurOffer)
                    result.SentOffers.Add(detail);
                else
                    result.ReceivedOffers.Add(detail);
            }

            if (forceSentOffers
                && result.SentOffers.Count == 0
                && html.Contains("data-economy-item", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var detail in ParseAnonymousSentItemGroups(html))
                    result.SentOffers.Add(detail);
            }

            FillWebAssetDescriptions(result);
            return result;
        }

        public static TradeOfferDetail? ParseTradeOfferDetailPage(string html, string tradeOfferId, bool forceSentOffer)
        {
            if (string.IsNullOrWhiteSpace(html))
                return null;

            var descriptions = ExtractWebDescriptions(html);
            var detail = new TradeOfferDetail
            {
                TradeOfferId = (tradeOfferId ?? "").Trim(),
                PartnerSteamId = ExtractWebOfferPartnerSteamId(html),
                PartnerName = ExtractWebOfferPartnerName(html),
                Message = ExtractWebOfferStatusText(html),
                TradeOfferState = MapTradeOfferDetailPageState(html),
                IsOurOffer = forceSentOffer,
                TimeCreated = ExtractWebOfferTimestamp(html)
            };

            var lists = ExtractWebOfferItemLists(html);
            ApplyWebOfferItemLists(detail, lists, forceSentOffer, detail.Message);
            if (detail.ItemsToGive.Count == 0 && detail.ItemsToReceive.Count == 0)
            {
                var assets = ExtractWebOfferAssetsFromEconomyAttributes(html);
                if (forceSentOffer)
                    detail.ItemsToGive.AddRange(assets);
                else
                    detail.ItemsToReceive.AddRange(assets);
            }

            FillWebAssetDescriptions(detail.ItemsToGive, descriptions);
            FillWebAssetDescriptions(detail.ItemsToReceive, descriptions);
            return detail.ItemsToGive.Count > 0 || detail.ItemsToReceive.Count > 0
                ? detail
                : null;
        }

        private static int MapTradeOfferDetailPageState(string html)
        {
            string status = ExtractWebOfferStatusText(html);
            string raw = html ?? "";
            if (LooksLikeMobileConfirmationState(status)
                || HasTradeOfferStateClass(raw, "tradeoffer_needs_confirmation", "tradeoffer_mobile_confirmation"))
                return 9;
            if (ContainsAny(status, "交易暂挂", "暂挂", "In Escrow", "Escrow", "Trade Hold")
                || HasTradeOfferStateClass(raw, "tradeoffer_escrow", "tradeoffer_in_escrow"))
                return 11;
            if (ContainsAny(status, "二次验证取消", "CanceledBySecondFactor", "Canceled By Second Factor")
                || HasTradeOfferStateClass(raw, "tradeoffer_canceled_by_second_factor"))
                return 10;
            if (ContainsAny(status, "无效物品", "Invalid Items", "InvalidItems")
                || HasTradeOfferStateClass(raw, "tradeoffer_invalid_items"))
                return 8;
            if (ContainsAny(status, "已拒绝", "Declined", "Rejected")
                || HasTradeOfferStateClass(raw, "tradeoffer_declined"))
                return 7;
            if (ContainsAny(status, "已取消", "Canceled", "Cancelled")
                || HasTradeOfferStateClass(raw, "tradeoffer_canceled", "tradeoffer_cancelled"))
                return 6;
            if (ContainsAny(status, "已过期", "Expired")
                || HasTradeOfferStateClass(raw, "tradeoffer_expired"))
                return 5;
            if (ContainsAny(status, "已还价", "Countered", "Counter Offer")
                || HasTradeOfferStateClass(raw, "tradeoffer_countered"))
                return 4;
            if (ContainsAny(status, "已接受", "已同意", "已完成", "Accepted", "Completed")
                || HasTradeOfferStateClass(raw, "tradeoffer_accepted"))
                return 3;
            return 2;
        }

        private static bool HasTradeOfferStateClass(string html, params string[] classNames)
        {
            foreach (string className in classNames)
            {
                string pattern = "class\\s*=\\s*[\"'][^\"']*(?<![A-Za-z0-9_-])"
                    + Regex.Escape(className)
                    + "(?![A-Za-z0-9_-])[^\"']*[\"']";
                if (Regex.IsMatch(html ?? "", pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    return true;
            }

            return false;
        }

        public static string ExtractTradeOfferIdFromHtml(string html)
        {
            return ExtractTradeOfferId(html);
        }

        private static IEnumerable<string> ExtractWebOfferBlocks(string html)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string offerHtml in ExtractDivsByClass(html, "tradeoffer"))
            {
                string id = ExtractTradeOfferId(offerHtml);
                string key = string.IsNullOrWhiteSpace(id) ? offerHtml : id;
                if (seen.Add(key))
                    yield return offerHtml;
            }

            foreach (string offerHtml in ExtractDivsByTradeOfferId(html))
            {
                string id = ExtractTradeOfferId(offerHtml);
                string key = string.IsNullOrWhiteSpace(id) ? offerHtml : id;
                if (seen.Add(key))
                    yield return offerHtml;
            }

            foreach (string offerHtml in ExtractTradeOfferContainersFromEconomyItems(html))
            {
                string id = ExtractTradeOfferId(offerHtml);
                string key = string.IsNullOrWhiteSpace(id) ? offerHtml : id;
                if (seen.Add(key))
                    yield return offerHtml;
            }

            foreach (string offerHtml in ExtractDivsByClassPrefix(html, "tradeoffer"))
            {
                if (!offerHtml.Contains("data-economy-item", StringComparison.OrdinalIgnoreCase))
                    continue;

                string id = ExtractTradeOfferId(offerHtml);
                string key = string.IsNullOrWhiteSpace(id) ? offerHtml : id;
                if (seen.Add(key))
                    yield return offerHtml;
            }
        }

        private static TradeOfferDetail? ParseWebOfferHtml(string offerHtml, bool forceSentOffer)
        {
            string tradeOfferId = ExtractTradeOfferId(offerHtml);
            string statusText = ExtractWebOfferStatusText(offerHtml);
            if (LooksLikeTerminalWebOfferState(offerHtml, statusText))
                return null;

            bool isOurOffer = forceSentOffer || LooksLikeSentWebOffer(offerHtml, statusText);
            var detail = new TradeOfferDetail
            {
                TradeOfferId = tradeOfferId,
                PartnerSteamId = ExtractWebOfferPartnerSteamId(offerHtml),
                PartnerName = ExtractWebOfferPartnerName(offerHtml),
                Message = statusText,
                TradeOfferState = LooksLikeMobileConfirmationState(statusText) || LooksLikeMobileConfirmationState(offerHtml) ? 9 : 2,
                IsOurOffer = isOurOffer,
                TimeCreated = ExtractWebOfferTimestamp(offerHtml)
            };

            var lists = ExtractWebOfferItemLists(offerHtml);
            ApplyWebOfferItemLists(detail, lists, isOurOffer, statusText);
            if (string.IsNullOrWhiteSpace(tradeOfferId) && (!forceSentOffer || (detail.ItemsToGive.Count == 0 && detail.ItemsToReceive.Count == 0)))
                return null;

            return detail;
        }

        private static List<WebOfferItemList> ExtractWebOfferItemLists(string offerHtml)
        {
            var lists = new List<WebOfferItemList>();
            foreach (string listHtml in ExtractDivsByClass(offerHtml, "tradeoffer_item_list"))
            {
                var assets = ExtractWebOfferAssets(listHtml);
                if (assets.Count == 0)
                    continue;

                string label = ExtractVisibleText(GetContextBefore(offerHtml, listHtml, 260));
                var direction = InferWebOfferItemDirection(label);
                lists.Add(new WebOfferItemList(direction, assets));
            }

            if (lists.Count == 0)
            {
                foreach (string listHtml in ExtractDivsByClass(offerHtml, "tradeoffer_items_ctn"))
                {
                    var assets = ExtractWebOfferAssets(listHtml);
                    if (assets.Count == 0)
                        continue;

                    string label = ExtractVisibleText(GetContextBefore(offerHtml, listHtml, 260));
                    var direction = InferWebOfferItemDirection(label);
                    lists.Add(new WebOfferItemList(direction, assets));
                }
            }

            return lists;
        }

        private static void ApplyWebOfferItemLists(
            TradeOfferDetail detail,
            IReadOnlyList<WebOfferItemList> lists,
            bool isOurOffer,
            string statusText)
        {
            if (lists.Count == 0)
                return;

            if (lists.Count == 2)
            {
                // Steam tradeoffers DOM renders the partner side first, then our side.
                detail.ItemsToReceive.AddRange(lists[0].Assets);
                detail.ItemsToGive.AddRange(lists[1].Assets);
                return;
            }

            bool hasExplicitDirection = lists.Any(x => x.Direction != WebOfferItemDirection.Unknown);
            for (int i = 0; i < lists.Count; i++)
            {
                var list = lists[i];
                if (list.Direction == WebOfferItemDirection.Give)
                    detail.ItemsToGive.AddRange(list.Assets);
                else if (list.Direction == WebOfferItemDirection.Receive)
                    detail.ItemsToReceive.AddRange(list.Assets);
                else if (hasExplicitDirection)
                    AddListByDomOrder(detail, lists, i, isOurOffer);
            }

            if (hasExplicitDirection)
                return;

            if (lists.Count >= 2)
            {
                detail.ItemsToReceive.AddRange(lists[0].Assets);
                detail.ItemsToGive.AddRange(lists[1].Assets);
                for (int i = 2; i < lists.Count; i++)
                    AddFallbackWebOfferAssets(detail, lists[i].Assets, isOurOffer);
                return;
            }

            AddFallbackWebOfferAssets(detail, lists[0].Assets, isOurOffer);
        }

        private static void AddListByDomOrder(TradeOfferDetail detail, IReadOnlyList<WebOfferItemList> lists, int index, bool isOurOffer)
        {
            if (index < 0 || index >= lists.Count)
                return;

            if (lists.Count >= 2)
            {
                if (index == 0)
                    detail.ItemsToReceive.AddRange(lists[index].Assets);
                else if (index == 1)
                    detail.ItemsToGive.AddRange(lists[index].Assets);
                else
                    AddFallbackWebOfferAssets(detail, lists[index].Assets, isOurOffer);
                return;
            }

            AddFallbackWebOfferAssets(detail, lists[index].Assets, isOurOffer);
        }

        private static void AddFallbackWebOfferAssets(TradeOfferDetail detail, IEnumerable<TradeAsset> assets, bool preferGive)
        {
            if (preferGive)
                detail.ItemsToGive.AddRange(assets);
            else
                detail.ItemsToReceive.AddRange(assets);
        }

        private static List<TradeAsset> ExtractWebOfferAssets(string html)
        {
            var assets = new List<TradeAsset>();
            foreach (var item in ExtractDivBlocksByClass(html, "trade_item"))
            {
                var asset = CreateWebOfferAsset(item.Html, GetContextAround(html, item.Index, item.Html.Length, 1400, 1400));
                if (asset != null)
                    assets.Add(asset);
            }

            if (assets.Count == 0)
                assets.AddRange(ExtractWebOfferAssetsFromEconomyAttributes(html));

            return assets;
        }

        private static IEnumerable<string> ExtractTradeOfferContainersFromEconomyItems(string html)
        {
            string text = html ?? "";
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var itemMatches = Regex.Matches(
                text,
                @"\bdata-economy-item\s*=",
                RegexOptions.IgnoreCase | RegexOptions.Singleline,
                TimeSpan.FromMilliseconds(200));

            foreach (Match itemMatch in itemMatches)
            {
                string container = FindContainingTradeOfferContainerForIndex(text, itemMatch.Index);
                if (string.IsNullOrWhiteSpace(container))
                    continue;
                if (seen.Add(container))
                    yield return container;
            }
        }

        private static string FindContainingTradeOfferContainerForIndex(string html, int itemIndex)
        {
            int windowStart = Math.Max(0, itemIndex - 12000);
            string prefix = html[windowStart..itemIndex];
            var divStarts = Regex.Matches(
                    prefix,
                    @"<div\b[^>]*>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline,
                    TimeSpan.FromMilliseconds(200))
                .Cast<Match>()
                .Select(match => windowStart + match.Index)
                .Reverse()
                .Take(100)
                .ToList();

            string bestContainer = "";
            foreach (int start in divStarts)
            {
                string div = ReadBalancedHtmlDiv(html, start);
                if (string.IsNullOrWhiteSpace(div))
                    continue;
                if (start + div.Length <= itemIndex)
                    continue;
                if (!div.Contains("data-economy-item", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (ContainsTradeOfferIdSignal(div))
                    return div;

                string tag = GetFirstTag(div);
                if (string.IsNullOrWhiteSpace(bestContainer)
                    && TagHasClassPrefix(tag, "tradeoffer")
                    && !TagHasTradeOfferItemGroupClass(tag))
                {
                    bestContainer = div;
                }
            }

            return bestContainer;
        }

        private static IEnumerable<TradeOfferDetail> ParseAnonymousSentItemGroups(string html)
        {
            var groups = new List<string>();
            AddAnonymousSentGroups(groups, ExtractDivsByClass(html, "tradeoffer_item_list"));
            if (groups.Count == 0)
                AddAnonymousSentGroups(groups, ExtractDivsByClass(html, "tradeoffer_items_ctn"));
            if (groups.Count == 0)
                groups.AddRange(ExtractDataEconomyItemElements(html));

            foreach (string groupHtml in groups)
            {
                var assets = ExtractWebOfferAssets(groupHtml);
                if (assets.Count == 0)
                    continue;

                var detail = new TradeOfferDetail
                {
                    TradeOfferId = "",
                    Message = "正在等待手机确认",
                    TradeOfferState = 9,
                    IsOurOffer = true,
                    TimeCreated = ExtractWebOfferTimestamp(groupHtml)
                };
                detail.ItemsToGive.AddRange(assets);
                yield return detail;
            }
        }

        private static void AddAnonymousSentGroups(List<string> groups, IEnumerable<string> candidates)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string candidate in candidates)
            {
                if (!candidate.Contains("data-economy-item", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (seen.Add(candidate))
                    groups.Add(candidate);
            }
        }

        private static List<TradeAsset> ExtractWebOfferAssetsFromEconomyAttributes(string html)
        {
            var assets = new List<TradeAsset>();
            foreach (string itemHtml in ExtractDataEconomyItemElements(html))
            {
                var asset = CreateWebOfferAsset(itemHtml, GetContextAround(html, html.IndexOf(itemHtml, StringComparison.Ordinal), itemHtml.Length, 1400, 1400));
                if (asset != null)
                    assets.Add(asset);
            }

            return assets;
        }

        private static IEnumerable<string> ExtractDataEconomyItemElements(string html)
        {
            var matches = Regex.Matches(
                html ?? "",
                @"<(?<tag>[a-zA-Z0-9]+)\b(?=[^>]*\bdata-economy-item\s*=)[^>]*(?:>.*?</\k<tag>>|/?>)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline,
                TimeSpan.FromMilliseconds(200));
            foreach (Match match in matches)
                yield return match.Value;
        }

        private static TradeAsset? CreateWebOfferAsset(string itemHtml, string contextHtml = "")
        {
            string economyItem = HtmlDecode(GetHtmlAttribute(GetFirstTag(itemHtml), "data-economy-item"));
            if (string.IsNullOrWhiteSpace(economyItem) && !itemHtml.Contains("trade_item", StringComparison.OrdinalIgnoreCase))
                return null;

            var (appId, contextId, classId, instanceId, assetId) = ParseEconomyItem(economyItem);
            string image = FirstText(
                GetHtmlAttribute(GetFirstTagByName(itemHtml, "img"), "src"),
                GetHtmlAttribute(GetFirstTagByName(itemHtml, "img"), "data-src"));
            string name = ExtractWebOfferItemName(itemHtml, contextHtml);
            if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(classId))
                name = SteamAssetNameCompletionHelper.PendingSteamItemName;

            return new TradeAsset
            {
                AppId = appId,
                ContextId = contextId,
                AssetId = assetId,
                ClassId = classId,
                InstanceId = instanceId,
                Amount = 1,
                MarketHashName = name,
                IconUrl = NormalizeWebImageUrl(image)
            };
        }

        private static Dictionary<string, TradeItemDescription> ExtractWebDescriptions(string html)
        {
            var descriptions = new Dictionary<string, TradeItemDescription>(StringComparer.OrdinalIgnoreCase);
            foreach (string json in ExtractJavascriptAssignmentJson(html, "g_rgDescriptions", "rgDescriptions", "g_rgAssets", "rgAssets"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    CollectWebDescriptions(doc.RootElement, descriptions, "");
                }
                catch
                {
                    // Steam occasionally embeds non-critical JS snippets around these globals.
                    // If one description block is not valid JSON, keep parsing the rest of the page.
                }
            }

            return descriptions;
        }

        private static void CollectWebDescriptions(
            JsonElement element,
            Dictionary<string, TradeItemDescription> descriptions,
            string propertyPath)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return;

            string classId = GetJsonString(element, "classid", "class_id");
            string instanceId = GetJsonString(element, "instanceid", "instance_id");
            if (string.IsNullOrWhiteSpace(classId))
            {
                var keyParts = propertyPath.Split(new[] { '_', '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (keyParts.Length >= 2)
                {
                    classId = keyParts[^2];
                    instanceId = keyParts[^1];
                }
            }

            string name = FirstText(
                GetJsonString(element, "name"),
                GetJsonString(element, "market_name"),
                GetJsonString(element, "market_hash_name"));
            string iconUrl = FirstText(
                GetJsonString(element, "icon_url"),
                GetJsonString(element, "icon_url_large"),
                GetJsonString(element, "icon"));
            if (!string.IsNullOrWhiteSpace(classId)
                && (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(iconUrl)))
            {
                string key = BuildDescriptionKey(classId, instanceId);
                descriptions[key] = new TradeItemDescription
                {
                    MarketHashName = name,
                    IconUrl = NormalizeWebImageUrl(iconUrl),
                    Type = GetJsonString(element, "type")
                };
            }

            foreach (var property in element.EnumerateObject())
                CollectWebDescriptions(property.Value, descriptions, string.IsNullOrWhiteSpace(propertyPath) ? property.Name : propertyPath + "_" + property.Name);
        }

        private static void FillWebAssetDescriptions(TradeOffersResult result)
        {
            foreach (var detail in result.SentOffers.Concat(result.ReceivedOffers))
            {
                FillWebAssetDescriptions(detail.ItemsToGive, result.Descriptions);
                FillWebAssetDescriptions(detail.ItemsToReceive, result.Descriptions);
            }
        }

        private static void FillWebAssetDescriptions(IEnumerable<TradeAsset> assets, IReadOnlyDictionary<string, TradeItemDescription> descriptions)
        {
            foreach (var asset in assets)
            {
                if (!descriptions.TryGetValue(BuildDescriptionKey(asset.ClassId, asset.InstanceId), out var description))
                    continue;

                if (SteamAssetNameCompletionHelper.ShouldReplaceWithDescription(asset.MarketHashName, description.MarketHashName))
                    asset.MarketHashName = description.MarketHashName;
                if (string.IsNullOrWhiteSpace(asset.IconUrl) || IsPlaceholderIconUrl(asset.IconUrl))
                    asset.IconUrl = description.IconUrl;
            }
        }

        private static bool IsPlaceholderIconUrl(string iconUrl)
        {
            return (iconUrl ?? "").Contains("/trans.gif", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractWebOfferItemName(string itemHtml, string contextHtml = "")
        {
            string firstTag = GetFirstTag(itemHtml);
            foreach (string candidate in new[]
            {
                GetHtmlAttribute(firstTag, "title"),
                GetHtmlAttribute(firstTag, "data-title"),
                GetHtmlAttribute(firstTag, "data-name"),
                GetHtmlAttribute(firstTag, "data-market-hash-name"),
                GetHtmlAttribute(firstTag, "data-market_hash_name"),
                GetHtmlAttribute(firstTag, "data-market-name"),
                GetHtmlAttribute(firstTag, "data-hash-name"),
                GetHtmlAttribute(firstTag, "aria-label"),
                GetHtmlAttribute(GetFirstTagByName(itemHtml, "img"), "alt"),
                GetHtmlAttribute(GetFirstTagByName(itemHtml, "img"), "title"),
                ExtractMarketListingHash(itemHtml),
                ExtractMarketListingHash(contextHtml)
            })
            {
                string name = CleanWebItemName(candidate);
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }

            foreach (string className in new[] { "trade_item_name", "hover_item_name", "item_name", "market_listing_item_name" })
            {
                string div = ExtractFirstDivByClass(itemHtml, className);
                string name = CleanWebItemName(ExtractVisibleText(div));
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }

            string visible = CleanWebItemName(ExtractVisibleText(itemHtml));
            return FirstText(visible, CleanWebItemName(ExtractMarketListingHash(contextHtml)));
        }

        private static string CleanWebItemName(string text)
        {
            text = HtmlDecode(text);
            text = Regex.Replace(text, @"\s+", " ").Trim();
            if (text.Equals("无图", StringComparison.OrdinalIgnoreCase))
                return "";
            return text;
        }

        private static (long AppId, long ContextId, string ClassId, string InstanceId, string AssetId) ParseEconomyItem(string economyItem)
        {
            string text = (economyItem ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
                return (0, 0, "", "", "");

            string[] parts = text.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            int classInfo = Array.FindIndex(parts, x => x.Equals("classinfo", StringComparison.OrdinalIgnoreCase));
            if (classInfo >= 0 && parts.Length > classInfo + 2)
            {
                long appId = ParseLong(parts[classInfo + 1]);
                long contextId = 0;
                string classId;
                string instanceId;
                if (parts.Length > classInfo + 4)
                {
                    contextId = ParseLong(parts[classInfo + 2]);
                    classId = parts[classInfo + 3];
                    instanceId = parts[classInfo + 4];
                }
                else
                {
                    classId = parts[classInfo + 2];
                    instanceId = parts.Length > classInfo + 3 ? parts[classInfo + 3] : "0";
                }

                return (appId, contextId, classId, string.IsNullOrWhiteSpace(instanceId) ? "0" : instanceId, "");
            }

            int assetInfo = Array.FindIndex(parts, x => x.Equals("asset", StringComparison.OrdinalIgnoreCase));
            if (assetInfo >= 0 && parts.Length > assetInfo + 3)
            {
                long appId = ParseLong(parts[assetInfo + 1]);
                long contextId = ParseLong(parts[assetInfo + 2]);
                string assetId = parts[assetInfo + 3];
                return (appId, contextId, "", "0", assetId);
            }

            return (0, 0, "", "", "");
        }

        private static string ExtractMarketListingHash(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return "";

            string encoded = FirstText(
                MatchGroup(html, @"(?:https?:)?//steamcommunity\.com/market/listings/730/([^""'<>?#]+)", 1),
                MatchGroup(html, @"/market/listings/730/([^""'<>?#]+)", 1),
                MatchGroup(html, @"""market_hash_name""\s*:\s*""([^""]+)""", 1),
                MatchGroup(html, @"'market_hash_name'\s*:\s*'([^']+)'", 1));
            if (string.IsNullOrWhiteSpace(encoded))
                return "";

            encoded = HtmlDecode(encoded).Replace("+", "%20", StringComparison.Ordinal);
            try
            {
                return Uri.UnescapeDataString(encoded).Trim();
            }
            catch
            {
                return encoded.Trim();
            }
        }

        private static string ExtractWebOfferPartnerName(string offerHtml)
        {
            foreach (string className in new[] { "tradeoffer_partner_name", "tradeoffer_partner", "playerAvatar" })
            {
                string part = ExtractFirstDivByClass(offerHtml, className);
                string name = ExtractVisibleText(part);
                if (!string.IsNullOrWhiteSpace(name))
                    return name;

                name = ExtractFirstElementTextByClass(offerHtml, className);
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }

            return "";
        }

        private static string ExtractWebOfferPartnerSteamId(string offerHtml)
        {
            string accountId = FirstText(
                MatchGroup(offerHtml, @"\bdata-miniprofile\s*=\s*[""']?(\d+)", 1),
                MatchGroup(offerHtml, @"/profiles/(\d{16,20})", 1));
            if (accountId.Length >= 16)
                return accountId;
            return AccountIdToSteamId64(accountId);
        }

        private static string ExtractWebOfferStatusText(string offerHtml)
        {
            foreach (string className in new[] { "tradeoffer_items_banner", "tradeoffer_status", "tradeoffer_header", "tradeoffer_footer_note" })
            {
                string part = ExtractFirstDivByClass(offerHtml, className);
                string text = ExtractVisibleText(part);
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            string visible = ExtractVisibleText(offerHtml);
            string marker = FirstText(
                MatchGroup(visible, @"([^。；;]*手机确认[^。；;]*)", 1),
                MatchGroup(visible, @"([^。；;]*等待[^。；;]*确认[^。；;]*)", 1));
            return marker;
        }

        private static DateTime ExtractWebOfferTimestamp(string offerHtml)
        {
            string timestamp = FirstText(
                MatchGroup(offerHtml, @"\bdata-timestamp\s*=\s*[""']?(\d{9,12})", 1),
                MatchGroup(offerHtml, @"\bdata-time-created\s*=\s*[""']?(\d{9,12})", 1));
            long value = ParseLong(timestamp);
            return FromUnix(value);
        }

        private static WebOfferItemDirection InferWebOfferItemDirection(string label)
        {
            string text = label ?? "";
            if (ContainsAny(text, "我方转出", "您的物品", "你将给予", "您将给予", "您将失去", "你会失去", "您会失去", "会失去物品", "失去物品", "You will give", "Your items", "Items to give"))
                return WebOfferItemDirection.Give;
            if (ContainsAny(text, "我方收货", "对方的物品", "您将收到", "你会收到", "您会收到", "会收到物品", "收到物品", "You will receive", "Their items", "Items to receive"))
                return WebOfferItemDirection.Receive;
            return WebOfferItemDirection.Unknown;
        }

        private static bool LooksLikeSentWebOffer(string offerHtml, string statusText)
        {
            return ContainsAny(offerHtml, "tradeoffer_sent", "tradeoffer_state_sent");
        }

        private static bool LooksLikeMobileConfirmationState(string text)
        {
            return ContainsAny(
                text,
                "手机确认",
                "等待确认",
                "需要确认",
                "需确认",
                "CreatedNeedsConfirmation",
                "NeedsConfirmation",
                "Needs Mobile Confirmation",
                "Awaiting Mobile Confirmation");
        }

        private static bool LooksLikeTerminalWebOfferState(string offerHtml, string statusText)
        {
            string text = ExtractVisibleText(offerHtml) + " " + (statusText ?? "") + " " + (offerHtml ?? "");
            return ContainsAny(
                text,
                "已失效",
                "已过期",
                "已取消",
                "已拒绝",
                "已接受",
                "已同意",
                "已完成",
                "失效",
                "过期",
                "Expired",
                "Canceled",
                "Cancelled",
                "Declined",
                "Rejected",
                "Accepted",
                "Completed",
                "Inactive",
                "tradeoffer_expired",
                "tradeoffer_canceled",
                "tradeoffer_cancelled",
                "tradeoffer_declined",
                "tradeoffer_accepted",
                "tradeoffer_inactive");
        }

        private static IEnumerable<string> ExtractDivsByClass(string html, string className)
        {
            foreach (var block in ExtractDivBlocksByClass(html, className))
                yield return block.Html;
        }

        private static IEnumerable<HtmlBlock> ExtractDivBlocksByClass(string html, string className)
        {
            string text = html ?? "";
            int searchFrom = 0;
            while (searchFrom < text.Length)
            {
                var match = Regex.Match(text, @"<div\b[^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
                while (match.Success && match.Index < searchFrom)
                    match = match.NextMatch();
                if (!match.Success)
                    yield break;

                if (!TagHasClass(match.Value, className))
                {
                    searchFrom = match.Index + Math.Max(1, match.Length);
                    continue;
                }

                string div = ReadBalancedHtmlDiv(text, match.Index);
                if (!string.IsNullOrWhiteSpace(div))
                {
                    yield return new HtmlBlock(div, match.Index);
                    searchFrom = match.Index + Math.Max(1, div.Length);
                }
                else
                {
                    searchFrom = match.Index + Math.Max(1, match.Length);
                }
            }
        }

        private static IEnumerable<string> ExtractDivsByTradeOfferId(string html)
        {
            string text = html ?? "";
            var ids = Regex.Matches(
                text,
                @"(?:tradeofferid_|data-tradeofferid\s*=\s*[""']?|/tradeoffer/)(\d{6,})",
                RegexOptions.IgnoreCase | RegexOptions.Singleline,
                TimeSpan.FromMilliseconds(200));

            foreach (Match idMatch in ids)
            {
                string div = FindSmallestContainingTradeOfferDiv(text, idMatch.Index);
                if (!string.IsNullOrWhiteSpace(div))
                    yield return div;
            }
        }

        private static IEnumerable<string> ExtractDivsByClassPrefix(string html, string classPrefix)
        {
            string text = html ?? "";
            int searchFrom = 0;
            while (searchFrom < text.Length)
            {
                var match = Regex.Match(text, @"<div\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromMilliseconds(200));
                while (match.Success && match.Index < searchFrom)
                    match = match.NextMatch();
                if (!match.Success)
                    yield break;

                if (!TagHasClassPrefix(match.Value, classPrefix)
                    || TagHasTradeOfferItemGroupClass(match.Value))
                {
                    searchFrom = match.Index + Math.Max(1, match.Length);
                    continue;
                }

                string div = ReadBalancedHtmlDiv(text, match.Index);
                if (!string.IsNullOrWhiteSpace(div))
                {
                    yield return div;
                    searchFrom = match.Index + Math.Max(1, div.Length);
                }
                else
                {
                    searchFrom = match.Index + Math.Max(1, match.Length);
                }
            }
        }

        private static string ExtractTradeOfferId(string offerHtml)
        {
            return FirstText(
                MatchGroup(offerHtml, @"\bid\s*=\s*[""']tradeofferid_(\d+)[""']", 1),
                MatchGroup(offerHtml, @"\bdata-tradeofferid\s*=\s*[""']?(\d+)", 1),
                MatchGroup(offerHtml, @"/tradeoffer/(\d+)", 1),
                MatchGroup(offerHtml, @"\btradeofferid[_'"":=\s]+(\d+)", 1));
        }

        private static string FindSmallestContainingTradeOfferDiv(string html, int index)
        {
            int windowStart = Math.Max(0, index - 6000);
            string prefix = html[windowStart..index];
            var divStarts = Regex.Matches(
                    prefix,
                    @"<div\b[^>]*>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline,
                    TimeSpan.FromMilliseconds(200))
                .Cast<Match>()
                .Select(match => windowStart + match.Index)
                .Reverse()
                .Take(40)
                .ToList();

            string fallback = "";
            foreach (int start in divStarts)
            {
                string div = ReadBalancedHtmlDiv(html, start);
                if (string.IsNullOrWhiteSpace(div) || !ContainsTradeOfferIdSignal(div))
                    continue;

                fallback = div;
                if (div.Contains("data-economy-item", StringComparison.OrdinalIgnoreCase))
                    return div;
            }

            return fallback;
        }

        private static bool ContainsTradeOfferIdSignal(string html)
        {
            return Regex.IsMatch(
                html ?? "",
                @"(?:tradeofferid_|data-tradeofferid\s*=\s*[""']?|/tradeoffer/)\d{6,}",
                RegexOptions.IgnoreCase | RegexOptions.Singleline,
                TimeSpan.FromMilliseconds(200));
        }

        private static string ExtractFirstDivByClass(string html, string className)
        {
            return ExtractDivsByClass(html, className).FirstOrDefault() ?? "";
        }

        private static string ExtractFirstElementTextByClass(string html, string className)
        {
            if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(className))
                return "";

            var match = Regex.Match(
                html,
                @"<(?<tag>[a-zA-Z0-9]+)\b(?=[^>]*\bclass\s*=\s*(?:""[^""]*\b" + Regex.Escape(className) + @"\b[^""]*""|'[^']*\b" + Regex.Escape(className) + @"\b[^']*'|[^\s>]*\b" + Regex.Escape(className) + @"\b[^\s>]*))[^>]*>(?<body>.*?)</\k<tag>>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline,
                TimeSpan.FromMilliseconds(200));
            return match.Success ? ExtractVisibleText(match.Value) : "";
        }

        private static string ReadBalancedHtmlDiv(string html, int startIndex)
        {
            if (startIndex < 0 || startIndex >= html.Length)
                return "";

            int depth = 0;
            var tagRegex = new Regex(@"</?div\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match match in tagRegex.Matches(html, startIndex))
            {
                if (match.Value.StartsWith("</", StringComparison.Ordinal))
                    depth--;
                else
                    depth++;

                if (depth == 0)
                    return html[startIndex..(match.Index + match.Length)];
            }

            return "";
        }

        private static bool TagHasClass(string tag, string className)
        {
            string classes = GetHtmlAttribute(tag, "class");
            if (string.IsNullOrWhiteSpace(classes))
                return false;

            return classes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(x => x.Equals(className, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TagHasClassPrefix(string tag, string classPrefix)
        {
            string classes = GetHtmlAttribute(tag, "class");
            if (string.IsNullOrWhiteSpace(classes))
                return false;

            return classes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(x => x.StartsWith(classPrefix, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TagHasTradeOfferItemGroupClass(string tag)
        {
            string classes = GetHtmlAttribute(tag, "class");
            if (string.IsNullOrWhiteSpace(classes))
                return false;

            return classes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(x => x.Equals("tradeoffer_item_list", StringComparison.OrdinalIgnoreCase)
                    || x.Equals("tradeoffer_items", StringComparison.OrdinalIgnoreCase)
                    || x.Equals("tradeoffer_items_ctn", StringComparison.OrdinalIgnoreCase)
                    || x.Equals("tradeoffer_items_banner", StringComparison.OrdinalIgnoreCase)
                    || x.Equals("tradeoffer_items_header", StringComparison.OrdinalIgnoreCase));
        }

        private static string GetFirstTag(string html)
        {
            return Regex.Match(html ?? "", @"<\w+\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Value;
        }

        private static string GetFirstTagByName(string html, string tagName)
        {
            return Regex.Match(html ?? "", @"<" + Regex.Escape(tagName) + @"\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Value;
        }

        private static string GetHtmlAttribute(string tag, string attributeName)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return "";

            var match = Regex.Match(
                tag,
                @"\b" + Regex.Escape(attributeName) + @"\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))",
                RegexOptions.IgnoreCase | RegexOptions.Singleline,
                TimeSpan.FromMilliseconds(200));
            if (!match.Success)
                return "";

            return HtmlDecode(FirstText(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value));
        }

        private static string ExtractVisibleText(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return "";

            string text = Regex.Replace(html, @"(?is)<script\b.*?</script>|<style\b.*?</style>", " ");
            text = Regex.Replace(text, @"(?is)<br\s*/?>", " ");
            text = Regex.Replace(text, @"(?is)<[^>]+>", " ");
            return HtmlDecode(Regex.Replace(text, @"\s+", " ").Trim());
        }

        private static string GetContextBefore(string fullHtml, string partHtml, int maxLength)
        {
            int index = fullHtml.IndexOf(partHtml, StringComparison.Ordinal);
            if (index <= 0)
                return "";

            int start = Math.Max(0, index - maxLength);
            return fullHtml[start..index];
        }

        private static string GetContextAround(string fullHtml, int index, int length, int before, int after)
        {
            if (string.IsNullOrWhiteSpace(fullHtml) || index < 0 || index >= fullHtml.Length)
                return "";

            int start = Math.Max(0, index - before);
            int end = Math.Min(fullHtml.Length, index + Math.Max(0, length) + after);
            return fullHtml[start..end];
        }

        private static string MatchGroup(string text, string pattern, int group)
        {
            var match = Regex.Match(text ?? "", pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromMilliseconds(200));
            return match.Success && match.Groups.Count > group ? HtmlDecode(match.Groups[group].Value) : "";
        }

        private static IEnumerable<string> ExtractJavascriptAssignmentJson(string html, params string[] variableNames)
        {
            foreach (string variableName in variableNames)
            {
                string text = html ?? "";
                int index = text.IndexOf(variableName, StringComparison.OrdinalIgnoreCase);
                while (index >= 0)
                {
                    int equals = text.IndexOf('=', index + variableName.Length);
                    if (equals < 0)
                        break;

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
                        yield return json;

                    index = text.IndexOf(variableName, index + variableName.Length, StringComparison.OrdinalIgnoreCase);
                }
            }
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

        private static string GetJsonString(JsonElement element, params string[] names)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return "";

            foreach (string name in names)
            {
                if (!element.TryGetProperty(name, out var value))
                    continue;
                if (value.ValueKind == JsonValueKind.String)
                    return value.GetString() ?? "";
                if (value.ValueKind == JsonValueKind.Number)
                    return value.ToString();
            }

            return "";
        }

        private static string BuildDescriptionKey(string classId, string instanceId)
        {
            return (classId ?? "").Trim() + "_" + (string.IsNullOrWhiteSpace(instanceId) ? "0" : instanceId.Trim());
        }

        private static string NormalizeWebImageUrl(string url)
        {
            url = HtmlDecode((url ?? "").Trim());
            if (string.IsNullOrWhiteSpace(url))
                return "";
            if (url.StartsWith("//", StringComparison.Ordinal))
                return "https:" + url;
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return url;
            if (url.StartsWith("/", StringComparison.Ordinal))
                return SteamUrls.CommunityBase + url;
            return BuildIconUrl(url);
        }

        private static string BuildIconUrl(string iconPath)
        {
            iconPath = (iconPath ?? "").Trim();
            if (string.IsNullOrWhiteSpace(iconPath))
                return "";
            if (iconPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || iconPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return iconPath;
            return SteamUrls.EconomyImage(iconPath);
        }

        private static string AccountIdToSteamId64(string accountIdText)
        {
            if (!ulong.TryParse(accountIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong accountId))
                return "";
            const ulong steamIdBase = 76561197960265728UL;
            return (steamIdBase + accountId).ToString(CultureInfo.InvariantCulture);
        }

        private static DateTime FromUnix(long unixSeconds)
        {
            if (unixSeconds <= 0)
                return DateTime.MinValue;

            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private static bool ContainsAny(string text, params string[] needles)
        {
            return needles.Any(needle => !string.IsNullOrWhiteSpace(needle) && (text ?? "").Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        private static long ParseLong(string text)
        {
            return long.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
                ? value
                : 0;
        }

        private static string HtmlDecode(string text)
        {
            return WebUtility.HtmlDecode(text ?? "") ?? "";
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

        private enum WebOfferItemDirection
        {
            Unknown = 0,
            Give = 1,
            Receive = 2
        }

        private sealed record WebOfferItemList(WebOfferItemDirection Direction, List<TradeAsset> Assets);
        private sealed record HtmlBlock(string Html, int Index);
    }
}
