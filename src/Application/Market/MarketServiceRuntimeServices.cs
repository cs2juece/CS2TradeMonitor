using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.SystemServices;
using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor.Application.Market
{
    internal sealed class MarketServiceRuntimeServices
    {
        private MarketServiceRuntimeServices(
            IDomesticHttpClientFactory domesticHttpFactory,
            CsqaqRequestRateLimiter csqaqRateLimiter)
        {
            DomesticHttpFactory = domesticHttpFactory ?? throw new ArgumentNullException(nameof(domesticHttpFactory));
            CsqaqRateLimiter = csqaqRateLimiter ?? throw new ArgumentNullException(nameof(csqaqRateLimiter));
        }

        public IDomesticHttpClientFactory DomesticHttpFactory { get; }
        public CsqaqRequestRateLimiter CsqaqRateLimiter { get; }

        public static MarketServiceRuntimeServices Resolve()
        {
            return Resolve(AppServices.Provider);
        }

        public static MarketServiceRuntimeServices Resolve(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return new MarketServiceRuntimeServices(
                provider.GetRequiredService<IDomesticHttpClientFactory>(),
                provider.GetRequiredService<CsqaqRequestRateLimiter>());
        }
    }
}
