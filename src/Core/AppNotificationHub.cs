using CS2TradeMonitor.src.SystemServices;
using System;

namespace CS2TradeMonitor.src.Core
{
    public sealed class AppNotificationHub
    {
        private static readonly Lazy<AppNotificationHub> LazyInstance = new(() => new AppNotificationHub());
        public static AppNotificationHub Instance => LazyInstance.Value;

        private AppNotificationHub()
        {
        }

        public event EventHandler<AppNotificationEventArgs>? NotificationRequested;

        public void Request(
            string title,
            string message,
            AppNotificationSeverity severity = AppNotificationSeverity.Info,
            AppNotificationPlacement placement = AppNotificationPlacement.Desktop,
            bool playSound = false,
            bool showToast = true,
            string? source = null,
            string? dedupKey = null)
        {
            var args = new AppNotificationEventArgs(title, message, severity, placement, playSound, showToast, source, dedupKey);
            var handlers = NotificationRequested;
            if (handlers == null)
                return;

            foreach (EventHandler<AppNotificationEventArgs> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, args);
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Ignored("Notification", "DispatchAppNotification", ex, retryable: true, category: "UI");
                }
            }
        }
    }
}
