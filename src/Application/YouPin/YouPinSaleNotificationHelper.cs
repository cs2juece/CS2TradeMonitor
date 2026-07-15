using CS2TradeMonitor.Domain.YouPin;
using System;

namespace CS2TradeMonitor.Application.YouPin
{
    internal static class YouPinSaleNotificationHelper
    {
        public static bool ShouldIncludeTodo(YouPinSaleOrder order)
        {
            if (order == null) return false;

            string name = order.Name ?? "";
            string message = order.Message ?? "";
            string fullText = name + " | " + message;

            string[] exclusions = { "支付中", "转交中", "转交成功", "租赁成功", "已完成", "普通待办消息", "系统通知", "活动", "消息中心" };
            foreach (var exclusion in exclusions)
            {
                if (fullText.Contains(exclusion, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            string[] actionKeywords =
            {
                "待处理",
                "无法接收归还",
                "报价处理",
                "待发货",
                "待您发货",
                "待您发送报价",
                "待发送报价",
                "待您确认报价",
                "等待确认报价",
                "待确认报价",
                "确认报价"
            };
            foreach (var keyword in actionKeywords)
            {
                if (fullText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static string BuildNotificationMessage(YouPinSaleOrder order, bool isMsgCenter)
        {
            if (isMsgCenter)
            {
                string name = string.IsNullOrWhiteSpace(order.Name) ? "系统消息" : order.Name;
                string message = string.IsNullOrWhiteSpace(order.Message) ? "收到新消息" : order.Message;
                return $"{name}\n{message}";
            }

            bool buyerPurchase = IsBuyerPurchaseTodo(order);
            string orderName = string.IsNullOrWhiteSpace(order.Name) ? "悠悠有品待办消息" : order.Name;
            string orderMessage = string.IsNullOrWhiteSpace(order.Message) ? "有新的待办消息" : order.Message;
            if (buyerPurchase)
                return string.IsNullOrWhiteSpace(order.Name)
                    ? $"有买家下单，订单号：{order.OrderNo}"
                    : $"有买家购买：{orderName}\n订单号：{order.OrderNo}";

            return $"{orderName}\n{orderMessage}";
        }

        private static bool IsBuyerPurchaseTodo(YouPinSaleOrder order)
        {
            if (order == null) return false;

            string name = order.Name ?? "";
            string message = order.Message ?? "";
            string fullText = name + " | " + message;

            string[] exclusions = { "支付中", "转交中", "转交成功", "租赁成功", "已完成", "普通待办消息", "系统通知", "活动", "消息中心" };
            foreach (var exclusion in exclusions)
            {
                if (fullText.Contains(exclusion, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return message.Contains("有买家下单", StringComparison.Ordinal)
                || message.Contains("待您发送报价", StringComparison.Ordinal)
                || message.Contains("有买家购买", StringComparison.Ordinal)
                || message.Contains("待发货", StringComparison.Ordinal)
                || message.Contains("待您发货", StringComparison.Ordinal)
                || message.Contains("待您发送报价", StringComparison.Ordinal)
                || message.Contains("待发送报价", StringComparison.Ordinal)
                || message.Contains("待您确认报价", StringComparison.Ordinal)
                || message.Contains("等待确认报价", StringComparison.Ordinal)
                || message.Contains("待确认报价", StringComparison.Ordinal)
                || (message.Contains("买家", StringComparison.Ordinal) && (message.Contains("下单", StringComparison.Ordinal) || message.Contains("购买", StringComparison.Ordinal)));
        }
    }
}
