using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Infrastructure.Paths
{
    public sealed class InstanceRuntimeContext : IInstanceRuntimeContext
    {
        private static readonly Lazy<InstanceRuntimeContext> CurrentValue = new(() => new InstanceRuntimeContext());
        private readonly string[] _managedDirectories;

        public InstanceRuntimeContext()
            : this(InstallationPaths.InstallDirectory, new WindowsCanonicalPathResolver())
        {
        }

        internal InstanceRuntimeContext(string installRoot, WindowsCanonicalPathResolver resolver)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
            ArgumentNullException.ThrowIfNull(resolver);

            string fullInstallRoot = Path.GetFullPath(installRoot);
            string installRootPrefix = Path.GetPathRoot(fullInstallRoot) ?? string.Empty;
            InstallRoot = fullInstallRoot.Length > installRootPrefix.Length
                ? fullInstallRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : fullInstallRoot;
            CanonicalInstallRoot = resolver.ResolveDirectory(InstallRoot);
            InstanceHash = InstanceIdentity.ComputeHash(CanonicalInstallRoot);
            UserDataRoot = Path.Combine(InstallRoot, "user-data");
            DataDirectory = Path.Combine(UserDataRoot, "data");
            SecureDirectory = Path.Combine(UserDataRoot, "secure");
            LogsDirectory = Path.Combine(UserDataRoot, "logs");
            CacheDirectory = Path.Combine(UserDataRoot, "cache");
            BackupDirectory = Path.Combine(UserDataRoot, "backup");
            UpdatesDirectory = Path.Combine(UserDataRoot, "updates");
            _managedDirectories =
            [
                DataDirectory,
                SecureDirectory,
                LogsDirectory,
                CacheDirectory,
                BackupDirectory,
                UpdatesDirectory
            ];
        }

        public static InstanceRuntimeContext Current => CurrentValue.Value;

        public string InstallRoot { get; }
        public string CanonicalInstallRoot { get; }
        public string InstanceHash { get; }
        public string UserDataRoot { get; }
        public string DataDirectory { get; }
        public string SecureDirectory { get; }
        public string LogsDirectory { get; }
        public string CacheDirectory { get; }
        public string BackupDirectory { get; }
        public string UpdatesDirectory { get; }

        public string GetDataFile(string name) => BuildManagedPath(DataDirectory, name);
        public string GetSecureFile(string name) => BuildManagedPath(SecureDirectory, name);
        public string GetLogFile(string name) => BuildManagedPath(LogsDirectory, name);
        public string GetCacheFile(string name) => BuildManagedPath(CacheDirectory, name);
        public string GetBackupFile(string name) => BuildManagedPath(BackupDirectory, name);
        public string GetUpdateFile(string name) => BuildManagedPath(UpdatesDirectory, name);

        public string BuildOsResourceName(InstanceResourceKind kind)
        {
            return kind switch
            {
                InstanceResourceKind.Bootstrap => $"Global\\CS2TradeMonitor.Bootstrap.{InstanceHash}",
                InstanceResourceKind.Application => $"Global\\CS2TradeMonitor.App.{InstanceHash}",
                InstanceResourceKind.ArgumentsPipe => $"CS2TradeMonitor.Args.{InstanceHash}",
                InstanceResourceKind.Update => $"Global\\CS2TradeMonitor.Update.{InstanceHash}",
                InstanceResourceKind.AutoStart => $"CS2TradeMonitor_AutoStart_{InstanceHash}",
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知的实例资源类型。")
            };
        }

        public void EnsureWritable()
        {
            foreach (string directory in _managedDirectories)
                ProbeDirectory(directory);
        }

        internal static string ComputeInstanceHash(string canonicalInstallRoot)
            => InstanceIdentity.ComputeHash(canonicalInstallRoot);

        private static string BuildManagedPath(string root, string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (Path.IsPathRooted(name))
                throw new ArgumentException("实例数据文件名必须是相对路径。", nameof(name));

            string rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(Path.Combine(rootPath, name));
            if (!candidate.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("实例数据文件不能位于所属目录之外。", nameof(name));

            return candidate;
        }

        private static void ProbeDirectory(string directory)
        {
            string? sourcePath = null;
            string? destinationPath = null;
            string operation = "创建目录";
            try
            {
                Directory.CreateDirectory(directory);
                sourcePath = Path.Combine(directory, $".write-probe-{Environment.ProcessId}-{Guid.NewGuid():N}.tmp");
                destinationPath = sourcePath + ".renamed";

                operation = "创建并写入临时文件";
                using (var stream = new FileStream(sourcePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write("CS2TradeMonitor"u8);
                    operation = "刷新临时文件";
                    stream.Flush(flushToDisk: true);
                }

                operation = "重命名临时文件";
                File.Move(sourcePath, destinationPath);
                sourcePath = null;

                operation = "删除临时文件";
                File.Delete(destinationPath);
                destinationPath = null;
            }
            catch (Exception ex)
            {
                throw new InstanceDataWriteException(directory, operation, ex);
            }
            finally
            {
                TryDelete(sourcePath);
                TryDelete(destinationPath);
            }
        }

        private static void TryDelete(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                File.Delete(path);
            }
            catch
            {
                // The original probe failure carries the actionable path and operation.
            }
        }
    }

    public sealed class InstanceDataWriteException : IOException
    {
        public InstanceDataWriteException(string directoryPath, string operation, Exception innerException)
            : base($"实例数据目录不可写。路径：{directoryPath}；操作：{operation}；原因：{innerException.Message}", innerException)
        {
            DirectoryPath = directoryPath;
            Operation = operation;
        }

        public string DirectoryPath { get; }
        public string Operation { get; }
    }
}
