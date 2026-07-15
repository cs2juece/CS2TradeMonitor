using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CS2TradeMonitor.Domain.YouPin
{
    public enum YouPinProfitLossDirection
    {
        Buy,
        Sell
    }

    public class YouPinProfitLossHistory
    {
        public int SchemaVersion { get; set; } = 2;
        public DateTime LastSync { get; set; } = DateTime.MinValue;
        public List<YouPinProfitLossRecord> Records { get; set; } = new();
    }

    public class YouPinProfitLossRecord
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public YouPinProfitLossDirection Direction { get; set; }
        public string OrderNo { get; set; } = "";
        public string DetailNo { get; set; } = "";
        public string Name { get; set; } = "";
        public string TemplateId { get; set; } = "";
        public string CommodityId { get; set; } = "";
        public string CommodityHashName { get; set; } = "";
        public string Abrade { get; set; } = "";
        public string AssetId { get; set; } = "";
        public string SourceEndpoint { get; set; } = "";
        public double Amount { get; set; }
        public int Quantity { get; set; } = 1;
        public DateTime Time { get; set; } = DateTime.MinValue;
        public string Status { get; set; } = "";
    }

    public class YouPinProfitLossRow
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string TemplateId { get; set; } = "";
        public int BuyCount { get; set; }
        public int SellCount { get; set; }
        public double BuyAmount { get; set; }
        public double SellAmount { get; set; }
        public double NetProfit { get; set; }
        public double NetRate { get; set; }
        public DateTime LastTradeTime { get; set; } = DateTime.MinValue;

        [JsonIgnore] public double AverageBuy => BuyCount > 0 ? BuyAmount / BuyCount : 0;
        [JsonIgnore] public double AverageSell => SellCount > 0 ? SellAmount / SellCount : 0;
    }
}
