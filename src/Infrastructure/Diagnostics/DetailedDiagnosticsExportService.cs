using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CS2TradeMonitor.Application.Abstractions;

namespace CS2TradeMonitor.Infrastructure.Diagnostics
{
    public sealed partial class DetailedDiagnosticsExportService : IDetailedDiagnosticsExportService
    {
        private readonly DetailedDiagnosticsService _diagnostics;
        private readonly IInstanceRuntimeContext _instance;
        private readonly IClock _clock;
        private readonly string _sessionsRoot;
        private readonly string _exportRoot;

        public DetailedDiagnosticsExportService(
            DetailedDiagnosticsService diagnostics,
            IInstanceRuntimeContext instance,
            IClock clock)
        {
            _diagnostics = diagnostics;
            _instance = instance;
            _clock = clock;
            _sessionsRoot = Path.Combine(diagnostics.DiagnosticsDirectory, "sessions");
            _exportRoot = instance.GetCacheFile("diagnostic-export");
        }

        public string? GetPreferredLogFilePath()
        {
            DetailedDiagnosticsStatus status = _diagnostics.GetStatus();
            if (status.IsEnabled && File.Exists(status.ActiveLogFilePath))
                return status.ActiveLogFilePath;
            if (!Directory.Exists(_sessionsRoot))
                return null;

            return new DirectoryInfo(_sessionsRoot)
                .EnumerateDirectories("*", SearchOption.TopDirectoryOnly)
                .Where(directory => (directory.Attributes & FileAttributes.ReparsePoint) == 0)
                .OrderByDescending(directory => directory.LastWriteTimeUtc)
                .Select(directory => Path.Combine(directory.FullName, DetailedDiagnosticsService.DiagnosticLogFileName))
                .FirstOrDefault(File.Exists);
        }

        public async Task<DetailedDiagnosticsExportResult> ExportAsync(
            string destinationZipPath,
            IReadOnlyDictionary<string, object?>? whitelistedConfiguration = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationZipPath);
            string destination = Path.GetFullPath(destinationZipPath);
            await _diagnostics.FlushAsync(cancellationToken).ConfigureAwait(false);
            string? sourceLog = GetPreferredLogFilePath();
            if (string.IsNullOrWhiteSpace(sourceLog) || !File.Exists(sourceLog))
                throw new InvalidOperationException("当前没有可导出的详细诊断会话。");

