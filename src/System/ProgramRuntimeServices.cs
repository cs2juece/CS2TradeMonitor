using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.src.Core.Modules;
using CS2TradeMonitor.src.Core.State;
using CS2TradeMonitor.src.SystemServices;
using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor
{
    internal sealed class ProgramRuntimeServices
    {
        private ProgramRuntimeServices(
            IMonitorModuleHost moduleHost,
            ISteamOfferService steamOffers,
            ISteamSessionKeepAliveService steamSessionKeepAlive,
            IAppConfigState appConfigState,
            NetworkRouteRecoveryCoordinator networkRouteRecovery,
            SteamConnectivitySupervisor steamConnectivity)
        {
            ModuleHost = moduleHost ?? throw new ArgumentNullException(nameof(moduleHost));
            SteamOffers = steamOffers ?? throw new ArgumentNullException(nameof(steamOffers));
            SteamSessionKeepAlive = steamSessionKeepAlive ?? throw new ArgumentNullException(nameof(steamSessionKeepAlive));
            AppConfigState = appConfigState ?? throw new ArgumentNullException(nameof(appConfigState));
            NetworkRouteRecovery = networkRouteRecovery ?? throw new ArgumentNullException(nameof(networkRouteRecovery));
            SteamConnectivity = steamConnectivity ?? throw new ArgumentNullException(nameof(steamConnectivity));
        }

        public IMonitorModuleHost ModuleHost { get; }

        public ISteamOfferService SteamOffers { get; }

        public ISteamSessionKeepAliveService SteamSessionKeepAlive { get; }

        public IAppConfigState AppConfigState { get; }

        public NetworkRouteRecoveryCoordinator NetworkRouteRecovery { get; }

        public SteamConnectivitySupervisor SteamConnectivity { get; }

        public static ProgramRuntimeServices Resolve()
        {
            return Resolve(AppServices.Provider);
        }

        public static ProgramRuntimeServices Resolve(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return new ProgramRuntimeServices(
                provider.GetRequiredService<IMonitorModuleHost>(),
                provider.GetRequiredService<ISteamOfferService>(),
                provider.GetRequiredService<ISteamSessionKeepAliveService>(),
                provider.GetRequiredService<IAppConfigState>(),
                provider.GetRequiredService<NetworkRouteRecoveryCoordinator>(),
                provider.GetRequiredService<SteamConnectivitySupervisor>());
        }
    }
}
