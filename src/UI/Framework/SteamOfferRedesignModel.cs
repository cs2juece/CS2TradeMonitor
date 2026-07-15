using CS2TradeMonitor.Domain.Steam;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CS2TradeMonitor.src.UI.Framework.SteamOffers
{
    internal enum SteamOfferRedesignRule
    {
        Pure,
        Any,
        YouPinPurchase,
        YouPinSale,
        YouPinRental
    }

    internal enum SteamOfferRedesignCategory
    {
        Pure,
        YouPinPurchase,
        YouPinSale,
        YouPinRental,
        Unknown
    }

    internal static class SteamOfferRedesignModel
    {
        public const string DefaultRuleKey = "Pure";

        public static IReadOnlyList<SteamOfferRuleOption> RuleOptions { get; } = new[]
        {
            new SteamOfferRuleOption(SteamOfferRedesignRule.Pure, "纯报价", "只处理无需付出库存的报价"),
            new SteamOfferRuleOption(SteamOfferRedesignRule.Any, "全部报价纳入确认", "当前列表报价全部进入确认，可在项目内同意"),
            new SteamOfferRuleOption(SteamOfferRedesignRule.YouPinPurchase, "只同意悠悠购买报价", "悠悠购买后发来的收货报价"),
            new SteamOfferRuleOption(SteamOfferRedesignRule.YouPinSale, "只同意悠悠出售报价", "仅处理悠悠出售订单相关报价"),
            new SteamOfferRuleOption(SteamOfferRedesignRule.YouPinRental, "只同意悠悠出租报价", "悠悠租赁订单相关报价")
        };

        public static SteamOfferRedesignRule ParseRule(string? value)
        {
            return Enum.TryParse(value, ignoreCase: true, out SteamOfferRedesignRule rule)
                ? rule
                : SteamOfferRedesignRule.Pure;
        }

        public static string ToSettingValue(SteamOfferRedesignRule rule)
        {
            return Enum.IsDefined(typeof(SteamOfferRedesignRule), rule)
                ? rule.ToString()
                : DefaultRuleKey;
        }

        public static IReadOnlyList<SteamOfferItem> FilterEligibleOffers(IEnumerable<SteamOfferItem> offers, SteamOfferRedesignRule rule)
        {
            ArgumentNullException.ThrowIfNull(offers);

            return offers
                .Where(offer => offer.Status == SteamOfferStatus.Pending)
                .Where(offer => IsEligible(offer, rule))
                .OrderByDescending(offer => offer.CreatedAt)
                .ToList();
        }

        public static bool IsEligible(SteamOfferItem offer, SteamOfferRedesignRule rule)
        {
            ArgumentNullException.ThrowIfNull(offer);

            if (offer.Status != SteamOfferStatus.Pending)
                return false;

            return rule switch
            {
                SteamOfferRedesignRule.Any => true,
                SteamOfferRedesignRule.YouPinPurchase => Classify(offer) == SteamOfferRedesignCategory.YouPinPurchase,
                SteamOfferRedesignRule.YouPinSale => Classify(offer) == SteamOfferRedesignCategory.YouPinSale,
                SteamOfferRedesignRule.YouPinRental => Classify(offer) == SteamOfferRedesignCategory.YouPinRental,
                _ => IsPureIncoming(offer)
            };
        }

        public static SteamOfferRedesignCategory Classify(SteamOfferItem offer)
        {
            ArgumentNullException.ThrowIfNull(offer);

            if (IsYouPinRentalOffer(offer))
                return SteamOfferRedesignCategory.YouPinRental;

            if (IsYouPinSaleOffer(offer))
                return SteamOfferRedesignCategory.YouPinSale;

            if (IsYouPinPurchaseOffer(offer))
                return SteamOfferRedesignCategory.YouPinPurchase;

            return IsPureIncoming(offer)
                ? SteamOfferRedesignCategory.Pure
                : SteamOfferRedesignCategory.Unknown;
        }

        public static string CategoryTagText(SteamOfferItem offer)
        {
            return Classify(offer) switch
            {
                SteamOfferRedesignCategory.Pure => "纯报价",
                SteamOfferRedesignCategory.YouPinPurchase => "悠悠购买报价",
                SteamOfferRedesignCategory.YouPinSale => "悠悠出售报价",
                SteamOfferRedesignCategory.YouPinRental => "悠悠出租报价",
                _ => offer.ItemsToGive.Count > 0 ? "会失去物品" : "来源不确定"
            };
        }

        public static bool IsPureIncoming(SteamOfferItem offer)
        {
            return offer.ItemsToGive.Count == 0 && offer.ItemsToReceive.Count > 0;
        }

        public static bool LosesInventory(SteamOfferItem offer)
        {
            return offer.ItemsToGive.Count > 0 || offer.Type == SteamOfferType.Outgoing || offer.Type == SteamOfferType.TwoWay;
        }

        public static BatchAcceptSummary BuildBatchSummary(IEnumerable<SteamOfferItem> allOffers, SteamOfferRedesignRule rule)
        {
            ArgumentNullException.ThrowIfNull(allOffers);

            List<SteamOfferItem> pending = allOffers
                .Where(offer => offer.Status == SteamOfferStatus.Pending)
                .ToList();
            List<SteamOfferItem> eligible = FilterEligibleOffers(pending, rule).ToList();
            int losing = eligible.Count(LosesInventory);
            int unknown = eligible.Count(offer => Classify(offer) == SteamOfferRedesignCategory.Unknown);
            int excluded = Math.Max(0, pending.Count - eligible.Count);

            return new BatchAcceptSummary(
                eligible,
                eligible.Count,
                losing,
                unknown,
                excluded,
                BuildRuleTitle(rule));
        }

        public static string BuildRuleTitle(SteamOfferRedesignRule rule)
        {
            return RuleOptions.FirstOrDefault(option => option.Rule == rule)?.Title ?? "纯报价";
        }

        public static string BuildDirectionLine(SteamOfferItem offer)
        {
            string receive = SteamOfferDisplayFormatter.BuildAssetList(offer.ItemsToReceive, int.MaxValue);
            string give = SteamOfferDisplayFormatter.BuildAssetList(offer.ItemsToGive, int.MaxValue);
            if (offer.ItemsToGive.Count > 0)
                return "失去 " + give;
            if (offer.ItemsToReceive.Count > 0)
                return "收到 " + receive;
            return "暂无物品明细";
        }

        public static string BuildRulesHelpText()
        {
            return "收货：纯报价只纳入无需付出库存的报价。\n"
                + "范围：全部报价纳入确认表示当前列表全部进入二次确认，确认后可在项目内同意。\n"
                + "悠悠购买：只处理悠悠购买后发来的收货报价。\n"
                + "悠悠出售：只处理悠悠出售订单相关报价。\n"
                + "悠悠出租：只处理悠悠租赁订单相关报价。\n"
                + "确认：只有点击同意报价或一键同意时才弹二次确认。";
        }

        public static string BuildConfirmReminderStatus(bool skipSingleConfirm, bool skipBatchConfirm)
        {
            if (!skipSingleConfirm && !skipBatchConfirm)
                return "二次确认：单条和一键同意都会弹确认。";

            if (skipSingleConfirm && skipBatchConfirm)
                return "二次确认：单条和一键同意当前都已跳过。";

            return skipSingleConfirm
                ? "二次确认：单条同意已跳过，一键同意仍会弹确认。"
                : "二次确认：一键同意已跳过，单条同意仍会弹确认。";
        }

        public static string BuildConfirmReminderHelp(bool skipSingleConfirm, bool skipBatchConfirm)
        {
            string status = BuildConfirmReminderStatus(skipSingleConfirm, skipBatchConfirm);
            return status + " 恢复按钮只重置本页提醒偏好，不会同意、拒绝或清空任何报价。";
        }

        public static string BuildEmptyOfferText()
        {
            return "暂无待处理报价\n点击“刷新报价”读取 Steam；若仍为空，检查令牌、登录状态或稍后重试。";
        }

        public static string BuildVisibleOfferSummary(int visibleCount, int totalCount, bool showAll)
        {
            visibleCount = Math.Max(0, visibleCount);
            totalCount = Math.Max(0, totalCount);
            if (totalCount == 0)
                return string.Empty;

            if (showAll || visibleCount >= totalCount)
                return $"已显示全部 {totalCount} 条报价。";

            return $"已显示 {visibleCount} / {totalCount} 条报价，点击“显示全部”查看其余报价。";
        }

        public static int CalculateExpandedOfferListCardHeight(int contentHostHeight, int minimumCardHeight, int bottomPadding)
        {
            int minimum = Math.Max(1, minimumCardHeight);
            int fillHeight = Math.Max(1, contentHostHeight - Math.Max(0, bottomPadding));
            return Math.Max(minimum, fillHeight);
        }

        private static bool IsYouPinPurchaseOffer(SteamOfferItem offer)
        {
            if (!IsYouPinOffer(offer) || !IsPureIncoming(offer))
                return false;

            string text = BuildSearchText(offer);
            return ContainsAny(text, "购买", "买入", "收货") || !LosesInventory(offer);
        }

        private static bool IsYouPinSaleOffer(SteamOfferItem offer)
        {
            if (!IsYouPinOffer(offer) || ContainsAny(BuildSearchText(offer), "包租公"))
                return false;

            string text = BuildSearchText(offer);
            return ContainsAny(text, "出售", "卖出", "发货", "待办")
                || (LosesInventory(offer) && !IsYouPinRentalOffer(offer));
        }

        private static bool IsYouPinRentalOffer(SteamOfferItem offer)
        {
            return IsYouPinOffer(offer) && ContainsAny(BuildSearchText(offer), "出租", "租赁", "租借", "租金");
        }

        private static bool IsYouPinOffer(SteamOfferItem offer)
        {
            if (offer.VerifiedByYouPin)
                return true;

            string text = BuildSearchText(offer);
            return ContainsAny(text, "悠悠", "youpin", "you pin");
        }

        private static string BuildSearchText(SteamOfferItem offer)
        {
            return string.Join(' ',
                offer.Title,
                offer.Source,
                offer.ItemSummary,
                offer.YouPinOrderNo,
                offer.PlatformOrderNo,
                offer.YouPinItemName).ToLowerInvariant();
        }

        private static bool ContainsAny(string text, params string[] needles)
        {
            foreach (string needle in needles)
            {
                if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }

    internal sealed record SteamOfferRuleOption(
        SteamOfferRedesignRule Rule,
        string Title,
        string Description);

    internal sealed record BatchAcceptSummary(
        IReadOnlyList<SteamOfferItem> EligibleOffers,
        int EligibleCount,
        int LosingInventoryCount,
        int UnknownSourceCount,
        int ExcludedCount,
        string RuleTitle);
}
