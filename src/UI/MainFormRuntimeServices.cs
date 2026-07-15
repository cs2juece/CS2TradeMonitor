using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Framework;
using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor
{
    internal sealed class MainFormRuntimeServices
    {
        private MainFormRuntimeServices(
            IRenderScheduler renderScheduler,
            IYouPinInventoryService youPinInventory,
            IYouPinSaleReminderService youPinSaleReminders,
            IYouPinLandlordAutomation youPinLandlordAutomation)
        {
            RenderScheduler = renderScheduler ?? throw new ArgumentNullException(nameof(renderScheduler));
            YouPinInventory = youPinInventory ?? throw new ArgumentNullException(nameof(youPinInventory));
            YouPinSaleReminders = youPinSaleReminders ?? throw new ArgumentNullException(nameof(youPinSaleReminders));
            YouPinLandlordAutomation = youPinLandlordAutomation ?? throw new ArgumentNullException(nameof(youPinLandlordAutomation));
        }

        public IRenderScheduler RenderScheduler { get; }

        public IYouPinInventoryService YouPinInventory { get; }

        public IYouPinSaleReminderService YouPinSaleReminders { get; }

        public IYouPinLandlordAutomation YouPinLandlordAutomation { get; }

        public static MainFormRuntimeServices Resolve()
        {
            return Resolve(AppServices.Provider);
        }

        public static MainFormRuntimeServices Resolve(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return new MainFormRuntimeServices(
                provider.GetRequiredService<IRenderScheduler>(),
                provider.GetRequiredService<IYouPinInventoryService>(),
                provider.GetRequiredService<IYouPinSaleReminderService>(),
                provider.GetRequiredService<IYouPinLandlordAutomation>());
        }
    }
}
