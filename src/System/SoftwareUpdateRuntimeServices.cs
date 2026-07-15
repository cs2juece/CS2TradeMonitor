using CS2TradeMonitor.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor.src.SystemServices
{
    internal sealed class SoftwareUpdateRuntimeServices
    {
        private SoftwareUpdateRuntimeServices(IDomesticHttpClientFactory domesticHttpFactory)
        {
            DomesticHttpFactory = domesticHttpFactory ?? throw new ArgumentNullException(nameof(domesticHttpFactory));
        }

        public IDomesticHttpClientFactory DomesticHttpFactory { get; }

        public static SoftwareUpdateRuntimeServices Resolve()
        {
            return Resolve(AppServices.Provider);
        }

        public static SoftwareUpdateRuntimeServices Resolve(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return new SoftwareUpdateRuntimeServices(provider.GetRequiredService<IDomesticHttpClientFactory>());
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
