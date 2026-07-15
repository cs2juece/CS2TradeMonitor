using CS2TradeMonitor.Application.Steam.Auth;
using CS2TradeMonitor.Domain.Steam;
using System;
using System.Collections.Generic;

namespace CS2TradeMonitor.Application.Steam
{
    public sealed class SteamOfferState
    {
        public SteamAuthStoreStatus AuthStatus { get; set; } = new();
        public List<SteamOfferItem> Offers { get; set; } = new();
        public DateTime LastRefresh { get; set; }
        public string LastStatus { get; set; } = "";
        public string LastError { get; set; } = "";
        public string HighlightTradeOfferId { get; set; } = "";
        public SteamAutoConfirmState AutoConfirm { get; set; } = new();
        public SteamAutoTradeState AutoTrade { get; set; } = new();
    }

    public sealed class SteamAutoConfirmState
    {
        public bool IsRunning { get; set; }
        public DateTime LastCheckTime { get; set; }
        public int TotalAccepted { get; set; }
        public string LastStatus { get; set; } = "";
        public int IntervalSeconds { get; set; }
        public bool AutoAcceptSafe { get; set; }
        public bool AllowYouPinVerifiedAccept { get; set; }
    }

    public sealed class SteamAutoTradeSettings
    {
        public bool Enabled { get; set; }
        public bool AcceptPureIncomingEnabled { get; set; }
        public bool AcceptYouPinPurchaseEnabled { get; set; }
        public bool SendYouPinSaleEnabled { get; set; }
        public bool SendYouPinRentalEnabled { get; set; }
        public int IntervalSeconds { get; set; } = 300;

        public static SteamAutoTradeSettings ReadOnly(int intervalSeconds)
            => new() { Enabled = false, IntervalSeconds = intervalSeconds };
    }

    public sealed class SteamAutoTradeState
    {
        public bool IsRunning { get; set; }
        public bool ProcessingEnabled { get; set; }
        public DateTime LastCheckTime { get; set; }
        public DateTime LastProcessTime { get; set; }
        public DateTime NextCheckTime { get; set; }
        public int TodaySuccess { get; set; }
        public int TodayFailure { get; set; }
        public string StatusText { get; set; } = "已关闭";
        public string LastStatus { get; set; } = "";
        public string LastFailureReason { get; set; } = "";
        public int IntervalSeconds { get; set; } = 300;
        public List<SteamAutoTradeRecord> RecentRecords { get; set; } = new();
    }

    public enum SteamAutoTradeDirection
    {
        Unknown = 0,
        Incoming = 1,
        Outgoing = 2,
        TwoWay = 3
    }

    public enum SteamAutoTradeCategory
    {
        Unknown = 0,
        PureIncoming = 1,
        YouPinPurchase = 2,
        YouPinSale = 3,
        YouPinRental = 4
    }

    public enum SteamAutoTradeAction
    {
        Skip = 0,
        AcceptOffer = 1,
        SendOffer = 2,
        ConfirmMobile = 3,
        ConfirmYouPinOffer = 4
    }

    public enum SteamAutoTradeRecordType
    {
        AutoAccept = 0,
        AutoSend = 1,
        AutoMobileConfirm = 2,
        ManualAccept = 3,
        ManualSend = 4,
        ManualMobileConfirm = 5,
        Skip = 6,
        Failed = 7,
        Pending = 8,
        Unresolved = 9,
        TerminalFailure = 10,
        AutoYouPinConfirm = 11
    }

    public enum SteamAutoTradePendingStage
    {
        Unknown = 0,
        TradeOfferSync = 1,
        MobileConfirmation = 2
    }

    public sealed class SteamAutoTradePlanItem
    {
        public string TradeOfferId { get; set; } = "";
        public SteamAutoTradeDirection Direction { get; set; } = SteamAutoTradeDirection.Unknown;
        public SteamAutoTradeCategory Category { get; set; } = SteamAutoTradeCategory.Unknown;
        public List<string> ItemNames { get; set; } = new();
        public string MatchedOrderNo { get; set; } = "";
        public SteamAutoTradeAction Action { get; set; } = SteamAutoTradeAction.Skip;
        public bool Allowed { get; set; }
        public string SkipReason { get; set; } = "";
    }

