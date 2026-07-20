using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using CS2TradeMonitor.Infrastructure.Diagnostics;

namespace CS2TradeMonitor.src.SystemServices
{
    public static class DiagnosticsLogger
    {
        private static readonly object LockObj = new();
        private static readonly Lazy<string> LogDirectoryLazy = new(ResolveLogDirectory);
        private static string? _testLogDirectory;
        private static readonly Regex SensitivePairRegex = new(
            @"(?i)\b(api[-_\s]?key|api[-_\s]?token|apitoken|send[-_\s]?key|token|device[-_\s]?token|deviceuk|access[-_\s]?token|refresh[-_\s]?token|password|cookie|authorization|secret|shared[-_\s]?secret|identity[-_\s]?secret|steamloginsecure|sessionid|steamid|partnersteamid|orderno|platformorderno|youpinorderno|userid|phone|mobile)\b([""']?\s*[:=]\s*[""']?)([^\s,;&""']+)",
            RegexOptions.Compiled);
        private static readonly Regex BearerRegex = new(
            @"(?i)\b(bearer\s+)([A-Za-z0-9._\-+/=]+)",
            RegexOptions.Compiled);
        private static readonly Regex PhoneRegex = new(@"\b1[3-9]\d{9}\b", RegexOptions.Compiled);
        private static readonly Regex SteamIdRegex = new(@"\b7656\d{13}\b", RegexOptions.Compiled);
        private static readonly HashSet<string> CoalescedSources = new(StringComparer.OrdinalIgnoreCase)
        {
            "SteamDT",
            "QAQ",
            "Perf",
            "SteamOffer",
            "SteamDTItem",
            "RefreshPipeline"
        };
        private static readonly Dictionary<string, DuplicateLogState> DuplicateLogStates = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, DateTime> ThrottledInfoStates = new(StringComparer.Ordinal);
        private static readonly TimeSpan DuplicateLogWindow = TimeSpan.FromMinutes(1);
        private const long MaxLogBytes = 2 * 1024 * 1024;
        public const string LogFileName = "CS2TradeMonitor_Error.log";
        public const string SafeCopyNotice = "本日志不含任何敏感信息 可随意复制转发";
        public static string LogDirectory => LogDirectoryLazy.Value;
        public static string LogFilePath => Path.Combine(LogDirectory, LogFileName);

        private sealed class DuplicateLogState
        {
            public DateTime LastWritten { get; set; }
            public int Suppressed { get; set; }
        }

        public static string GetDiagnosticFilePath(string fileName)
        {
            string safeName = Path.GetFileName(fileName);
            return Path.Combine(LogDirectory, string.IsNullOrWhiteSpace(safeName) ? LogFileName : safeName);
        }

        public static string EnsureLogFile()
        {
            string logPath = LogFilePath;
            try
            {
                lock (LockObj)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? RuntimeDataPaths.LogsDirectory);
                    string existing = File.Exists(logPath)
                        ? File.ReadAllText(logPath, Encoding.UTF8)
                        : string.Empty;
                    string normalized = EnsureSafeCopyNotice(existing);
                    if (!File.Exists(logPath) || !string.Equals(existing, normalized, StringComparison.Ordinal))
                        File.WriteAllText(logPath, normalized, new UTF8Encoding(false));
                }
            }
            catch
            {
                // Diagnostics must never affect the monitor itself.
            }

