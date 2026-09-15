using CS2TradeMonitor.Application.Notify;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface IServerChanPushService
    {
        Task<PhoneAlertSendResult> SendConfiguredAsync(
            Settings cfg,
            string title,
            string message,
            CancellationToken cancellationToken = default);

        Task<PhoneAlertSendResult> SendAsync(
            string? sendKey,
            string title,
            string message,
            CancellationToken cancellationToken = default);
    }
}
