using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.SystemServices;
using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor.src.Core.State
{
    internal sealed class RuntimeStateCoordinatorRuntimeServices
    {
        private RuntimeStateCoordinatorRuntimeServices(
            IYouPinInventoryService youPinInventory,
            IYouPinSaleReminderService youPinSaleReminders,
            ISteamOfferService steamOffers,
            IRuntimeAppState runtimeState)
        {
            YouPinInventory = youPinInventory ?? throw new ArgumentNullException(nameof(youPinInventory));
            YouPinSaleReminders = youPinSaleReminders ?? throw new ArgumentNullException(nameof(youPinSaleReminders));
            SteamOffers = steamOffers ?? throw new ArgumentNullException(nameof(steamOffers));
            RuntimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
        }

        public IYouPinInventoryService YouPinInventory { get; }

        public IYouPinSaleReminderService YouPinSaleReminders { get; }

        public ISteamOfferService SteamOffers { get; }

        public IRuntimeAppState RuntimeState { get; }

        public static RuntimeStateCoordinatorRuntimeServices Resolve()
        {
            return Resolve(AppServices.Provider);
        }

        public static RuntimeStateCoordinatorRuntimeServices Resolve(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return new RuntimeStateCoordinatorRuntimeServices(
                provider.GetRequiredService<IYouPinInventoryService>(),
                provider.GetRequiredService<IYouPinSaleReminderService>(),
                provider.GetRequiredService<ISteamOfferService>(),
                provider.GetRequiredService<IRuntimeAppState>());
        }
    }
}
