using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Collections.Generic;

namespace CS2TradeMonitor.Application.YouPin
{
    public class YouPinSaleReminderState
    {
        public bool Enabled { get; set; }
        public DateTime LastCheck { get; set; }
        public string LastStatus { get; set; } = "";
        public string LastError { get; set; } = "";
        public List<YouPinSaleOrder> RecentOrders { get; set; } = new();

        public bool MsgCenterEnabled { get; set; }
        public DateTime LastMsgCenterCheck { get; set; }
        public string LastMsgCenterStatus { get; set; } = "";
        public string LastMsgCenterError { get; set; } = "";
        public List<YouPinSaleOrder> RecentMsgCenterOrders { get; set; } = new();
        public bool QuoteAutoRefreshEnabled { get; set; }
        public List<YouPinSaleOrder> RecentWaitDeliverOrders { get; set; } = new();
        public DateTime LastAutoDeliveryCheck { get; set; }
        public string LastAutoDeliveryStatus { get; set; } = "";
        public string LastAutoDeliveryError { get; set; } = "";
        public string HistoryPersistenceWarning { get; set; } = "";
    }

    public class YouPinSaleReminderCheckResult
    {
        public bool Ok { get; set; }
        public bool Skipped { get; set; }
        public string Message { get; set; } = "";
        public int NewOrderCount { get; set; }

        public static YouPinSaleReminderCheckResult Success(string message, int newOrderCount) => new() { Ok = true, Message = message, NewOrderCount = newOrderCount };
        public static YouPinSaleReminderCheckResult Failed(string message) => new() { Ok = false, Message = message };
        public static YouPinSaleReminderCheckResult Skip(string message) => new() { Ok = false, Skipped = true, Message = message };
    }

    public class YouPinSaleActionResult
    {
        public bool Ok { get; set; }
        public bool Skipped { get; set; }
        public string Message { get; set; } = "";
        public string TradeOfferId { get; set; } = "";
        public int Status { get; set; }

        public static YouPinSaleActionResult Success(string message, string tradeOfferId = "", int status = 0)
            => new() { Ok = true, Message = message, TradeOfferId = tradeOfferId, Status = status };

        public static YouPinSaleActionResult Failed(string message)
            => new() { Ok = false, Message = message };

        public static YouPinSaleActionResult Skip(string message)
            => new() { Ok = false, Skipped = true, Message = message };
    }
}
