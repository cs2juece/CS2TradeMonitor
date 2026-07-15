using System;
using System.Collections.Generic;

namespace CS2TradeMonitor.Domain.YouPin
{
    public class YouPinSaleReminderHistory
    {
        public List<string> KnownOrderIds { get; set; } = new();
        public List<YouPinSaleOrder> RecentOrders { get; set; } = new();
        public List<string> KnownMsgCenterIds { get; set; } = new();
        public List<YouPinSaleOrder> RecentMsgCenterOrders { get; set; } = new();
        public List<string> KnownWaitDeliverIds { get; set; } = new();
        public List<YouPinSaleOrder> RecentWaitDeliverOrders { get; set; } = new();
    }

    public class YouPinSaleOrder
    {
        public string AccountKey { get; set; } = "";
        public string OrderNo { get; set; } = "";
        public string Name { get; set; } = "";
        public string Message { get; set; } = "";
        public double Price { get; set; }
        public DateTime DetectedAt { get; set; }
        public string Source { get; set; } = "";
        public string ImageUrl { get; set; } = "";
        public string TradeOfferId { get; set; } = "";
        public int OrderType { get; set; }
        public int OrderStatus { get; set; }
        public int OrderSubStatus { get; set; }
        public int RealOrderSubStatus { get; set; }
        public string OrderStatusDesc { get; set; } = "";
        public string LeaseType { get; set; } = "";
        public string OfferId { get; set; } = "";
        public string SteamPersonaName { get; set; } = "";
        public string SteamAvatarUrl { get; set; } = "";
        public int SteamPlayerLevel { get; set; }
        public int SteamGameTime { get; set; }
        public string SteamJoinDate { get; set; } = "";
        public string SteamCounterpartyStatus { get; set; } = "";
        public bool IsOrderGroup { get; set; }
        public string OrderGroupId { get; set; } = "";
        public List<string> OrderNos { get; set; } = new();
        public List<YouPinSaleOrderItem> OrderItems { get; set; } = new();
        public string LocalQuoteState { get; set; } = "";
        public DateTime LocalQuoteStateAt { get; set; }
    }

    public class YouPinSaleOrderItem
    {
        public string OrderNo { get; set; } = "";
        public string Name { get; set; } = "";
        public double Price { get; set; }
        public string ImageUrl { get; set; } = "";
        public string TradeOfferId { get; set; } = "";
    }
}
