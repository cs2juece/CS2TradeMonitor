using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Notify;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.SystemServices.InfoService;
using CS2TradeMonitor.src.UI.Framework;
using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor
{
    internal sealed class UIControllerRuntimeServices
    {
        private UIControllerRuntimeServices(
            ICs2UpdateReminderService cs2UpdateReminder,
            IMarketAlertService marketAlerts,
            IPhoneAlertDispatchService phoneAlerts,
            IRenderScheduler renderScheduler,
            IInfoService infoService)
        {
            Cs2UpdateReminder = cs2UpdateReminder ?? throw new ArgumentNullException(nameof(cs2UpdateReminder));
            MarketAlerts = marketAlerts ?? throw new ArgumentNullException(nameof(marketAlerts));
            PhoneAlerts = phoneAlerts ?? throw new ArgumentNullException(nameof(phoneAlerts));
            RenderScheduler = renderScheduler ?? throw new ArgumentNullException(nameof(renderScheduler));
            InfoService = infoService ?? throw new ArgumentNullException(nameof(infoService));
        }

        public ICs2UpdateReminderService Cs2UpdateReminder { get; }

        public IMarketAlertService MarketAlerts { get; }

        public IPhoneAlertDispatchService PhoneAlerts { get; }

        public IRenderScheduler RenderScheduler { get; }

        public IInfoService InfoService { get; }

        public static UIControllerRuntimeServices Resolve()
        {
            return Resolve(AppServices.Provider);
        }

        public static UIControllerRuntimeServices Resolve(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return new UIControllerRuntimeServices(
                provider.GetRequiredService<ICs2UpdateReminderService>(),
                provider.GetRequiredService<IMarketAlertService>(),
                provider.GetRequiredService<IPhoneAlertDispatchService>(),
                provider.GetRequiredService<IRenderScheduler>(),
                provider.GetRequiredService<IInfoService>());
        }
    }
}
