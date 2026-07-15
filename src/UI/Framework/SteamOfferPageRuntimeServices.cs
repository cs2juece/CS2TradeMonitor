using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Steam.Auth;
using CS2TradeMonitor.src.SystemServices;
using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor.src.UI.Framework.SteamOffers
{
    internal sealed class SteamOfferPageRuntimeServices
    {
        private SteamOfferPageRuntimeServices(
            ISteamOfferService steamOffers,
            ISteamAuthStore steamAuthStore,
            ISteamTokenVault steamTokenVault,
            ISteamLoginService steamLogin,
            ISteamConnectionResolver steamConnection)
        {
            SteamOffers = steamOffers ?? throw new ArgumentNullException(nameof(steamOffers));
            SteamAuthStore = steamAuthStore ?? throw new ArgumentNullException(nameof(steamAuthStore));
            SteamTokenVault = steamTokenVault ?? throw new ArgumentNullException(nameof(steamTokenVault));
            SteamLogin = steamLogin ?? throw new ArgumentNullException(nameof(steamLogin));
            SteamConnection = steamConnection ?? throw new ArgumentNullException(nameof(steamConnection));
        }

        public ISteamOfferService SteamOffers { get; }

        public ISteamAuthStore SteamAuthStore { get; }

        public ISteamTokenVault SteamTokenVault { get; }

        public ISteamLoginService SteamLogin { get; }

        public ISteamConnectionResolver SteamConnection { get; }

        public static SteamOfferPageRuntimeServices Resolve()
        {
            return Resolve(AppServices.Provider);
        }

        public static SteamOfferPageRuntimeServices Resolve(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return new SteamOfferPageRuntimeServices(
                provider.GetRequiredService<ISteamOfferService>(),
                provider.GetRequiredService<ISteamAuthStore>(),
                provider.GetRequiredService<ISteamTokenVault>(),
                provider.GetRequiredService<ISteamLoginService>(),
                provider.GetRequiredService<ISteamConnectionResolver>());
        }
    }
}
