namespace CS2TradeMonitor.Application.Abstractions
{
    public interface IAppDataPathProvider
    {
        string InstallRoot { get; }
        string InstanceHash { get; }
        string UserDataRoot { get; }
        string DataDirectory { get; }
        string SecureDirectory { get; }
        string LogsDirectory { get; }
        string CacheDirectory { get; }
        string BackupDirectory { get; }
        string UpdatesDirectory { get; }
        string GetDataFilePath(string fileName);
        string GetSecureFilePath(string fileName);
        string GetLogFilePath(string fileName);
        string GetCacheFilePath(string fileName);
        string GetBackupFilePath(string fileName);
        string GetUpdateFilePath(string fileName);
    }
}
