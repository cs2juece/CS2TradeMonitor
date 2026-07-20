using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;

namespace CS2TradeMonitor.Application.YouPin
{
    internal interface IYouPinLandlordGateway
    {
        Task ValidateLoginAsync(Settings settings, CancellationToken cancellationToken);

        Task<YouPinLandlordRemoteSnapshot> ReadSnapshotAsync(
            Settings settings,
            YouPinRentalScanScope scope,
            string runId,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<YouPinLandlordMarketListing>> ReadMarketAsync(
            Settings settings,
            string templateId,
            string itemName,
            string runId,
            CancellationToken cancellationToken);

        Task<YouPinLandlordPricingQuote> ReadOneClickPricingAsync(
            Settings settings,
            YouPinLandlordRemoteListing listing,
            string runId,
            CancellationToken cancellationToken);

        Task<YouPinLandlordRemoteListing?> RevalidateListingAsync(
            Settings settings,
            string listingId,
            YouPinRentalShelfType rentalType,
            string runId,
            string actionId,
            CancellationToken cancellationToken);

        Task<YouPinLandlordWriteResult> ChangeLeasePriceAsync(
            Settings settings,
            YouPinLandlordRepriceCommand command,
            string runId,
            string actionId,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<YouPinLandlordRemoteInventoryItem>> ReadInventoryAsync(
            Settings settings,
            string runId,
            CancellationToken cancellationToken);

        Task<YouPinLandlordInventoryWriteResult> ListInventoryAsync(
            Settings settings,
            YouPinLandlordInventoryListCommand command,
            string runId,
            string actionId,
            CancellationToken cancellationToken);
    }

    internal interface IYouPinLandlordAuditStore
    {
        Task AppendAsync(YouPinLandlordOperationRecord record, CancellationToken cancellationToken);

        Task<IReadOnlyList<YouPinLandlordOperationRecord>> ReadRecentAsync(
            int count,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<YouPinLandlordOperationRecord>> QueryAsync(
            YouPinLandlordAuditQuery query,
            CancellationToken cancellationToken);

        YouPinLandlordAuditHealth GetHealth();
    }

    internal sealed record YouPinLandlordRemoteSnapshot(
        IReadOnlyList<YouPinLandlordRemoteListing> Listings,
        YouPinLandlordPricingPreference PricingPreference);

    internal sealed record YouPinLandlordRemoteListing(
        string ListingId,
        string AssetId,
        string TemplateId,
        string ItemName,
        YouPinRentalShelfType RentalType,
        decimal ShortRent,
        decimal LongRent = 0m,
        decimal Deposit = 0m,
        int LeaseMaxDays = 0,
        bool IsCanLease = true,
        bool IsCanSold = false,
        decimal SellPrice = 0m,
        decimal ReferencePrice = 0m,
        string MarketHashName = "");

    internal sealed record YouPinLandlordMarketListing(
        string ListingId,
        decimal ShortRent,
        bool IsOwn);

    internal sealed record YouPinLandlordRepriceCommand(
        string ListingId,
        decimal ShortRent,
        decimal LongRent,
        decimal Deposit,
        int LeaseMaxDays,
        bool IsCanLease,
        bool IsCanSold,
        decimal SellPrice);

    internal sealed record YouPinLandlordWriteResult(bool Success, string Message);

    internal sealed record YouPinLandlordRemoteInventoryItem(
        string AssetId,
        string TemplateId,
        string ItemName,
        decimal ReferencePrice,
        bool IsEligible,
        YouPinLandlordInventoryEligibilityCode EligibilityCode,
        string EligibilityReason,
        int CompensationType = 0,
        decimal NormalChargePercent = 0m,
        decimal VipChargePercent = 0m,
        int VipSwitchStatus = 0,
        string MarketHashName = "",
        bool IsSaleEligible = false,
        string SaleEligibilityReason = "");

    internal sealed record YouPinLandlordInventoryListCommand(
        string AssetId,
        decimal ShortRent,
        decimal LongRent,
        decimal Deposit,
        int LeaseMaxDays,
        decimal SellPrice,
        int CompensationType,
        decimal NormalChargePercent,
        decimal VipChargePercent,
        int VipSwitchStatus,
        bool IsCanLease = true,
        bool IsCanSold = false);

    internal sealed record YouPinLandlordInventoryWriteResult(
        bool Success,
        string ListingId,
        string Message);

    internal sealed record YouPinLandlordPricingQuote(
        decimal ShortRent,
        decimal LongRent,
        decimal Deposit,
        int LeaseMaxDays,
        decimal SellPrice = 0m);
}
