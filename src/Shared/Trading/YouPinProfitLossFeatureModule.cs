using System.Text.Json;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Shared.Configuration;
using CS2TradeMonitor.Shared.Contracts;
using CS2TradeMonitor.Shared.Core;

namespace CS2TradeMonitor.Shared.Trading;

public sealed record YouPinProfitLossItemProjection(
    string Key,
    string Name,
    string TemplateId,
    string AssetId,
    string MatchStatus,
    string BadgeText,
    bool IsMatched,
    bool IsFailed,
    int BuyCount,
    int SellCount,
    double BuyAmount,
    double SellAmount,
    double NetProfit,
    double NetRate,
    DateTime LastTradeTime,
    string SearchText);

public sealed record YouPinProfitLossProjection(
    bool HasCredential,
    DateTime LastSync,
    string LastStatus,
    string LastError,
    int RecordCount,
    int ItemCount,
    int FailedCount,
    int FailedBuyCount,
    int FailedSellCount,
    double MatchedBuyTotal,
    double MatchedSellTotal,
    double NetTotal,
    double NetRate,
    double ProfitTotal,
    double LossTotal,
    double FailedBuyAmount,
    double FailedSellAmount,
    IReadOnlyList<YouPinProfitLossItemProjection> Rows);

/// <summary>
/// Exposes the exact desktop completed-order parsing, deduplication, FIFO
/// matching, and missing-record rules through the one physical service.
/// </summary>
public sealed class YouPinProfitLossFeatureModule : ITradeMonitorCoreModule
{
    public const string QueryBinding = "QueryYouPinProfitLoss";
    public const string SyncBinding = "SyncCompletedYouPinTrades";

    private readonly IYouPinProfitLossService _service;
    private readonly ISettingsSnapshotStore _settings;

    public YouPinProfitLossFeatureModule(
        IYouPinProfitLossService service,
        ISettingsSnapshotStore settings)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public bool CanHandle(string bindingName)
        => bindingName is QueryBinding or SyncBinding;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public async Task<FeatureStateProjection> QueryAsync(
        FeatureStateQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        YouPinProfitLossProjection projection = Project(_service.GetState(snapshot.Settings));
        return new FeatureStateProjection(
            query.SemanticId,
            FeatureAvailability.Available,
            projection.HasCredential ? "ok" : "youpin.auth-required",
            FirstText(projection.LastError, projection.LastStatus, "吃米/亏米统计未同步。"),
            JsonSerializer.Serialize(projection),
            snapshot.Version,
            DateTimeOffset.UtcNow);
    }

    public async Task<CoreCommandResult> ExecuteAsync(
        FeatureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.BindingName != SyncBinding)
        {
            return CoreCommandResult.Disabled(
                "youpin.profit-loss.command-unavailable",
                "该吃米/亏米统计命令未注册。",
                command.CorrelationId);
        }

        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        YouPinProfitLossRefreshResult result = await _service.RefreshAsync(
            snapshot.Settings,
            cancellationToken).ConfigureAwait(false);
        if (result.Ok)
            return CoreCommandResult.Success(result.Message, command.CorrelationId, snapshot.Version);
        if (result.Skipped)
        {
            return CoreCommandResult.Skipped(
                "youpin.profit-loss.sync-skipped",
                result.Message,
                command.CorrelationId,
                snapshot.Version);
        }
        if (result.Message.Contains("登录", StringComparison.Ordinal))
        {
            return CoreCommandResult.NeedsUserAction(
                "youpin.auth-required",
                result.Message,
                command.CorrelationId,
                snapshot.Version);
        }
        return CoreCommandResult.Failed(
            "youpin.profit-loss.sync-failed",
            result.Message,
            command.CorrelationId,
            snapshot.Version);
    }

    public Task<AutomationCycleResult?> RunCycleAsync(
        AutomationCycleTrigger trigger,
        CancellationToken cancellationToken = default)
        => Task.FromResult<AutomationCycleResult?>(null);

    private static YouPinProfitLossProjection Project(YouPinProfitLossState state)
    {
        YouPinProfitLossRedesignView view = YouPinProfitLossRedesignProjection.Build(state.Records);
        YouPinProfitLossItemProjection[] rows = view.Rows.Select(row => new YouPinProfitLossItemProjection(
            row.Key,
            row.Name,
            row.TemplateId,
            row.AssetId,
            row.MatchStatus,
            row.BadgeText,
            row.IsMatched,
            row.IsFailed,
            row.BuyCount,
            row.SellCount,
            row.BuyAmount,
            row.SellAmount,
            row.NetProfit,
            row.NetRate,
            row.LastTradeTime,
            row.SearchText)).ToArray();
        return new YouPinProfitLossProjection(
            state.HasCredential,
            state.LastSync,
            state.LastStatus,
            state.LastError,
            view.RecordCount,
            view.ItemCount,
            view.FailedCount,
            view.FailedBuyCount,
            view.FailedSellCount,
            view.MatchedBuyTotal,
            view.MatchedSellTotal,
            view.NetTotal,
            view.NetRate,
            view.ProfitTotal,
            view.LossTotal,
            view.FailedBuyAmount,
            view.FailedSellAmount,
            rows);
    }

    private static string FirstText(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