            return logPath;
        }

        public static void Info(string source, string message)
        {
            Write(source, message);
        }

        public static void InfoEvent(
            string source,
            string eventName,
            string message,
            IReadOnlyDictionary<string, object?>? data = null)
        {
            Write(
                source,
                message,
                detailedEvent: eventName,
                detailedData: data);
        }

        public static void WarningEvent(
            string source,
            string eventName,
            string message,
            IReadOnlyDictionary<string, object?>? data = null)
        {
            Write(
                source,
                message,
                detailedLevel: "Warning",
                detailedEvent: eventName,
                detailedPriority: DetailedDiagnosticPriority.Critical,
                detailedData: data);
        }

        public static void InfoThrottled(string source, string key, string message, TimeSpan window)
        {
            if (string.IsNullOrWhiteSpace(key) || window <= TimeSpan.Zero)
            {
                Info(source, message);
                return;
            }

            string safeSource = string.IsNullOrWhiteSpace(source) ? "General" : source.Trim();
            string stateKey = safeSource + "\u001f" + key.Trim();
            var now = DateTime.Now;
            lock (LockObj)
            {
                if (ThrottledInfoStates.TryGetValue(stateKey, out var last) && now - last < window)
                    return;

                ThrottledInfoStates[stateKey] = now;
            }

            Info(safeSource, message);
        }

        public static void Error(string source, string message, Exception? ex = null)
        {
            if (ex == null)
            {
                Write(
                    source,
                    message,
                    detailedLevel: "Error",
                    detailedEvent: "Error",
                    detailedPriority: DetailedDiagnosticPriority.Critical);
                return;
            }

            WriteBlock(source, message, ex);
        }

        public static void ErrorEvent(
            string source,
            string eventName,
            string message,
            IReadOnlyDictionary<string, object?>? data = null,
            Exception? exception = null)
        {
            if (exception is null)
            {
                Write(
                    source,
                    message,
                    detailedLevel: "Error",
                    detailedEvent: eventName,
                    detailedPriority: DetailedDiagnosticPriority.Critical,
                    detailedData: data);
                return;
            }

            WriteBlock(source, message, exception, eventName, data);
        }

        public static void Ignored(string source, string operation, Exception ex, bool retryable = false, string category = "BestEffort")
        {
            string safeOperation = string.IsNullOrWhiteSpace(operation) ? "UnknownOperation" : operation.Trim();
            string safeCategory = string.IsNullOrWhiteSpace(category) ? "BestEffort" : category.Trim();
            Error(
                source,
                $"Suppressed exception. Operation={safeOperation}; Category={safeCategory}; Retryable={retryable}",
                ex);
        }

        public static void Ignored(Exception ex, bool retryable = false, string category = "BestEffort", [CallerMemberName] string operation = "")
        {
            Ignored("SuppressedException", operation, ex, retryable, category);
        }

        private static void Write(
            string source,
            string message,
            string detailedLevel = "Information",
            string detailedEvent = "Log",
            DetailedDiagnosticPriority detailedPriority = DetailedDiagnosticPriority.Normal,
            IReadOnlyDictionary<string, object?>? detailedData = null)
        {
            try
            {
                string logPath = LogFilePath;
                string safeSource = Sanitize(source);
                string safeMessage = Sanitize(message);
                var now = DateTime.Now;
                string line = $"[{now:yyyy-MM-dd HH:mm:ss}] [{safeSource}] {safeMessage}{Environment.NewLine}";
                bool written;
                lock (LockObj)
                {
                    string? text = BuildCoalescedText(safeSource, safeMessage, line, now);
                    written = !string.IsNullOrEmpty(text);
                    if (!string.IsNullOrEmpty(text))
                        AppendText(logPath, text);
                }
                if (written)
                {
                    DetailedDiagnosticsRuntime.Record(
                        detailedLevel,
                        safeSource,
                        detailedEvent,
                        BuildDetailedData(safeMessage, detailedData),
                        priority: detailedPriority);
                }
            }
            catch
            {
                // Diagnostics must never affect the monitor itself.
            }
        }

        private static void WriteBlock(
            string source,
            string message,
            Exception ex,
            string detailedEvent = "Exception",
            IReadOnlyDictionary<string, object?>? detailedData = null)
        {
            try
            {
                string logPath = LogFilePath;
                string safeSource = Sanitize(source);
                string safeMessage = Sanitize(message);
                string safeException = Sanitize(ex.ToString());
                string block =
                    "--------------------------------------------------" + Environment.NewLine +
                    $"[Time]: {DateTime.Now:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine +
                    $"[Source]: {safeSource}" + Environment.NewLine +
                    $"[Message]: {safeMessage}" + Environment.NewLine +
                    $"[Exception]:{Environment.NewLine}{safeException}" + Environment.NewLine +
                    "--------------------------------------------------" + Environment.NewLine;

                lock (LockObj)
                {
                    AppendText(logPath, block);
                }
                Dictionary<string, object?> payload = BuildDetailedData(safeMessage, detailedData);
                payload["exceptionType"] = ex.GetType().FullName ?? ex.GetType().Name;
                payload["exception"] = safeException;
                payload["hresult"] = ex.HResult;
                DetailedDiagnosticsRuntime.Record(
                    "Error",
                    safeSource,
                    detailedEvent,
                    payload,
                    priority: DetailedDiagnosticPriority.Critical);
            }
            catch
            {
                // Diagnostics must never affect the monitor itself.
            }
        }

        private static Dictionary<string, object?> BuildDetailedData(
            string message,
            IReadOnlyDictionary<string, object?>? data)
        {
            var payload = data is null
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?>(data, StringComparer.Ordinal);
            payload["message"] = message;
            return payload;
        }

        private static string? BuildCoalescedText(string source, string message, string line, DateTime now)
        {
            if (!CoalescedSources.Contains(source))
                return line;

            string key = source + "\u001f" + message;
            if (DuplicateLogStates.TryGetValue(key, out var state))
            {
                if (now - state.LastWritten < DuplicateLogWindow)
                {
                    state.Suppressed++;
                    return null;
                }

                string text = line;
                if (state.Suppressed > 0)
                {
                    text += $"[{now:yyyy-MM-dd HH:mm:ss}] [{source}] 上一条相同日志已合并 {state.Suppressed} 次{Environment.NewLine}";
                    state.Suppressed = 0;
                }

                state.LastWritten = now;
                CleanupDuplicateStates(now);
                return text;
            }

            DuplicateLogStates[key] = new DuplicateLogState { LastWritten = now };
            CleanupDuplicateStates(now);
            return line;
        }

        private static void CleanupDuplicateStates(DateTime now)
        {
            if (DuplicateLogStates.Count <= 256)
                return;

            var staleKeys = new List<string>();
            foreach (var pair in DuplicateLogStates)
            {
                if (pair.Value.Suppressed == 0 && now - pair.Value.LastWritten > TimeSpan.FromHours(1))
                    staleKeys.Add(pair.Key);
            }

            foreach (string key in staleKeys)
                DuplicateLogStates.Remove(key);
        }

        public static void PrependRaw(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            try
            {
                string logPath = LogFilePath;
                lock (LockObj)
                {
                    AppendText(logPath, Sanitize(text));
                }
            }
            catch
            {
                // Diagnostics must never affect the monitor itself.
            }
        }

        private static void AppendText(string logPath, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? RuntimeDataPaths.LogsDirectory);
            string normalizedText = NormalizeLogEntryText(text);
            RotateIfNeeded(logPath, Encoding.UTF8.GetByteCount(normalizedText) + Encoding.UTF8.GetByteCount(SafeCopyNotice + Environment.NewLine));
            EnsureLogFileHeader(logPath);

            File.AppendAllText(logPath, normalizedText, new UTF8Encoding(false));
        }

        private static void EnsureLogFileHeader(string logPath)
        {
            if (!File.Exists(logPath) || new FileInfo(logPath).Length == 0)
            {
                File.WriteAllText(logPath, SafeCopyNotice + Environment.NewLine, new UTF8Encoding(false));
                return;
            }

            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
            string? firstLine = reader.ReadLine();
            if (string.Equals(firstLine, SafeCopyNotice, StringComparison.Ordinal))
                return;

            string existing = File.ReadAllText(logPath, Encoding.UTF8);
            string tempPath = logPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tempPath, EnsureSafeCopyNotice(existing), new UTF8Encoding(false));
                File.Move(tempPath, logPath, overwrite: true);
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
                    // Best-effort cleanup only.
                }
            }
        }

        private static string NormalizeLogEntryText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return text.EndsWith(Environment.NewLine, StringComparison.Ordinal)
                ? text
                : text + Environment.NewLine;
        }

        private static string EnsureSafeCopyNotice(string text)
        {
            string body = Sanitize(RemoveSafeCopyNotice(text));
            return SafeCopyNotice + Environment.NewLine + body;
        }

        private static string RemoveSafeCopyNotice(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            using var reader = new StringReader(text);
            string? firstLine = reader.ReadLine();
            if (!string.Equals(firstLine, SafeCopyNotice, StringComparison.Ordinal))
                return text;

            return reader.ReadToEnd();
        }

        private static void RotateIfNeeded(string logPath, int incomingBytes)
        {
            try
            {
                var info = new FileInfo(logPath);
                if (!info.Exists || info.Length + incomingBytes < MaxLogBytes) return;

                string archive1 = logPath + ".1";
                string archive2 = logPath + ".2";
                string archive3 = logPath + ".3";

                if (File.Exists(archive3)) File.Delete(archive3);
                if (File.Exists(archive2)) File.Move(archive2, archive3);
                if (File.Exists(archive1)) File.Move(archive1, archive2);

                File.Move(logPath, archive1);
            }
            catch
            {
                try { File.WriteAllText(logPath, SafeCopyNotice + Environment.NewLine, new UTF8Encoding(false)); } catch { /* 日志文件不可写时没有可靠日志目标，只能放弃本次轮转。 */ }
            }
        }

        public static string Redact(string? text) => Sanitize(text);

        private static string ResolveLogDirectory()
        {
            return _testLogDirectory ?? RuntimeDataPaths.LogsDirectory;
        }

        internal static void ConfigureLogDirectoryForTests(string directory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            if (LogDirectoryLazy.IsValueCreated)
                throw new InvalidOperationException("日志目录已经初始化，不能再配置测试目录。");

            _testLogDirectory = Path.GetFullPath(directory);
        }

        private static string Sanitize(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            string redacted = BearerRegex.Replace(text, "$1[REDACTED]");
            redacted = SensitivePairRegex.Replace(redacted, "$1$2[REDACTED]");
            redacted = PhoneRegex.Replace(redacted, "[REDACTED_PHONE]");
            redacted = SteamIdRegex.Replace(redacted, "[REDACTED_STEAMID]");
            return redacted;
        }
    }
}
