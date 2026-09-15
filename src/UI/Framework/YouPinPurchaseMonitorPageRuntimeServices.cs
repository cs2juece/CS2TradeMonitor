using CS2TradeMonitor.Application.YouPin.PurchaseMonitoring;
using CS2TradeMonitor.src.SystemServices;
using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor.src.UI.Framework;

internal sealed class YouPinPurchaseMonitorPageRuntimeServices
{
    private YouPinPurchaseMonitorPageRuntimeServices(IYouPinPurchaseMonitoringModule module)
        => Module = module ?? throw new ArgumentNullException(nameof(module));

    public IYouPinPurchaseMonitoringModule Module { get; }

    public static YouPinPurchaseMonitorPageRuntimeServices Resolve()
        => Resolve(AppServices.Provider);

    internal static YouPinPurchaseMonitorPageRuntimeServices Resolve(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return new YouPinPurchaseMonitorPageRuntimeServices(
            provider.GetRequiredService<IYouPinPurchaseMonitoringModule>());
    }
}
