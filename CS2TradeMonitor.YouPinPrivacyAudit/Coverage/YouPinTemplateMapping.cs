namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage
{
    /// <summary>
    /// Explicit exact-name mapping. This is the only supported bridge from a market identity
    /// to a YouPin template ID; SteamDT item IDs are never accepted in its place.
    /// </summary>
    public sealed class YouPinTemplateMapping
    {
        private YouPinTemplateMapping(string marketHashName, long templateId)
        {
            MarketHashName = marketHashName;
            TemplateId = templateId;
        }

        public string MarketHashName { get; }
        public long TemplateId { get; }

        public static YouPinTemplateMapping Create(string marketHashName, long templateId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(marketHashName);
            string normalizedName = marketHashName.Trim();
            if (!string.Equals(normalizedName, marketHashName, StringComparison.Ordinal)
                || normalizedName.Length > 512
                || normalizedName.Any(char.IsControl))
            {
                throw new ArgumentException("market_hash_name 格式无效。", nameof(marketHashName));
            }
            if (templateId <= 0)
                throw new ArgumentOutOfRangeException(nameof(templateId), "悠悠模板 ID 必须为正整数。");

            return new YouPinTemplateMapping(normalizedName, templateId);
        }
    }
}
