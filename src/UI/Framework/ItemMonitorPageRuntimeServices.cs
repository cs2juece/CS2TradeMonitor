using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.SystemServices;
using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class ItemMonitorPageRuntimeServices
    {
        private ItemMonitorPageRuntimeServices(ISteamDtItemService steamDtItems)
        {
            SteamDtItems = steamDtItems ?? throw new ArgumentNullException(nameof(steamDtItems));
        }

        public ISteamDtItemService SteamDtItems { get; }

        public static ItemMonitorPageRuntimeServices Resolve()
        {
            return Resolve(AppServices.Provider);
        }

        public static ItemMonitorPageRuntimeServices Resolve(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return new ItemMonitorPageRuntimeServices(provider.GetRequiredService<ISteamDtItemService>());
        }
    }
}
