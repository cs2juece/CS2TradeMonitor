using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Collections.Generic;

namespace CS2TradeMonitor.Application.YouPin
{
    public class YouPinInventoryState
    {
        public bool Enabled { get; set; }
        public string LastStatus { get; set; } = "";
        public string LastError { get; set; } = "";
        public DateTime LastFetch { get; set; } = DateTime.MinValue;
        public int TotalCount { get; set; }
        public double TotalValue { get; set; }
        public double PreviousTotalValue { get; set; }
        public double TotalDelta { get; set; }
        public double TotalDeltaPercent { get; set; }
        public List<YouPinInventoryItem> Items { get; set; } = new();
        public List<YouPinInventoryChange> RecentChanges { get; set; } = new();
        public List<YouPinInventoryValueAlert> RecentValueAlerts { get; set; } = new();
        public List<YouPinStopProfitLossAlert> RecentStopProfitLossAlerts { get; set; } = new();
        public List<YouPinDailyPnl> DailyPoints { get; set; } = new();
    }

    public class YouPinStopProfitLossState
    {
        public bool Enabled { get; set; }
        public string LastStatus { get; set; } = "";
        public string LastError { get; set; } = "";
        public DateTime LastFetch { get; set; } = DateTime.MinValue;
        public int AlertCount { get; set; }
        public List<YouPinStopProfitLossAlert> RecentAlerts { get; set; } = new();
    }

    public class YouPinInventoryTrendState
    {
        public string LastStatus { get; set; } = "";
        public string LastError { get; set; } = "";
        public DateTime LastFetch { get; set; } = DateTime.MinValue;
        public int TotalCount { get; set; }
        public double TotalValue { get; set; }
        public double TotalDelta { get; set; }
        public double TotalDeltaPercent { get; set; }
        public double PurchaseValue { get; set; }
        public int MissingPurchaseCount { get; set; }
        public bool HasOfficialProfitAndLoss { get; set; }
        public List<YouPinInventoryTrendRow> Rows { get; set; } = new();
    }

    internal sealed class RemoteInventoryResult
    {
        public List<YouPinInventoryItem> Items { get; set; } = new();
        public double TotalValue { get; set; }
        public YouPinInventoryTrendState? TrendState { get; set; }
    }

    public class YouPinInventoryFetchResult
    {
        public bool Ok { get; set; }
        public bool Skipped { get; set; }
        public string Message { get; set; } = "";

        public static YouPinInventoryFetchResult Success(string message) => new() { Ok = true, Message = message };
        public static YouPinInventoryFetchResult Failed(string message) => new() { Ok = false, Message = message };
        public static YouPinInventoryFetchResult Skip(string message) => new() { Ok = false, Skipped = true, Message = message };
    }

    internal sealed class InventoryUnitPriceGroup
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public int Quantity { get; set; }
        public double UnitPrice { get; set; }
    }
}
