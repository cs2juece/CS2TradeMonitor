using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.Core.Refresh;


namespace CS2TradeMonitor.Application.Market
{
    public class SteamDtItemData
    {
        public string ItemId { get; set; } = "";
        public double Price { get; set; }
        public double Change { get; set; }
        public double ChangeRatio { get; set; }
        public long UpdateTime { get; set; }
        public DateTime RetrievedAt { get; set; } = DateTime.Now;
        public bool IsStale { get; set; }
        public string Source { get; set; } = "未获取";
        public bool HasChangeData { get; set; } = false;

        public string FormatPrice() => Price.ToString("F2");
        public string FormatChange() => HasChangeData ? MarketDisplayFormatter.FormatSignedChange(Change) : "";
        public string FormatRatio() => HasChangeData ? MarketDisplayFormatter.FormatSignedPercent(ChangeRatio) : "";
    }

}
