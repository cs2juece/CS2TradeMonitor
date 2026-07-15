using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.Steam;
using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CS2TradeMonitor.Application.Steam
{
    public static class SteamAutoTradePlanner
    {
        public static IReadOnlyList<SteamAutoTradePlanItem> BuildOfferPlans(
            IEnumerable<SteamOfferItem> offers,
            YouPinSaleReminderState? youPinState,
            SteamAutoTradeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(offers);
            settings ??= new SteamAutoTradeSettings();

            var orders = BuildYouPinOrderLookup(youPinState);
            var plans = new List<SteamAutoTradePlanItem>();
            foreach (SteamOfferItem offer in offers.Where(x => x.Status == SteamOfferStatus.Pending))
            {
                YouPinSaleOrder? matchedOrder = FindMatchedOrder(offer, orders);
                SteamAutoTradeCategory category = ClassifyOffer(offer, matchedOrder);
                SteamAutoTradeDirection direction = GetDirection(offer);
                string matchedOrderNo = FirstText(matchedOrder?.OrderNo, offer.PlatformOrderNo, offer.YouPinOrderNo);
                SteamAutoTradePlanItem plan = new()
                {
                    TradeOfferId = offer.TradeOfferId ?? "",
                    Direction = direction,
                    Category = category,
                    ItemNames = BuildItemNames(offer, direction).ToList(),
                    MatchedOrderNo = matchedOrderNo,
                    Action = GetOfferAction(category),
                    Allowed = IsOfferAllowed(category, settings)
                };
                if (!plan.Allowed)
                    plan.SkipReason = BuildSkipReason(category);
                plans.Add(plan);
            }

            return plans;
        }

        public static IReadOnlyList<SteamAutoTradePlanItem> BuildYouPinSendPlans(
            YouPinSaleReminderState? youPinState,
            SteamAutoTradeSettings settings)
        {
            settings ??= new SteamAutoTradeSettings();
            var orders = (youPinState?.RecentWaitDeliverOrders ?? new List<YouPinSaleOrder>())
                .Where(x => !string.IsNullOrWhiteSpace(x.OrderNo))
                .OrderByDescending(x => x.DetectedAt)
                .Take(10)
                .ToList();
            var plans = new List<SteamAutoTradePlanItem>();
            foreach (YouPinSaleOrder order in orders)
            {
                YouPinSaleOrderAction action = YouPinSaleOrderActionResolver.Resolve(order);
                if (!ShouldBuildYouPinSendPlan(order, action))
                    continue;

                bool rental = IsRentalOrder(order);
                SteamAutoTradeCategory category = rental ? SteamAutoTradeCategory.YouPinRental : SteamAutoTradeCategory.YouPinSale;
                bool allowed = rental ? settings.SendYouPinRentalEnabled : settings.SendYouPinSaleEnabled;
                plans.Add(new SteamAutoTradePlanItem
                {
                    TradeOfferId = order.TradeOfferId ?? "",
                    Direction = rental && action.Kind == YouPinSaleOrderActionKind.QueryStatus
                        ? SteamAutoTradeDirection.Unknown
                        : SteamAutoTradeDirection.Outgoing,
                    Category = category,
                    ItemNames = BuildOrderItemNames(order).ToList(),
                    MatchedOrderNo = order.OrderNo ?? "",
                    Action = GetYouPinOrderAction(action, rental),
                    Allowed = allowed,
                    SkipReason = allowed ? "" : BuildSkipReason(category)
                });
            }

            return plans;
        }

        public static SteamAutoTradePlanItem BuildManuallySentYouPinPlan(
            YouPinSaleOrder order,
            string tradeOfferId,
            SteamAutoTradeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(order);
            settings ??= new SteamAutoTradeSettings();

            bool rental = IsRentalOrder(order);
            SteamAutoTradeCategory category = rental ? SteamAutoTradeCategory.YouPinRental : SteamAutoTradeCategory.YouPinSale;
            bool allowed = settings.Enabled
                && (rental ? settings.SendYouPinRentalEnabled : settings.SendYouPinSaleEnabled);
            return new SteamAutoTradePlanItem
            {
                TradeOfferId = tradeOfferId?.Trim() ?? "",
                Direction = SteamAutoTradeDirection.Outgoing,
                Category = category,
                ItemNames = BuildOrderItemNames(order).ToList(),
                MatchedOrderNo = order.OrderNo?.Trim() ?? "",
                Action = SteamAutoTradeAction.ConfirmMobile,
                Allowed = allowed,
                SkipReason = allowed ? "" : BuildSkipReason(category)
            };
        }

        private static bool ShouldBuildYouPinSendPlan(YouPinSaleOrder order, YouPinSaleOrderAction action)
        {
            if (!action.CanRun)
                return false;

            if (action.Kind is YouPinSaleOrderActionKind.SendOffer or YouPinSaleOrderActionKind.ConfirmOffer)
                return true;

            return action.Kind == YouPinSaleOrderActionKind.QueryStatus
                && (action.StatusReason.Contains("手机令牌", StringComparison.Ordinal)
                    || action.StatusReason.Contains("Steam 报价", StringComparison.Ordinal));
        }

        private static SteamAutoTradeAction GetYouPinOrderAction(YouPinSaleOrderAction action, bool rental)
        {
            return action.Kind switch
            {
                YouPinSaleOrderActionKind.SendOffer => SteamAutoTradeAction.SendOffer,
                YouPinSaleOrderActionKind.ConfirmOffer => SteamAutoTradeAction.ConfirmYouPinOffer,
                YouPinSaleOrderActionKind.QueryStatus when rental => SteamAutoTradeAction.AcceptOffer,
                YouPinSaleOrderActionKind.QueryStatus => SteamAutoTradeAction.ConfirmMobile,
                _ => SteamAutoTradeAction.Skip
            };
        }

        public static SteamAutoTradeCategory ClassifyOffer(SteamOfferItem offer, YouPinSaleOrder? matchedOrder = null)
        {
            ArgumentNullException.ThrowIfNull(offer);

            matchedOrder ??= string.IsNullOrWhiteSpace(offer.PlatformOrderNo) && string.IsNullOrWhiteSpace(offer.YouPinOrderNo)
                ? null
                : new YouPinSaleOrder
                {
                    OrderNo = FirstText(offer.PlatformOrderNo, offer.YouPinOrderNo),
                    OrderType = IsLikelyRentalText(offer.Source, offer.Title, offer.ItemSummary) ? 2 : 0
                };

            if (matchedOrder != null)
            {
                if (IsRentalOrder(matchedOrder))
                    return SteamAutoTradeCategory.YouPinRental;

                return GetDirection(offer) == SteamAutoTradeDirection.Incoming
                    ? SteamAutoTradeCategory.YouPinPurchase
                    : SteamAutoTradeCategory.YouPinSale;
            }

            return IsPureIncomingOffer(offer)
                ? SteamAutoTradeCategory.PureIncoming
                : SteamAutoTradeCategory.Unknown;
        }

        public static SteamAutoTradeDirection GetDirection(SteamOfferItem offer)
        {
            ArgumentNullException.ThrowIfNull(offer);

            if (offer.ItemsToGive.Count > 0 && offer.ItemsToReceive.Count > 0)
                return SteamAutoTradeDirection.TwoWay;
            if (offer.ItemsToGive.Count > 0 || offer.Type == SteamOfferType.Outgoing)
                return SteamAutoTradeDirection.Outgoing;
            if (offer.ItemsToReceive.Count > 0 || offer.Type == SteamOfferType.IncomingGift)
                return SteamAutoTradeDirection.Incoming;
            return SteamAutoTradeDirection.Unknown;
        }

        public static bool IsMobileConfirmationMatch(SteamAutoTradePlanItem plan, SteamOfferItem confirmation)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(confirmation);

            if (!IsTradeConfirmation(confirmation))
                return false;

            string planTradeOfferId = (plan.TradeOfferId ?? string.Empty).Trim();
            string confirmationTradeOfferId = (confirmation.TradeOfferId ?? string.Empty).Trim();
            return !string.IsNullOrWhiteSpace(planTradeOfferId)
                && !string.IsNullOrWhiteSpace(confirmationTradeOfferId)
                && string.Equals(planTradeOfferId, confirmationTradeOfferId, StringComparison.OrdinalIgnoreCase);
        }

        public static string FormatDirection(SteamAutoTradeDirection direction)
        {
            return direction switch
            {
                SteamAutoTradeDirection.Incoming => "进",
                SteamAutoTradeDirection.Outgoing => "出",
                SteamAutoTradeDirection.TwoWay => "双向",
                _ => "-"
            };
        }

        public static string FormatRecordType(SteamAutoTradeRecordType type)
        {
            return type switch
            {
                SteamAutoTradeRecordType.AutoAccept => "自动接收",
                SteamAutoTradeRecordType.AutoSend => "自动发送",
                SteamAutoTradeRecordType.AutoYouPinConfirm => "自动确认悠悠报价",
                SteamAutoTradeRecordType.AutoMobileConfirm => "自动手机确认",
                SteamAutoTradeRecordType.ManualAccept => "手动接收",
                SteamAutoTradeRecordType.ManualSend => "手动发送",
                SteamAutoTradeRecordType.ManualMobileConfirm => "手动手机确认",
                SteamAutoTradeRecordType.Skip => "跳过",
                SteamAutoTradeRecordType.Failed => "失败",
                SteamAutoTradeRecordType.Pending => "待确认",
                SteamAutoTradeRecordType.Unresolved => "状态未决",
                SteamAutoTradeRecordType.TerminalFailure => "失败",
                _ => "未知"
            };
        }

        public static string FormatCategory(SteamAutoTradeCategory category)
        {
            return category switch
            {
                SteamAutoTradeCategory.PureIncoming => "纯收货",
                SteamAutoTradeCategory.YouPinPurchase => "悠悠购买",
                SteamAutoTradeCategory.YouPinSale => "悠悠出售",
                SteamAutoTradeCategory.YouPinRental => "悠悠出租",
                _ => "Steam 报价（未关联悠悠订单）"
            };
        }

        public static IEnumerable<string> BuildItemNames(SteamOfferItem offer, SteamAutoTradeDirection direction)
        {
            IEnumerable<TradeAsset> assets = direction switch
            {
                SteamAutoTradeDirection.Outgoing => offer.ItemsToGive,
                SteamAutoTradeDirection.Incoming => offer.ItemsToReceive,
                SteamAutoTradeDirection.TwoWay => offer.ItemsToGive.Concat(offer.ItemsToReceive),
                _ => offer.ItemsToGive.Concat(offer.ItemsToReceive)
            };

            return assets
                .Select(x => (x.MarketHashName ?? "").Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        public static IEnumerable<string> BuildOrderItemNames(YouPinSaleOrder order)
        {
            if (order.OrderItems.Count > 0)
            {
                return order.OrderItems
                    .Select(x => (x.Name ?? "").Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return string.IsNullOrWhiteSpace(order.Name)
                ? Array.Empty<string>()
                : new[] { order.Name.Trim() };
        }

        private static bool IsOfferAllowed(
            SteamAutoTradeCategory category,
            SteamAutoTradeSettings settings)
        {
            return category switch
            {
                SteamAutoTradeCategory.PureIncoming => false,
                SteamAutoTradeCategory.YouPinPurchase => settings.AcceptYouPinPurchaseEnabled,
                SteamAutoTradeCategory.YouPinRental => settings.SendYouPinRentalEnabled,
                _ => false
            };
        }

        private static SteamAutoTradeAction GetOfferAction(SteamAutoTradeCategory category)
        {
            if (category is SteamAutoTradeCategory.PureIncoming
                or SteamAutoTradeCategory.YouPinPurchase
                or SteamAutoTradeCategory.YouPinRental)
                return SteamAutoTradeAction.AcceptOffer;
            return SteamAutoTradeAction.Skip;
        }

        private static string BuildSkipReason(SteamAutoTradeCategory category)
        {
            return category switch
            {
                SteamAutoTradeCategory.PureIncoming => "仅自动接收匹配的悠悠购买报价",
                SteamAutoTradeCategory.YouPinPurchase => "悠悠购买接收规则未开启",
                SteamAutoTradeCategory.YouPinSale => "悠悠出售发送规则未开启",
                SteamAutoTradeCategory.YouPinRental => "悠悠出租发送规则未开启",
                _ => "未关联悠悠订单"
            };
        }

        private static Dictionary<string, YouPinSaleOrder> BuildYouPinOrderLookup(YouPinSaleReminderState? state)
        {
            var orders = (state?.RecentOrders ?? new List<YouPinSaleOrder>())
                .Concat(state?.RecentMsgCenterOrders ?? new List<YouPinSaleOrder>())
                .Concat(state?.RecentWaitDeliverOrders ?? new List<YouPinSaleOrder>())
                .Where(x => !string.IsNullOrWhiteSpace(x.TradeOfferId))
                .GroupBy(x => x.TradeOfferId.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            return orders;
        }

        private static YouPinSaleOrder? FindMatchedOrder(SteamOfferItem offer, IReadOnlyDictionary<string, YouPinSaleOrder> orders)
        {
            if (!string.IsNullOrWhiteSpace(offer.TradeOfferId)
                && orders.TryGetValue(offer.TradeOfferId.Trim(), out YouPinSaleOrder? byOffer))
                return byOffer;

            string orderNo = FirstText(offer.PlatformOrderNo, offer.YouPinOrderNo);
            if (!string.IsNullOrWhiteSpace(orderNo))
            {
                return orders.Values.FirstOrDefault(x => string.Equals(x.OrderNo, orderNo, StringComparison.OrdinalIgnoreCase)
                    || x.OrderNos.Any(order => string.Equals(order, orderNo, StringComparison.OrdinalIgnoreCase)));
            }

            return null;
        }

        private static bool IsPureIncomingOffer(SteamOfferItem offer)
        {
            return offer.Type == SteamOfferType.IncomingGift
                && offer.ItemsToGive.Count == 0
                && offer.ItemsToReceive.Count > 0
                && !offer.VerifiedByYouPin
                && string.IsNullOrWhiteSpace(offer.PlatformOrderNo)
                && string.IsNullOrWhiteSpace(offer.YouPinOrderNo);
        }

        private static bool IsRentalOrder(YouPinSaleOrder order)
        {
            return order.OrderType == 2 || IsLikelyRentalText(order.Message, order.Source, order.OrderStatusDesc, order.LeaseType);
        }

        private static bool IsLikelyRentalText(params string[] values)
        {
            string text = string.Join(" ", values ?? Array.Empty<string>());
            return text.Contains("出租", StringComparison.Ordinal)
                || text.Contains("租赁", StringComparison.Ordinal)
                || text.Contains("转交", StringComparison.Ordinal);
        }

        private static bool IsTradeConfirmation(SteamOfferItem confirmation)
        {
            if (confirmation.MobileConfirmationType.HasValue)
                return confirmation.MobileConfirmationType == SteamMobileConfirmationType.Trade;

            string text = string.Join(" ", confirmation.ConfirmationType, confirmation.Title, confirmation.Source, confirmation.ItemSummary);
            if (text.Contains("登录", StringComparison.Ordinal)
                || text.Contains("上架", StringComparison.Ordinal)
                || text.Contains("改价", StringComparison.Ordinal))
                return false;

            return string.IsNullOrWhiteSpace(confirmation.ConfirmationType)
                || text.Contains("交易", StringComparison.Ordinal)
                || text.Contains("trade", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(confirmation.TradeOfferId);
        }

        private static string FirstText(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }
    }
}
