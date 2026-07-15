using System;
using System.IO;
using System.Linq;

namespace CS2TradeMonitor.UpdateSecurity
{
    internal static class UpdatePackageFilePolicy
    {
        public static bool IsForbidden(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return false;

            string normalized = relativePath.Replace('\\', '/').Trim();
            string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(segment =>
                    segment.Equals("user-data", StringComparison.OrdinalIgnoreCase)
                    || segment.Equals("secure", StringComparison.OrdinalIgnoreCase)
                    || segment.Equals("logs", StringComparison.OrdinalIgnoreCase)
                    || segment.Equals("backup", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            string fileName = Path.GetFileName(normalized);
            if (string.IsNullOrWhiteSpace(fileName))
                return segments.Any(segment => segment.Equals("user-data", StringComparison.OrdinalIgnoreCase));

            if (fileName.Equals("CS2TradeMonitor.App.exe", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("settings.json", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("settings.json.", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals(".env", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".dump", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".har", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".download", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".flag", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("steam_tokens.dat", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("steam_auth.dat", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("steam_manual_proxy.dat", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("youpin_auth.dat", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("youpin_device_profile.json", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return segments.Any(segment =>
                segment.StartsWith(".env", StringComparison.OrdinalIgnoreCase)
                || segment.StartsWith("token", StringComparison.OrdinalIgnoreCase)
                || segment.StartsWith("cookie", StringComparison.OrdinalIgnoreCase));
        }
    }
}
