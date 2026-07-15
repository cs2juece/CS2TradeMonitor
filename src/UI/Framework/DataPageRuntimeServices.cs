using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.SystemServices;
using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class DataPageRuntimeServices
    {
        private DataPageRuntimeServices(ISteamDtService steamDtService, ICsqaqService csqaqService)
        {
            SteamDtService = steamDtService ?? throw new ArgumentNullException(nameof(steamDtService));
            CsqaqService = csqaqService ?? throw new ArgumentNullException(nameof(csqaqService));
        }

        public ISteamDtService SteamDtService { get; }

        public ICsqaqService CsqaqService { get; }

        public static DataPageRuntimeServices Resolve()
        {
            return Resolve(AppServices.Provider);
        }

        public static DataPageRuntimeServices Resolve(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return new DataPageRuntimeServices(
                provider.GetRequiredService<ISteamDtService>(),
                provider.GetRequiredService<ICsqaqService>());
        }
    }
}
