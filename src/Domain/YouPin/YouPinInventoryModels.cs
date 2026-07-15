using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CS2TradeMonitor.Domain.YouPin
{
    public class YouPinInventorySnapshot
    {
        public DateTime Time { get; set; }
        public string Source { get; set; } = "";
        public int TotalCount { get; set; }
        public double TotalValue { get; set; }
        public double PreviousTotalValue { get; set; }
        public double TotalDelta { get; set; }
        public double TotalDeltaPercent { get; set; }
        public List<YouPinInventoryItem> Items { get; set; } = new();
    }

    public class YouPinInventoryItem
    {
        public string AssetId { get; set; } = "";
        public string TemplateId { get; set; } = "";
        public string Name { get; set; } = "";
        public double Price { get; set; }
        public double PurchasePrice { get; set; }
        public int Quantity { get; set; } = 1;
        public string RawStatus { get; set; } = "";
    }

    public class YouPinInventoryTrendRow
    {
        public string Name { get; set; } = "";
        public string TemplateId { get; set; } = "";
        public int Quantity { get; set; }
        public double CurrentPrice { get; set; }
        public double PreviousPrice { get; set; }
        public double Delta { get; set; }
        public double Percent { get; set; }
        public double PurchasePrice { get; set; }
        public int MissingEstimateCount { get; set; }
        public int MissingPurchaseCount { get; set; }

        [JsonIgnore] public bool HasEstimate => CurrentPrice > 0;
        [JsonIgnore] public bool HasPurchasePrice => PurchasePrice > 0;
    }

    public class YouPinInventoryHistory
    {
        public List<YouPinInventorySnapshot> Snapshots { get; set; } = new();
        public List<YouPinInventoryChange> Changes { get; set; } = new();
        public List<YouPinDailyPnl> Daily { get; set; } = new();
        public List<YouPinInventoryValueAlert> ValueAlerts { get; set; } = new();
        public DateTime LastValueAlertTime { get; set; } = DateTime.MinValue;
        public List<YouPinStopProfitLossAlert> StopProfitLossAlerts { get; set; } = new();
        public Dictionary<string, DateTime> LastStopProfitLossAlertTimes { get; set; } = new();
    }

    public class YouPinInventoryChange
    {
        public DateTime Time { get; set; }
        public string Type { get; set; } = "";
        public string AssetId { get; set; } = "";
        public string Name { get; set; } = "";
        public double OldPrice { get; set; }
        public double NewPrice { get; set; }
        public double Delta { get; set; }
    }

    public class YouPinInventoryValueAlert
    {
        public DateTime Time { get; set; }
        public string Direction { get; set; } = "";
        public double OldValue { get; set; }
        public double NewValue { get; set; }
        public double Delta { get; set; }
        public double Percent { get; set; }
        public string Source { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public class YouPinStopProfitLossAlert
    {
        public DateTime Time { get; set; }
        public DateTime BaselineTime { get; set; }
        public string Direction { get; set; } = "";
        public string Name { get; set; } = "";
        public int Quantity { get; set; }
        public double OldUnitPrice { get; set; }
        public double NewUnitPrice { get; set; }
        public double Delta { get; set; }
        public double Percent { get; set; }
        public int WindowMinutes { get; set; }
        public string DedupeKey { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public class YouPinDailyPnl
    {
        public string Date { get; set; } = "";
        public double EndValue { get; set; }
        public double ProfitAndLoss { get; set; }
        public double ProfitAndLossPercent { get; set; }
        public bool HasProfitAndLoss { get; set; }
        public double Pnl { get; set; }
        public int Count { get; set; }
        public DateTime LastUpdate { get; set; }
    }
}
