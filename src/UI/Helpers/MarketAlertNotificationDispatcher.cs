using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Notify;
using System;
using System.Windows.Forms;

namespace CS2TradeMonitor
{
    internal static class MarketAlertNotificationDispatcher
    {
        public static bool Show(
            Settings cfg,
            MainForm mainForm,
            IPhoneAlertDispatchService phoneAlerts,
            string title,
            string message,
            ToolTipIcon icon,
            Action<PhoneAlertSendResult>? phoneDeliveryCompleted = null)
        {
            if (cfg.DoNotDisturbEnabled)
            {
                phoneDeliveryCompleted?.Invoke(PhoneAlertSendResult.Skip("勿扰模式已启用"));
                return false;
            }

            TrySendPhoneAlert(cfg, phoneAlerts, title, message, phoneDeliveryCompleted);

            bool shown = GlobalPromptService.Notify(
                title,
                message,
                GlobalPromptService.MapToolTipIcon(icon),
                source: "大盘预警",
                dedupKey: "MarketAlert:" + title + "|" + message,
                owner: mainForm,
                respectDoNotDisturb: false);

            if (shown)
                return true;

            return mainForm.TryShowNotification(title, message, icon);
        }

        private static void TrySendPhoneAlert(
            Settings cfg,
            IPhoneAlertDispatchService phoneAlerts,
            string title,
            string message,
            Action<PhoneAlertSendResult>? phoneDeliveryCompleted)
        {
            if (!phoneAlerts.IsConfigured(cfg))
            {
                phoneDeliveryCompleted?.Invoke(PhoneAlertSendResult.Skip("没有已启用且配置完整的手机提醒通道"));
                return;
            }

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var result = await phoneAlerts.SendConfiguredAsync(cfg, title, message).ConfigureAwait(false);
                    phoneDeliveryCompleted?.Invoke(result);
                }
                catch
                {
                    // The service already wraps expected failures; never let phone alerts affect local notifications.
                    phoneDeliveryCompleted?.Invoke(PhoneAlertSendResult.Fail("手机提醒发送失败"));
                }
            });
        }
    }
}
