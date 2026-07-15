using System.Text;
using System.Text.Json;

namespace CS2TradeMonitor.Infrastructure.Diagnostics
{
    internal sealed class DetailedDiagnosticsPersistedState
    {
        public int Version { get; set; } = 1;
        public bool IsEnabled { get; set; }
        public string? SessionId { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public DateTime? LastSessionEndedAtUtc { get; set; }
        public string? LastStopReason { get; set; }
        public long DroppedEventCount { get; set; }
        public int CapacityCleanupCount { get; set; }
    }

    internal sealed class DetailedDiagnosticsStateStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        private readonly string _path;

        public DetailedDiagnosticsStateStore(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            _path = global::System.IO.Path.GetFullPath(path);
        }

        public string StatePath => _path;

        public DetailedDiagnosticsPersistedState Load()
        {
            if (!File.Exists(_path))
                return new DetailedDiagnosticsPersistedState();

            DetailedDiagnosticsPersistedState state = JsonSerializer.Deserialize<DetailedDiagnosticsPersistedState>(
                File.ReadAllText(_path, Encoding.UTF8),
                JsonOptions) ?? throw new InvalidDataException("详细诊断状态文件为空。");
            if (state.Version != 1)
                throw new InvalidDataException("不支持的详细诊断状态版本。");
            if (state.IsEnabled
                && (string.IsNullOrWhiteSpace(state.SessionId)
                    || state.StartedAtUtc is null
                    || state.ExpiresAtUtc is null))
            {
                throw new InvalidDataException("详细诊断活动会话状态不完整。");
            }

            return state;
        }

        public void Save(DetailedDiagnosticsPersistedState state)
        {
            ArgumentNullException.ThrowIfNull(state);
            string directory = global::System.IO.Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("详细诊断状态路径没有父目录。");
            Directory.CreateDirectory(directory);
            string tempPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tempPath, JsonSerializer.Serialize(state, JsonOptions), new UTF8Encoding(false));
                File.Move(tempPath, _path, overwrite: true);
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
                    // A unique state temp file is harmless; the next save uses a different name.
                }
            }
        }
    }
}
