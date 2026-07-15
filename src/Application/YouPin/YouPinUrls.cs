namespace CS2TradeMonitor.Application.YouPin
{
    internal static class YouPinUrls
    {
        public const string ApiBase = "https://api.youpin898.com";
        public const string HybridBase = "https://hybrid.youpin898.com";
        public const string HybridBaseWithSlash = HybridBase + "/";
        public const string LandlordNormalShelf = "/api/youpin/bff/new/commodity/v1/commodity/list/lease";
        public const string LandlordZeroCdShelf = "/api/youpin/bff/new/commodity/v1/commodity/list/zeroCDLease";
        public const string LandlordPricingPreference =
            "/api/youpin/bff/service-user/app/preference/setting/v1/queryUserCustomConfig";
        public const string LandlordMarketLease = "/api/homepage/v3/detail/commodity/list/lease";
        public const string LandlordRepriceInit =
            "/api/youpin/bff/new/commodity/commodity/change/price/v3/init/info";
        public const string LandlordOneClickPricing =
            "/api/youpin/bff/new/commodity/v3/price/templatePricing";
        public const string LandlordChangeLeasePrice =
            "/api/commodity/Commodity/PriceChangeWithLeaseV2";
        public const string LandlordInventory =
            "/api/youpin/commodity-agg/inventory/list/pull";
        public const string LandlordInventoryQualification =
            "/api/youpin/bff/new/commodity/v1/inv/checkOnShelfQualificationInfo";
        public const string LandlordInventoryExtend =
            "/api/youpin/bff/new/commodity/v3/inv/upload/getInventoryExtendInfo";
        public const string LandlordListInventory =
            "/api/commodity/Inventory/SellInventoryWithLeaseV2";
        public const string LandlordGlobalReminderConfirm =
            "/api/youpin/safe/user/global/remind/data/push";
    }
}
