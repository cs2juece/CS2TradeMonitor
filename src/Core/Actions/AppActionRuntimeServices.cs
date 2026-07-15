using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Framework;
using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor.src.Core.Actions
{
    internal sealed class AppActionRuntimeServices
    {
        private AppActionRuntimeServices(
            IYouPinInventoryService youPinInventory,
            IYouPinSaleReminderService youPinSaleReminders,
            IMarketAlertService marketAlerts,
            IRenderScheduler renderScheduler)
        {
            YouPinInventory = youPinInventory ?? throw new ArgumentNullException(nameof(youPinInventory));
            YouPinSaleReminders = youPinSaleReminders ?? throw new ArgumentNullException(nameof(youPinSaleReminders));
            MarketAlerts = marketAlerts ?? throw new ArgumentNullException(nameof(marketAlerts));
            RenderScheduler = renderScheduler ?? throw new ArgumentNullException(nameof(renderScheduler));
        }

        public IYouPinInventoryService YouPinInventory { get; }

        public IYouPinSaleReminderService YouPinSaleReminders { get; }

        public IMarketAlertService MarketAlerts { get; }

        public IRenderScheduler RenderScheduler { get; }

        public static AppActionRuntimeServices Resolve()
        {
            return Resolve(AppServices.Provider);
        }

        public static AppActionRuntimeServices Resolve(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return new AppActionRuntimeServices(
                provider.GetRequiredService<IYouPinInventoryService>(),
                provider.GetRequiredService<IYouPinSaleReminderService>(),
                provider.GetRequiredService<IMarketAlertService>(),
                provider.GetRequiredService<IRenderScheduler>());
        }
    }
}