    public sealed class SteamAutoTradeRecord
    {
        public DateTime Time { get; set; } = DateTime.Now;
        public DateTime CreatedTime { get; set; }
        public SteamAutoTradeRecordType Type { get; set; }
        public SteamAutoTradeDirection Direction { get; set; }
        public List<string> ItemNames { get; set; } = new();
        public string Source { get; set; } = "";
        public string Result { get; set; } = "";
        public string Reason { get; set; } = "";
        public string TradeOfferId { get; set; } = "";
        public string OrderNo { get; set; } = "";
        public SteamAutoTradePendingStage PendingStage { get; set; } = SteamAutoTradePendingStage.Unknown;
    }

    public sealed class SteamOfferActionResult
    {
        public bool Ok { get; set; }
        public string Message { get; set; } = "";
        public string Code { get; set; } = "";

        public static SteamOfferActionResult Success(string message) => new() { Ok = true, Message = message, Code = "ok" };
        public static SteamOfferActionResult Failed(string message, string code = "") => new() { Ok = false, Message = message, Code = code };
    }

    public sealed class SteamAutoLoginRequest
    {
        public string SharedSecret { get; set; } = "";
        public string IdentitySecret { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public sealed class SteamOfferImportFileResult
    {
        public bool Ok { get; set; }
        public bool RequiresSelection { get; set; }
        public string Message { get; set; } = "";
        public string Text { get; set; } = "";
        public string SourcePath { get; set; } = "";
        public List<SteamOfferImportCandidate> Candidates { get; set; } = new();

        public static SteamOfferImportFileResult Success(string text, string sourcePath, string message) => new()
        {
            Ok = true,
            Text = text,
            SourcePath = sourcePath,
            Message = message
        };

        public static SteamOfferImportFileResult Failed(string message, string sourcePath = "", string text = "") => new()
        {
            Ok = false,
            Message = message,
            SourcePath = sourcePath,
            Text = text
        };

        public static SteamOfferImportFileResult NeedsSelection(
            string text,
            string sourcePath,
            string message,
            List<SteamOfferImportCandidate> candidates) => new()
            {
                Ok = true,
                RequiresSelection = true,
                Message = message,
                Text = text,
                SourcePath = sourcePath,
                Candidates = candidates
            };
    }

    public sealed class SteamOfferImportCandidate
    {
        public string DisplayName { get; set; } = "";
        public string Path { get; set; } = "";
    }

    public enum SteamTradeOfferStatusKind
    {
        Active = 0,
        NeedsMobileConfirmation = 1,
        Accepted = 2,
        Failed = 3,
        NotFound = 4,
        QueryFailed = 5
    }

    public sealed class SteamTradeOfferStatusResult
    {
        public SteamTradeOfferStatusKind Kind { get; init; }
        public int RawState { get; init; }
        public string Message { get; init; } = "";

        public static SteamTradeOfferStatusResult Active(string message, int rawState = 2) => new() { Kind = SteamTradeOfferStatusKind.Active, RawState = rawState, Message = message };
        public static SteamTradeOfferStatusResult NeedsMobileConfirmation(string message, int rawState = 9) => new() { Kind = SteamTradeOfferStatusKind.NeedsMobileConfirmation, RawState = rawState, Message = message };
        public static SteamTradeOfferStatusResult Accepted(string message, int rawState) => new() { Kind = SteamTradeOfferStatusKind.Accepted, RawState = rawState, Message = message };
        public static SteamTradeOfferStatusResult Failed(string message, int rawState) => new() { Kind = SteamTradeOfferStatusKind.Failed, RawState = rawState, Message = message };
        public static SteamTradeOfferStatusResult NotFound(string message) => new() { Kind = SteamTradeOfferStatusKind.NotFound, Message = message };
        public static SteamTradeOfferStatusResult QueryFailed(string message) => new() { Kind = SteamTradeOfferStatusKind.QueryFailed, Message = message };
    }
}
