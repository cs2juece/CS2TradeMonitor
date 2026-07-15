namespace CS2TradeMonitor.Application.Abstractions
{
    public enum InstanceResourceKind
    {
        Bootstrap,
        Application,
        ArgumentsPipe,
        Update,
        AutoStart
    }

    public interface IInstanceRuntimeContext
    {
        string InstallRoot { get; }
        string CanonicalInstallRoot { get; }
        string InstanceHash { get; }
        string UserDataRoot { get; }
        string DataDirectory { get; }
        string SecureDirectory { get; }
        string LogsDirectory { get; }
        string CacheDirectory { get; }
        string BackupDirectory { get; }
        string UpdatesDirectory { get; }
        string GetDataFile(string name);
        string GetSecureFile(string name);
        string GetLogFile(string name);
        string GetCacheFile(string name);
        string GetBackupFile(string name);
        string GetUpdateFile(string name);
        string BuildOsResourceName(InstanceResourceKind kind);
        void EnsureWritable();
    }
}
