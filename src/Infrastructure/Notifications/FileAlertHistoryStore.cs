using System.Text.Json;
using System.Text.RegularExpressions;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Monitoring;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Infrastructure.Notifications
{
    internal sealed class FileAlertHistoryStore : IAlertHistoryStore
    {
        internal const int MaximumEntries = 500;
        private const string FileName = "alert-history.json";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };
        private static readonly Regex SecretPairRegex = new(
            @"(?i)\b(sendkey|token|cookie|authorization|secret|password|session)\s*[:=]\s*[^\s;，。]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex EmailRegex = new(
            @"(?i)(?<![\w.])[\w.+-]+@[\w.-]+\.[a-z]{2,}(?![\w.])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex PhoneRegex = new(
            @"(?<!\d)1[3-9]\d{9}(?!\d)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly string _path;
        private readonly Action<string, string> _writeTextAtomic;
        private readonly Func<DateTimeOffset> _now;
        private readonly object _sync = new();

        public FileAlertHistoryStore(IAppDataPathProvider pathProvider)
            : this(
                pathProvider?.GetDataFilePath(FileName)
                    ?? throw new ArgumentNullException(nameof(pathProvider)),
                RuntimeDataPaths.WriteTextAtomic,
                () => DateTimeOffset.Now)
        {
        }

        internal FileAlertHistoryStore(
            string path,
            Action<string, string> writeTextAtomic,
            Func<DateTimeOffset> now)
        {
            _path = string.IsNullOrWhiteSpace(path)
                ? throw new ArgumentException("提醒历史路径不能为空。", nameof(path))
                : path;
            _writeTextAtomic = writeTextAtomic ?? throw new ArgumentNullException(nameof(writeTextAtomic));
            _now = now ?? throw new ArgumentNullException(nameof(now));
        }

        public Task AppendAsync(AlertHistoryEntry entry, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            return Task.Run(() => AppendCore(entry, cancellationToken), cancellationToken);
        }

        public Task<IReadOnlyList<AlertHistoryEntry>> ReadLatestAsync(
            int limit,
            CancellationToken cancellationToken = default)
        {
            int normalizedLimit = Math.Clamp(limit, 1, MaximumEntries);
            return Task.Run<IReadOnlyList<AlertHistoryEntry>>(
                () => ReadLatestCore(normalizedLimit, cancellationToken),
                cancellationToken);
        }

        private void AppendCore(AlertHistoryEntry entry, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    var entries = LoadCore();
                    entries.Add(Sanitize(entry, _now()));
                    entries = entries
                        .OrderBy(item => item.OccurredAt)
                        .TakeLast(MaximumEntries)
                        .ToList();
                    _writeTextAtomic(_path, JsonSerializer.Serialize(entries, JsonOptions));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored(
                    "AlertHistory",
                    "Append",
                    ex,
                    retryable: true,
                    category: "Storage");
            }
        }

        private IReadOnlyList<AlertHistoryEntry> ReadLatestCore(int limit, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    return LoadCore()
                        .OrderByDescending(item => item.OccurredAt)
                        .Take(limit)
                        .ToArray();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Array.Empty<AlertHistoryEntry>();
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored(
                    "AlertHistory",
                    "ReadLatest",
                    ex,
                    retryable: true,
                    category: "Storage");
                return Array.Empty<AlertHistoryEntry>();
            }
        }

        private List<AlertHistoryEntry> LoadCore()
        {
            if (!File.Exists(_path))
                return new List<AlertHistoryEntry>();

            try
            {
                string json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<List<AlertHistoryEntry>>(json, JsonOptions)
                    ?? new List<AlertHistoryEntry>();
            }
            catch (Exception ex)
            {
                BackupCorruptFile();
                DiagnosticsLogger.Ignored(
                    "AlertHistory",
                    "LoadCorruptHistory",
                    ex,
                    retryable: false,
                    category: "Storage");
                return new List<AlertHistoryEntry>();
            }
        }

        private void BackupCorruptFile()
        {
            try
            {
                if (!File.Exists(_path))
                    return;

                string directory = Path.GetDirectoryName(_path) ?? RuntimeDataPaths.DataDirectory;
                string stem = Path.GetFileName(_path) + ".corrupt_" + _now().ToString("yyyyMMdd_HHmmss");
                string backupPath = Path.Combine(directory, stem);
                int suffix = 1;
                while (File.Exists(backupPath))
                {
                    backupPath = Path.Combine(directory, stem + "_" + suffix.ToString("00"));
                    suffix++;
                }

                File.Move(_path, backupPath);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored(
                    "AlertHistory",
                    "BackupCorruptHistory",
                    ex,
                    retryable: true,
                    category: "Storage");
            }
        }

        private static AlertHistoryEntry Sanitize(AlertHistoryEntry entry, DateTimeOffset now)
        {
            return entry with
            {
                Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : Trim(entry.Id, 64),
                OccurredAt = entry.OccurredAt == default ? now : entry.OccurredAt,
                Source = SanitizeText(entry.Source, 48),
                EventType = SanitizeText(entry.EventType, 48),
                Title = SanitizeText(entry.Title, 96),
                Summary = SanitizeText(entry.Summary, 240),
                Channel = SanitizeText(entry.Channel, 32),
                Detail = SanitizeText(entry.Detail, 160)
            };
        }

        internal static string SanitizeText(string? value, int maximumLength)
        {
            string text = DiagnosticsLogger.Redact(value ?? string.Empty);
            text = SecretPairRegex.Replace(text, "$1=[REDACTED]");
            text = EmailRegex.Replace(text, "[REDACTED_EMAIL]");
            text = PhoneRegex.Replace(text, "[REDACTED_PHONE]");
            text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return Trim(text, maximumLength);
        }

        private static string Trim(string value, int maximumLength)
        {
            if (value.Length <= maximumLength)
                return value;

            return value[..Math.Max(0, maximumLength - 3)] + "...";
        }
    }
}
