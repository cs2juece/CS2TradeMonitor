using CS2TradeMonitor.Application.Notify;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface IPhoneAlertDispatchService
    {
        bool IsConfigured(Settings? cfg);

        string GetHelpUrl(PhoneAlertChannelType type);

        string MaskSecret(PhoneAlertChannelConfig channel);

        bool IsChannelConfigured(PhoneAlertChannelConfig? channel);

        Task<PhoneAlertSendResult> SendConfiguredAsync(
            Settings cfg,
            string title,
            string message,
            CancellationToken cancellationToken = default);

        Task<PhoneAlertSendResult> SendChannelAsync(
            PhoneAlertChannelConfig channel,
            string title,
            string message,
            CancellationToken cancellationToken = default);

        Task<List<PhoneAlertChannelTestResult>> TestAllEnabledAsync(
            Settings cfg,
            string title,
            string message,
            CancellationToken cancellationToken = default);
    }
}
