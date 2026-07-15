using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.SystemServices;
using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor.Application.Notify
{
    internal sealed class NotifyRuntimeServices
    {
        private NotifyRuntimeServices(
            IDomesticHttpClientFactory domesticHttpFactory,
            IServerChanPushService serverChanPush,
            IWxPusherService wxPusher)
        {
            DomesticHttpFactory = domesticHttpFactory ?? throw new ArgumentNullException(nameof(domesticHttpFactory));
            ServerChanPush = serverChanPush ?? throw new ArgumentNullException(nameof(serverChanPush));
            WxPusher = wxPusher ?? throw new ArgumentNullException(nameof(wxPusher));
        }

        public IDomesticHttpClientFactory DomesticHttpFactory { get; }

        public IServerChanPushService ServerChanPush { get; }

        public IWxPusherService WxPusher { get; }

        public static NotifyRuntimeServices Resolve()
        {
            return Resolve(AppServices.Provider);
        }

        public static NotifyRuntimeServices Resolve(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return new NotifyRuntimeServices(
                provider.GetRequiredService<IDomesticHttpClientFactory>(),
                provider.GetRequiredService<IServerChanPushService>(),
                provider.GetRequiredService<IWxPusherService>());
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
