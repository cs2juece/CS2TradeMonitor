using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.SystemServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CS2TradeMonitor.Application.YouPin
{
    internal sealed class YouPinGridExecutionJournalFileStore : IYouPinGridExecutionJournal
    {
        internal const string FileName = "youpin_grid_execution_journal.json";
        internal const int MaximumRecordCount = 200;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly string _path;
        private readonly IAppDiagnostics _diagnostics;
        private readonly object _sync = new();
        private YouPinGridExecutionJournalState _state;
        private bool _loadFailed;

        public YouPinGridExecutionJournalFileStore(
            IAppDataPathProvider pathProvider,
            IAppDiagnostics diagnostics)
            : this(
                (pathProvider ?? throw new ArgumentNullException(nameof(pathProvider))).GetDataFilePath(FileName),
                diagnostics)
        {
        }

        internal YouPinGridExecutionJournalFileStore(string path, IAppDiagnostics diagnostics)
        {
            _path = string.IsNullOrWhiteSpace(path)
                ? throw new ArgumentException("Grid execution journal path is required.", nameof(path))
                : path;
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _state = Load();
        }

        public YouPinGridExecutionRecord? FindActive(string strategyId)
        {
            string id = (strategyId ?? string.Empty).Trim();
            if (id.Length == 0)
                return null;

            lock (_sync)
            {
                if (_loadFailed)
                    return CreateLoadFailureRecord(id);
                YouPinGridExecutionRecord? record = _state.Records
                    .Where(item => string.Equals(item.StrategyId, id, StringComparison.Ordinal))
                    .Where(item => item.Stage is YouPinGridExecutionStage.Prepared
                        or YouPinGridExecutionStage.AwaitingSettlement
                        or YouPinGridExecutionStage.RequiresManualReview)
                    .OrderByDescending(item => item.UpdatedAt)
                    .FirstOrDefault();
                return record == null ? null : Clone(record);
            }
        }

        public YouPinGridExecutionRecord? FindLatest(string strategyId)
        {
            string id = (strategyId ?? string.Empty).Trim();
            if (id.Length == 0)
                return null;

            lock (_sync)
            {
                if (_loadFailed)
                    return CreateLoadFailureRecord(id);
                YouPinGridExecutionRecord? record = _state.Records
                    .Where(item => string.Equals(item.StrategyId, id, StringComparison.Ordinal))
                    .OrderByDescending(item => item.UpdatedAt)
                    .ThenByDescending(item => item.CreatedAt)
                    .FirstOrDefault();
                return record == null ? null : Clone(record);
            }
        }

        public bool Save(YouPinGridExecutionRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (string.IsNullOrWhiteSpace(record.Id)
                || string.IsNullOrWhiteSpace(record.StrategyId)
                || record.Action is not (YouPinGridAction.Buy or YouPinGridAction.Sell)
                || record.Stage == YouPinGridExecutionStage.None
                || !Enum.IsDefined(typeof(YouPinGridExecutionStage), record.Stage)
                || record.Quantity <= 0)
            {
                return false;
            }

            lock (_sync)
            {
                if (_loadFailed)
                    return false;
                YouPinGridExecutionJournalState candidate = CloneState(_state);
                YouPinGridExecutionRecord copy = Clone(record);
                int index = candidate.Records.FindIndex(item =>
                    string.Equals(item.Id, copy.Id, StringComparison.Ordinal));
                if (index >= 0)
                    candidate.Records[index] = copy;
                else
                    candidate.Records.Add(copy);

                candidate.Records = TrimRecords(candidate.Records);
                if (!Persist(candidate))
                    return false;

                _state = candidate;
                return true;
            }
        }

        private YouPinGridExecutionJournalState Load()
        {
            try
            {
                if (!File.Exists(_path))
                    return new YouPinGridExecutionJournalState();

                string json = File.ReadAllText(_path);
                using (JsonDocument document = JsonDocument.Parse(json))
                    ValidateDocument(document.RootElement);
                YouPinGridExecutionJournalState state =
                    JsonSerializer.Deserialize<YouPinGridExecutionJournalState>(json, JsonOptions)
                    ?? throw new InvalidDataException("Grid execution journal root cannot be null.");
                ValidateState(state);
                return CloneState(state);
            }
            catch (Exception ex)
            {
                _loadFailed = true;
                _diagnostics.Ignored(
                    "YouPinGrid",
                    "LoadExecutionJournal",
                    ex,
                    retryable: true,
                    category: "Storage");
                return new YouPinGridExecutionJournalState();
            }
        }

        private bool Persist(YouPinGridExecutionJournalState state)
        {
            try
            {
                string json = JsonSerializer.Serialize(state, JsonOptions);
                RuntimeDataPaths.WriteTextAtomic(_path, json);
                return true;
            }
            catch (Exception ex)
            {
                _diagnostics.Ignored(
                    "YouPinGrid",
                    "SaveExecutionJournal",
                    ex,
                    retryable: true,
                    category: "Storage");
                return false;
            }
        }

        private static List<YouPinGridExecutionRecord> TrimRecords(
            IEnumerable<YouPinGridExecutionRecord> records)
        {
            YouPinGridExecutionRecord[] active = records
                .Where(item => IsActiveStage(item.Stage))
                .OrderByDescending(item => item.UpdatedAt)
                .ToArray();
            int terminalCapacity = Math.Max(0, MaximumRecordCount - active.Length);
            return active
                .Concat(records
                    .Where(item => !IsActiveStage(item.Stage))
                    .OrderByDescending(item => item.UpdatedAt)
                    .Take(terminalCapacity))
                .ToList();
        }

        private static bool IsActiveStage(YouPinGridExecutionStage stage)
        {
            return stage is YouPinGridExecutionStage.Prepared
                or YouPinGridExecutionStage.AwaitingSettlement
                or YouPinGridExecutionStage.RequiresManualReview;
        }

        private static void ValidateDocument(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(nameof(YouPinGridExecutionJournalState.SchemaVersion), out JsonElement schema)
                || schema.ValueKind != JsonValueKind.Number
                || !root.TryGetProperty(nameof(YouPinGridExecutionJournalState.Records), out JsonElement records)
                || records.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Grid execution journal structure is invalid.");
            }
        }

        private static void ValidateState(YouPinGridExecutionJournalState state)
        {
            if (state.SchemaVersion != 1 || state.Records == null)
                throw new InvalidDataException("Grid execution journal schema is unsupported.");

            bool invalidRecord = state.Records.Any(record =>
                record == null
                || string.IsNullOrWhiteSpace(record.Id)
                || string.IsNullOrWhiteSpace(record.StrategyId)
                || record.Action is not (YouPinGridAction.Buy or YouPinGridAction.Sell)
                || record.Stage == YouPinGridExecutionStage.None
                || !Enum.IsDefined(typeof(YouPinGridExecutionStage), record.Stage)
                || record.Quantity <= 0);
            if (invalidRecord)
                throw new InvalidDataException("Grid execution journal contains an invalid record.");
        }

        private static YouPinGridExecutionRecord CreateLoadFailureRecord(string strategyId)
        {
            return new YouPinGridExecutionRecord
            {
                Id = "journal-load-failure",
                StrategyId = strategyId,
                Stage = YouPinGridExecutionStage.RequiresManualReview,
                Message = "本地网格执行日志读取失败，已停止自动交易以防重复下单"
            };
        }

        private static YouPinGridExecutionJournalState CloneState(YouPinGridExecutionJournalState state)
        {
            return new YouPinGridExecutionJournalState
            {
                SchemaVersion = state.SchemaVersion,
                Records = (state.Records ?? new List<YouPinGridExecutionRecord>())
                    .Where(item => item != null)
                    .Select(Clone)
                    .ToList()
            };
        }

        private static YouPinGridExecutionRecord Clone(YouPinGridExecutionRecord value)
        {
            return new YouPinGridExecutionRecord
            {
                Id = value.Id,
                StrategyId = value.StrategyId,
                TemplateId = value.TemplateId,
                Fingerprint = value.Fingerprint,
                Action = value.Action,
                Stage = value.Stage,
                Quantity = value.Quantity,
                UnitPrice = value.UnitPrice,
                TriggerPrice = value.TriggerPrice,
                TargetReference = value.TargetReference,
                RemoteReference = value.RemoteReference,
                Message = value.Message,
                CreatedAt = value.CreatedAt,
                UpdatedAt = value.UpdatedAt
            };
        }
    }
}
