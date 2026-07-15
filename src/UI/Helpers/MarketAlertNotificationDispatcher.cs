using CS2TradeMonitor.Application.Abstractions;
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
            ToolTipIcon icon)
        {
            if (cfg.DoNotDisturbEnabled)
            {
                return false;
            }

            TrySendPhoneAlert(cfg, phoneAlerts, title, message);

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

        private static void TrySendPhoneAlert(Settings cfg, IPhoneAlertDispatchService phoneAlerts, string title, string message)
        {
            if (!phoneAlerts.IsConfigured(cfg))
                return;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await phoneAlerts.SendConfiguredAsync(cfg, title, message).ConfigureAwait(false);
                }
                catch
                {
                    // The service already wraps expected failures; never let phone alerts affect local notifications.
                }
            });
        }
    }
}
