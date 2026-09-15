using System.Text.Json;
using YouPinPurchaseMonitor.Domain;
using YouPinPurchaseMonitor.Models;

namespace YouPinPurchaseMonitor.Infrastructure;

/// <summary>Indexes report metadata; reads event payloads only for the requested page.</summary>
public sealed class StoreActivityArchive
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Dictionary<(Guid, DateTimeOffset), IndexedBatch> _index = [];
    private readonly Dictionary<Guid, (DateTimeOffset Time, string Note)> _watchNotes = [];
    private readonly string _statePath;
    private PreferenceDocument _state = new(DateTimeOffset.UtcNow, []);

    public StoreActivityArchive(string applicationDirectory)
        => _statePath = Path.Combine(applicationDirectory, "activity-preferences.json");

    public async Task InitializeAsync(string reportDirectory, CancellationToken cancellationToken = default)
    {
        lock (_sync) { _index.Clear(); _watchNotes.Clear(); }
        if (File.Exists(_statePath))
        {
            if (new FileInfo(_statePath).Length > 1024 * 1024)
                throw new InvalidDataException("店铺动态设置超过大小上限。");
            string json = await File.ReadAllTextAsync(_statePath, cancellationToken).ConfigureAwait(false);
            PreferenceDocument document = JsonSerializer.Deserialize<PreferenceDocument>(json)
                ?? throw new InvalidDataException("店铺动态设置为空。");
            if (document.ImportedThrough == default || document.Stores is null || document.Stores.Count > 10000
                || document.Stores.Any(item => item.Key == Guid.Empty || item.Value is null || item.Value.ReadThrough == default))
                throw new InvalidDataException("店铺动态设置无效；未覆盖原文件。");
            lock (_sync) _state = document;
        }
        else
        {
            await SaveAsync(_state, cancellationToken).ConfigureAwait(false);
        }
        string directory = Path.Combine(reportDirectory, "changes");
        if (!Directory.Exists(directory)) return;
        foreach (string path in Directory.EnumerateFiles(directory, "coverage-tick-*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (path.EndsWith(".core.json", StringComparison.OrdinalIgnoreCase)) continue;
            ChangeBatchView? batch = await ChangeReportStore.ReadReportAsync(path, cancellationToken).ConfigureAwait(false);
            if (batch is not null) Record(batch);
        }
    }

    public bool Record(ChangeBatchView batch)
    {
        int[] masks = StoreActivityProjection.Build(batch).Select(activity => activity.Changes
            .Aggregate(0, (mask, change) => mask | 1 << (int)change.Kind)).ToArray();
        lock (_sync)
        {
            bool added = !_index.ContainsKey((batch.WatchId, batch.ObservedAt));
            _index[(batch.WatchId, batch.ObservedAt)] = new IndexedBatch(batch.JsonPath,
                batch.WatchId, batch.ObservedAt, batch.SafeNote, masks);
            if (!_watchNotes.TryGetValue(batch.WatchId, out var previous) || batch.ObservedAt >= previous.Time)
                _watchNotes[batch.WatchId] = (batch.ObservedAt, batch.SafeNote);
            return added;
        }
    }

    public StoreActivityPreferences Preferences(Guid storeId)
    {
        lock (_sync) return _state.Stores.GetValueOrDefault(storeId) ?? new(_state.ImportedThrough);
    }

    public int Count(IReadOnlyCollection<Guid> watchIds, DateTimeOffset since, bool exclusive = false)
    {
        var ids = watchIds.ToHashSet();
        lock (_sync) return _index.Values.Where(item => ids.Contains(item.WatchId)
            && (exclusive ? item.ObservedAt > since : item.ObservedAt >= since)).Sum(item => item.Masks.Length);
    }

    public IReadOnlyDictionary<Guid, string> WatchNotes()
    {
        lock (_sync) return _watchNotes.ToDictionary(item => item.Key, item => item.Value.Note);
    }

    public async Task UpdatePreferencesAsync(Guid storeId, DateTimeOffset? readThrough = null,
        bool? notificationsEnabled = null, CancellationToken cancellationToken = default)
    {
        if (storeId == Guid.Empty) throw new ArgumentException("店铺标识不能为空。", nameof(storeId));
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StoreActivityPreferences current = Preferences(storeId);
            var next = current with
            {
                ReadThrough = readThrough is { } time && time > current.ReadThrough ? time : current.ReadThrough,
                NotificationsEnabled = notificationsEnabled ?? current.NotificationsEnabled
            };
            PreferenceDocument document;
            lock (_sync) document = _state with { Stores = new Dictionary<Guid, StoreActivityPreferences>(_state.Stores) { [storeId] = next } };
            await SaveAsync(document, cancellationToken).ConfigureAwait(false);
            lock (_sync) _state = document;
        }
        finally { _writeGate.Release(); }
    }

    public async Task<StoreActivityPage> QueryAsync(StoreActivityQuery query, CancellationToken cancellationToken = default)
    {
        HashSet<Guid>? ids = query.WatchIds?.ToHashSet();
        int mask = query.Kinds?.Aggregate(0, (value, kind) => value | 1 << (int)kind) ?? 0;
        IndexedBatch[] batches;
        lock (_sync) batches = _index.Values.ToArray();
        batches = batches.Where(item => (ids is null || ids.Contains(item.WatchId))
                && item.ObservedAt >= query.Since && item.Masks.Any(value => mask == 0 || (value & mask) != 0))
            .OrderByDescending(item => item.ObservedAt).ThenByDescending(item => item.WatchId).ToArray();
        int total = batches.Sum(item => item.Masks.Count(value => mask == 0 || (value & mask) != 0));
        int size = Math.Clamp(query.PageSize, 1, 200);
        var result = new List<StoreActivity>(size + 1);
        foreach (IndexedBatch indexed in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (query.Before is { } cursor && (indexed.ObservedAt > cursor.ObservedAt
                || indexed.ObservedAt == cursor.ObservedAt && indexed.WatchId.CompareTo(cursor.WatchId) > 0)) continue;
            ChangeBatchView? batch = await ChangeReportStore.ReadReportAsync(indexed.Path, cancellationToken).ConfigureAwait(false);
            if (batch is null) continue;
            foreach (StoreActivity activity in StoreActivityProjection.Build(batch))
            {
                if (query.Before is { } before && activity.ObservedAt == before.ObservedAt
                    && activity.WatchId == before.WatchId && activity.Sequence <= before.Sequence) continue;
                if (mask != 0 && !activity.Changes.Any(change => (mask & 1 << (int)change.Kind) != 0)) continue;
                result.Add(activity);
                if (result.Count > size) break;
            }
            if (result.Count > size) break;
        }
        StoreActivityCursor? next = result.Count > size
            ? new(result[size - 1].ObservedAt, result[size - 1].WatchId, result[size - 1].Sequence) : null;
        return new StoreActivityPage(result.Take(size).ToArray(), next, total);
    }

    private async Task SaveAsync(PreferenceDocument document, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        string temporary = _statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(document), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, _statePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record IndexedBatch(string Path, Guid WatchId, DateTimeOffset ObservedAt, string SafeNote, int[] Masks);
    private sealed record PreferenceDocument(DateTimeOffset ImportedThrough, Dictionary<Guid, StoreActivityPreferences> Stores);
}
