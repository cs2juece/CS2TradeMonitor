using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.YouPin;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CS2TradeMonitor.Application.YouPin
{
    internal sealed class YouPinLandlordAuditFileStore : IYouPinLandlordAuditStore
    {
        internal const string FileName = "youpin_landlord_operations.jsonl";
        private const long MaxFileBytes = 2 * 1024 * 1024;
        private const int ArchiveCount = 3;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly string _path;
        private readonly IAppDiagnostics _diagnostics;
        private readonly SemaphoreSlim _ioGate = new(1, 1);
        private readonly object _healthLock = new();
        private YouPinLandlordAuditHealth _health = YouPinLandlordAuditHealth.Healthy;

        public YouPinLandlordAuditFileStore(
            IAppDataPathProvider pathProvider,
            IAppDiagnostics diagnostics)
            : this(
                (pathProvider ?? throw new ArgumentNullException(nameof(pathProvider)))
                    .GetLogFilePath(FileName),
                diagnostics)
        {
        }

        internal YouPinLandlordAuditFileStore(string path, IAppDiagnostics diagnostics)
        {
            _path = string.IsNullOrWhiteSpace(path)
                ? throw new ArgumentException("Audit path is required.", nameof(path))
                : path;
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public async Task AppendAsync(
            YouPinLandlordOperationRecord record,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(record);
            cancellationToken.ThrowIfCancellationRequested();

            bool entered = false;
            try
            {
                YouPinLandlordOperationRecord safeRecord = Sanitize(record);
                string line = JsonSerializer.Serialize(safeRecord, JsonOptions) + Environment.NewLine;
                await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                entered = true;
                EnsureDirectory();
                RotateIfNeeded(Encoding.UTF8.GetByteCount(line));
                await File.AppendAllTextAsync(
                    _path,
                    line,
                    new UTF8Encoding(false),
                    cancellationToken).ConfigureAwait(false);
                lock (_healthLock)
                {
                    _health = new YouPinLandlordAuditHealth(
                        true,
                        DateTime.Now,
                        _health.LastFailureAt,
                        string.Empty);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lock (_healthLock)
                {
                    _health = new YouPinLandlordAuditHealth(
                        false,
                        _health.LastSuccessfulWriteAt,
                        DateTime.Now,
                        Normalize(_diagnostics.Redact(ex.Message)));
                }
                _diagnostics.Ignored(
                    "YouPinLandlord",
                    "AppendAuditRecord",
                    ex,
                    retryable: true,
                    category: "Storage");
            }
            finally
            {
                if (entered)
                    _ioGate.Release();
            }
        }

        public async Task<IReadOnlyList<YouPinLandlordOperationRecord>> ReadRecentAsync(
            int count,
            CancellationToken cancellationToken)
        {
            return await QueryAsync(
                YouPinLandlordAuditQuery.Recent with { Limit = count },
                cancellationToken).ConfigureAwait(false);
        }

        public YouPinLandlordAuditHealth GetHealth()
        {
            lock (_healthLock)
                return _health;
        }

        public async Task<IReadOnlyList<YouPinLandlordOperationRecord>> QueryAsync(
            YouPinLandlordAuditQuery query,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(query);
            int limit = Math.Clamp(query.Limit, 0, 1000);
            if (limit == 0)
                return Array.Empty<YouPinLandlordOperationRecord>();

            bool entered = false;
            try
            {
                await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                entered = true;
                var records = new List<YouPinLandlordOperationRecord>();
                foreach (string path in EnumerateHistoryPaths())
                {
                    if (!File.Exists(path))
                        continue;
                    await using var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite,
                        4096,
                        useAsync: true);
                    using var reader = new StreamReader(
                        stream,
                        Encoding.UTF8,
                        detectEncodingFromByteOrderMarks: true);
                    while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is string line)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            continue;
                        try
                        {
                            YouPinLandlordOperationRecord? record =
                                JsonSerializer.Deserialize<YouPinLandlordOperationRecord>(line, JsonOptions);
                            if (record != null && Matches(record, query))
                                records.Add(record);
                        }
                        catch (JsonException ex)
                        {
                            _diagnostics.InfoThrottled(
                                "YouPinLandlord",
                                "audit-line-corrupt",
                                "包租公操作历史中存在无法解析的单条记录，已跳过。Error="
                                + _diagnostics.Redact(ex.Message),
                                TimeSpan.FromMinutes(10));
                        }
                    }
                }
                return records
                    .OrderByDescending(record => record.Time)
                    .Take(limit)
                    .ToArray();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _diagnostics.Ignored(
                    "YouPinLandlord",
                    "ReadAuditRecords",
                    ex,
                    retryable: true,
                    category: "Storage");
                return Array.Empty<YouPinLandlordOperationRecord>();
            }
            finally
            {
                if (entered)
                    _ioGate.Release();
            }
        }

        private IEnumerable<string> EnumerateHistoryPaths()
        {
            yield return _path;
            for (int index = 1; index <= ArchiveCount; index++)
                yield return _path + "." + index;
        }

        private static bool Matches(
            YouPinLandlordOperationRecord record,
            YouPinLandlordAuditQuery query)
        {
            return (!query.From.HasValue || record.Time >= query.From.Value)
                && (!query.To.HasValue || record.Time <= query.To.Value)
                && (!query.Workflow.HasValue || record.Workflow == query.Workflow.Value)
                && (!query.RentalType.HasValue || record.RentalType == query.RentalType.Value)
                && Contains(record.ItemName, query.ItemName)
                && Contains(record.Result, query.Result)
                && StartsWith(record.RunId, query.RunId)
                && StartsWith(record.ActionId, query.ActionId);
        }

        private static bool Contains(string value, string query)
        {
            return string.IsNullOrWhiteSpace(query)
                || value.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase);
        }

        private static bool StartsWith(string value, string query)
        {
            return string.IsNullOrWhiteSpace(query)
                || value.StartsWith(query.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private YouPinLandlordOperationRecord Sanitize(YouPinLandlordOperationRecord record)
        {
            return record with
            {
                RunId = Normalize(_diagnostics.Redact(record.RunId)),
                ActionId = Normalize(_diagnostics.Redact(record.ActionId)),
                ItemName = Normalize(_diagnostics.Redact(record.ItemName)),
                Result = Normalize(_diagnostics.Redact(record.Result)),
                Message = Normalize(_diagnostics.Redact(record.Message)),
                ResourceKeyHash = Normalize(_diagnostics.Redact(record.ResourceKeyHash))
            };
        }

        private static string Normalize(string? value)
        {
            return (value ?? string.Empty)
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();
        }

        private void EnsureDirectory()
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
        }

        private void RotateIfNeeded(int incomingBytes)
        {
            if (!File.Exists(_path))
                return;

            var info = new FileInfo(_path);
            if (info.Length + incomingBytes <= MaxFileBytes)
                return;

            for (int index = ArchiveCount; index >= 1; index--)
            {
                string current = _path + "." + index;
                if (index == ArchiveCount && File.Exists(current))
                    File.Delete(current);

                string previous = index == 1 ? _path : _path + "." + (index - 1);
                if (File.Exists(previous))
                    File.Move(previous, current, overwrite: true);
            }
        }
    }
}
