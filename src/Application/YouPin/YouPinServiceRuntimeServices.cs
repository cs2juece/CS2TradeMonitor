using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.SystemServices;
using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor.Application.YouPin
{
    internal sealed class YouPinServiceRuntimeServices
    {
        private YouPinServiceRuntimeServices(
            IYouPinAuthService auth,
            IDomesticHttpClientFactory domesticHttpFactory)
        {
            Auth = auth ?? throw new ArgumentNullException(nameof(auth));
            DomesticHttpFactory = domesticHttpFactory ?? throw new ArgumentNullException(nameof(domesticHttpFactory));
        }

        public IYouPinAuthService Auth { get; }

        public IDomesticHttpClientFactory DomesticHttpFactory { get; }

        public static YouPinServiceRuntimeServices Resolve()
        {
            return Resolve(AppServices.Provider);
        }

        public static YouPinServiceRuntimeServices Resolve(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return new YouPinServiceRuntimeServices(
                provider.GetRequiredService<IYouPinAuthService>(),
                provider.GetRequiredService<IDomesticHttpClientFactory>());
        }

        public static IDomesticHttpClientFactory ResolveDomesticHttpFactory()
        {
            return ResolveDomesticHttpFactory(AppServices.Provider);
        }

        public static IDomesticHttpClientFactory ResolveDomesticHttpFactory(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return provider.GetRequiredService<IDomesticHttpClientFactory>();
        }
    }
}
