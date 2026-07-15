using CS2TradeMonitor.Domain.YouPin;
using System;

namespace CS2TradeMonitor.Application.YouPin
{
    internal enum YouPinSaleOrderActionKind
    {
        SendOffer,
        ConfirmOffer,
        QueryStatus,
        Disabled
    }

    internal readonly record struct YouPinSaleOrderAction(
        YouPinSaleOrderActionKind Kind,
        string ButtonText,
        string ActionName,
        string StatusReason,
        bool CanRun);

    internal static class YouPinSaleOrderActionResolver
    {
        public static YouPinSaleOrderAction Resolve(YouPinSaleOrder? order)
        {
            if (order == null)
                return Disabled("不可操作", "订单为空。");

            if (string.IsNullOrWhiteSpace(order.OrderNo))
                return Disabled("不可操作", "订单号为空。");

            if (YouPinQuoteLocalState.IsConfirmSubmitted(order))
                return Disabled("等待同步", "确认报价已提交，等待平台同步。");

            if (IsPendingBuyQuote(order))
                return Disabled("手机处理", "悠悠购买待收货订单已读取；不会调用卖家发货接口。");

            string text = string.Join(" ", order.Message ?? string.Empty, order.Source ?? string.Empty, order.OrderStatusDesc ?? string.Empty).Trim();
            bool rentalQuote = IsRentalQuote(order, text);
            bool hasTradeOffer = !string.IsNullOrWhiteSpace(order.TradeOfferId);
            bool waitingCounterpartyConfirm = IsWaitingCounterpartyConfirm(text);
            bool waitingToken = IsWaitingSteamToken(order, text, hasTradeOffer);
            bool confirmState = ContainsAny(text, "待您确认报价", "等待确认报价", "待确认报价", "请确认报价");
            bool sendState = ContainsAny(text, "待您发送报价", "待发送报价", "发送报价", "待发报价");

            if (waitingCounterpartyConfirm)
            {
                return new YouPinSaleOrderAction(
                    YouPinSaleOrderActionKind.QueryStatus,
                    "查状态",
                    "查询报价状态",
                    "报价已发出，等待对方确认。",
                    CanRun: true);
            }

            if (confirmState)
            {
                return new YouPinSaleOrderAction(
                    YouPinSaleOrderActionKind.ConfirmOffer,
                    "确认报价",
                    "确认报价",
                    rentalQuote || order.OrderType == 2
                        ? "租赁报价等待在悠悠确认。"
                        : "订单等待确认报价。",
                    CanRun: true);
            }

            if (waitingToken)
            {
                return new YouPinSaleOrderAction(
                    YouPinSaleOrderActionKind.QueryStatus,
                    "查状态",
                    "查询报价状态",
                    "已发送报价，待您在 Steam 手机令牌中确认。",
                    CanRun: true);
            }

            if (sendState)
            {
                return new YouPinSaleOrderAction(
                    YouPinSaleOrderActionKind.SendOffer,
                    "发送报价",
                    "发送报价",
                    "订单等待发送报价。",
                    CanRun: true);
            }

            if (rentalQuote)
                return Disabled("租赁报价", "租赁报价正在等待平台或对方推进，进入可操作状态后将由悠悠出租自动处理接管。");

            if (order.OrderType == 2)
            {
                return new YouPinSaleOrderAction(
                    YouPinSaleOrderActionKind.QueryStatus,
                    "查状态",
                    "查询报价状态",
                    "出租订单需在 Steam 手机令牌或悠悠租赁流程中确认。",
                    CanRun: true);
            }

            if (ContainsAny(text, "待发货", "待您发货", "报价处理", "待处理"))
            {
                return new YouPinSaleOrderAction(
                    YouPinSaleOrderActionKind.QueryStatus,
                    "查状态",
                    "查询报价状态",
                    "订单状态不明确，先查询报价状态。",
                    CanRun: true);
            }

            return Disabled("不可发", "当前订单状态不支持发送或确认报价。");
        }

        public static bool IsPendingBuyQuote(YouPinSaleOrder? order)
        {
            return order != null
                && order.OrderStatus == 140
                && (order.Source?.Contains("悠悠购买", StringComparison.Ordinal) == true
                    || order.Message?.Contains("悠悠购买待收货", StringComparison.Ordinal) == true);
        }

        private static YouPinSaleOrderAction Disabled(string buttonText, string reason)
        {
            return new YouPinSaleOrderAction(
                YouPinSaleOrderActionKind.Disabled,
                buttonText,
                "处理报价",
                reason,
                CanRun: false);
        }

        private static bool ContainsAny(string text, params string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            foreach (string keyword in keywords)
            {
                if (text.Contains(keyword, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool IsRentalQuote(YouPinSaleOrder order, string text)
        {
            string combined = string.Join(" ", text, order.OrderStatusDesc ?? string.Empty, order.LeaseType ?? string.Empty);
            return ContainsAny(combined, "出租转交", "租赁转交", "转交饰品给新租客", "老承租方确认报价");
        }

        private static bool IsWaitingSteamToken(YouPinSaleOrder order, string text, bool hasTradeOffer)
        {
            string combined = string.Join(" ", text, order.OrderStatusDesc ?? string.Empty);
            return hasTradeOffer
                || ContainsAny(combined, "待您令牌验证", "Steam令牌", "Steam 令牌", "需令牌", "令牌确认", "手机令牌");
        }

        private static bool IsWaitingCounterpartyConfirm(string text)
        {
            return ContainsAny(text, "待对方确认报价", "对方确认报价");
        }
    }
}
