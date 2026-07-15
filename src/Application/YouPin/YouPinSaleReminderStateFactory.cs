using CS2TradeMonitor.Domain.YouPin;
using System.Collections.Generic;

namespace CS2TradeMonitor.Application.YouPin
{
    internal static class YouPinSaleReminderStateFactory
    {
        public static YouPinSaleReminderState CreateNoCredentialState(Settings settings)
        {
            string todoStatus = settings.YouPinSaleReminderEnabled ? "未登录" : "未启用";
            string msgCenterStatus = settings.YouPinMsgCenterEnabled ? "未登录" : "未启用";
            string quoteStatus = settings.YouPinQuoteAutoRefreshEnabled ? "未登录" : "未启用";
            return new YouPinSaleReminderState
            {
                Enabled = settings.YouPinSaleReminderEnabled,
                LastStatus = todoStatus,
                LastError = "",
                RecentOrders = new List<YouPinSaleOrder>(),
                MsgCenterEnabled = settings.YouPinMsgCenterEnabled,
                LastMsgCenterStatus = msgCenterStatus,
                LastMsgCenterError = "",
                RecentMsgCenterOrders = new List<YouPinSaleOrder>(),
                QuoteAutoRefreshEnabled = settings.YouPinQuoteAutoRefreshEnabled,
                RecentWaitDeliverOrders = new List<YouPinSaleOrder>(),
                LastAutoDeliveryStatus = quoteStatus,
                LastAutoDeliveryError = ""
            };
        }
    }
}
