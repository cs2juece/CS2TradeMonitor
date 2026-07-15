using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Collections.Generic;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class YouPinSaleReminderPageModel
    {
        private const string OrderDetailTitle = "悠悠有品订单详情";

        public static IReadOnlyList<YouPinSaleReminderNotificationModeOption> NotificationModeOptions { get; } =
            Array.AsReadOnly(new[]
            {
                new YouPinSaleReminderNotificationModeOption("桌面右下角气泡", YouPinSaleReminderNotificationMode.Bubble),
                new YouPinSaleReminderNotificationModeOption("提示音", YouPinSaleReminderNotificationMode.Sound),
                new YouPinSaleReminderNotificationModeOption("右下角气泡 + 提示音", YouPinSaleReminderNotificationMode.BubbleAndSound)
            });

        public static YouPinSaleReminderNotificationMode NormalizeNotificationMode(YouPinSaleReminderNotificationMode mode)
        {
            return Enum.IsDefined(typeof(YouPinSaleReminderNotificationMode), mode)
                ? mode
                : YouPinSaleReminderNotificationMode.Bubble;
        }

        public static int NormalizeRefreshSeconds(int value, int defaultValue)
        {
            int normalized = value <= 0 ? defaultValue : value;
            return Math.Max(30, normalized);
        }

        public static YouPinSaleReminderSettingsDefaults BuildSettingsDefaults(
            string? inventoryToken,
            string? inventoryDeviceToken,
            int todoRefreshSec,
            int quoteAutoRefreshSec,
            int msgCenterRefreshSec,
            YouPinSaleReminderNotificationMode todoNotificationMode,
            YouPinSaleReminderNotificationMode msgCenterNotificationMode)
        {
            return new YouPinSaleReminderSettingsDefaults(
                inventoryToken ?? string.Empty,
                inventoryDeviceToken ?? string.Empty,
                NormalizeRefreshSeconds(todoRefreshSec, 180),
                NormalizeRefreshSeconds(quoteAutoRefreshSec, 180),
                NormalizeRefreshSeconds(msgCenterRefreshSec, 60),
                NormalizeNotificationMode(todoNotificationMode),
                NormalizeNotificationMode(msgCenterNotificationMode));
        }

        public static bool TryParseNotificationMode(string? selectedValue, out YouPinSaleReminderNotificationMode mode)
        {
            mode = YouPinSaleReminderNotificationMode.Bubble;
            if (!int.TryParse(selectedValue, out int value)
                || !Enum.IsDefined(typeof(YouPinSaleReminderNotificationMode), value))
            {
                return false;
            }

            mode = (YouPinSaleReminderNotificationMode)value;
            return true;
        }

        public static YouPinSaleReminderOrderDetailViewModel BuildOrderDetail(YouPinSaleOrder order)
        {
            ArgumentNullException.ThrowIfNull(order);

            var action = YouPinSaleOrderActionResolver.Resolve(order);
            var rows = new List<YouPinSaleReminderOrderDetailRowViewModel>
            {
                new("名称", YouPinSaleReminderOrderDisplay.BuildItemName(order)),
                new("订单号", string.IsNullOrWhiteSpace(order.OrderNo) ? "暂无" : order.OrderNo),
                new("状态", YouPinSaleReminderOrderDisplay.BuildStatusText(order)),
                new("价格", order.Price > 0 ? $"¥{order.Price:F2}" : "暂无价格"),
                new("图片", string.IsNullOrWhiteSpace(order.ImageUrl) ? "暂无" : "已获取"),
                new("Steam 报价号", string.IsNullOrWhiteSpace(order.TradeOfferId) ? "暂无" : order.TradeOfferId),
                new("Steam 对方", BuildSteamCounterpartyText(order)),
                new("当前操作", action.CanRun ? action.ButtonText : action.StatusReason),
                new("来源", string.IsNullOrWhiteSpace(order.Source) ? "待办消息" : order.Source),
                new("时间", order.DetectedAt == default ? "暂无时间" : order.DetectedAt.ToString("yyyy-MM-dd HH:mm:ss")),
                new("下一步", YouPinSaleReminderOrderDisplay.BuildNextStep(order))
            };

            return new YouPinSaleReminderOrderDetailViewModel(
                OrderDetailTitle,
                rows,
                YouPinSaleReminderOrderDisplay.BuildDetailBody(order));
        }

        private static string BuildSteamCounterpartyText(YouPinSaleOrder order)
        {
            if (!string.IsNullOrWhiteSpace(order.SteamPersonaName))
            {
                var parts = new List<string> { order.SteamPersonaName.Trim() };
                if (order.SteamPlayerLevel > 0)
                    parts.Add("等级 " + order.SteamPlayerLevel);
                if (order.SteamGameTime > 0)
                    parts.Add("游戏时长 " + order.SteamGameTime + "h");
                if (!string.IsNullOrWhiteSpace(order.SteamJoinDate))
                    parts.Add("加入 " + order.SteamJoinDate.Trim());
                return string.Join(" · ", parts);
            }

            return string.IsNullOrWhiteSpace(order.SteamCounterpartyStatus)
                ? "未获取"
                : order.SteamCounterpartyStatus.Trim();
        }
    }

    internal sealed record YouPinSaleReminderNotificationModeOption(
        string Text,
        YouPinSaleReminderNotificationMode Mode);

    internal sealed record YouPinSaleReminderTabRefreshPlan(
        bool UpdateWaitDeliverList,
        bool UpdateMsgCenterList,
        bool UpdateAutoDelivery);

    internal sealed record YouPinSaleReminderSettingsDefaults(
        string InventoryToken,
        string InventoryDeviceToken,
        int TodoRefreshSec,
        int QuoteAutoRefreshSec,
        int MsgCenterRefreshSec,
        YouPinSaleReminderNotificationMode TodoNotificationMode,
        YouPinSaleReminderNotificationMode MsgCenterNotificationMode);

    internal sealed record YouPinSaleReminderOrderDetailViewModel(
        string Title,
        IReadOnlyList<YouPinSaleReminderOrderDetailRowViewModel> Rows,
        string Body);

    internal sealed record YouPinSaleReminderOrderDetailRowViewModel(
        string Title,
        string Value);
}
