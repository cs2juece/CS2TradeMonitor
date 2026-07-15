using System;
using System.Collections.Generic;

namespace CS2TradeMonitor.Domain.Market
{
    public class SteamDtBaseCacheContainer
    {
        public DateTime LastUpdated { get; set; } = DateTime.MinValue;
        public List<SteamDtBaseItem> Items { get; set; } = new();
    }

    public class SteamDtBaseItem
    {
        public string Name { get; set; } = "";
        public string MarketHashName { get; set; } = "";
        public List<SteamDtPlatformItem> PlatformList { get; set; } = new();
    }

    public class SteamDtPlatformItem
    {
        public string Name { get; set; } = "";
        public string ItemId { get; set; } = "";
    }

    public class SteamDtSearchCandidate
    {
        public string ItemId { get; set; } = "";
        public string Name { get; set; } = "";
        public double Price { get; set; }
        public double Change { get; set; }
        public double ChangeRatio { get; set; }
        public string Source { get; set; } = "";
        public string MarketHashName { get; set; } = "";
        public string PlatformItemId { get; set; } = "";
    }
}
