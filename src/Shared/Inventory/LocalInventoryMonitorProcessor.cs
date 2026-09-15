using CS2TradeMonitor.Application.Inventory;
using CS2TradeMonitor.Domain.InventoryMonitoring;

namespace CS2TradeMonitor.Shared.Inventory;

/// <summary>
/// The single desktop-authoritative rule engine for local inventory baselines,
/// event de-duplication, thresholds, retention, projections and alert text.
/// Windows and Android own only scheduling, persistence and presentation.
/// </summary>
public sealed class LocalInventoryMonitorProcessor
{
    public LocalInventoryMonitorProcessor(LocalInventoryMonitorHistory history)
    {
        History = history ?? throw new ArgumentNullException(nameof(history));
        Normalize(History);
    }

    public LocalInventoryMonitorHistory History { get; }

    public LocalInventoryStoredTarget GetOrCreateTarget(string steamId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(steamId);
        LocalInventoryStoredTarget? target = History.Targets.FirstOrDefault(item =>
            string.Equals(item.SteamId, steamId, StringComparison.Ordinal));
        if (target is not null)
            return target;

        target = new LocalInventoryStoredTarget { SteamId = steamId.Trim() };
        History.Targets.Add(target);
        return target;
    }

    public bool PruneRemovedTargets(IReadOnlyList<LocalInventoryWatchTargetSpec> specs)
    {
        ArgumentNullException.ThrowIfNull(specs);
        var configuredIds = new HashSet<string>(
            specs.Select(item => item.SteamId),
            StringComparer.Ordinal);
        return History.Targets.RemoveAll(item => !configuredIds.Contains(item.SteamId)) > 0;
    }

    public IReadOnlyList<LocalInventoryChangeEvent> ProcessTargetResult(
        LocalInventoryStoredTarget stored,
        CsqaqInventoryTargetRecord resolved,
        IReadOnlyList<LocalInventoryChangeEvent> events,
        int minimumChangeCount,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(events);

        var seen = new HashSet<string>(stored.SeenEventKeys, StringComparer.Ordinal);
        LocalInventoryChangeEvent[] unseen = events
            .Where(item => !seen.Contains(item.EventKey))
            .OrderBy(item => item.OccurredAt)
            .ToArray();
        LocalInventoryChangeEvent[] alerts = [];
        if (stored.BaselineEstablished)
        {
            int threshold = Math.Clamp(minimumChangeCount, 1, 100000);
            alerts = unseen
                .Where(item => item.Kind != LocalInventoryChangeKind.Baseline && item.Count >= threshold)
                .ToArray();
            History.RecentEvents.AddRange(unseen.Where(item => item.Kind != LocalInventoryChangeKind.Baseline));
        }

        foreach (LocalInventoryChangeEvent item in events)
            seen.Add(item.EventKey);

        stored.TaskId = resolved.TaskId;
        stored.SteamName = resolved.SteamName;
        stored.TotalItemCount = resolved.TotalItemCount;
        stored.UpdatedAt = resolved.UpdatedAt;
        stored.LastChangedAt = resolved.LastChangedAt;
        stored.LastCheckedAt = now;
        stored.Status = stored.BaselineEstablished
            ? unseen.Length == 0 ? "无新异动" : $"发现 {unseen.Length} 条新异动"
            : "基线已建立";
        stored.Error = string.Empty;
        stored.BaselineEstablished = true;
        stored.SeenEventKeys = seen.TakeLast(500).ToList();
        Normalize(History);
        return alerts;
    }

    public LocalInventoryMonitorSnapshot CreateSnapshot(
        bool enabled,
        string status,
        string error,
        DateTimeOffset? lastRefreshAt,
        string? watchList)
    {
        IReadOnlyDictionary<string, string> aliases = LocalInventoryWatchListParser
            .Parse(watchList)
            .ToDictionary(item => item.SteamId, item => item.Alias, StringComparer.Ordinal);
        LocalInventoryTargetSnapshot[] targets = History.Targets
            .Select(item => new LocalInventoryTargetSnapshot(
                item.TaskId,
                item.SteamId,
                ResolveDisplayName(item, aliases),
                item.SteamName,
                item.TotalItemCount,
                item.LastChangedAt,
                item.LastCheckedAt,
                item.Status,
                item.Error))
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        LocalInventoryChangeEvent[] events = History.RecentEvents
            .OrderByDescending(item => item.OccurredAt)
            .Take(100)
            .ToArray();
        return new LocalInventoryMonitorSnapshot(
            enabled,
            status,
            error,
            lastRefreshAt,
            targets,
            events);
    }

    public LocalInventoryAlertBatch CreateAlertBatch(
        IReadOnlyList<LocalInventoryChangeEvent> events,
        IReadOnlyList<LocalInventoryWatchTargetSpec> watchList)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(watchList);
        IReadOnlyDictionary<string, string> aliases = watchList
            .ToDictionary(item => item.SteamId, item => item.Alias, StringComparer.Ordinal);
        LocalInventoryChangeEvent[] ordered = events
            .OrderByDescending(item => item.OccurredAt)
            .ToArray();
        string title = ordered.Length == 1
            ? "大商库存异动提醒"
            : $"大商库存异动提醒（{ordered.Length} 条）";
        string message = string.Join(
            Environment.NewLine,
            ordered.Take(5).Select(item =>
                $"{ResolveEventTargetName(item, aliases)} · {item.KindText} {item.Count} 件 · {item.MarketName}"));
        if (ordered.Length > 5)
            message += Environment.NewLine + $"另有 {ordered.Length - 5} 条异动，请在本机库存监控页面查看。";
        string deduplicationKey = "LocalInventory:" + string.Join(
            ',',
            ordered.Take(8).Select(item => item.EventKey));
        return new LocalInventoryAlertBatch(title, message, deduplicationKey);
    }

    public static void Normalize(LocalInventoryMonitorHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        history.Version = 1;
        history.Targets ??= new List<LocalInventoryStoredTarget>();
        history.RecentEvents ??= new List<LocalInventoryChangeEvent>();
        foreach (LocalInventoryStoredTarget target in history.Targets)
        {
            target.SteamId ??= string.Empty;
            target.SteamName ??= string.Empty;
            target.SeenEventKeys ??= new List<string>();
            target.SeenEventKeys = target.SeenEventKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal)
                .TakeLast(500)
                .ToList();
        }

        history.Targets = history.Targets
            .Where(target => !string.IsNullOrWhiteSpace(target.SteamId))
            .GroupBy(target => target.SteamId, StringComparer.Ordinal)
            .Select(group => group.Last())
            .Take(50)
            .ToList();
        history.RecentEvents = history.RecentEvents
            .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.EventKey))
            .GroupBy(item => item.EventKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.OccurredAt)
            .TakeLast(500)
            .ToList();
    }

    private static string ResolveDisplayName(
        LocalInventoryStoredTarget target,
        IReadOnlyDictionary<string, string> aliases)
    {
        if (aliases.TryGetValue(target.SteamId, out string? alias) && !string.IsNullOrWhiteSpace(alias))
            return alias;
        if (!string.IsNullOrWhiteSpace(target.SteamName))
            return target.SteamName;
        return target.SteamId;
    }

    private static string ResolveEventTargetName(
        LocalInventoryChangeEvent item,
        IReadOnlyDictionary<string, string> aliases)
    {
        if (aliases.TryGetValue(item.SteamId, out string? alias) && !string.IsNullOrWhiteSpace(alias))
            return alias;
        return string.IsNullOrWhiteSpace(item.SteamName) ? item.SteamId : item.SteamName;
    }
}