            string workspace = Path.Combine(_exportRoot, Guid.NewGuid().ToString("N"));
            string zipTemp = workspace + ".zip";
            EnsureUnderRoot(workspace, _exportRoot);
            EnsureUnderRoot(zipTemp, _exportRoot);
            try
            {
                string detailedDirectory = Path.Combine(workspace, "diagnostics");
                string regularDirectory = Path.Combine(workspace, "regular");
                Directory.CreateDirectory(detailedDirectory);
                string snapshotLog = Path.Combine(detailedDirectory, DetailedDiagnosticsService.DiagnosticLogFileName);
                await CopyConsistentPrefixAsync(sourceLog, snapshotLog, cancellationToken).ConfigureAwait(false);
                (DateTime startUtc, DateTime endUtc) = ReadTimeRange(snapshotLog);

                bool includedRegular = false;
                string regularLogPath = _instance.GetLogFile("CS2TradeMonitor_Error.log");
                if (File.Exists(regularLogPath))
                {
                    string filtered = FilterRegularLog(
                        File.ReadAllLines(regularLogPath, Encoding.UTF8),
                        startUtc,
                        endUtc);
                    if (!string.IsNullOrWhiteSpace(filtered))
                    {
                        Directory.CreateDirectory(regularDirectory);
                        File.WriteAllText(
                            Path.Combine(regularDirectory, "CS2TradeMonitor_Error.log"),
                            _diagnostics.SanitizeText(filtered),
                            new UTF8Encoding(false));
                        includedRegular = true;
                    }
                }

                DetailedDiagnosticsStatus status = _diagnostics.GetStatus();
                JsonNode? safeConfiguration = _diagnostics.SanitizeData(whitelistedConfiguration);
                var runtimeSummary = new JsonObject
                {
                    ["softwareVersion"] = typeof(DetailedDiagnosticsExportService).Assembly.GetName().Version?.ToString() ?? "unknown",
                    ["windows"] = _diagnostics.SanitizeText(RuntimeInformation.OSDescription),
                    ["dotnet"] = _diagnostics.SanitizeText(RuntimeInformation.FrameworkDescription),
                    ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
                    ["processorCount"] = Environment.ProcessorCount,
                    ["sessionStartedAtUtc"] = startUtc,
                    ["sessionEndedAtUtc"] = endUtc,
                    ["exportedAtUtc"] = EnsureUtc(_clock.UtcNow),
                    ["diagnosticBytes"] = status.TotalBytes,
                    ["diagnosticMaximumBytes"] = status.MaximumBytes,
                    ["droppedEvents"] = status.DroppedEventCount,
                    ["capacityCleanupCount"] = status.CapacityCleanupCount,
                    ["configuration"] = safeConfiguration
                };
                string runtimeSummaryText = runtimeSummary
                    .ToJsonString(new JsonSerializerOptions { WriteIndented = true })
                    .Replace(_instance.InstanceHash, "[INSTANCE]", StringComparison.OrdinalIgnoreCase);
                File.WriteAllText(
                    Path.Combine(workspace, "runtime-summary.json"),
                    runtimeSummaryText,
                    new UTF8Encoding(false));
                string summary = BuildReadableSummary(startUtc, endUtc, status, includedRegular);
                File.WriteAllText(Path.Combine(workspace, "summary.txt"), summary, new UTF8Encoding(false));

                ValidateWorkspace(workspace);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)
                    ?? throw new InvalidOperationException("诊断包保存位置没有父目录。"));
                ZipFile.CreateFromDirectory(workspace, zipTemp, CompressionLevel.Optimal, includeBaseDirectory: false);
                ValidateZip(zipTemp);
                File.Move(zipTemp, destination, overwrite: true);
                var result = new DetailedDiagnosticsExportResult(
                    destination,
                    startUtc,
                    endUtc,
                    new FileInfo(destination).Length,
                    includedRegular);
                return result;
            }
            finally
            {
                TryDeleteWorkspace(workspace);
                TryDeleteFile(zipTemp);
            }
        }

        private async Task CopyConsistentPrefixAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
        {
            using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            long remaining = source.Length;
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] buffer = new byte[81920];
            while (remaining > 0)
            {
                int read = await source.ReadAsync(
                    buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)),
                    cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    break;
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                remaining -= read;
            }
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private static (DateTime StartUtc, DateTime EndUtc) ReadTimeRange(string jsonlPath)
        {
            DateTime? start = null;
            DateTime? end = null;
            foreach (string line in File.ReadLines(jsonlPath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                try
                {
                    using JsonDocument document = JsonDocument.Parse(line);
                    if (!document.RootElement.TryGetProperty("timestampUtc", out JsonElement timestamp)
                        || !timestamp.TryGetDateTime(out DateTime value))
                    {
                        continue;
                    }
                    value = EnsureUtc(value);
                    start = start is null || value < start ? value : start;
                    end = end is null || value > end ? value : end;
                }
                catch (JsonException)
                {
                    // A partial final line from an active session is excluded from the consistent prefix.
                }
            }

            if (start is null || end is null)
                throw new InvalidDataException("详细诊断会话没有可导出的完整事件。");
            return (start.Value, end.Value);
        }

        private string FilterRegularLog(IEnumerable<string> lines, DateTime startUtc, DateTime endUtc)
        {
            var output = new StringBuilder();
            var block = new List<string>();
            DateTime? blockTime = null;
            foreach (string line in lines)
            {
                if (line.StartsWith("--------------------------------------------------", StringComparison.Ordinal))
                {
                    if (block.Count > 0)
                        AppendBlockIfInRange(output, block, blockTime, startUtc, endUtc);
                    block.Clear();
                    blockTime = null;
                    block.Add(line);
                    continue;
                }

                if (block.Count > 0)
                {
                    block.Add(line);
                    blockTime ??= TryParseRegularTime(line);
                    continue;
                }

                DateTime? time = TryParseRegularTime(line);
                if (time is not null && IsWithin(time.Value, startUtc, endUtc))
                    output.AppendLine(line);
            }
            if (block.Count > 0)
                AppendBlockIfInRange(output, block, blockTime, startUtc, endUtc);
            return output.ToString();
        }

        private static void AppendBlockIfInRange(
            StringBuilder output,
            IEnumerable<string> block,
            DateTime? time,
            DateTime startUtc,
            DateTime endUtc)
        {
            if (time is null || !IsWithin(time.Value, startUtc, endUtc))
                return;
            foreach (string line in block)
                output.AppendLine(line);
        }

        private static DateTime? TryParseRegularTime(string line)
        {
            Match normal = RegularLineTimePattern().Match(line);
            Match block = RegularBlockTimePattern().Match(line);
            string value = normal.Success ? normal.Groups[1].Value : block.Success ? block.Groups[1].Value : "";
            if (!DateTime.TryParseExact(
                    value,
                    "yyyy-MM-dd HH:mm:ss",
                    global::System.Globalization.CultureInfo.InvariantCulture,
                    global::System.Globalization.DateTimeStyles.AssumeLocal,
                    out DateTime parsed))
            {
                return null;
            }
            return parsed.ToUniversalTime();
        }

        private string BuildReadableSummary(
            DateTime startUtc,
            DateTime endUtc,
            DetailedDiagnosticsStatus status,
            bool includedRegular)
        {
            return string.Join(
                Environment.NewLine,
                "CS2 Trade Monitor 诊断包",
                "",
                $"诊断开始（UTC）：{startUtc:yyyy-MM-dd HH:mm:ss}",
                $"诊断结束（UTC）：{endUtc:yyyy-MM-dd HH:mm:ss}",
                $"详细日志占用：{status.TotalBytes} / {status.MaximumBytes} 字节",
                $"已丢弃低优先级事件：{status.DroppedEventCount}",
                $"容量清理次数：{status.CapacityCleanupCount}",
                $"包含同期常规错误日志：{(includedRegular ? "是" : "否")}",
                "",
                "本诊断包由用户手动导出，软件不会自动上传或发送。",
                "敏感字段已经脱敏；无法确认安全的正文子树不会保留。",
                "包内不包含原始关联密钥、实例哈希或真实安装路径。",
                "");
        }

        private void ValidateWorkspace(string workspace)
        {
            foreach (string path in Directory.EnumerateFiles(workspace, "*", SearchOption.AllDirectories))
            {
                foreach (string line in File.ReadLines(path, Encoding.UTF8))
                    ValidateSafeText(line);
            }
        }

        private void ValidateZip(string zipPath)
        {
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string normalized = entry.FullName.Replace('\\', '/');
                if (normalized.Contains("user-data", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("diagnostics-correlation-key", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("诊断包包含禁止路径。");
                }
                if (string.IsNullOrWhiteSpace(entry.Name))
                    continue;
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                string? line;
                while ((line = reader.ReadLine()) is not null)
                    ValidateSafeText(line);
            }
        }

        private void ValidateSafeText(string text)
        {
            if (text.Contains(_instance.InstallRoot, StringComparison.OrdinalIgnoreCase)
                || text.Contains(_instance.CanonicalInstallRoot, StringComparison.OrdinalIgnoreCase)
                || text.Contains(_instance.InstanceHash, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
                    && text.Contains(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        StringComparison.OrdinalIgnoreCase))
                || (Environment.MachineName.Length >= 4
                    && text.Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("诊断包隐私扫描发现实例或用户环境原文。");
            }
            if (UnsafeSecretValuePattern().IsMatch(text))
                throw new InvalidDataException("诊断包隐私扫描发现未脱敏秘密字段。");
        }

        private static bool IsWithin(DateTime timeUtc, DateTime startUtc, DateTime endUtc)
            => timeUtc >= startUtc.AddSeconds(-1) && timeUtc <= endUtc.AddSeconds(1);

        private static void EnsureUnderRoot(string path, string root)
        {
            string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string pathFull = Path.GetFullPath(path);
            if (!pathFull.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("诊断导出工作区越过实例缓存边界。");
        }

        private static void TryDeleteWorkspace(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Export workspace cleanup is best effort and retried by later cache maintenance.
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // A GUID-isolated failed export temp file cannot replace the chosen destination.
            }
        }

        private static DateTime EnsureUtc(DateTime value)
            => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

        [GeneratedRegex(@"^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\]")]
        private static partial Regex RegularLineTimePattern();

        [GeneratedRegex(@"^\[Time\]:\s*(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})")]
        private static partial Regex RegularBlockTimePattern();

        [GeneratedRegex("""(?i)\b(token|cookie|authorization|password|secret|sessionid)\b["']?\s*[:=]\s*["']?(?!\[REDACTED\]|\[REMOVED_)[^\s,;}"']+""")]
        private static partial Regex UnsafeSecretValuePattern();
    }
}
