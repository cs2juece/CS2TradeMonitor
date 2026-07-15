using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Infrastructure.Paths
{
    public sealed class RuntimeDataPathProvider : IAppDataPathProvider
    {
        private readonly IInstanceRuntimeContext _instanceContext;

        public RuntimeDataPathProvider(IInstanceRuntimeContext instanceContext)
        {
            _instanceContext = instanceContext ?? throw new ArgumentNullException(nameof(instanceContext));
        }

        public string InstallRoot => _instanceContext.InstallRoot;
        public string InstanceHash => _instanceContext.InstanceHash;
        public string UserDataRoot => _instanceContext.UserDataRoot;
        public string DataDirectory => _instanceContext.DataDirectory;
        public string SecureDirectory => _instanceContext.SecureDirectory;
        public string LogsDirectory => _instanceContext.LogsDirectory;
        public string CacheDirectory => _instanceContext.CacheDirectory;
        public string BackupDirectory => _instanceContext.BackupDirectory;
        public string UpdatesDirectory => _instanceContext.UpdatesDirectory;

        public string GetDataFilePath(string fileName) => _instanceContext.GetDataFile(fileName);

        public string GetSecureFilePath(string fileName) => _instanceContext.GetSecureFile(fileName);
        public string GetLogFilePath(string fileName) => _instanceContext.GetLogFile(fileName);
        public string GetCacheFilePath(string fileName) => _instanceContext.GetCacheFile(fileName);
        public string GetBackupFilePath(string fileName) => _instanceContext.GetBackupFile(fileName);
        public string GetUpdateFilePath(string fileName) => _instanceContext.GetUpdateFile(fileName);
    }
}
