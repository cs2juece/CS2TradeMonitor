using CS2TradeMonitor.Application.Abstractions;

namespace CS2TradeMonitor.Infrastructure.Diagnostics
{
    internal sealed record DetailedDiagnosticsMaintenanceResult(
        long TotalBytes,
        int DeletedForRetention,
        int DeletedForCapacity,
        bool HasCapacity);

    internal sealed class DetailedDiagnosticsRetentionManager
    {
        private readonly string _sessionsRoot;
        private readonly IClock _clock;
        private readonly DetailedDiagnosticsOptions _options;

        public DetailedDiagnosticsRetentionManager(
            string sessionsRoot,
            IClock clock,
            DetailedDiagnosticsOptions options)
        {
            _sessionsRoot = Path.GetFullPath(sessionsRoot);
            _clock = clock;
            _options = options;
        }

        public DetailedDiagnosticsMaintenanceResult Maintain(string? activeSessionId, long incomingBytes = 0)
        {
            IReadOnlyCollection<string> protectedSessionIds = string.IsNullOrWhiteSpace(activeSessionId)
                ? Array.Empty<string>()
                : new[] { activeSessionId };
            return MaintainProtected(protectedSessionIds, incomingBytes);
        }

        public DetailedDiagnosticsMaintenanceResult MaintainProtected(
            IReadOnlyCollection<string> protectedSessionIds,
            long incomingBytes = 0)
        {
            ArgumentNullException.ThrowIfNull(protectedSessionIds);
            Directory.CreateDirectory(_sessionsRoot);
            int retentionDeletes = 0;
            int capacityDeletes = 0;
            DateTime cutoffUtc = _clock.UtcNow - _options.EndedSessionRetention;
            List<DirectoryInfo> ended = GetEndedSessions(protectedSessionIds);

            foreach (DirectoryInfo directory in ended.Where(directory => directory.LastWriteTimeUtc < cutoffUtc).ToArray())
            {
                if (TryDeleteSession(directory))
                {
                    ended.Remove(directory);
                    retentionDeletes++;
                }
            }

            long total = GetTotalBytes();
            foreach (DirectoryInfo directory in ended.OrderBy(directory => directory.LastWriteTimeUtc).ToArray())
            {
                if (total + incomingBytes <= _options.MaximumTotalBytes)
                    break;
                if (!TryDeleteSession(directory))
                    continue;
                capacityDeletes++;
                total = GetTotalBytes();
            }

            return new DetailedDiagnosticsMaintenanceResult(
                total,
                retentionDeletes,
                capacityDeletes,
                total + incomingBytes <= _options.MaximumTotalBytes);
        }

        public long GetTotalBytes()
        {
            if (!Directory.Exists(_sessionsRoot))
                return 0;

            long total = 0;
            foreach (string file in Directory.EnumerateFiles(_sessionsRoot, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch
                {
                    // A concurrently removed file contributes no stable bytes to the quota snapshot.
                }
            }
            return total;
        }

        private List<DirectoryInfo> GetEndedSessions(IReadOnlyCollection<string> protectedSessionIds)
        {
            if (!Directory.Exists(_sessionsRoot))
                return new List<DirectoryInfo>();

            var protectedNames = new HashSet<string>(
                protectedSessionIds.Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.OrdinalIgnoreCase);
            return new DirectoryInfo(_sessionsRoot)
                .EnumerateDirectories("*", SearchOption.TopDirectoryOnly)
                .Where(directory => !protectedNames.Contains(directory.Name))
                .ToList();
        }

        private static bool TryDeleteSession(DirectoryInfo directory)
        {
            try
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    return false;
                directory.Delete(recursive: true);
                return true;
            }
            catch
            {
                // Retention is best effort; a locked ended session can be retried later.
                return false;
            }
        }
    }
}
