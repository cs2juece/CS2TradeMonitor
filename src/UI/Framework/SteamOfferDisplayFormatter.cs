using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.Domain.Steam;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace CS2TradeMonitor.src.UI.Framework.SteamOffers
{
    internal static class SteamOfferDisplayFormatter
    {
        public static string BuildOfferTitle(SteamOfferItem offer)
        {
            if (offer.ItemsToGive.Count > 0)
                return "发送给 Steam 玩家的报价";

            if (!string.IsNullOrWhiteSpace(offer.PartnerSteamId) && offer.ItemsToReceive.Count > 0)
                return "来自 Steam 玩家的报价";

            return OfferTypeText(offer.Type);
        }

        public static string BuildOfferDetail(SteamOfferItem offer)
        {
            string receive = BuildAssetList(offer.ItemsToReceive, int.MaxValue);
            string give = BuildAssetList(offer.ItemsToGive, int.MaxValue);
            string order = string.IsNullOrWhiteSpace(offer.YouPinOrderNo) ? "无" : offer.YouPinOrderNo;
            string price = offer.YouPinPrice > 0 ? $"¥{offer.YouPinPrice:F2}" : "无";
            string reason = offer.CanAcceptSafely
                ? FirstText(offer.SafeReason, "通过安全校验")
                : FirstText(offer.FailureReason, "未通过安全校验");
            string summary = string.IsNullOrWhiteSpace(offer.ItemSummary) ? "" : $"\n原始摘要：{offer.ItemSummary}";

            return $"收货物品：{receive}\n转出物品：{give}\n悠悠订单：{order}\n悠悠金额：{price}\n安全状态：{reason}{summary}";
        }

        public static string BuildPartnerLine(SteamOfferItem offer)
        {
            string partner = FirstText(offer.PartnerName, offer.PartnerSteamId, "未知玩家");
            string id = string.IsNullOrWhiteSpace(offer.TradeOfferId) ? "暂无" : offer.TradeOfferId;
            return $"对方：{partner}  报价号：{id}";
        }

        public static string BuildAssetList(IReadOnlyList<TradeAsset>? assets, int maxItems = 3)
        {
            if (assets == null || assets.Count == 0)
                return "无";

            int take = Math.Min(maxItems, assets.Count);
            var names = assets
                .Take(take)
                .Select(asset =>
                {
                    string name = FirstText(asset.MarketHashName, asset.AssetId, "未知物品");
                    return asset.Amount > 1 ? $"{name} x{asset.Amount}" : name;
                });

            string text = string.Join("，", names);
            if (assets.Count > take)
                text += $" ... 等{assets.Count}件";
            return text;
        }

        public static string OfferStatusText(SteamOfferStatus status) => status switch
        {
            SteamOfferStatus.Accepted => "已同意",
            SteamOfferStatus.Denied => "已拒绝",
            _ => "待处理"
        };

        public static string OfferTypeText(SteamOfferType type) => type switch
        {
            SteamOfferType.IncomingGift => "纯收货/礼物",
            SteamOfferType.Outgoing => "发货报价",
            SteamOfferType.TwoWay => "双向报价",
            _ => "未知报价"
        };

        public static string RiskText(SteamOfferRisk risk) => risk switch
        {
            SteamOfferRisk.SafeIncoming => "安全收货",
            SteamOfferRisk.YouPinVerified => "悠悠校验",
            _ => "需人工核对"
        };

        public static Color RiskColor(SteamOfferRisk risk) => risk switch
        {
            SteamOfferRisk.SafeIncoming => Color.FromArgb(0, 170, 90),
            SteamOfferRisk.YouPinVerified => UIColors.Primary,
            _ => UIColors.TextWarn
        };

        public static string FormatTime(DateTime? time)
        {
            if (!time.HasValue || time.Value == default) return "暂无";
            return time.Value.ToString("MM-dd HH:mm:ss");
        }

        public static string CompactStatusText(string text)
        {
            string value = (text ?? "").Trim().TrimEnd('。', '.', '；', ';');
            if (string.IsNullOrWhiteSpace(value)) return "未刷新";
            if (value.Contains("Steam 未登录", StringComparison.OrdinalIgnoreCase)) return "Steam 未登录";
            if (value.Contains("未绑定 Steam 令牌", StringComparison.OrdinalIgnoreCase)) return "未绑定令牌";
            if (value.Contains("已刷新", StringComparison.OrdinalIgnoreCase)) return value;
            if (value.Length <= 28) return value;
            return value[..28] + "...";
        }

        public static string BuildOfferPlaceholderText(SteamOfferState state)
        {
            string status = (state.LastStatus ?? "").Trim();
            string error = (state.LastError ?? "").Trim();
            if (string.IsNullOrWhiteSpace(status))
                status = "暂无报价";

            if (!string.IsNullOrWhiteSpace(error)
                && !status.Contains(error, StringComparison.OrdinalIgnoreCase))
                return $"状态：{status}\r\n原因：{error}";

            return $"状态：{status}\r\n下一步：点击“刷新报价”；未登录时先绑定/管理令牌。";
        }

        public static string FormatExpirationTime(DateTime expirationTime)
        {
            if (expirationTime == default)
                return "暂无";

            return IsExpired(expirationTime) ? "已过期" : expirationTime.ToString("MM-dd HH:mm:ss");
        }

        public static string FormatRemainingTime(DateTime expirationTime)
        {
            if (expirationTime == default)
                return "暂无到期";

            DateTime localExpiration = ToLocalTime(expirationTime);
            TimeSpan remaining = localExpiration - DateTime.Now;
            if (remaining <= TimeSpan.Zero)
                return "已过期";

            if (remaining.TotalDays >= 1)
                return $"剩余 {(int)remaining.TotalDays}天{remaining.Hours}时";
            if (remaining.TotalHours >= 1)
                return $"剩余 {(int)remaining.TotalHours}时{remaining.Minutes}分";
            return $"剩余 {remaining.Minutes}分{remaining.Seconds}秒";
        }

        public static bool IsExpired(DateTime expirationTime)
        {
            return expirationTime != default && ToLocalTime(expirationTime) <= DateTime.Now;
        }

        private static DateTime ToLocalTime(DateTime time)
        {
            return time.Kind == DateTimeKind.Utc ? time.ToLocalTime() : time;
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
    }
}
