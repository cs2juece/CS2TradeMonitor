using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class YouPinSaleReminderOrderDisplay
    {
        public static string BuildListSignature(IReadOnlyList<YouPinSaleOrder> orders, string emptyText, int rowHeight)
        {
            var builder = new StringBuilder();
            builder.Append(rowHeight).Append('|').Append(orders.Count);
            if (orders.Count == 0)
                builder.Append('|').Append(emptyText);

            foreach (YouPinSaleOrder order in orders)
            {
                builder
                    .Append('\n')
                    .Append(order.OrderNo).Append('|')
                    .Append(order.TradeOfferId).Append('|')
                    .Append(order.Message).Append('|')
                    .Append(order.Name).Append('|')
                    .Append(order.Price.ToString("R")).Append('|')
                    .Append(order.Source).Append('|')
                    .Append(order.ImageUrl).Append('|')
                    .Append(order.OrderType).Append('|')
                    .Append(order.OrderStatus).Append('|')
                    .Append(order.OrderSubStatus).Append('|')
                    .Append(order.RealOrderSubStatus).Append('|')
                    .Append(order.OrderStatusDesc).Append('|')
                    .Append(order.LeaseType).Append('|')
                    .Append(order.OfferId).Append('|')
                    .Append(order.SteamPersonaName).Append('|')
                    .Append(order.SteamCounterpartyStatus).Append('|')
                    .Append(order.IsOrderGroup).Append('|')
                    .Append(order.OrderGroupId).Append('|')
                    .Append(order.LocalQuoteState).Append('|')
                    .Append(order.LocalQuoteStateAt.ToString("O")).Append('|')
                    .Append(string.Join(",", order.OrderNos ?? new List<string>())).Append('|')
                    .Append(BuildOrderGroupItemsSignature(order));
            }

            return builder.ToString();
        }

        public static string BuildRenderSignature(YouPinSaleOrder order)
        {
            return string.Join("|", new[]
            {
                order.OrderNo ?? string.Empty,
                order.TradeOfferId ?? string.Empty,
                order.Message ?? string.Empty,
                order.Name ?? string.Empty,
                order.Price.ToString("R"),
                order.Source ?? string.Empty,
                order.ImageUrl ?? string.Empty,
                order.OrderType.ToString(),
                order.OrderStatus.ToString(),
                order.OrderSubStatus.ToString(),
                order.RealOrderSubStatus.ToString(),
                order.OrderStatusDesc ?? string.Empty,
                order.LeaseType ?? string.Empty,
                order.OfferId ?? string.Empty,
                order.SteamPersonaName ?? string.Empty,
                order.SteamCounterpartyStatus ?? string.Empty,
                BuildTime(order),
                order.IsOrderGroup.ToString(),
                order.OrderGroupId ?? string.Empty,
                order.LocalQuoteState ?? string.Empty,
                order.LocalQuoteStateAt.ToString("O"),
                string.Join(",", order.OrderNos ?? new List<string>()),
                BuildOrderGroupItemsSignature(order)
            });
        }

        public static bool IsActionableQuoteOrder(YouPinSaleOrder order)
        {
            if (YouPinSaleOrderActionResolver.IsPendingBuyQuote(order))
                return true;

            var action = YouPinSaleOrderActionResolver.Resolve(order);
            return action.CanRun
                && (action.Kind == YouPinSaleOrderActionKind.SendOffer
                    || action.Kind == YouPinSaleOrderActionKind.ConfirmOffer
                    || IsRentalSteamProcessingAction(order, action));
        }

        public static string BuildCompactQuoteMeta(YouPinSaleOrder order)
        {
            string source = YouPinSaleOrderActionResolver.IsPendingBuyQuote(order) ? "悠悠购买" : "悠悠出售";
            return source + " · " + BuildRelativeTime(order.DetectedAt);
        }

        public static string BuildCompactQuoteStatusText(YouPinSaleOrder order)
        {
            return BuildStatusText(order) switch
            {
                "待您发送报价" => "待发送报价",
                "待您确认报价" => "待确认报价",
                "待对方确认报价" => "待对方确认",
                "待您令牌验证" => "待令牌验证",
                "确认报价成功" => "等待同步",
                "等待平台同步" => "等待同步",
                "待查状态" => "待查状态",
                string value when string.IsNullOrWhiteSpace(value) => "待处理",
                string value => value
            };
        }

        public static string BuildTitle(YouPinSaleOrder order)
        {
            if (YouPinSaleOrderActionResolver.IsPendingBuyQuote(order))
                return "购买待收货，待发送报价";

            if (YouPinQuoteLocalState.IsConfirmSubmitted(order))
                return "确认报价成功";

            if (IsWaitingForCounterpartyConfirm(order))
                return "待对方确认报价";

            var action = YouPinSaleOrderActionResolver.Resolve(order);
            if (IsRentalSteamProcessingAction(order, action))
                return "租赁报价，待处理";
            if (order.OrderType == 2
                && action.Kind is YouPinSaleOrderActionKind.SendOffer or YouPinSaleOrderActionKind.ConfirmOffer)
            {
                return "租赁报价，待处理";
            }
            if (action.Kind == YouPinSaleOrderActionKind.ConfirmOffer)
                return "确认报价，待处理";
            if (action.Kind == YouPinSaleOrderActionKind.SendOffer)
                return "报价处理，待处理";
            if (action.Kind == YouPinSaleOrderActionKind.QueryStatus)
                return IsWaitingForSteamToken(order) ? "待您令牌验证" : "报价状态待核对";
            if (string.Equals(action.ButtonText, "租赁报价", StringComparison.Ordinal))
                return "租赁报价，等待中";

            string text = order.Message ?? string.Empty;
            if (text.Contains("无法接收归还", StringComparison.Ordinal))
                return "无法接收归还，待处理";
            if (text.Contains("报价处理", StringComparison.Ordinal)
                || text.Contains("待您发送报价", StringComparison.Ordinal)
                || text.Contains("待发送报价", StringComparison.Ordinal))
                return "报价处理，待处理";
            if (text.Contains("待发货", StringComparison.Ordinal) || text.Contains("待您发货", StringComparison.Ordinal))
                return "待发货，待处理";
            if (text.Contains("待处理", StringComparison.Ordinal))
                return text;
            return string.IsNullOrWhiteSpace(order.Source) ? "待办消息，待处理" : order.Source;
        }

        public static string BuildItemText(YouPinSaleOrder order)
        {
            string name = BuildItemName(order);
            return order.Price > 0 ? $"{name}  ¥{order.Price:F2}" : name;
        }

        public static string BuildMeta(YouPinSaleOrder order)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(order.OrderNo))
                parts.Add("订单号 " + MaskId(order.OrderNo));
            if (!string.IsNullOrWhiteSpace(order.TradeOfferId))
                parts.Add("报价号 " + MaskId(order.TradeOfferId));
            string reason = BuildReason(order);
            if (!string.IsNullOrWhiteSpace(reason))
                parts.Add("原因：" + reason);
            parts.Add("下一步：" + BuildNextStep(order));
            return parts.Count == 0 ? "暂无订单信息" : string.Join("  |  ", parts);
        }

        public static string BuildReason(YouPinSaleOrder order)
        {
            if (YouPinSaleOrderActionResolver.IsPendingBuyQuote(order))
                return "悠悠购买待收货，等待发送报价";

            if (YouPinQuoteLocalState.IsConfirmSubmitted(order))
                return "确认报价已提交，等待平台同步";

            if (IsWaitingForCounterpartyConfirm(order))
                return "报价已发出，等待对方确认";

            var action = YouPinSaleOrderActionResolver.Resolve(order);
            if (!string.IsNullOrWhiteSpace(action.StatusReason))
                return action.StatusReason;

            string text = order.Message ?? string.Empty;
            if (text.Contains("无法接收归还", StringComparison.Ordinal))
                return "平台提示无法接收归还";
            if (text.Contains("报价处理", StringComparison.Ordinal)
                || text.Contains("待您发送报价", StringComparison.Ordinal)
                || text.Contains("待发送报价", StringComparison.Ordinal))
                return "需要发送或核对 Steam 报价";
            if (text.Contains("待发货", StringComparison.Ordinal) || text.Contains("待您发货", StringComparison.Ordinal))
                return "订单等待发货";
            if (!string.IsNullOrWhiteSpace(order.Source))
                return order.Source;
            return "待办同步返回待处理状态";
        }

        public static string BuildNextStep(YouPinSaleOrder order)
        {
            if (YouPinSaleOrderActionResolver.IsPendingBuyQuote(order))
                return "已读取购买待收货订单；当前不会调用卖家发货接口";

            if (YouPinQuoteLocalState.IsConfirmSubmitted(order))
                return "等待平台同步，后台会自动复核";

            if (IsWaitingForCounterpartyConfirm(order))
                return "等待对方确认报价，可点击“查状态”核对结果";

            var action = YouPinSaleOrderActionResolver.Resolve(order);
            if (IsRentalSteamProcessingAction(order, action))
                return "已开启租赁自动处理时，后台会按 Steam 真实状态自动接收或确认；也可点击“查状态”核对";
            return action.Kind switch
            {
                YouPinSaleOrderActionKind.SendOffer => "点击“发送报价”创建 Steam 报价",
                YouPinSaleOrderActionKind.ConfirmOffer when order.OrderType == 2 => "先在悠悠确认报价，成功后后台再处理对应的 Steam 报价",
                YouPinSaleOrderActionKind.ConfirmOffer => "点击“确认报价”确认已有 Steam 报价",
                YouPinSaleOrderActionKind.QueryStatus when IsWaitingForSteamToken(order) => "请在 Steam 手机令牌中确认报价，然后点击“查状态”核对结果",
                YouPinSaleOrderActionKind.QueryStatus => "点击“查状态”核对订单当前报价状态",
                YouPinSaleOrderActionKind.Disabled => action.StatusReason,
                _ => action.StatusReason
            };
        }

        public static string BuildTime(YouPinSaleOrder order)
        {
            return order.DetectedAt == default
                ? "暂无时间"
                : order.DetectedAt.ToString("MM-dd HH:mm:ss");
        }

        private static string BuildRelativeTime(DateTime time)
        {
            if (time == default)
                return "暂无时间";

            DateTime now = DateTime.Now;
            if (time.Date == now.Date)
                return "今天 " + time.ToString("HH:mm");
            if (time.Date == now.Date.AddDays(-1))
                return "昨天 " + time.ToString("HH:mm");
            return time.ToString("MM-dd HH:mm");
        }

        public static string BuildDetailBody(YouPinSaleOrder order)
        {
            if (YouPinSaleOrderActionResolver.IsPendingBuyQuote(order))
                return "已从悠悠购买待收货列表读取该订单。当前仅展示，不会调用卖家发货接口。";

            if (YouPinQuoteLocalState.IsConfirmSubmitted(order))
                return "确认报价已提交，等待平台同步。后台刷新会自动复核结果。";

            if (IsWaitingForCounterpartyConfirm(order))
                return "报价已发出，等待对方确认。可点击“查状态”核对订单当前状态。";

            if (IsWaitingForSteamToken(order))
                return "请在 Steam 手机令牌中确认报价，确认后回到本页点击“查状态”。";

            var action = YouPinSaleOrderActionResolver.Resolve(order);
            if (IsRentalSteamProcessingAction(order, action))
                return "后台会按 Steam 真实状态自动接收或确认该租赁报价。也可点击“查状态”核对。";
            return action.Kind switch
            {
                YouPinSaleOrderActionKind.SendOffer => "点击“发送报价”创建 Steam 报价。",
                YouPinSaleOrderActionKind.ConfirmOffer when order.OrderType == 2 => "先在悠悠确认报价；确认成功后，后台再处理对应的 Steam 报价。",
                YouPinSaleOrderActionKind.ConfirmOffer => "点击“确认报价”确认已有 Steam 报价。",
                YouPinSaleOrderActionKind.QueryStatus => "点击“查状态”核对订单当前报价状态。",
                YouPinSaleOrderActionKind.Disabled => action.StatusReason,
                _ => string.IsNullOrWhiteSpace(order.Message) ? "暂无消息内容" : order.Message.Trim()
            };
        }

        public static string BuildStatusText(YouPinSaleOrder order)
        {
            if (YouPinSaleOrderActionResolver.IsPendingBuyQuote(order))
                return "待您发送报价";

            if (YouPinQuoteLocalState.IsConfirmSubmitted(order))
                return "等待平台同步";

            if (IsWaitingForCounterpartyConfirm(order))
                return "待对方确认报价";

            if (IsWaitingForSteamToken(order))
                return "待您令牌验证";

            var action = YouPinSaleOrderActionResolver.Resolve(order);
            if (IsRentalSteamProcessingAction(order, action))
                return "待 Steam 处理";
            return action.Kind switch
            {
                YouPinSaleOrderActionKind.SendOffer => "待您发送报价",
                YouPinSaleOrderActionKind.ConfirmOffer => "待您确认报价",
                YouPinSaleOrderActionKind.QueryStatus => "待查状态",
                _ when string.Equals(action.ButtonText, "租赁报价", StringComparison.Ordinal) => "租赁报价等待中",
                _ => string.IsNullOrWhiteSpace(CleanStatusText(order.OrderStatusDesc)) ? "待处理" : CleanStatusText(order.OrderStatusDesc)
            };
        }

        public static bool IsWaitingForSteamToken(YouPinSaleOrder order)
        {
            if (YouPinQuoteLocalState.IsConfirmSubmitted(order))
                return false;

            if (IsWaitingForCounterpartyConfirm(order))
                return false;

            if (!string.IsNullOrWhiteSpace(order.TradeOfferId))
                return true;

            string text = string.Join(" ", order.Message ?? string.Empty, order.OrderStatusDesc ?? string.Empty);
            return text.Contains("待您令牌验证", StringComparison.Ordinal)
                || text.Contains("Steam令牌", StringComparison.Ordinal)
                || text.Contains("Steam 令牌", StringComparison.Ordinal)
                || text.Contains("需令牌", StringComparison.Ordinal)
                || text.Contains("令牌确认", StringComparison.Ordinal)
                || text.Contains("手机令牌", StringComparison.Ordinal);
        }

        public static bool IsWaitingForCounterpartyConfirm(YouPinSaleOrder order)
        {
            string text = string.Join(" ", order.Message ?? string.Empty, order.OrderStatusDesc ?? string.Empty);
            return text.Contains("待对方确认报价", StringComparison.Ordinal)
                || text.Contains("对方确认报价", StringComparison.Ordinal);
        }

        private static bool IsRentalSteamProcessingAction(
            YouPinSaleOrder order,
            YouPinSaleOrderAction action)
        {
            return order.OrderType == 2
                && action.CanRun
                && action.Kind == YouPinSaleOrderActionKind.QueryStatus
                && action.StatusReason.Contains("Steam 报价", StringComparison.Ordinal);
        }

        public static string BuildOrderGroupItemsSignature(YouPinSaleOrder order)
        {
            if (order.OrderItems == null || order.OrderItems.Count == 0)
                return string.Empty;

            return string.Join(";", order.OrderItems.Select(x =>
                string.Join(",", new[]
                {
                    x.OrderNo ?? string.Empty,
                    x.Name ?? string.Empty,
                    x.Price.ToString("R"),
                    x.ImageUrl ?? string.Empty,
                    x.TradeOfferId ?? string.Empty
                })));
        }

        public static int GetOrderGroupItemCount(YouPinSaleOrder order)
        {
            int itemCount = order.OrderItems?.Count ?? 0;
            int orderNoCount = order.OrderNos?.Count ?? 0;
            return Math.Max(itemCount, orderNoCount);
        }

        public static string BuildOrderGroupItemSummary(YouPinSaleOrder order)
        {
            if (order.OrderItems != null && order.OrderItems.Count > 0)
            {
                string summary = string.Join("、", order.OrderItems
                    .Select(x => string.IsNullOrWhiteSpace(x.Name) ? MaskId(x.OrderNo) : x.Name)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Take(3));
                if (!string.IsNullOrWhiteSpace(summary))
                    return summary;
            }

            string name = order.Name ?? "";
            int separator = name.IndexOf('：');
            return separator >= 0 && separator + 1 < name.Length ? name[(separator + 1)..] : name;
        }

        public static string BuildItemName(YouPinSaleOrder order)
        {
            string name = "";
            if (order.IsOrderGroup)
                name = BuildOrderGroupItemSummary(order);

            if (string.IsNullOrWhiteSpace(name) || LooksLikeLegacyGroupLabel(name))
                name = order.Name ?? "";
            if (order.IsOrderGroup && LooksLikeLegacyGroupLabel(name))
                name = "";

            return string.IsNullOrWhiteSpace(name) ? MaskId(order.OrderNo) : name;
        }

        private static bool LooksLikeLegacyGroupLabel(string text)
        {
            return !string.IsNullOrWhiteSpace(text)
                && text.Contains('\u5408', StringComparison.Ordinal)
                && text.Contains('\u5e76', StringComparison.Ordinal)
                && text.Contains('\u8ba2', StringComparison.Ordinal)
                && text.Contains('\u5355', StringComparison.Ordinal);
        }

        public static string MaskId(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return "暂无";
            string value = id.Trim();
            if (value.Length <= 8)
                return value;
            return value[..4] + "..." + value[^4..];
        }

        private static string CleanStatusText(string? text)
        {
            string value = (text ?? string.Empty).Trim();
            if (value.EndsWith(" -s", StringComparison.OrdinalIgnoreCase))
                return value[..^3].TrimEnd();
            if (value.EndsWith("-s", StringComparison.OrdinalIgnoreCase))
                return value[..^2].TrimEnd();
            return value;
        }
    }
}
