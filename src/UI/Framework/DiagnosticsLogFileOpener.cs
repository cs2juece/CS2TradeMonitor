using System.Diagnostics;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class DiagnosticsLogFileOpener
    {
        public static string EnsureLogFile()
        {
            return DiagnosticsLogger.EnsureLogFile();
        }

        public static void OpenLogFile()
        {
            string path = EnsureLogFile();
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }
}
