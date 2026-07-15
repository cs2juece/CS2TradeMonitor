using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.SystemServices;
using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor.src.UI.SettingsPage
{
    internal sealed class YouPinAuthRuntimeServices
    {
        private YouPinAuthRuntimeServices(
            IYouPinAuthService auth,
            ISteamOfferService steamOffers,
            IYouPinSaleReminderService youPinSaleReminders)
        {
            Auth = auth ?? throw new ArgumentNullException(nameof(auth));
            SteamOffers = steamOffers ?? throw new ArgumentNullException(nameof(steamOffers));
            YouPinSaleReminders = youPinSaleReminders ?? throw new ArgumentNullException(nameof(youPinSaleReminders));
        }

        public IYouPinAuthService Auth { get; }

        public ISteamOfferService SteamOffers { get; }

        public IYouPinSaleReminderService YouPinSaleReminders { get; }

        public static YouPinAuthRuntimeServices Resolve()
        {
            return Resolve(AppServices.Provider);
        }

        public static YouPinAuthRuntimeServices Resolve(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return new YouPinAuthRuntimeServices(
                provider.GetRequiredService<IYouPinAuthService>(),
                provider.GetRequiredService<ISteamOfferService>(),
                provider.GetRequiredService<IYouPinSaleReminderService>());
        }
    }
}
