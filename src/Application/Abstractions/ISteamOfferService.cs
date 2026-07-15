using CS2TradeMonitor.Application.Steam;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface ISteamOfferService
    {
        event Action? DataUpdated;

        long SteamTimeOffsetSeconds { get; }

        long GetCorrectedSteamTimeSeconds();

        Task SyncSteamTimeOffsetAsync();

        SteamOfferState GetState();

        void StartAutoConfirm(int intervalSeconds, bool autoAcceptSafe, bool allowYouPinVerifiedAccept = true);

        void StartAutoTrade(SteamAutoTradeSettings settings);

        void StopAutoConfirm();

        void RecordAutoTradeAction(SteamAutoTradeRecord record);

        void HighlightTradeOffer(string tradeOfferId);

        SteamOfferActionResult AddManualTradeOffer(string tradeOfferId);

        SteamOfferActionResult ImportMaFileText(string jsonText, string sourcePath = "");

        SteamOfferImportFileResult LoadMaFileImportFile(string sourcePath);

        SteamOfferActionResult UpdateSession(
            string sessionId,
            string steamLoginSecure,
            string steamLogin = "",
            string apiKey = "",
            string accessToken = "",
            string refreshToken = "",
            string steamId = "");

        Task<SteamOfferActionResult> EnsureSessionAsync();

        Task<SteamOfferActionResult> RestoreLoginStateFromTokenTextAsync(string tokenText);

        Task<SteamOfferActionResult> LoginAndConfigureAsync(SteamAutoLoginRequest request);

        Task<SteamOfferActionResult> RefreshSteamApiKeyAsync();

        SteamOfferActionResult SaveManualTokenSecrets(string sharedSecret, string identitySecret);

        Task<SteamOfferActionResult> ReloginAsync(string reason);

        Task<SteamOfferActionResult> EnsureSteamLoginStateAsync(
            string reason,
            bool allowPasswordFallback = true,
            bool preferPasswordFallback = false);

        void ClearCredentials();

        SteamOfferActionResult ClearTokenSecrets();

        SteamOfferActionResult ClearLoginState();

        Task<SteamOfferActionResult> LoadOffersAsync(bool useMock = false, bool allowAutoRelogin = true);

        Task<SteamOfferActionResult> LoadOffersForAutoTradeAsync();

        Task<SteamOfferActionResult> AcceptSafeOffersAsync(bool allowYouPinVerified = true);

        Task<SteamOfferActionResult> AcceptOfferAsync(string tradeOfferId, bool requireSafe);

        Task<SteamOfferActionResult> AcceptAutoTradeOfferAsync(SteamAutoTradePlanItem plan);

        Task<SteamOfferActionResult> ConfirmMatchedMobileTradeAsync(SteamAutoTradePlanItem plan);

        Task<SteamTradeOfferStatusResult> QueryTradeOfferStatusAsync(string tradeOfferId);

        Task<SteamOfferActionResult> DenyOfferAsync(string tradeOfferId);
    }
}
