using System.Diagnostics;
using System.IO.Compression;
using CS2TradeMonitor.Updater.Paths;
using CS2TradeMonitor.UpdateSecurity;

namespace CS2TradeMonitor.Updater
{
    internal static class Program
    {
        private const string AppExeName = "CS2TradeMonitor.exe";
        private const string UsageFileName = "使用说明(必读).txt";
        private const string PayloadAppHostRelativePath = @"app\CS2TradeMonitor.exe";
        private const string PayloadAppDllRelativePath = @"app\CS2TradeMonitor.dll";
        private const string UpdaterRelativePath = @"app\CS2TradeMonitor.Updater.exe";
        private const string SuccessFlagName = "CS2TradeMonitor_UpdateSuccess.flag";
        private const string ErrorLogName = "CS2TradeMonitor_UpdateError.log";

        internal sealed class UpdateArgs
        {
            public string ZipPath { get; set; } = "";
            public string InstallDir { get; set; } = "";
            public string LauncherPath { get; set; } = "";
            public string InstanceHash { get; set; } = "";
            public int Pid { get; set; }
            public bool Restart { get; set; }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            UpdateArgs? parsed = ParseArgs(args);
            if (parsed == null)
                return 2;

            string? validatedInstallDir = null;
            Mutex? updateMutex = null;
            bool ownsMutex = false;
            try
            {
                NormalizeAndValidateArguments(parsed);
                validatedInstallDir = parsed.InstallDir;

                string mutexName = "Global\\CS2TradeMonitor.Update." + parsed.InstanceHash;
                updateMutex = new Mutex(initiallyOwned: true, mutexName, out ownsMutex);
                if (!ownsMutex)
                    throw new InvalidOperationException("当前目录实例已有更新任务正在执行。");

                WaitForMainProcessExit(parsed.Pid, parsed.InstallDir);

                string workRoot = Path.Combine(
                    Path.GetTempPath(),
                    "CS2TradeMonitor",
                    parsed.InstanceHash,
                    "update",
                    parsed.Pid + "-" + Guid.NewGuid().ToString("N"));
                string extractDir = Path.Combine(workRoot, "extract");
                string backupDir = Path.Combine(workRoot, "backup");
                Directory.CreateDirectory(extractDir);
                Directory.CreateDirectory(backupDir);

                try
                {
                    ValidateArchiveEntries(parsed.ZipPath);
                    ZipFile.ExtractToDirectory(parsed.ZipPath, extractDir, overwriteFiles: true);
                    string sourceRoot = ResolvePackageRoot(extractDir);
                    ValidatePackageRoot(sourceRoot);

                    UpdatePackageInstaller.Apply(sourceRoot, parsed.InstallDir, backupDir);
                    WriteStatusFile(parsed.InstallDir, SuccessFlagName, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    TryDelete(parsed.ZipPath);

                    if (parsed.Restart)
                        RestartMain(parsed.LauncherPath, parsed.InstallDir);
                    return 0;
                }
                finally
                {
                    TryDeleteDirectory(workRoot);
                }
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(validatedInstallDir))
                    WriteError(validatedInstallDir, ex);
                return 1;
            }
            finally
            {
                if (ownsMutex)
                {
                    try { updateMutex?.ReleaseMutex(); }
                    catch (ApplicationException)
                    {
                        // Mutex ownership was already released while unwinding another failure.
                    }
                }
                updateMutex?.Dispose();
            }
        }

        internal static UpdateArgs? ParseArgs(string[] args)
        {
            var result = new UpdateArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                string NextValue() => i + 1 < args.Length ? args[++i] : "";
                switch (key)
                {
                    case "--zip": result.ZipPath = NextValue(); break;
                    case "--install-dir": result.InstallDir = NextValue(); break;
                    case "--launcher": result.LauncherPath = NextValue(); break;
                    case "--instance-hash": result.InstanceHash = NextValue(); break;
                    case "--pid": int.TryParse(NextValue(), out int pid); result.Pid = pid; break;
                    case "--restart": result.Restart = true; break;
                }
            }

            return string.IsNullOrWhiteSpace(result.ZipPath)
                || string.IsNullOrWhiteSpace(result.InstallDir)
                || string.IsNullOrWhiteSpace(result.LauncherPath)
                || string.IsNullOrWhiteSpace(result.InstanceHash)
                || result.Pid <= 0
                ? null
                : result;
        }

