using System.Diagnostics;

namespace CS2TradeMonitor.src.SystemServices
{
    internal static class LauncherExecutablePath
    {
        private const string DotNetHostExeName = "dotnet.exe";
        private const string BootstrapperExeName = "CS2TradeMonitor.exe";

        public static string GetCurrent()
        {
            string? current = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(current))
                current = System.Windows.Forms.Application.ExecutablePath;
            return Resolve(current, AppContext.BaseDirectory, File.Exists);
        }

        internal static string Resolve(
            string currentExecutable,
            string appDirectory,
            Func<string, bool> fileExists)
        {
            string directory = Path.GetFullPath(appDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Path.GetFileName(directory).Equals("app", StringComparison.OrdinalIgnoreCase))
            {
                string? packageRoot = Directory.GetParent(directory)?.FullName;
                if (!string.IsNullOrWhiteSpace(packageRoot))
                {
                    string rootBootstrapper = Path.Combine(packageRoot, BootstrapperExeName);
                    if (fileExists(rootBootstrapper))
                        return rootBootstrapper;
                }
            }

            string currentName = Path.GetFileName(currentExecutable);
            bool isDotNetHost = currentName.Equals(DotNetHostExeName, StringComparison.OrdinalIgnoreCase);
            if (!isDotNetHost)
                return currentExecutable;

            if (string.IsNullOrWhiteSpace(directory))
                return currentExecutable;

            string bootstrapper = Path.Combine(directory, BootstrapperExeName);
            if (fileExists(bootstrapper))
                return bootstrapper;

            string? parent = Directory.GetParent(directory)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent))
            {
                bootstrapper = Path.Combine(parent, BootstrapperExeName);
                if (fileExists(bootstrapper))
                    return bootstrapper;
            }

            return currentExecutable;
        }
    }
}
