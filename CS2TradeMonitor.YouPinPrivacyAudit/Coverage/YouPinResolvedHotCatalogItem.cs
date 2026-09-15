namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage
{
    public sealed class YouPinResolvedHotCatalogItem
    {
        internal YouPinResolvedHotCatalogItem(YouPinHotCatalogItem source, long templateId)
        {
            Rank = source.Rank;
            MarketHashName = source.MarketHashName;
            SteamDtItemId = source.SteamDtItemId;
            Category = source.Category;
            TemplateId = templateId;
        }

        public int Rank { get; }
        public string MarketHashName { get; }
        public string SteamDtItemId { get; }
        public string Category { get; }
        public long TemplateId { get; }
    }
}
