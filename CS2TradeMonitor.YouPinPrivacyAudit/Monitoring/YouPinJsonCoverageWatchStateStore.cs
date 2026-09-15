using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring
{
    /// <summary>
    /// Atomic, user-ID-free JSON persistence for Coverage Watch state. File names contain only
    /// the Watch ID and payloads contain only plan, cursor, template baseline, and observation data.
    /// </summary>
    public sealed class YouPinJsonCoverageWatchStateStore : IYouPinCoverageWatchStateStore, IDisposable
    {
        public const int SchemaVersion = 1;
        public const int MaximumStateBytes = 4 * 1024 * 1024;

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        private readonly string _rootDirectory;
        private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);
        private int _disposed;

        public YouPinJsonCoverageWatchStateStore(string rootDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
            _rootDirectory = Path.GetFullPath(rootDirectory);
        }

        public async Task<YouPinCoverageWatchState?> LoadAsync(
            Guid watchId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateWatchId(watchId);
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string path = GetStatePath(watchId);
                if (!File.Exists(path))
                    return null;

                await using FileStream source = new(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
                byte[] bytes = await BoundedInputReader
                    .ReadAllBytesAsync(source, MaximumStateBytes, cancellationToken)
                    .ConfigureAwait(false);
                StateDto? dto;
                try
                {
                    dto = JsonSerializer.Deserialize<StateDto>(bytes, JsonOptions);
                }
                catch (JsonException exception)
                {
                    throw new InvalidDataException("覆盖监控状态不是有效的 JSON。", exception);
                }

                if (dto is null || dto.SchemaVersion != SchemaVersion || dto.WatchId != watchId)
                    throw new InvalidDataException("覆盖监控状态版本或 Watch ID 无效。");
                if (dto.Baselines is null)
                    throw new InvalidDataException("覆盖监控状态缺少模板基线。");

                YouPinTemplateBaseline[] baselines = dto.Baselines
                    .Select(RestoreBaseline)
                    .ToArray();
                return YouPinCoverageWatchState.Restore(
                    dto.WatchId,
                    dto.PlanFingerprint ?? string.Empty,
                    dto.NextCursor,
                    dto.CompletedCoverageCycles,
                    dto.LastTickAt,
                    baselines);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task SaveAsync(
            YouPinCoverageWatchState state,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(state);
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(_rootDirectory);
                string path = GetStatePath(state.WatchId);
                string temporaryPath = Path.Combine(
                    _rootDirectory,
                    $".{state.WatchId:N}.{Guid.NewGuid():N}.tmp");
                try
                {
                    StateDto dto = CreateDto(state);
                    await using (var destination = new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None))
                    {
                        await JsonSerializer.SerializeAsync(
                            destination,
                            dto,
                            JsonOptions,
                            cancellationToken).ConfigureAwait(false);
                        if (destination.Length > MaximumStateBytes)
                            throw new InvalidDataException("覆盖监控状态超过安全大小上限。");
                    }

                    File.Move(temporaryPath, path, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        private static YouPinTemplateBaseline RestoreBaseline(BaselineDto dto)
        {
            if (dto.Observations is null)
                throw new InvalidDataException("模板基线缺少观察记录数组。");
            YouPinPurchaseObservation[] observations = dto.Observations
                .Select(observation => new YouPinPurchaseObservation(
                    dto.TemplateId,
                    observation.CommodityName,
                    observation.PurchasePrice,
                    observation.SurplusQuantity,
                    observation.AbradeText,
                    observation.FadeText,
                    observation.AutoReceived))
                .ToArray();
            return YouPinTemplateBaseline.Restore(
                dto.TemplateId,
                dto.ObservedAt,
                observations);
        }

        private static StateDto CreateDto(YouPinCoverageWatchState state)
            => new()
            {
                SchemaVersion = SchemaVersion,
                WatchId = state.WatchId,
                PlanFingerprint = state.PlanFingerprint,
                NextCursor = state.NextCursor,
                CompletedCoverageCycles = state.CompletedCoverageCycles,
                LastTickAt = state.LastTickAt,
                Baselines = state.Baselines.Select(baseline => new BaselineDto
                {
                    TemplateId = baseline.TemplateId,
                    ObservedAt = baseline.ObservedAt,
                    Observations = baseline.Observations.Select(observation => new ObservationDto
                    {
                        CommodityName = observation.CommodityName,
                        PurchasePrice = observation.PurchasePrice,
                        SurplusQuantity = observation.SurplusQuantity,
                        AbradeText = observation.AbradeText,
                        FadeText = observation.FadeText,
                        AutoReceived = observation.AutoReceived
                    }).ToArray()
                }).ToArray()
            };

        private string GetStatePath(Guid watchId)
            => Path.Combine(_rootDirectory, $"{watchId:N}.json");

        private static void ValidateWatchId(Guid watchId)
        {
            if (watchId == Guid.Empty)
                throw new ArgumentException("监控 ID 不能为空。", nameof(watchId));
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _gate.Dispose();
        }

        private sealed class StateDto
        {
            [JsonPropertyName("schemaVersion")]
            public int SchemaVersion { get; init; }

            [JsonPropertyName("watchId")]
            public Guid WatchId { get; init; }

            [JsonPropertyName("planFingerprint")]
            public string? PlanFingerprint { get; init; }

            [JsonPropertyName("nextCursor")]
            public int NextCursor { get; init; }

            [JsonPropertyName("completedCoverageCycles")]
            public long CompletedCoverageCycles { get; init; }

            [JsonPropertyName("lastTickAt")]
            public DateTimeOffset? LastTickAt { get; init; }

            [JsonPropertyName("baselines")]
            public IReadOnlyList<BaselineDto>? Baselines { get; init; }
        }

        private sealed class BaselineDto
        {
            [JsonPropertyName("templateId")]
            public long TemplateId { get; init; }

            [JsonPropertyName("observedAt")]
            public DateTimeOffset ObservedAt { get; init; }

            [JsonPropertyName("observations")]
            public IReadOnlyList<ObservationDto>? Observations { get; init; }
        }

        private sealed class ObservationDto
        {
            [JsonPropertyName("commodityName")]
            public string? CommodityName { get; init; }

            [JsonPropertyName("purchasePrice")]
            public decimal? PurchasePrice { get; init; }

            [JsonPropertyName("surplusQuantity")]
            public int? SurplusQuantity { get; init; }

            [JsonPropertyName("abradeText")]
            public string? AbradeText { get; init; }

            [JsonPropertyName("fadeText")]
            public string? FadeText { get; init; }

            [JsonPropertyName("autoReceived")]
            public bool? AutoReceived { get; init; }
        }
    }
}
