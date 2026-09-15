namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage
{
    /// <summary>
    /// One ranked market identity from a frozen popular-item source. The SteamDT item ID is
    /// provenance only and must never be used as a YouPin template ID.
    /// </summary>
    public sealed class YouPinHotCatalogItem
    {
        internal YouPinHotCatalogItem(
            int rank,
            string marketHashName,
            string steamDtItemId,
            string category)
        {
            Rank = rank;
            MarketHashName = marketHashName;
            SteamDtItemId = steamDtItemId;
            Category = category;
        }

        public int Rank { get; }
        public string MarketHashName { get; }
        public string SteamDtItemId { get; }
        public string Category { get; }
    }
}
