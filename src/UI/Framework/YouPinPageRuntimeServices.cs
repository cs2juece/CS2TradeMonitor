using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Notify;
using CS2TradeMonitor.src.SystemServices;
using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class YouPinPageRuntimeServices
    {
        private YouPinPageRuntimeServices(
            IYouPinInventoryService inventory,
            IYouPinInventoryStorageService inventoryStorage,
            IYouPinAuthService auth,
            ISteamDtItemService steamDtItems,
            ISteamOfferService steamOffers,
            IManualYouPinOfferAutoConfirmation manualOfferAutoConfirmation,
            IYouPinProfitLossService profitLoss,
            IYouPinSaleReminderService saleReminders,
            IYouPinGridTradingService gridTrading,
            IYouPinLandlordAutomation landlordAutomation,
            IPhoneAlertDispatchService phoneAlerts)
        {
            Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            InventoryStorage = inventoryStorage ?? throw new ArgumentNullException(nameof(inventoryStorage));
            Auth = auth ?? throw new ArgumentNullException(nameof(auth));
            SteamDtItems = steamDtItems ?? throw new ArgumentNullException(nameof(steamDtItems));
            SteamOffers = steamOffers ?? throw new ArgumentNullException(nameof(steamOffers));
            ManualOfferAutoConfirmation = manualOfferAutoConfirmation ?? throw new ArgumentNullException(nameof(manualOfferAutoConfirmation));
            ProfitLoss = profitLoss ?? throw new ArgumentNullException(nameof(profitLoss));
            SaleReminders = saleReminders ?? throw new ArgumentNullException(nameof(saleReminders));
            GridTrading = gridTrading ?? throw new ArgumentNullException(nameof(gridTrading));
            LandlordAutomation = landlordAutomation ?? throw new ArgumentNullException(nameof(landlordAutomation));
            PhoneAlerts = phoneAlerts ?? throw new ArgumentNullException(nameof(phoneAlerts));
        }

        public IYouPinInventoryService Inventory { get; }

        public IYouPinInventoryStorageService InventoryStorage { get; }

        public IYouPinAuthService Auth { get; }

        public ISteamDtItemService SteamDtItems { get; }

        public ISteamOfferService SteamOffers { get; }

        public IManualYouPinOfferAutoConfirmation ManualOfferAutoConfirmation { get; }

        public IYouPinProfitLossService ProfitLoss { get; }

        public IYouPinSaleReminderService SaleReminders { get; }

        public IYouPinGridTradingService GridTrading { get; }

        public IYouPinLandlordAutomation LandlordAutomation { get; }

        public IPhoneAlertDispatchService PhoneAlerts { get; }

        public static YouPinPageRuntimeServices Resolve()
        {
            return Resolve(AppServices.Provider);
        }

        public static YouPinPageRuntimeServices Resolve(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return new YouPinPageRuntimeServices(
                provider.GetRequiredService<IYouPinInventoryService>(),
                provider.GetRequiredService<IYouPinInventoryStorageService>(),
                provider.GetRequiredService<IYouPinAuthService>(),
                provider.GetRequiredService<ISteamDtItemService>(),
                provider.GetRequiredService<ISteamOfferService>(),
                provider.GetRequiredService<IManualYouPinOfferAutoConfirmation>(),
                provider.GetRequiredService<IYouPinProfitLossService>(),
                provider.GetRequiredService<IYouPinSaleReminderService>(),
                provider.GetRequiredService<IYouPinGridTradingService>(),
                provider.GetRequiredService<IYouPinLandlordAutomation>(),
                provider.GetRequiredService<IPhoneAlertDispatchService>());
        }
    }
}