        internal static void NormalizeAndValidateArguments(UpdateArgs args)
        {
            args.ZipPath = Path.GetFullPath(args.ZipPath);
            args.InstallDir = Path.GetFullPath(args.InstallDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            args.LauncherPath = Path.GetFullPath(args.LauncherPath);
            args.InstanceHash = args.InstanceHash.Trim().ToLowerInvariant();

            if (!Directory.Exists(args.InstallDir))
                throw new DirectoryNotFoundException("安装目录不存在：" + args.InstallDir);
            if (!File.Exists(args.ZipPath))
                throw new FileNotFoundException("更新包不存在。", args.ZipPath);

            (string _, string actualHash) = InstanceIdentity.Resolve(args.InstallDir);
            if (!string.Equals(actualHash, args.InstanceHash, StringComparison.Ordinal))
                throw new InvalidDataException("更新器实例身份校验失败，安装目录与 InstanceHash 不匹配。");

            string expectedLauncher = Path.Combine(args.InstallDir, AppExeName);
            if (!string.Equals(expectedLauncher, args.LauncherPath, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(args.LauncherPath))
            {
                throw new InvalidDataException("更新器启动器路径不属于目标安装根。");
            }

            string updatesRoot = Path.Combine(args.InstallDir, "user-data", "updates");
            if (!IsPathInside(args.ZipPath, updatesRoot))
                throw new InvalidDataException("更新包必须位于当前实例的 user-data\\updates 目录。");
        }

        internal static bool IsPathInside(string path, string root)
        {
            string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string pathFull = Path.GetFullPath(path);
            return pathFull.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        internal static void WaitForMainProcessExit(int pid, string installDir)
        {
            try
            {
                using Process process = Process.GetProcessById(pid);
                string executable = process.MainModule?.FileName ?? "";
                if (!IsPathInside(executable, installDir))
                    throw new InvalidDataException("发起更新的 PID 不属于目标安装根，已拒绝等待。");
                if (!process.WaitForExit(30000))
                    throw new TimeoutException("发起更新的程序在 30 秒内未退出，更新已取消。");
            }
            catch (ArgumentException)
            {
                // 发起进程已经退出。
            }
        }

        private static void ValidateArchiveEntries(string zipPath)
        {
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string name = entry.FullName.Replace('\\', '/');
                if (name.StartsWith("/", StringComparison.Ordinal)
                    || name.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or "..")
                    || UpdatePackageFilePolicy.IsForbidden(name))
                {
                    throw new InvalidDataException("更新包包含禁止或非法路径：" + name);
                }
            }
        }

        private static string ResolvePackageRoot(string extractDir)
        {
            string[] entries = Directory.GetFileSystemEntries(extractDir);
            return entries.Length == 1 && Directory.Exists(entries[0]) ? entries[0] : extractDir;
        }

        internal static void ValidatePackageRoot(string sourceRoot)
        {
            if (!File.Exists(Path.Combine(sourceRoot, AppExeName)))
                throw new InvalidDataException("更新包缺少 " + AppExeName);
            if (!File.Exists(Path.Combine(sourceRoot, PayloadAppHostRelativePath)))
                throw new InvalidDataException("更新包缺少 " + PayloadAppHostRelativePath);
            if (!File.Exists(Path.Combine(sourceRoot, PayloadAppDllRelativePath)))
                throw new InvalidDataException("更新包缺少 " + PayloadAppDllRelativePath);
            if (!File.Exists(Path.Combine(sourceRoot, UpdaterRelativePath)))
                throw new InvalidDataException("更新包缺少 " + UpdaterRelativePath);
            if (!File.Exists(Path.Combine(sourceRoot, UsageFileName)))
                throw new InvalidDataException("更新包缺少 " + UsageFileName);

            string[] allowedRootFiles = { AppExeName, UsageFileName };
            if (Directory.GetFiles(sourceRoot, "*", SearchOption.TopDirectoryOnly)
                .Any(path => !allowedRootFiles.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("更新包根目录只能包含主程序和使用说明。");
            }

            foreach (string directory in Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceRoot, directory);
                if (UpdatePackageFilePolicy.IsForbidden(relative + "/"))
                    throw new InvalidDataException("更新包包含禁止目录：" + relative);
            }

            _ = ProgramFileManifestPolicy.LoadAndValidate(sourceRoot);
        }

        private static void RestartMain(string launcherPath, string installDir)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = launcherPath,
                    Arguments = "--updated",
                    WorkingDirectory = installDir,
                    UseShellExecute = true
                });
            }
            catch
            {
                // The update is already applied; restart failure is reported on the next manual launch.
            }
        }

        private static void WriteError(string installDir, Exception ex)
        {
            try
            {
                WriteStatusFile(installDir, ErrorLogName, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + ex);
            }
            catch
            {
                // Do not replace the original update failure with a status-file write failure.
            }
        }

        private static void WriteStatusFile(string installDir, string fileName, string contents)
        {
            string statusDirectory = Path.Combine(installDir, "user-data", "updates");
            Directory.CreateDirectory(statusDirectory);
            File.WriteAllText(Path.Combine(statusDirectory, fileName), contents);
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch
            {
                // A stale downloaded package is harmless and may be retried on the next launch.
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
            catch
            {
                // Temporary updater files are isolated by instance hash and unique invocation id.
            }
        }
    }
}
