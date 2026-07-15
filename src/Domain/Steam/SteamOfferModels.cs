using System;
using System.Collections.Generic;

namespace CS2TradeMonitor.Domain.Steam
{
    public sealed class SteamOfferItem
    {
        public string TradeOfferId { get; set; } = "";
        public string ConfirmationId { get; set; } = "";
        public string ConfirmationKey { get; set; } = "";
        public string Title { get; set; } = "";
        public string ItemSummary { get; set; } = "";
        public SteamOfferType Type { get; set; } = SteamOfferType.Unknown;
        public string Source { get; set; } = "";
        public SteamOfferStatus Status { get; set; } = SteamOfferStatus.Pending;
        public SteamOfferRisk RiskLevel { get; set; } = SteamOfferRisk.Unverified;
        public bool VerifiedByYouPin { get; set; }
        public string YouPinOrderNo { get; set; } = "";
        public string YouPinItemName { get; set; } = "";
        public double YouPinPrice { get; set; }
        public bool CanAcceptSafely { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string SafeReason { get; set; } = "";
        public string FailureReason { get; set; } = "";
        public string PlatformOrderNo { get; set; } = "";
        public double? Amount { get; set; }
        public string ConfirmationType { get; set; } = "";
        public SteamMobileConfirmationType? MobileConfirmationType { get; set; }
        public List<TradeAsset> ItemsToGive { get; set; } = new();
        public List<TradeAsset> ItemsToReceive { get; set; } = new();
        public string PartnerSteamId { get; set; } = "";
        public string PartnerName { get; set; } = "";
        public DateTime ExpirationTime { get; set; } = DateTime.MinValue;
    }

    public enum SteamOfferType
    {
        Unknown = 0,
        IncomingGift = 1,
        Outgoing = 2,
        TwoWay = 3
    }

    public enum SteamMobileConfirmationType
    {
        Unknown = 0,
        Trade = 2
    }

    public enum SteamOfferStatus
    {
        Pending = 0,
        Accepted = 1,
        Denied = 2
    }

    public enum SteamOfferRisk
    {
        SafeIncoming = 0,
        YouPinVerified = 1,
        Unverified = 2
    }

    public sealed class TradeAsset
    {
        public long AppId { get; set; }
        public long ContextId { get; set; }
        public string AssetId { get; set; } = "";
        public string ClassId { get; set; } = "";
        public string InstanceId { get; set; } = "";
        public int Amount { get; set; }
        public string MarketHashName { get; set; } = "";
        public string IconUrl { get; set; } = "";
    }

    public sealed class TradeOffersResult
    {
        public List<TradeOfferDetail> SentOffers { get; set; } = new();
        public List<TradeOfferDetail> ReceivedOffers { get; set; } = new();
        public Dictionary<string, TradeItemDescription> Descriptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed record SteamTradeOfferAcceptResult(
        bool Ok,
        bool NeedsMobileConfirmation,
        bool AlreadyHandled,
        string Message)
    {
        public static SteamTradeOfferAcceptResult Success(string message = "")
            => new(true, false, false, message);

        public static SteamTradeOfferAcceptResult NeedsConfirmation(string message = "")
            => new(false, true, false, message);

        public static SteamTradeOfferAcceptResult Handled(string message = "")
            => new(true, false, true, message);

        public static SteamTradeOfferAcceptResult Failed(string message)
            => new(false, false, false, message);
    }

    public sealed class TradeOfferDetail
    {
        public string TradeOfferId { get; set; } = "";
        public string PartnerSteamId { get; set; } = "";
        public string PartnerName { get; set; } = "";
        public string Message { get; set; } = "";
        public int TradeOfferState { get; set; }
        public List<TradeAsset> ItemsToGive { get; set; } = new();
        public List<TradeAsset> ItemsToReceive { get; set; } = new();
        public bool IsOurOffer { get; set; }
        public DateTime TimeCreated { get; set; } = DateTime.MinValue;
        public DateTime TimeUpdated { get; set; } = DateTime.MinValue;
        public DateTime ExpirationTime { get; set; } = DateTime.MinValue;
    }

    public sealed class TradeItemDescription
    {
        public string MarketHashName { get; set; } = "";
        public string IconUrl { get; set; } = "";
        public string Type { get; set; } = "";
    }

    public sealed record SteamOfferSnapshot(
        int OfferCount,
        int SafeOfferCount,
        DateTime RetrievedAt,
        string Status)
    {
        public static SteamOfferSnapshot Empty { get; } = new(0, 0, DateTime.MinValue, "未获取");
    }
}
