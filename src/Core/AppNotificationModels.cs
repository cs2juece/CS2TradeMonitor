using System;

namespace CS2TradeMonitor.src.Core
{
    public enum AppNotificationSeverity
    {
        Info,
        Warning,
        Success,
        Error,
        Loading
    }

    public enum AppNotificationPlacement
    {
        Desktop,
        BottomLeft
    }

    public sealed class AppNotificationEventArgs : EventArgs
    {
        public AppNotificationEventArgs(
            string title,
            string message,
            AppNotificationSeverity severity,
            AppNotificationPlacement placement,
            bool playSound,
            bool showToast,
            string? source = null,
            string? dedupKey = null)
        {
            Title = title;
            Message = message;
            Severity = severity;
            Placement = placement;
            PlaySound = playSound;
            ShowToast = showToast;
            Source = source;
            DedupKey = dedupKey;
        }

        public string Title { get; }
        public string Message { get; }
        public AppNotificationSeverity Severity { get; }
        public AppNotificationPlacement Placement { get; }
        public bool PlaySound { get; }
        public bool ShowToast { get; }
        public string? Source { get; }
        public string? DedupKey { get; }
    }
}
