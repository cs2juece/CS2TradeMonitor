using System;
using System.Runtime.InteropServices;

namespace CS2TradeMonitor.src.Core
{
    public static class FullscreenActivityDetector
    {
        public static bool ShouldSuppressNotifications()
        {
            try
            {
                if (SHQueryUserNotificationState(out var state) == 0)
                {
                    if (state == QueryUserNotificationState.QUNS_BUSY
                        || state == QueryUserNotificationState.QUNS_RUNNING_D3D_FULL_SCREEN
                        || state == QueryUserNotificationState.QUNS_PRESENTATION_MODE
                        || state == QueryUserNotificationState.QUNS_QUIET_TIME)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Fall through to geometry based detection.
            }

            return IsForegroundWindowFullscreen();
        }

        private static bool IsForegroundWindowFullscreen()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
                    return false;

                IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (monitor == IntPtr.Zero)
                    return false;

                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (!GetMonitorInfo(monitor, ref info))
                    return false;

                const int tolerance = 2;
                return rect.Left <= info.rcMonitor.Left + tolerance
                    && rect.Top <= info.rcMonitor.Top + tolerance
                    && rect.Right >= info.rcMonitor.Right - tolerance
                    && rect.Bottom >= info.rcMonitor.Bottom - tolerance;
            }
            catch
            {
                return false;
            }
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        private enum QueryUserNotificationState
        {
            QUNS_NOT_PRESENT = 1,
            QUNS_BUSY = 2,
            QUNS_RUNNING_D3D_FULL_SCREEN = 3,
            QUNS_PRESENTATION_MODE = 4,
            QUNS_ACCEPTS_NOTIFICATIONS = 5,
            QUNS_QUIET_TIME = 6,
            QUNS_APP = 7
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out QueryUserNotificationState pquns);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    }
}
