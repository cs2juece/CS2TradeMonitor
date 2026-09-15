using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Application.Notify
{
    internal static class PhoneAlertNotificationDelivery
    {
        public static async Task<PhoneAlertSendResult> SendIfRequestedAsync(
            Settings cfg,
            IPhoneAlertDispatchService phoneAlerts,
            AppNotificationEventArgs notification,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(cfg);
            ArgumentNullException.ThrowIfNull(phoneAlerts);
            ArgumentNullException.ThrowIfNull(notification);

            if (!notification.SendToPhone)
                return PhoneAlertSendResult.Skip("当前通知未请求手机提醒");

            if (cfg.DoNotDisturbEnabled)
                return PhoneAlertSendResult.Skip("勿扰模式已启用");

            if (!phoneAlerts.IsConfigured(cfg))
                return PhoneAlertSendResult.Skip("没有已启用且配置完整的手机提醒通道");

            try
            {
                return await phoneAlerts.SendConfiguredAsync(
                    cfg,
                    notification.Title,
                    notification.Message,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                DiagnosticsLogger.Ignored(
                    "PhoneAlert",
                    "DeliverAppNotification",
                    exception,
                    retryable: true,
                    category: "Notify");
                return PhoneAlertSendResult.Fail("手机提醒发送失败");
            }
        }
    }
}
