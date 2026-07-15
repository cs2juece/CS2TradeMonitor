using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.SystemServices;
using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor.Application.Steam
{
    internal sealed class SteamServiceRuntimeServices
    {
        private SteamServiceRuntimeServices(
            ISteamTokenVault tokenVault,
            ISteamManualProxyStore manualProxyStore,
            ISteamRoutedHttpClientFactory routedHttpFactory,
            ISteamConnectionResolver connection,
            ISteamAuthStore authStore,
            ISteamConfirmationClient confirmationClient,
            ISteamTradeOfferClient tradeOfferClient,
            ISteamLoginService loginService,
            IYouPinSaleReminderService youPinSaleReminders)
        {
            TokenVault = tokenVault ?? throw new ArgumentNullException(nameof(tokenVault));
            ManualProxyStore = manualProxyStore ?? throw new ArgumentNullException(nameof(manualProxyStore));
            RoutedHttpFactory = routedHttpFactory ?? throw new ArgumentNullException(nameof(routedHttpFactory));
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            AuthStore = authStore ?? throw new ArgumentNullException(nameof(authStore));
            ConfirmationClient = confirmationClient ?? throw new ArgumentNullException(nameof(confirmationClient));
            TradeOfferClient = tradeOfferClient ?? throw new ArgumentNullException(nameof(tradeOfferClient));
            LoginService = loginService ?? throw new ArgumentNullException(nameof(loginService));
            YouPinSaleReminders = youPinSaleReminders ?? throw new ArgumentNullException(nameof(youPinSaleReminders));
        }

        public ISteamTokenVault TokenVault { get; }

        public ISteamManualProxyStore ManualProxyStore { get; }

        public ISteamRoutedHttpClientFactory RoutedHttpFactory { get; }

        public ISteamConnectionResolver Connection { get; }

        public ISteamAuthStore AuthStore { get; }

        public ISteamConfirmationClient ConfirmationClient { get; }

        public ISteamTradeOfferClient TradeOfferClient { get; }

        public ISteamLoginService LoginService { get; }

        public IYouPinSaleReminderService YouPinSaleReminders { get; }

        public static SteamServiceRuntimeServices Resolve()
        {
            return Resolve(AppServices.Provider);
        }

        public static SteamServiceRuntimeServices Resolve(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return new SteamServiceRuntimeServices(
                provider.GetRequiredService<ISteamTokenVault>(),
                provider.GetRequiredService<ISteamManualProxyStore>(),
                provider.GetRequiredService<ISteamRoutedHttpClientFactory>(),
                provider.GetRequiredService<ISteamConnectionResolver>(),
                provider.GetRequiredService<ISteamAuthStore>(),
                provider.GetRequiredService<ISteamConfirmationClient>(),
                provider.GetRequiredService<ISteamTradeOfferClient>(),
                provider.GetRequiredService<ISteamLoginService>(),
                provider.GetRequiredService<IYouPinSaleReminderService>());
        }

        public static ISteamTokenVault ResolveTokenVault()
        {
            return ResolveTokenVault(AppServices.Provider);
        }

        public static ISteamTokenVault ResolveTokenVault(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return provider.GetRequiredService<ISteamTokenVault>();
        }

        public static ISteamManualProxyStore ResolveManualProxyStore()
        {
            return ResolveManualProxyStore(AppServices.Provider);
        }

        public static ISteamManualProxyStore ResolveManualProxyStore(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return provider.GetRequiredService<ISteamManualProxyStore>();
        }

        public static ISteamRoutedHttpClientFactory ResolveRoutedHttpFactory()
        {
            return ResolveRoutedHttpFactory(AppServices.Provider);
        }

        public static ISteamRoutedHttpClientFactory ResolveRoutedHttpFactory(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return provider.GetRequiredService<ISteamRoutedHttpClientFactory>();
        }

        public static ISteamConnectionResolver ResolveConnection()
        {
            return ResolveConnection(AppServices.Provider);
        }

        public static ISteamConnectionResolver ResolveConnection(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return provider.GetRequiredService<ISteamConnectionResolver>();
        }
    }

    internal sealed class SteamLoginRuntimeServices
    {
        private SteamLoginRuntimeServices(
            ISteamAuthStore authStore,
            ISteamTokenVault tokenVault,
            ISteamRoutedHttpClientFactory routedHttpFactory)
        {
            AuthStore = authStore ?? throw new ArgumentNullException(nameof(authStore));
            TokenVault = tokenVault ?? throw new ArgumentNullException(nameof(tokenVault));
            RoutedHttpFactory = routedHttpFactory ?? throw new ArgumentNullException(nameof(routedHttpFactory));
        }

        public ISteamAuthStore AuthStore { get; }

        public ISteamTokenVault TokenVault { get; }

        public ISteamRoutedHttpClientFactory RoutedHttpFactory { get; }

        public static SteamLoginRuntimeServices Resolve()
        {
            return Resolve(AppServices.Provider);
        }

        public static SteamLoginRuntimeServices Resolve(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return new SteamLoginRuntimeServices(
                provider.GetRequiredService<ISteamAuthStore>(),
                provider.GetRequiredService<ISteamTokenVault>(),
                provider.GetRequiredService<ISteamRoutedHttpClientFactory>());
        }
    }
}
