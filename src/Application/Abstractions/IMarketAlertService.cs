using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface IMarketAlertService
    {
        event EventHandler<MarketAlertNotificationEventArgs>? AlertRequested;

        void ApplySettings(Settings cfg);

        void Evaluate(Settings cfg);
    }
}
