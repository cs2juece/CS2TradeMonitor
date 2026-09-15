using System.Text.Json;
using CS2TradeMonitor.Shared.Configuration;
using CS2TradeMonitor.Shared.Contracts;
using CS2TradeMonitor.Shared.Core;
using CS2TradeMonitor.Shared.Ports;
using CS2TradeMonitor.src.Core;

namespace CS2TradeMonitor.Shared.Market;

/// <summary>
/// Owns desktop-authoritative market configuration and item-monitor commands.
/// Hosts provide only HTTP transport and notification surfaces.
/// </summary>
public sealed class MarketFeatureModule : ITradeMonitorCoreModule
{
    public const string QueryBinding = "MarketFeature";
    public const string SearchItemsBinding = "SearchMonitoredItems";
    public const string AddItemBinding = "AddMonitoredItem";
    public const string RemoveItemBinding = "RemoveMonitoredItem";
    public const string ClearItemsBinding = "ClearMonitoredItems";
    public const string RefreshItemsBinding = "RefreshMonitoredItemPrices";
    public const string SaveItemAlertBinding = "SaveItemPriceAlert";
    public const string ResetItemAlertBinding = "ResetItemPriceAlertDraft";
    public const string SaveSourcesBinding = "SaveMarketDataSources";
    public const string RefreshSourcesBinding = "RefreshMarketDataSources";
    public const string TestSteamDtBinding = "TestSteamDtConnection";
    public const string TestQaqBinding = "TestQaqConnection";
    public const string SaveMarketAlertsBinding = "SaveMarketAlertSettings";
    public const string SendSyntheticAlertBinding = "SendSyntheticMarketAlert";

    private static readonly JsonSerializerOptions JsonOptions = new();
    private readonly ISettingsSnapshotStore _settings;
    private readonly IMarketDataClient _market;
    private readonly IUserNotificationSink _notifications;
    private readonly IClock _clock;
    private readonly MarketAlertEvaluator _marketAlertEvaluator = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, DateTimeOffset> _lastItemFetchTimes = new(StringComparer.OrdinalIgnoreCase);
    private MarketSourceSnapshot _steamDt = MarketSourceSnapshot.Empty(MarketDataSourceIds.SteamDt, "SteamDT");
    private MarketSourceSnapshot _qaq = MarketSourceSnapshot.Empty(MarketDataSourceIds.Qaq, "QAQ");
    private DateTimeOffset _lastSteamDtRefreshAttempt;
    private DateTimeOffset _lastQaqRefreshAttempt;
    private IReadOnlyList<MarketItemCandidate> _searchCandidates = [];
    private string _searchKeyword = "";

    public MarketFeatureModule(
        ISettingsSnapshotStore settings,
        IMarketDataClient market,
        IUserNotificationSink notifications,
        IClock clock)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _market = market ?? throw new ArgumentNullException(nameof(market));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public bool CanHandle(string bindingName)
        => bindingName is QueryBinding
            or SearchItemsBinding
            or AddItemBinding
            or RemoveItemBinding
            or ClearItemsBinding
            or RefreshItemsBinding
            or SaveItemAlertBinding
            or ResetItemAlertBinding
            or SaveSourcesBinding
            or RefreshSourcesBinding
            or TestSteamDtBinding
            or TestQaqBinding
            or SaveMarketAlertsBinding
            or SendSyntheticAlertBinding;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public async Task<FeatureStateProjection> QueryAsync(
        FeatureStateQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        MarketFeatureProjection projection;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            projection = Project(snapshot.Settings);
        }
        finally
        {
            _gate.Release();
        }

