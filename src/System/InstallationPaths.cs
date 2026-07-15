using System;
using System.IO;

namespace CS2TradeMonitor.src.SystemServices
{
    internal static class InstallationPaths
    {
        private const string LauncherExeName = "CS2TradeMonitor.exe";
        private static readonly Lazy<string> InstallDirectoryValue = new(() =>
            ResolveInstallDirectory(AppContext.BaseDirectory, File.Exists));

        public static string InstallDirectory => InstallDirectoryValue.Value;

        public static string ResourcesDirectory => Path.Combine(InstallDirectory, "resources");

        internal static string ResolveInstallDirectory(string appDirectory, Func<string, bool> fileExists)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(appDirectory);
            ArgumentNullException.ThrowIfNull(fileExists);

            string current = Path.GetFullPath(appDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Path.GetFileName(current).Equals("app", StringComparison.OrdinalIgnoreCase))
            {
                string? parent = Directory.GetParent(current)?.FullName;
                if (!string.IsNullOrWhiteSpace(parent) && fileExists(Path.Combine(parent, LauncherExeName)))
                    return parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            string currentLauncher = Path.Combine(current, LauncherExeName);
            if (fileExists(currentLauncher))
                return current;

            return current;
        }
    }
}
