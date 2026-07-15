using System;
using System.Collections.Generic;

namespace CS2TradeMonitor.Domain.Market
{
    public sealed record MarketSnapshot(
        MarketIndexSnapshot? SteamDt,
        MarketIndexSnapshot? Qaq,
        IReadOnlyDictionary<string, MarketIndexSnapshot> Sources,
        DateTime UpdatedAt)
    {
        public static MarketSnapshot Empty { get; } = new(
            null,
            null,
            new Dictionary<string, MarketIndexSnapshot>(StringComparer.OrdinalIgnoreCase),
            DateTime.MinValue);
    }

    public sealed record MarketIndexSnapshot(
        string Id,
        string DisplayName,
        string TypeDescription,
        string Status,
        string LastRefresh,
        string LastError,
        double Index,
        double Change,
        double Percent,
        string Source,
        DateTime RetrievedAt,
        bool HasData,
        bool IsStale);

    public sealed record MonitoredItemSnapshot(
        string ItemKey,
        string ItemId,
        string Name,
        string ShortName,
        double Price,
        double Change,
        double ChangePercent,
        DateTime RetrievedAt,
        string Status,
        bool HasData,
        bool IsStale);
}