        return new FeatureStateProjection(
            query.SemanticId,
            FeatureAvailability.Available,
            "ok",
            BuildStateMessage(projection),
            JsonSerializer.Serialize(projection, JsonOptions),
            snapshot.Version,
            DateTimeOffset.UtcNow);
    }

    public async Task<CoreCommandResult> ExecuteAsync(
        FeatureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.BindingName switch
        {
            SearchItemsBinding => await SearchAsync(command, cancellationToken).ConfigureAwait(false),
            AddItemBinding => await AddItemAsync(command, cancellationToken).ConfigureAwait(false),
            RemoveItemBinding => await RemoveItemAsync(command, cancellationToken).ConfigureAwait(false),
            ClearItemsBinding => await ClearItemsAsync(command, cancellationToken).ConfigureAwait(false),
            RefreshItemsBinding => await RefreshItemsAsync(command, cancellationToken).ConfigureAwait(false),
            SaveItemAlertBinding => await SaveItemAlertAsync(command, cancellationToken).ConfigureAwait(false),
            ResetItemAlertBinding => await ResetItemAlertAsync(command, cancellationToken).ConfigureAwait(false),
            SaveSourcesBinding => await SaveSourcesAsync(command, cancellationToken).ConfigureAwait(false),
            RefreshSourcesBinding => await RefreshSourcesAsync(command, cancellationToken).ConfigureAwait(false),
            TestSteamDtBinding => await RefreshSingleSourceAsync(command, steamDt: true, cancellationToken).ConfigureAwait(false),
            TestQaqBinding => await RefreshSingleSourceAsync(command, steamDt: false, cancellationToken).ConfigureAwait(false),
            SaveMarketAlertsBinding => await SaveMarketAlertsAsync(command, cancellationToken).ConfigureAwait(false),
            SendSyntheticAlertBinding => await SendSyntheticAlertAsync(command, cancellationToken).ConfigureAwait(false),
            _ => CoreCommandResult.Disabled(
                "market.command-unavailable",
                "该市场命令未注册。",
                command.CorrelationId)
        };
    }

    public async Task<AutomationCycleResult?> RunCycleAsync(
        AutomationCycleTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        Settings next = snapshot.Settings.DeepClone();
        DateTimeOffset now = _clock.UtcNow;
        bool steamDue = IsDue(
            _lastSteamDtRefreshAttempt,
            Math.Max(Settings.DefaultMarketRefreshSec, next.SteamDtRefreshSec),
            now);
        bool qaqDue = IsDue(
            _lastQaqRefreshAttempt,
            Math.Max(Settings.DefaultMarketRefreshSec, next.CsqaqRefreshSec),
            now);
        int attempted = 0;
        int succeeded = 0;
        bool settingsChanged = false;

        if (steamDue)
        {
            MarketSourceSnapshot refreshed = await _market.RefreshSteamDtAsync(
                next.SteamDtApiKey,
                cancellationToken).ConfigureAwait(false);
            _lastSteamDtRefreshAttempt = now;
            await SetSourceAsync(refreshed, steamDt: true, cancellationToken).ConfigureAwait(false);
            attempted++;
            if (refreshed.HasData && !refreshed.IsStale)
                succeeded++;
        }

        if (qaqDue)
        {
            if (steamDue)
            {
                await _clock.DelayAsync(
                    TimeSpan.FromMilliseconds(Random.Shared.Next(1000, 3500)),
                    cancellationToken).ConfigureAwait(false);
            }
            MarketSourceSnapshot refreshed = await _market.RefreshQaqAsync(
                next.CsqaqApiToken,
                cancellationToken).ConfigureAwait(false);
            _lastQaqRefreshAttempt = now;
            await SetSourceAsync(refreshed, steamDt: false, cancellationToken).ConfigureAwait(false);
            attempted++;
            if (refreshed.HasData && !refreshed.IsStale)
                succeeded++;
        }

        ItemMonitorConfig[] dueItems = next.ItemConfigs
            .Where(item => item.Enabled
                && !string.IsNullOrWhiteSpace(FirstText(item.ItemId, item.MarketHashName, item.PlatformItemId))
                && IsDue(
                    _lastItemFetchTimes.GetValueOrDefault(ItemKey(item)),
                    item.RefreshIntervalSec > 0 ? item.RefreshIntervalSec : next.DefaultItemRefreshIntervalSec,
                    now))
            .ToArray();
        var itemAlerts = new List<ItemPriceAlertDecision>();
        for (int index = 0; index < dueItems.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ItemMonitorConfig item = dueItems[index];
            MarketItemRefreshResult refreshed = await _market.RefreshItemAsync(
                item,
                next.SteamDtApiKey,
                cancellationToken).ConfigureAwait(false);
            _lastItemFetchTimes[ItemKey(item)] = now;
            ApplyRefresh(item, refreshed);
            attempted++;
            settingsChanged = true;
            if (refreshed.Success)
            {
                succeeded++;
                ItemPriceAlertDecision? decision = EvaluateItemAlert(item, refreshed.Price, now, next);
                if (decision is not null)
                    itemAlerts.Add(decision);
            }
            await _clock.DelayAsync(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }

        (MarketSourceSnapshot steamDt, MarketSourceSnapshot qaq) = await GetSourcesAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<MarketAlertDispatch> marketAlerts = _marketAlertEvaluator.Evaluate(
            next,
            steamDt,
            qaq,
            suppress: false,
            now.LocalDateTime);

        if (settingsChanged)
            snapshot = await _settings.SaveAsync(next, snapshot.Version, cancellationToken).ConfigureAwait(false);

        if (!next.DoNotDisturbEnabled)
        {
            await PublishItemAlertsAsync(itemAlerts, cancellationToken).ConfigureAwait(false);
            await PublishMarketAlertsAsync(marketAlerts, cancellationToken).ConfigureAwait(false);
        }

        if (attempted == 0 && marketAlerts.Count == 0)
        {
            return new AutomationCycleResult(
                AutomationCycleStatus.Skipped,
                "market.cycle-not-due",
                "市场数据与单品监控尚未到刷新时间。",
                trigger.CorrelationId,
                snapshot.Version);
        }

        AutomationCycleStatus status = succeeded > 0 || attempted == 0
            ? AutomationCycleStatus.Completed
            : AutomationCycleStatus.Failed;
        return new AutomationCycleResult(
            status,
            status == AutomationCycleStatus.Completed ? "ok" : "market.cycle-refresh-failed",
            status == AutomationCycleStatus.Completed
                ? $"市场后台周期已完成：{succeeded}/{attempted} 项刷新成功。"
                : "市场后台周期未能刷新有效数据。",
            trigger.CorrelationId,
            snapshot.Version,
            attempted);
    }

    private async Task<CoreCommandResult> SearchAsync(FeatureCommand command, CancellationToken cancellationToken)
    {
        SearchMarketItemsCommand? payload = Deserialize<SearchMarketItemsCommand>(command.PayloadJson);
        string keyword = payload?.Keyword?.Trim() ?? "";
        if (keyword.Length < 2)
        {
            return CoreCommandResult.NeedsUserAction(
                "market.search-keyword-too-short",
                "请输入至少 2 个字符后搜索。",
                command.CorrelationId);
        }

        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<MarketItemCandidate> candidates = await _market.SearchItemsAsync(
            keyword,
            snapshot.Settings.SteamDtApiKey,
            cancellationToken).ConfigureAwait(false);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _searchKeyword = keyword;
            _searchCandidates = candidates
                .Where(IsValidCandidate)
                .DistinctBy(CandidateKey, StringComparer.OrdinalIgnoreCase)
                .Take(100)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }

        return CoreCommandResult.Success(
            _searchCandidates.Count == 0 ? "没有找到匹配饰品。" : $"找到 {_searchCandidates.Count} 个候选。",
            command.CorrelationId,
            snapshot.Version);
    }

    private async Task<CoreCommandResult> AddItemAsync(FeatureCommand command, CancellationToken cancellationToken)
    {
        AddMonitoredItemCommand? payload = Deserialize<AddMonitoredItemCommand>(command.PayloadJson);
        MarketItemCandidate? candidate = payload?.Candidate;
        if (candidate is null || !IsValidCandidate(candidate))
        {
            return CoreCommandResult.NeedsUserAction(
                "market.item-required",
                "请先选择要添加的饰品。",
                command.CorrelationId);
        }

        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        Settings next = snapshot.Settings.DeepClone();
        if (next.ItemConfigs.Any(item => IsSameItem(item, candidate)))
        {
            return CoreCommandResult.Skipped(
                "market.item-duplicate",
                "该饰品已在监控列表中。",
                command.CorrelationId,
                snapshot.Version);
        }

        var item = new ItemMonitorConfig
        {
            ItemId = FirstText(candidate.ItemId, candidate.MarketHashName, candidate.PlatformItemId),
            ItemKey = FirstText(candidate.MarketHashName, candidate.ItemId, candidate.PlatformItemId),
            Name = candidate.Name.Trim(),
            ShortName = MakeShortName(candidate.Name),
            MarketHashName = candidate.MarketHashName.Trim(),
            PlatformItemId = candidate.PlatformItemId.Trim(),
            Enabled = true,
            RefreshIntervalSec = Math.Max(60, next.DefaultItemRefreshIntervalSec <= 0 ? 600 : next.DefaultItemRefreshIntervalSec),
            VisibleInPanel = next.ItemMonitorDefaultVisibleInPanel,
            VisibleInTaskbar = next.ItemMonitorDefaultVisibleInTaskbar,
            SortIndex = next.ItemConfigs.Count,
            TaskbarSortIndex = next.ItemConfigs.Count,
            PriceAlertDesktopEnabled = true,
            PriceAlertPhoneEnabled = false,
            PriceAlertDeliverySchemaVersion = ItemMonitorConfig.CurrentPriceAlertDeliverySchemaVersion,
            PriceAlertWindowMinutes = Math.Clamp(next.DefaultItemPriceAlertWindowMinutes, 1, 10080),
            PriceAlertCooldownMinutes = Math.Clamp(next.DefaultItemPriceAlertCooldownMinutes, 1, 1440)
        };
        next.ItemConfigs.Add(item);
        SettingsSnapshot saved = await _settings.SaveAsync(next, snapshot.Version, cancellationToken).ConfigureAwait(false);

        MarketItemRefreshResult refreshed = await _market.RefreshItemAsync(
            item,
            saved.Settings.SteamDtApiKey,
            cancellationToken).ConfigureAwait(false);
        ItemPriceAlertDecision? alert = null;
        if (refreshed.Success)
        {
            Settings latest = saved.Settings.DeepClone();
            ItemMonitorConfig? persisted = FindItem(latest, item.ItemKey);
            if (persisted is not null)
            {
                ApplyRefresh(persisted, refreshed);
                alert = EvaluateItemAlert(persisted, refreshed.Price, _clock.UtcNow, latest);
                saved = await _settings.SaveAsync(latest, saved.Version, cancellationToken).ConfigureAwait(false);
            }
        }
        _lastItemFetchTimes[ItemKey(item)] = _clock.UtcNow;
        if (alert is not null && !saved.Settings.DoNotDisturbEnabled)
            await PublishItemAlertsAsync([alert], cancellationToken).ConfigureAwait(false);

        return CoreCommandResult.Success(
            refreshed.Success ? "已添加并更新价格。" : "已添加，价格稍后自动重试。",
            command.CorrelationId,
            saved.Version);
    }

    private async Task<CoreCommandResult> RemoveItemAsync(FeatureCommand command, CancellationToken cancellationToken)
    {
        RemoveMonitoredItemCommand? payload = Deserialize<RemoveMonitoredItemCommand>(command.PayloadJson);
        string itemKey = payload?.ItemKey?.Trim() ?? "";
        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        Settings next = snapshot.Settings.DeepClone();
        ItemMonitorConfig? item = FindItem(next, itemKey);
        if (item is null)
        {
            return CoreCommandResult.Skipped(
                "market.item-not-found",
                "该饰品已不在监控列表。",
                command.CorrelationId,
                snapshot.Version);
        }

        next.ItemConfigs.Remove(item);
        NormalizeSortIndexes(next.ItemConfigs);
        SettingsSnapshot saved = await _settings.SaveAsync(next, snapshot.Version, cancellationToken).ConfigureAwait(false);
        return CoreCommandResult.Success("已删除：" + item.Name, command.CorrelationId, saved.Version);
    }

    private async Task<CoreCommandResult> ClearItemsAsync(FeatureCommand command, CancellationToken cancellationToken)
    {
        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot.Settings.ItemConfigs.Count == 0)
        {
            return CoreCommandResult.Skipped(
                "market.items-empty",
                "监控列表已经为空。",
                command.CorrelationId,
                snapshot.Version);
        }

        Settings next = snapshot.Settings.DeepClone();
        next.ItemConfigs.Clear();
        SettingsSnapshot saved = await _settings.SaveAsync(next, snapshot.Version, cancellationToken).ConfigureAwait(false);
        return CoreCommandResult.Success("已清空单品监控列表。", command.CorrelationId, saved.Version);
    }

    private async Task<CoreCommandResult> RefreshItemsAsync(FeatureCommand command, CancellationToken cancellationToken)
    {
        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        Settings next = snapshot.Settings.DeepClone();
        ItemMonitorConfig[] enabled = next.ItemConfigs.Where(item => item.Enabled).ToArray();
        if (enabled.Length == 0)
        {
            return CoreCommandResult.Skipped(
                "market.items-empty",
                "没有已启用的监控单品。",
                command.CorrelationId,
                snapshot.Version);
        }

        int succeeded = 0;
        var alerts = new List<ItemPriceAlertDecision>();
        for (int index = 0; index < enabled.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ItemMonitorConfig item = enabled[index];
            MarketItemRefreshResult refreshed = await _market.RefreshItemAsync(
                item,
                next.SteamDtApiKey,
                cancellationToken).ConfigureAwait(false);
            _lastItemFetchTimes[ItemKey(item)] = _clock.UtcNow;
            ApplyRefresh(item, refreshed);
            if (refreshed.Success)
            {
                succeeded++;
                ItemPriceAlertDecision? alert = EvaluateItemAlert(item, refreshed.Price, _clock.UtcNow, next);
                if (alert is not null)
                    alerts.Add(alert);
            }
            await _clock.DelayAsync(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }

        SettingsSnapshot saved = await _settings.SaveAsync(next, snapshot.Version, cancellationToken).ConfigureAwait(false);
        if (!saved.Settings.DoNotDisturbEnabled)
            await PublishItemAlertsAsync(alerts, cancellationToken).ConfigureAwait(false);
        return succeeded > 0
            ? CoreCommandResult.Success($"已刷新 {succeeded}/{enabled.Length} 个监控单品。", command.CorrelationId, saved.Version)
            : CoreCommandResult.Failed("market.item-refresh-failed", "单品价格刷新失败，请检查数据源。", command.CorrelationId, saved.Version);
    }

    private async Task<CoreCommandResult> SaveItemAlertAsync(FeatureCommand command, CancellationToken cancellationToken)
    {
        SaveItemPriceAlertCommand? payload = Deserialize<SaveItemPriceAlertCommand>(command.PayloadJson);
        if (payload is null || string.IsNullOrWhiteSpace(payload.ItemKey))
        {
            return CoreCommandResult.NeedsUserAction(
                "market.item-required",
                "请先选择要配置提醒的饰品。",
                command.CorrelationId);
        }

        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        Settings next = snapshot.Settings.DeepClone();
        ItemMonitorConfig? item = FindItem(next, payload.ItemKey);
        if (item is null)
        {
            return CoreCommandResult.NeedsUserAction(
                "market.item-not-found",
                "该饰品已不在监控列表，请返回刷新。",
                command.CorrelationId,
                snapshot.Version);
        }

        item.PriceAlertTriggerMode = payload.TriggerMode is ItemPriceAlertTriggerMode.Percent or ItemPriceAlertTriggerMode.Breakthrough
            ? payload.TriggerMode
            : ItemPriceAlertTriggerMode.Auto;
        item.PriceAlertDesktopEnabled = payload.DesktopEnabled;
        item.PriceAlertPhoneEnabled = payload.PhoneEnabled;
        item.PriceAlertEnabled = payload.DesktopEnabled || payload.PhoneEnabled;
        item.PriceAlertDeliverySchemaVersion = ItemMonitorConfig.CurrentPriceAlertDeliverySchemaVersion;
        item.PriceAlertAbove = NonNegative(payload.Above);
        item.PriceAlertBelow = NonNegative(payload.Below);
        item.PriceAlertRisePercent = NonNegative(payload.RisePercent);
        item.PriceAlertFallPercent = NonNegative(payload.FallPercent);
        item.PriceAlertWindowMinutes = Math.Clamp(payload.WindowMinutes, 1, 10080);
        item.PriceAlertCooldownMinutes = Math.Clamp(payload.CooldownMinutes, 1, 1440);

        SettingsSnapshot saved = await _settings.SaveAsync(next, snapshot.Version, cancellationToken).ConfigureAwait(false);
        return CoreCommandResult.Success("已保存配置：" + item.Name, command.CorrelationId, saved.Version);
    }

    private async Task<CoreCommandResult> ResetItemAlertAsync(FeatureCommand command, CancellationToken cancellationToken)
    {
        RemoveMonitoredItemCommand? payload = Deserialize<RemoveMonitoredItemCommand>(command.PayloadJson);
        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        Settings next = snapshot.Settings.DeepClone();
        ItemMonitorConfig? item = FindItem(next, payload?.ItemKey ?? "");
        if (item is null)
        {
            return CoreCommandResult.NeedsUserAction(
                "market.item-not-found",
                "该饰品已不在监控列表。",
                command.CorrelationId,
                snapshot.Version);
        }

        item.PriceAlertTriggerMode = ItemPriceAlertTriggerMode.Auto;
        item.PriceAlertDesktopEnabled = true;
        item.PriceAlertPhoneEnabled = false;
        item.PriceAlertEnabled = true;
        item.PriceAlertDeliverySchemaVersion = ItemMonitorConfig.CurrentPriceAlertDeliverySchemaVersion;
        item.PriceAlertAbove = 0;
        item.PriceAlertBelow = 0;
        item.PriceAlertRisePercent = NonNegative(next.DefaultItemPriceAlertRisePercent);
        item.PriceAlertFallPercent = NonNegative(next.DefaultItemPriceAlertFallPercent);
        item.PriceAlertWindowMinutes = Math.Clamp(next.DefaultItemPriceAlertWindowMinutes, 1, 10080);
        item.PriceAlertCooldownMinutes = Math.Clamp(next.DefaultItemPriceAlertCooldownMinutes, 1, 1440);
        item.PriceAlertBaselinePrice = 0;
        item.PriceAlertBaselineTime = 0;
        item.PriceAlertLastTriggerTime = 0;
        item.PriceAlertLastMessage = "";

        SettingsSnapshot saved = await _settings.SaveAsync(next, snapshot.Version, cancellationToken).ConfigureAwait(false);
        return CoreCommandResult.Success("已恢复初始值：" + item.Name, command.CorrelationId, saved.Version);
    }

    private async Task<CoreCommandResult> SaveSourcesAsync(FeatureCommand command, CancellationToken cancellationToken)
    {
        SaveMarketSourceSettingsCommand? payload = Deserialize<SaveMarketSourceSettingsCommand>(command.PayloadJson);
        if (payload is null)
        {
            return CoreCommandResult.NeedsUserAction(
                "market.source-settings-required",
                "请提供数据源设置。",
                command.CorrelationId);
        }

        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        Settings next = snapshot.Settings.DeepClone();
        if (payload.SteamDtApiKey is not null)
            next.SteamDtApiKey = payload.SteamDtApiKey.Trim();
        if (payload.QaqApiToken is not null)
            next.CsqaqApiToken = payload.QaqApiToken.Trim();
        next.SteamDtRefreshSec = Math.Max(Settings.DefaultMarketRefreshSec, payload.SteamDtRefreshSeconds);
        next.CsqaqRefreshSec = Math.Max(Settings.DefaultMarketRefreshSec, payload.QaqRefreshSeconds);
        next.SteamDtShowPercent = payload.ShowPercent;
        SettingsSnapshot saved = await _settings.SaveAsync(next, snapshot.Version, cancellationToken).ConfigureAwait(false);
        return CoreCommandResult.Success("大盘数据源设置已保存。", command.CorrelationId, saved.Version);
    }

    private async Task<CoreCommandResult> RefreshSourcesAsync(FeatureCommand command, CancellationToken cancellationToken)
    {
        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        MarketSourceSnapshot steamDt = await _market.RefreshSteamDtAsync(
            snapshot.Settings.SteamDtApiKey,
            cancellationToken).ConfigureAwait(false);
        _lastSteamDtRefreshAttempt = _clock.UtcNow;
        await _clock.DelayAsync(
            TimeSpan.FromMilliseconds(Random.Shared.Next(1000, 3500)),
            cancellationToken).ConfigureAwait(false);
        MarketSourceSnapshot qaq = await _market.RefreshQaqAsync(
            snapshot.Settings.CsqaqApiToken,
            cancellationToken).ConfigureAwait(false);
        _lastQaqRefreshAttempt = _clock.UtcNow;
        await SetSourceAsync(steamDt, steamDt: true, cancellationToken).ConfigureAwait(false);
        await SetSourceAsync(qaq, steamDt: false, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<MarketAlertDispatch> alerts = _marketAlertEvaluator.Evaluate(
            snapshot.Settings,
            steamDt,
            qaq,
            suppress: false,
            _clock.UtcNow.LocalDateTime);
        if (!snapshot.Settings.DoNotDisturbEnabled)
            await PublishMarketAlertsAsync(alerts, cancellationToken).ConfigureAwait(false);

        int succeeded = (steamDt.HasData && !steamDt.IsStale ? 1 : 0) + (qaq.HasData && !qaq.IsStale ? 1 : 0);
        return succeeded > 0
            ? CoreCommandResult.Success($"已刷新 {succeeded}/2 个大盘数据源。", command.CorrelationId, snapshot.Version)
            : CoreCommandResult.Failed("market.refresh-failed", "两个大盘数据源均未刷新成功。", command.CorrelationId, snapshot.Version);
    }

    private async Task<CoreCommandResult> RefreshSingleSourceAsync(
        FeatureCommand command,
        bool steamDt,
        CancellationToken cancellationToken)
    {
        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        MarketSourceSnapshot result = steamDt
            ? await _market.RefreshSteamDtAsync(snapshot.Settings.SteamDtApiKey, cancellationToken).ConfigureAwait(false)
            : await _market.RefreshQaqAsync(snapshot.Settings.CsqaqApiToken, cancellationToken).ConfigureAwait(false);

        if (steamDt)
            _lastSteamDtRefreshAttempt = _clock.UtcNow;
        else
            _lastQaqRefreshAttempt = _clock.UtcNow;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (steamDt)
                _steamDt = result;
            else
                _qaq = result;
        }
        finally
        {
            _gate.Release();
        }

        return result.HasData && !result.IsStale
            ? CoreCommandResult.Success(result.DisplayName + " 连接正常。", command.CorrelationId, snapshot.Version)
            : CoreCommandResult.Failed(
                "market.connection-test-failed",
                string.IsNullOrWhiteSpace(result.Error) ? result.DisplayName + " 连接失败。" : result.Error,
                command.CorrelationId,
                snapshot.Version);
    }

    private async Task<CoreCommandResult> SaveMarketAlertsAsync(FeatureCommand command, CancellationToken cancellationToken)
    {
        SaveMarketAlertSettingsCommand? payload = Deserialize<SaveMarketAlertSettingsCommand>(command.PayloadJson);
        if (payload is null)
        {
            return CoreCommandResult.NeedsUserAction(
                "market.alert-settings-required",
                "请提供大盘预警设置。",
                command.CorrelationId);
        }

        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        Settings next = snapshot.Settings.DeepClone();
        next.MarketAlertsEnabled = payload.Enabled;
        next.MarketAlertNotificationMode = payload.NotificationMode;
        next.MarketAlertDeferWhenFullscreen = payload.DeferWhenFullscreen;
        next.MarketAlertDefaultWindowMinutes = Math.Clamp(payload.DefaultWindowMinutes, 1, 1440);
        next.MarketAlertDefaultCooldownMinutes = Math.Clamp(payload.DefaultCooldownMinutes, 1, 1440);
        next.MarketAlertRules = payload.Rules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.SourceId))
            .Select(rule => new MarketAlertRule
            {
                Id = string.IsNullOrWhiteSpace(rule.Id) ? Guid.NewGuid().ToString("N") : rule.Id.Trim(),
                Name = rule.Name?.Trim() ?? "",
                Enabled = rule.Enabled,
                SourceId = NormalizeSourceId(rule.SourceId),
                RuleType = rule.RuleType,
                Threshold = NonNegative(rule.Threshold),
                WindowMinutes = Math.Clamp(rule.WindowMinutes, 1, 1440),
                CooldownMinutes = Math.Clamp(rule.CooldownMinutes, 1, 1440)
            })
            .ToList();
        EnsureBuiltInRules(next.MarketAlertRules);

        SettingsSnapshot saved = await _settings.SaveAsync(next, snapshot.Version, cancellationToken).ConfigureAwait(false);
        return CoreCommandResult.Success("大盘预警设置已保存。", command.CorrelationId, saved.Version);
    }

    private async Task<CoreCommandResult> SendSyntheticAlertAsync(FeatureCommand command, CancellationToken cancellationToken)
    {
        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        await _notifications.PublishAsync(
            new UserNotification(
                "market-alert-test-" + Guid.NewGuid().ToString("N"),
                "大盘预警测试",
                "这是一条合成测试预警，不会改变真实规则状态。",
                UserNotificationSeverity.Warning,
                "/market/alerts"),
            cancellationToken).ConfigureAwait(false);
        return CoreCommandResult.Success("测试预警已发送。", command.CorrelationId, snapshot.Version);
    }

    private MarketFeatureProjection Project(Settings settings)
    {
        MonitoredItemProjection[] items = settings.ItemConfigs
            .OrderBy(item => item.SortIndex)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ProjectItem)
            .ToArray();
        MarketAlertRuleProjection[] rules = settings.MarketAlertRules
            .Select(rule => new MarketAlertRuleProjection(
                rule.Id,
                rule.Name,
                rule.Enabled,
                NormalizeSourceId(rule.SourceId),
                rule.RuleType,
                rule.Threshold,
                rule.WindowMinutes,
                rule.CooldownMinutes,
                IsBuiltInRule(rule)))
            .ToArray();

        return new MarketFeatureProjection(
            ProjectSource(_steamDt, !string.IsNullOrWhiteSpace(settings.SteamDtApiKey), settings.SteamDtRefreshSec),
            ProjectSource(_qaq, !string.IsNullOrWhiteSpace(settings.CsqaqApiToken), settings.CsqaqRefreshSec),
            items,
            _searchKeyword,
            _searchCandidates,
            settings.SteamDtShowPercent,
            settings.MarketAlertsEnabled,
            settings.MarketAlertNotificationMode,
            settings.MarketAlertDeferWhenFullscreen,
            settings.MarketAlertDefaultWindowMinutes,
            settings.MarketAlertDefaultCooldownMinutes,
            rules,
            DateTime.Now);
    }

    private static MarketSourceProjection ProjectSource(MarketSourceSnapshot source, bool hasCredential, int refreshSeconds)
        => new(
            source.Id,
            source.DisplayName,
            hasCredential,
            source.Index,
            source.Change,
            source.Percent,
            source.RetrievedAt,
            source.Source,
            source.Status,
            source.Error,
            source.HasData,
            source.IsStale,
            Math.Max(Settings.DefaultMarketRefreshSec, refreshSeconds));

    private static MonitoredItemProjection ProjectItem(ItemMonitorConfig item)
        => new(
            FirstText(item.ItemKey, item.MarketHashName, item.ItemId, item.PlatformItemId),
            item.ItemId,
            item.Name,
            item.ShortName,
            item.Enabled,
            Math.Max(60, item.RefreshIntervalSec),
            item.LastPrice,
            item.LastYouPinBidPrice,
            item.LastChange,
            item.LastChangeRatio,
            item.LastUpdateTime,
            item.HasChangeData,
            item.LastStatus,
            new ItemPriceAlertProjection(
                item.PriceAlertTriggerMode,
                item.PriceAlertDesktopEnabled,
                item.PriceAlertPhoneEnabled,
                item.PriceAlertAbove,
                item.PriceAlertBelow,
                item.PriceAlertRisePercent,
                item.PriceAlertFallPercent,
                item.PriceAlertWindowMinutes,
                item.PriceAlertCooldownMinutes,
                item.PriceAlertLastMessage));

    private static void ApplyRefresh(ItemMonitorConfig item, MarketItemRefreshResult result)
    {
        if (!result.Success)
        {
            item.LastStatus = string.IsNullOrWhiteSpace(result.Error)
                ? "刷新失败"
                : "刷新失败（" + result.Error + "）";
            return;
        }

        item.LastPrice = result.Price;
        item.LastYouPinBidPrice = result.YouPinBidPrice > 0 ? result.YouPinBidPrice : item.LastYouPinBidPrice;
        item.LastChange = result.Change;
        item.LastChangeRatio = result.ChangePercent;
        item.LastUpdateTime = result.UpdateTime;
        item.LastYouPinBidUpdateTime = result.YouPinBidUpdateTime;
        item.HasChangeData = result.HasChangeData;
        item.LastStatus = result.Status;
        item.LastYouPinBidStatus = result.YouPinBidPrice > 0 ? "成功" : item.LastYouPinBidPrice > 0 ? "缓存" : "暂无求购价";
    }

    private async Task SetSourceAsync(
        MarketSourceSnapshot source,
        bool steamDt,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (steamDt)
                _steamDt = source;
            else
                _qaq = source;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(MarketSourceSnapshot SteamDt, MarketSourceSnapshot Qaq)> GetSourcesAsync(
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (_steamDt, _qaq);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static ItemPriceAlertDecision? EvaluateItemAlert(
        ItemMonitorConfig item,
        double price,
        DateTimeOffset now,
        Settings settings)
        => ItemPriceAlertEvaluator.Evaluate(
            item,
            price,
            now,
            new ItemPriceAlertDefaults(
                settings.DefaultItemPriceAlertWindowMinutes,
                settings.DefaultItemPriceAlertCooldownMinutes,
                settings.DefaultItemPriceAlertRisePercent,
                settings.DefaultItemPriceAlertFallPercent));

    private async Task PublishItemAlertsAsync(
        IEnumerable<ItemPriceAlertDecision> decisions,
        CancellationToken cancellationToken)
    {
        foreach (ItemPriceAlertDecision decision in decisions)
        {
            await _notifications.PublishAsync(
                new UserNotification(
                    "item-price-alert-" + Guid.NewGuid().ToString("N"),
                    decision.Title,
                    decision.Message,
                    UserNotificationSeverity.Warning,
                    "/market/item-monitor/alert"),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PublishMarketAlertsAsync(
        IEnumerable<MarketAlertDispatch> dispatches,
        CancellationToken cancellationToken)
    {
        foreach (MarketAlertDispatch dispatch in dispatches)
        {
            await _notifications.PublishAsync(
                new UserNotification(
                    "market-alert-" + Guid.NewGuid().ToString("N"),
                    dispatch.Title,
                    dispatch.Message,
                    UserNotificationSeverity.Warning,
                    "/market/alerts"),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsDue(DateTimeOffset lastAttempt, int intervalSeconds, DateTimeOffset now)
        => lastAttempt == default
            || now - lastAttempt >= TimeSpan.FromSeconds(Math.Max(60, intervalSeconds));

    private static string ItemKey(ItemMonitorConfig item)
        => FirstText(item.ItemKey, item.ItemId, item.MarketHashName, item.PlatformItemId);

    private static ItemMonitorConfig? FindItem(Settings settings, string itemKey)
        => settings.ItemConfigs.FirstOrDefault(item =>
            EqualsText(item.ItemKey, itemKey)
            || EqualsText(item.ItemId, itemKey)
            || EqualsText(item.MarketHashName, itemKey)
            || EqualsText(item.PlatformItemId, itemKey));

    private static bool IsSameItem(ItemMonitorConfig item, MarketItemCandidate candidate)
        => EqualsText(item.ItemId, candidate.ItemId)
            || EqualsText(item.ItemKey, candidate.MarketHashName)
            || EqualsText(item.MarketHashName, candidate.MarketHashName)
            || EqualsText(item.PlatformItemId, candidate.PlatformItemId);

    private static bool EqualsText(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsValidCandidate(MarketItemCandidate candidate)
        => !string.IsNullOrWhiteSpace(candidate.Name)
            && !string.IsNullOrWhiteSpace(CandidateKey(candidate));

    private static string CandidateKey(MarketItemCandidate candidate)
        => FirstText(candidate.MarketHashName, candidate.ItemId, candidate.PlatformItemId);

    private static string FirstText(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static string MakeShortName(string name)
    {
        string value = name?.Trim() ?? "";
        return value.Length <= 24 ? value : value[..21] + "...";
    }

    private static double NonNegative(double value)
        => double.IsFinite(value) ? Math.Max(0, value) : 0;

    private static void NormalizeSortIndexes(List<ItemMonitorConfig> items)
    {
        for (int index = 0; index < items.Count; index++)
        {
            items[index].SortIndex = index;
            items[index].TaskbarSortIndex = index;
        }
    }

    private static string NormalizeSourceId(string? sourceId)
        => string.Equals(sourceId, MarketDataSourceIds.SteamDt, StringComparison.OrdinalIgnoreCase)
            ? MarketDataSourceIds.SteamDt
            : MarketDataSourceIds.Qaq;

    private static bool IsBuiltInRule(MarketAlertRule rule)
        => rule.Id.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase);

    private static void EnsureBuiltInRules(List<MarketAlertRule> rules)
    {
        foreach (MarketAlertRule defaultRule in Settings.CreateDefaultMarketAlertRules())
        {
            if (rules.All(rule => !string.Equals(rule.Id, defaultRule.Id, StringComparison.OrdinalIgnoreCase)))
                rules.Add(defaultRule);
        }
    }

    private static T? Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string BuildStateMessage(MarketFeatureProjection projection)
    {
        int liveSources = (projection.SteamDt.HasData && !projection.SteamDt.IsStale ? 1 : 0)
            + (projection.Qaq.HasData && !projection.Qaq.IsStale ? 1 : 0);
        return $"{projection.Items.Count} 个监控单品 · {liveSources}/2 个大盘数据源正常。";
    }
}
