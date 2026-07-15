using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.Application.Steam.Auth;
using CS2TradeMonitor.Domain.Steam;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface ISteamLoginService
    {
        Task<SteamOfferActionResult> RestoreFromRefreshTokenAsync(SteamAuthCredential credential, CancellationToken cancellationToken = default);

        Task<SteamOfferActionResult> RestoreFromAccessTokenAsync(SteamAuthCredential credential, CancellationToken cancellationToken = default);

        Task<SteamOfferActionResult> RefreshAccessTokenForAppAsync(SteamAuthCredential credential, CancellationToken cancellationToken = default);

        Task<SteamOfferActionResult> ValidateSavedSessionAsync(SteamAuthCredential credential, CancellationToken cancellationToken = default);

        Task<SteamOfferActionResult> RestoreFromTokenTextAsync(SteamAuthCredential credential, string tokenText, CancellationToken cancellationToken = default);

        Task<SteamOfferActionResult> LoginAndConfigureAsync(SteamAutoLoginRequest request, CancellationToken cancellationToken = default);

        Task<string> FetchApiKeyFromSavedSessionAsync(SteamAuthCredential credential, CancellationToken cancellationToken = default);

        Task<string> RefreshPersonaNameAsync(SteamAuthCredential credential, CancellationToken cancellationToken = default);

        Task<string> RefreshPersonaNameAsync(SteamTokenEntry token, CancellationToken cancellationToken = default);
    }
}
