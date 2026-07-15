using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.SystemServices.InfoService;
using CS2TradeMonitor.src.Core.State;
using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor.src.Core
{
    internal sealed class MetricRuntimeServices
    {
        private MetricRuntimeServices(IInfoService infoService, IAppConfigState appConfigState)
        {
            InfoService = infoService ?? throw new ArgumentNullException(nameof(infoService));
            AppConfigState = appConfigState ?? throw new ArgumentNullException(nameof(appConfigState));
        }

        public IInfoService InfoService { get; }

        public IAppConfigState AppConfigState { get; }

        public static MetricRuntimeServices Resolve()
        {
            return Resolve(AppServices.Provider);
        }

        public static MetricRuntimeServices Resolve(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return new MetricRuntimeServices(
                provider.GetRequiredService<IInfoService>(),
                provider.GetRequiredService<IAppConfigState>());
        }

        public static IInfoService ResolveInfoService()
        {
            return ResolveInfoService(AppServices.Provider);
        }

        public static IInfoService ResolveInfoService(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return provider.GetRequiredService<IInfoService>();
        }
    }
}
