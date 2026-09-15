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

    public sealed class SteamAutoLoginRequest
    {
        public string SharedSecret { get; set; } = "";
        public string IdentitySecret { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string Password { get; set; } = "";
    }

}
