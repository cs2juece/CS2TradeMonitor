using CS2TradeMonitor.Infrastructure.Paths;
using System.Text;

namespace CS2TradeMonitor.src.SystemServices
{
    public static class RuntimeDataPaths
    {
        private static InstanceRuntimeContext Context => InstanceRuntimeContext.Current;

        public static string InstallRoot => Context.InstallRoot;
        public static string InstanceHash => Context.InstanceHash;
        public static string UserDataRoot => Context.UserDataRoot;
        public static string DataDirectory => Context.DataDirectory;
        public static string SecureDirectory => Context.SecureDirectory;
        public static string LogsDirectory => Context.LogsDirectory;
        public static string CacheDirectory => Context.CacheDirectory;
        public static string BackupDirectory => Context.BackupDirectory;
        public static string UpdatesDirectory => Context.UpdatesDirectory;

        public static string GetDataFilePath(string fileName) => Context.GetDataFile(fileName);
        public static string GetSecureFilePath(string fileName) => Context.GetSecureFile(fileName);
        public static string GetLogFilePath(string fileName) => Context.GetLogFile(fileName);
        public static string GetCacheFilePath(string fileName) => Context.GetCacheFile(fileName);
        public static string GetBackupFilePath(string fileName) => Context.GetBackupFile(fileName);
        public static string GetUpdateFilePath(string fileName) => Context.GetUpdateFile(fileName);

        public static void EnsureWritable() => Context.EnsureWritable();

        public static void WriteTextAtomic(string path, string content)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tempPath, content, new UTF8Encoding(false));
                File.Move(tempPath, path, overwrite: true);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                    // Preserve the original write result; stale temp cleanup is best-effort.
                }
            }
        }
    }
}
