using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.Core.State;
using CS2TradeMonitor.src.SystemServices;
using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor.src.Core
{
    internal sealed class MarketDataSourceRuntimeServices
    {
        private MarketDataSourceRuntimeServices(
            ISteamDtService steamDt,
            ICsqaqService csqaq,
            ISteamDtItemService steamDtItems,
            IRuntimeAppState runtimeState,
            IAppConfigState appConfigState)
        {
            SteamDt = steamDt ?? throw new ArgumentNullException(nameof(steamDt));
            Csqaq = csqaq ?? throw new ArgumentNullException(nameof(csqaq));
            SteamDtItems = steamDtItems ?? throw new ArgumentNullException(nameof(steamDtItems));
            RuntimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
            AppConfigState = appConfigState ?? throw new ArgumentNullException(nameof(appConfigState));
        }

        public ISteamDtService SteamDt { get; }

        public ICsqaqService Csqaq { get; }

        public ISteamDtItemService SteamDtItems { get; }

        public IRuntimeAppState RuntimeState { get; }

        public IAppConfigState AppConfigState { get; }

        public static MarketDataSourceRuntimeServices Resolve()
        {
            return Resolve(AppServices.Provider);
        }

        public static MarketDataSourceRuntimeServices Resolve(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return new MarketDataSourceRuntimeServices(
                provider.GetRequiredService<ISteamDtService>(),
                provider.GetRequiredService<ICsqaqService>(),
                provider.GetRequiredService<ISteamDtItemService>(),
                provider.GetRequiredService<IRuntimeAppState>(),
                provider.GetRequiredService<IAppConfigState>());
        }
    }
}
