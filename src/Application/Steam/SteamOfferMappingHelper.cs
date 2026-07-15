using CS2TradeMonitor.Domain.Steam;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CS2TradeMonitor.Application.Steam
{
    internal static class SteamOfferMappingHelper
    {
        private const string MobileConfirmationPlaceholderClassId = "__mobile_confirmation_placeholder";

        public static SteamOfferItem ParseConfirmation(JsonElement item)
        {
            string tradeOfferId = FirstText(
                GetString(item, "creator_id", "creator", "Creator", "tradeofferid", "tradeOfferId", "offerId"),
                FindText(item, "tradeofferid", "tradeOfferId", "offerId"));
            string typeName = FirstText(GetString(item, "type_name", "typeName"), "移动确认");
            string iconUrl = BuildConfirmationIconUrl(FirstText(GetString(item, "icon", "icon_url", "image", "img"), FindText(item, "icon", "icon_url", "image", "img")));
            int confType = GetInt(item, "conf_type", "confType", "type");
            var offerType = confType == 2 || typeName.Contains("Trade", StringComparison.OrdinalIgnoreCase)
                ? SteamOfferType.Outgoing
                : SteamOfferType.Unknown;
            string normalizedTitle = BuildConfirmationTitle(offerType);
            string fallbackItemName = BuildConfirmationItemName(offerType);

            var offer = new SteamOfferItem
            {
                TradeOfferId = tradeOfferId,
                ConfirmationId = FirstText(GetString(item, "id", "confid", "confirmationId")),
                ConfirmationKey = FirstText(GetString(item, "nonce", "key", "ck", "confirmationKey")),
                Title = normalizedTitle,
                ItemSummary = fallbackItemName,
                Type = offerType,
                Source = "Steam移动确认",
                Status = SteamOfferStatus.Pending,
                RiskLevel = SteamOfferRisk.Unverified,
                CanAcceptSafely = false,
                CreatedAt = DateTime.Now,
                SafeReason = "",
                FailureReason = "",
                PlatformOrderNo = "",
                Amount = null,
                ConfirmationType = normalizedTitle,
                MobileConfirmationType = (SteamMobileConfirmationType)confType
            };

            if (!string.IsNullOrWhiteSpace(fallbackItemName) || !string.IsNullOrWhiteSpace(iconUrl))
            {
                var asset = new TradeAsset
                {
                    Amount = 1,
                    ClassId = MobileConfirmationPlaceholderClassId,
                    MarketHashName = FirstText(fallbackItemName, "Steam 移动确认报价"),
                    IconUrl = iconUrl
                };
                if (offerType == SteamOfferType.IncomingGift)
                    offer.ItemsToReceive.Add(asset);
                else
                    offer.ItemsToGive.Add(asset);
            }

            return offer;
        }

        public static Dictionary<string, TradeOfferDetail> BuildTradeOfferDetailLookup(TradeOffersResult result)
        {
            var lookup = new Dictionary<string, TradeOfferDetail>(StringComparer.OrdinalIgnoreCase);
            AddTradeOfferDetails(lookup, result.SentOffers);
            AddTradeOfferDetails(lookup, result.ReceivedOffers);
            return lookup;
        }

        public static bool NeedsTradeOfferDetail(SteamOfferItem offer)
        {
            return !string.IsNullOrWhiteSpace(offer.TradeOfferId)
                && (HasOnlyMobileConfirmationFallbackDetails(offer)
                    || offer.ItemsToGive.Concat(offer.ItemsToReceive).Any(SteamAssetNameCompletionHelper.NeedsExternalLookup));
        }

        public static void ApplyTradeOfferDetails(SteamOfferItem target, SteamOfferItem details)
        {
            if (HasOnlyMobileConfirmationFallbackDetails(target))
            {
                target.ItemsToGive.Clear();
                target.ItemsToReceive.Clear();
            }

            MergeTradeAssets(target.ItemsToGive, details.ItemsToGive);
            MergeTradeAssets(target.ItemsToReceive, details.ItemsToReceive);
            if (!string.IsNullOrWhiteSpace(details.PartnerSteamId))
                target.PartnerSteamId = details.PartnerSteamId;
            if (!string.IsNullOrWhiteSpace(details.PartnerName))
                target.PartnerName = details.PartnerName;
            if (!string.IsNullOrWhiteSpace(details.ItemSummary))
                target.ItemSummary = details.ItemSummary;
            if (!string.IsNullOrWhiteSpace(details.Title))
                target.Title = details.Title;
            if (details.Type != SteamOfferType.Unknown)
                target.Type = details.Type;
            target.Status = details.Status;
            if (details.CreatedAt != DateTime.MinValue)
                target.CreatedAt = details.CreatedAt;
            if (details.ExpirationTime != DateTime.MinValue)
                target.ExpirationTime = details.ExpirationTime;
            if (!string.IsNullOrWhiteSpace(details.ConfirmationType))
                target.ConfirmationType = details.ConfirmationType;
            if (details.MobileConfirmationType.HasValue)
                target.MobileConfirmationType = details.MobileConfirmationType;
        }

        public static List<SteamOfferItem> ConvertTradeOffersToItems(TradeOffersResult result)
        {
            var list = new List<SteamOfferItem>();
            foreach (var detail in result.SentOffers)
            {
                if (string.IsNullOrWhiteSpace(detail.TradeOfferId))
                    continue;
                FillAssetDescriptions(detail.ItemsToGive, result.Descriptions);
                FillAssetDescriptions(detail.ItemsToReceive, result.Descriptions);
                list.Add(CreateOfferFromTradeDetail(detail, isSentOffer: true));
            }

            foreach (var detail in result.ReceivedOffers)
            {
                if (string.IsNullOrWhiteSpace(detail.TradeOfferId))
                    continue;
                FillAssetDescriptions(detail.ItemsToGive, result.Descriptions);
                FillAssetDescriptions(detail.ItemsToReceive, result.Descriptions);
                list.Add(CreateOfferFromTradeDetail(detail, isSentOffer: false));
            }

            return list
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.TradeOfferId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static SteamOfferItem CreateOfferFromTradeDetail(TradeOfferDetail detail, bool isSentOffer)
        {
            var type = DetermineOfferType(detail, isSentOffer);
            return new SteamOfferItem
            {
                TradeOfferId = detail.TradeOfferId,
                Title = BuildTradeOfferTitle(type),
                ItemSummary = BuildTradeItemSummary(detail),
                Type = type,
                Source = detail.IsOurOffer ? "Steam报价（已发送）" : "Steam报价（收到）",
                Status = MapTradeOfferState(detail.TradeOfferState),
                RiskLevel = SteamOfferRisk.Unverified,
                CanAcceptSafely = false,
                CreatedAt = detail.TimeCreated,
                PartnerSteamId = detail.PartnerSteamId,
                PartnerName = detail.PartnerName,
                ExpirationTime = detail.ExpirationTime,
                ConfirmationType = detail.TradeOfferState == 9 ? "Steam 手机确认" : "",
                ItemsToGive = CloneTradeAssets(detail.ItemsToGive),
                ItemsToReceive = CloneTradeAssets(detail.ItemsToReceive)
            };
        }

        public static void FillAssetDescriptions(IEnumerable<TradeAsset> assets, IReadOnlyDictionary<string, TradeItemDescription> descriptions)
        {
            foreach (var asset in assets)
            {
                if (!descriptions.TryGetValue(BuildDescriptionKey(asset.ClassId, asset.InstanceId), out var description))
                    continue;
                if (SteamAssetNameCompletionHelper.ShouldReplaceWithDescription(asset.MarketHashName, description.MarketHashName))
                    asset.MarketHashName = description.MarketHashName;
                if (string.IsNullOrWhiteSpace(asset.IconUrl))
                    asset.IconUrl = description.IconUrl;
            }
        }

        public static void MergeMobileConfirmations(List<SteamOfferItem> tradeOffers, List<SteamOfferItem> confirmations)
        {
            var byId = tradeOffers
                .Where(x => !string.IsNullOrWhiteSpace(x.TradeOfferId))
                .GroupBy(x => x.TradeOfferId.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var confirmation in confirmations)
            {
                if (!byId.TryGetValue(confirmation.TradeOfferId, out var offer))
                {
                    tradeOffers.Add(confirmation);
                    continue;
                }

                offer.ConfirmationId = confirmation.ConfirmationId;
                offer.ConfirmationKey = confirmation.ConfirmationKey;
                offer.ConfirmationType = confirmation.ConfirmationType;
                offer.MobileConfirmationType = confirmation.MobileConfirmationType;
                if (offer.Type == SteamOfferType.Unknown)
                    offer.Type = confirmation.Type;
                if (string.IsNullOrWhiteSpace(offer.Source))
                    offer.Source = confirmation.Source;
            }
        }

        public static SteamOfferItem CloneOffer(SteamOfferItem offer)
        {
            return new SteamOfferItem
            {
                TradeOfferId = offer.TradeOfferId,
                ConfirmationId = offer.ConfirmationId,
                ConfirmationKey = offer.ConfirmationKey,
                Title = offer.Title,
                ItemSummary = offer.ItemSummary,
                Type = offer.Type,
                Source = offer.Source,
                Status = offer.Status,
                RiskLevel = offer.RiskLevel,
                VerifiedByYouPin = offer.VerifiedByYouPin,
                YouPinOrderNo = offer.YouPinOrderNo,
                YouPinItemName = offer.YouPinItemName,
                YouPinPrice = offer.YouPinPrice,
                CanAcceptSafely = offer.CanAcceptSafely,
                CreatedAt = offer.CreatedAt,
                SafeReason = offer.SafeReason,
                FailureReason = offer.FailureReason,
                PlatformOrderNo = offer.PlatformOrderNo,
                Amount = offer.Amount,
                ConfirmationType = offer.ConfirmationType,
                MobileConfirmationType = offer.MobileConfirmationType,
                ItemsToGive = CloneTradeAssets(offer.ItemsToGive),
                ItemsToReceive = CloneTradeAssets(offer.ItemsToReceive),
                PartnerSteamId = offer.PartnerSteamId,
                PartnerName = offer.PartnerName,
                ExpirationTime = offer.ExpirationTime
            };
        }

        public static List<TradeAsset> CloneTradeAssets(IEnumerable<TradeAsset>? assets)
        {
            if (assets == null) return new List<TradeAsset>();
            return assets.Select(x => new TradeAsset
            {
                AppId = x.AppId,
                ContextId = x.ContextId,
                AssetId = x.AssetId,
                ClassId = x.ClassId,
                InstanceId = x.InstanceId,
                Amount = x.Amount,
                MarketHashName = x.MarketHashName,
                IconUrl = x.IconUrl
            }).ToList();
        }

        private static void MergeTradeAssets(List<TradeAsset> target, IReadOnlyList<TradeAsset> details)
        {
            foreach (TradeAsset detail in details)
            {
                TradeAsset? existing = target.FirstOrDefault(asset => IsSameAsset(asset, detail));
                if (existing == null)
                {
                    target.Add(CloneTradeAssets(new[] { detail })[0]);
                    continue;
                }

                if (SteamAssetNameCompletionHelper.ShouldReplaceWithDescription(existing.MarketHashName, detail.MarketHashName))
                    existing.MarketHashName = detail.MarketHashName;
                if (string.IsNullOrWhiteSpace(existing.IconUrl) && !string.IsNullOrWhiteSpace(detail.IconUrl))
                    existing.IconUrl = detail.IconUrl;
                if (string.IsNullOrWhiteSpace(existing.AssetId) && !string.IsNullOrWhiteSpace(detail.AssetId))
                    existing.AssetId = detail.AssetId;
            }
        }

        private static bool IsSameAsset(TradeAsset left, TradeAsset right)
        {
            if (left.AppId != 0 && right.AppId != 0 && left.AppId != right.AppId)
                return false;
            if (left.ContextId != 0 && right.ContextId != 0 && left.ContextId != right.ContextId)
                return false;

            if (!string.IsNullOrWhiteSpace(left.AssetId) && !string.IsNullOrWhiteSpace(right.AssetId))
                return string.Equals(left.AssetId, right.AssetId, StringComparison.OrdinalIgnoreCase);

            return !string.IsNullOrWhiteSpace(left.ClassId)
                && string.Equals(left.ClassId, right.ClassId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.InstanceId ?? string.Empty, right.InstanceId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
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

        public static string? GetString(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out var value, names)) return null;
            if (value.ValueKind == JsonValueKind.String) return value.GetString();
            if (value.ValueKind == JsonValueKind.Number) return value.ToString();
            return null;
        }

        public static int GetInt(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out var value, names)) return 0;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result)) return result;
            return int.TryParse(value.ToString(), out result) ? result : 0;
        }

        public static bool GetBool(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out var value, names)) return false;
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            return bool.TryParse(value.ToString(), out bool result) && result;
        }

        public static string FindText(JsonElement element, params string[] names)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    if (names.Any(name => prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        string value = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? "" : prop.Value.ToString();
                        if (!string.IsNullOrWhiteSpace(value) && value != "0")
                            return value.Trim();
                    }

                    string nested = FindText(prop.Value, names);
                    if (!string.IsNullOrWhiteSpace(nested)) return nested;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    string nested = FindText(item, names);
                    if (!string.IsNullOrWhiteSpace(nested)) return nested;
                }
            }

            return "";
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

        public static bool HasOnlyMobileConfirmationFallbackDetails(SteamOfferItem offer)
        {
            if (offer.ItemsToGive.Count == 0 && offer.ItemsToReceive.Count == 0)
                return true;

            return offer.ItemsToGive.Concat(offer.ItemsToReceive)
                .All(asset => string.Equals(asset.ClassId, MobileConfirmationPlaceholderClassId, StringComparison.Ordinal));
        }

        private static void AddTradeOfferDetails(Dictionary<string, TradeOfferDetail> lookup, IEnumerable<TradeOfferDetail> details)
        {
            foreach (var detail in details)
            {
                string tradeOfferId = (detail.TradeOfferId ?? "").Trim();
                if (string.IsNullOrWhiteSpace(tradeOfferId) || !HasUsefulTradeOfferDetail(detail))
                    continue;

                if (!lookup.TryGetValue(tradeOfferId, out var existing) || IsBetterTradeOfferDetail(detail, existing))
                    lookup[tradeOfferId] = detail;
            }
        }

        private static bool HasUsefulTradeOfferDetail(TradeOfferDetail detail)
        {
            return detail.ItemsToGive.Count > 0
                || detail.ItemsToReceive.Count > 0
                || !string.IsNullOrWhiteSpace(detail.PartnerSteamId)
                || !string.IsNullOrWhiteSpace(detail.PartnerName);
        }

        private static bool IsBetterTradeOfferDetail(TradeOfferDetail candidate, TradeOfferDetail existing)
        {
            int candidateScore = candidate.ItemsToGive.Count + candidate.ItemsToReceive.Count;
            int existingScore = existing.ItemsToGive.Count + existing.ItemsToReceive.Count;
            if (!string.IsNullOrWhiteSpace(candidate.PartnerName))
                candidateScore++;
            if (!string.IsNullOrWhiteSpace(existing.PartnerName))
                existingScore++;
            if (candidateScore != existingScore)
                return candidateScore > existingScore;

            return (!string.IsNullOrWhiteSpace(candidate.PartnerSteamId)
                    && string.IsNullOrWhiteSpace(existing.PartnerSteamId))
                || (!string.IsNullOrWhiteSpace(candidate.PartnerName)
                    && string.IsNullOrWhiteSpace(existing.PartnerName));
        }

        private static string BuildConfirmationTitle(SteamOfferType offerType)
        {
            return offerType switch
            {
                SteamOfferType.IncomingGift => "Steam 收货确认",
                SteamOfferType.Outgoing => "Steam 发货确认",
                _ => "Steam 移动确认"
            };
        }

        private static string BuildConfirmationItemName(SteamOfferType offerType)
        {
            return offerType switch
            {
                SteamOfferType.IncomingGift => "待确认收货报价（Steam 未返回饰品明细）",
                SteamOfferType.Outgoing => "待确认发货报价（Steam 未返回饰品明细）",
                _ => "待确认移动报价（Steam 未返回饰品明细）"
            };
        }

        private static string BuildConfirmationIconUrl(string iconPath)
        {
            iconPath = (iconPath ?? "").Trim();
            if (string.IsNullOrWhiteSpace(iconPath))
                return "";
            if (iconPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || iconPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return iconPath;
            if (iconPath.StartsWith("//", StringComparison.Ordinal))
                return "https:" + iconPath;
            if (iconPath.StartsWith("/", StringComparison.Ordinal))
                return SteamUrls.CommunityBase + iconPath;

            return SteamUrls.EconomyImage(iconPath);
        }

        private static SteamOfferType DetermineOfferType(TradeOfferDetail detail, bool isSentOffer)
        {
            if (isSentOffer) return SteamOfferType.Outgoing;

            return detail.ItemsToGive.Count > 0
                ? SteamOfferType.TwoWay
                : detail.ItemsToReceive.Count > 0
                    ? SteamOfferType.IncomingGift
                    : SteamOfferType.Unknown;
        }

        private static SteamOfferStatus MapTradeOfferState(int state)
        {
            return state switch
            {
                3 => SteamOfferStatus.Accepted,
                6 or 7 => SteamOfferStatus.Denied,
                _ => SteamOfferStatus.Pending
            };
        }

        private static string BuildTradeOfferTitle(SteamOfferType type)
        {
            return type switch
            {
                SteamOfferType.IncomingGift => "纯收货/礼物报价",
                SteamOfferType.Outgoing => "发货报价",
                SteamOfferType.TwoWay => "双向报价",
                _ => "Steam 报价"
            };
        }

        private static string BuildTradeItemSummary(TradeOfferDetail detail)
        {
            string give = BuildAssetSummary(detail.ItemsToGive);
            string receive = BuildAssetSummary(detail.ItemsToReceive);
            if (!string.IsNullOrWhiteSpace(give) && !string.IsNullOrWhiteSpace(receive))
                return $"我方转出：{give}；对方转出：{receive}";
            if (!string.IsNullOrWhiteSpace(give))
                return $"我方转出：{give}";
            if (!string.IsNullOrWhiteSpace(receive))
                return $"对方转出：{receive}";
            return "暂无饰品明细";
        }

        private static string BuildAssetSummary(IEnumerable<TradeAsset> assets)
        {
            var names = assets
                .Select(x => string.IsNullOrWhiteSpace(x.MarketHashName) ? $"饰品编号 {x.ClassId}" : x.MarketHashName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(3)
                .ToList();
            if (names.Count == 0) return "";

            int total = assets.Sum(x => Math.Max(1, x.Amount));
            string suffix = total > names.Count ? $" 等 {total} 件" : "";
            return string.Join("、", names) + suffix;
        }

        private static string BuildDescriptionKey(string classId, string instanceId)
        {
            return (classId ?? "").Trim() + "_" + (string.IsNullOrWhiteSpace(instanceId) ? "0" : instanceId.Trim());
        }
    }
}
