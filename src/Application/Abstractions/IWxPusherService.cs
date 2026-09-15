using CS2TradeMonitor.Application.Notify;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface IWxPusherService
    {
        Task<WxPusherSendResult> SendConfiguredAsync(
            Settings cfg,
            string title,
            string message,
            CancellationToken cancellationToken = default);

        Task<WxPusherSendResult> SendAsync(
            string? spt,
            string title,
            string message,
            CancellationToken cancellationToken = default);
    }
}
