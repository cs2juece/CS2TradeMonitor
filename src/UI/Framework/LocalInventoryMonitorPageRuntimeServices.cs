using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.SystemServices;
using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class LocalInventoryMonitorPageRuntimeServices
    {
        private LocalInventoryMonitorPageRuntimeServices(ILocalInventoryMonitorService monitor)
        {
            Monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        }

        public ILocalInventoryMonitorService Monitor { get; }

        public static LocalInventoryMonitorPageRuntimeServices Resolve()
        {
            return Resolve(AppServices.Provider);
        }

        internal static LocalInventoryMonitorPageRuntimeServices Resolve(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);
            return new LocalInventoryMonitorPageRuntimeServices(
                provider.GetRequiredService<ILocalInventoryMonitorService>());
        }
    }
}
