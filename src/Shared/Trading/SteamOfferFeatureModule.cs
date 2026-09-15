using System.Text.Json;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.Application.Steam.Auth;
using CS2TradeMonitor.Domain.Steam;
using CS2TradeMonitor.Shared.Contracts;
using CS2TradeMonitor.Shared.Core;

namespace CS2TradeMonitor.Shared.Trading;

public sealed record SteamOfferCommand(string TradeOfferId);

public sealed record SteamAuthProjection(
    bool HasCredential,
    bool HasSecrets,
    bool HasSession,
    bool HasAutoLogin,
    string AccountName,
    string PersonaName,
    string SteamIdPreview,
    string Message,
    string Error);

public sealed record SteamOfferItemProjection(
    string TradeOfferId,
    string Title,
    string ItemSummary,
    string Direction,
    string PartnerName,
    string Risk,
    string Status,
    bool VerifiedByYouPin,
    bool CanAcceptSafely,
    string SafeReason,
    string FailureReason,
    DateTime CreatedAt,
    int GiveItemCount,
    int ReceiveItemCount,
    IReadOnlyList<string> GiveItemNames,
    IReadOnlyList<string> ReceiveItemNames);

public sealed record SteamOfferProjection(
    SteamAuthProjection Auth,
    IReadOnlyList<SteamOfferItemProjection> Offers,
    int IncomingCount,
    int OutgoingCount,
    int SafeIncomingCount,
    DateTime LastRefresh,
    string LastStatus,
    string LastError,
    bool AutoConfirmRunning,
    bool AutoTradeRunning);

/// <summary>
/// Presentation-independent Steam offer boundary. Both hosts execute the same
/// desktop-authoritative service; the Android page only renders this projection.
/// </summary>
public sealed class SteamOfferFeatureModule : ITradeMonitorCoreModule
{
    public const string RefreshBinding = "RefreshSteamOffers";
    public const string RefreshAndValidateBinding = "RefreshSteamOffersAndValidateSession";
    public const string AcceptAllBinding = "AcceptAllEligibleSteamOffers";
    public const string AcceptSingleBinding = "AcceptSteamOffer";
    public const string ClearTokenBinding = "ClearSteamTokenSecrets";
    public const string ClearLoginBinding = "ClearSteamLoginState";
    public const string ValidateTokenBinding = "ValidateSteamToken";

    private readonly ISteamOfferService _service;

    public SteamOfferFeatureModule(ISteamOfferService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public bool CanHandle(string bindingName)
        => bindingName is RefreshBinding
            or RefreshAndValidateBinding
            or AcceptAllBinding
            or AcceptSingleBinding
            or ClearTokenBinding
            or ClearLoginBinding
            or ValidateTokenBinding;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<FeatureStateProjection> QueryAsync(
        FeatureStateQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        SteamOfferState state = _service.GetState();
        SteamOfferProjection projection = Project(state);
        bool hasCredential = state.AuthStatus.HasCredential;
        return Task.FromResult(new FeatureStateProjection(
            query.SemanticId,
            hasCredential ? FeatureAvailability.Available : FeatureAvailability.NeedsUserAction,
            hasCredential ? "ok" : "steam.auth-required",
            hasCredential ? StateMessage(state) : "请先完成 Steam 登录或导入令牌。",
            JsonSerializer.Serialize(projection),
            Version(state),
            DateTimeOffset.UtcNow));
    }

    public async Task<CoreCommandResult> ExecuteAsync(
        FeatureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        SteamOfferActionResult result;

        switch (command.BindingName)
        {
            case RefreshBinding:
                result = await _service.LoadOffersAsync(useMock: false, allowAutoRelogin: true)
                    .ConfigureAwait(false);
                break;
            case RefreshAndValidateBinding:
                result = await _service.EnsureSessionAsync().ConfigureAwait(false);
                if (result.Ok)
                {
                    result = await _service.LoadOffersAsync(useMock: false, allowAutoRelogin: true)
                        .ConfigureAwait(false);
                }
                break;
            case AcceptAllBinding:
                result = await _service.AcceptSafeOffersAsync(allowYouPinVerified: true)
                    .ConfigureAwait(false);
                break;
            case AcceptSingleBinding:
                return await AcceptSingleAsync(command).ConfigureAwait(false);
            case ClearTokenBinding:
                result = _service.ClearTokenSecrets();
                break;
            case ClearLoginBinding:
                result = _service.ClearLoginState();
                break;
            case ValidateTokenBinding:
                result = await _service.EnsureSessionAsync().ConfigureAwait(false);
                break;
            default:
                return CoreCommandResult.Disabled(
                    "steam.command-unavailable",
                    "该 Steam 命令未注册。",
                    command.CorrelationId,
                    Version(_service.GetState()));
        }

        return ToCoreResult(result, command.CorrelationId);
    }

    public Task<AutomationCycleResult?> RunCycleAsync(
        AutomationCycleTrigger trigger,
        CancellationToken cancellationToken = default)
        => Task.FromResult<AutomationCycleResult?>(null);

    private async Task<CoreCommandResult> AcceptSingleAsync(FeatureCommand command)
    {
        SteamOfferCommand? payload = Deserialize(command.PayloadJson);
        string tradeOfferId = payload?.TradeOfferId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(tradeOfferId))
        {
            return CoreCommandResult.NeedsUserAction(
                "steam.offer-required",
                "请先选择要同意的 Steam 报价。",
                command.CorrelationId,
                Version(_service.GetState()));
        }

        SteamOfferItem? offer = _service.GetState().Offers.FirstOrDefault(item =>
            string.Equals(item.TradeOfferId, tradeOfferId, StringComparison.OrdinalIgnoreCase));
        if (offer is null)
        {
            return CoreCommandResult.NeedsUserAction(
                "steam.offer-not-found",
                "该 Steam 报价已不在当前列表，请刷新后重试。",
                command.CorrelationId,
                Version(_service.GetState()));
        }

        if (offer.Status != SteamOfferStatus.Pending || !offer.CanAcceptSafely)
        {
            return CoreCommandResult.Disabled(
                "steam.offer-not-safe",
                "该报价未通过安全校验，保持禁用。",
                command.CorrelationId,
                Version(_service.GetState()));
        }

        SteamOfferActionResult result = await _service.AcceptOfferAsync(tradeOfferId, requireSafe: true)
            .ConfigureAwait(false);
        return ToCoreResult(result, command.CorrelationId);
    }

    private CoreCommandResult ToCoreResult(SteamOfferActionResult result, string correlationId)
    {
        long version = Version(_service.GetState());
        return result.Ok
            ? CoreCommandResult.Success(result.Message, correlationId, version)
            : CoreCommandResult.Failed(
                string.IsNullOrWhiteSpace(result.Code) ? "steam.operation-failed" : result.Code,
                result.Message,
                correlationId,
                version);
    }

    private static SteamOfferCommand? Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<SteamOfferCommand>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SteamOfferProjection Project(SteamOfferState state)
    {
        SteamAuthStoreStatus auth = state.AuthStatus;
        SteamOfferItemProjection[] offers = state.Offers.Select(ProjectOffer).ToArray();
        return new SteamOfferProjection(
            new SteamAuthProjection(
                auth.HasCredential,
                auth.HasSecrets,
                auth.HasSession,
                auth.HasAutoLogin,
                auth.AccountName,
                auth.PersonaName,
                PreviewSteamId(auth.SteamId),
                auth.Message,
                auth.Error),
            offers,
            offers.Count(offer => offer.Direction is "收到" or "双向"),
            offers.Count(offer => offer.Direction == "发出"),
            offers.Count(offer => offer.Direction is "收到" or "双向" && offer.CanAcceptSafely),
            state.LastRefresh,
            state.LastStatus,
            state.LastError,
            state.AutoConfirm.IsRunning,
            state.AutoTrade.IsRunning);
    }

    private static SteamOfferItemProjection ProjectOffer(SteamOfferItem offer)
        => new(
            offer.TradeOfferId,
            offer.Title,
            offer.ItemSummary,
            offer.Type switch
            {
                SteamOfferType.IncomingGift => "收到",
                SteamOfferType.Outgoing => "发出",
                SteamOfferType.TwoWay => "双向",
                _ => "未知"
            },
            offer.PartnerName,
            offer.RiskLevel switch
            {
                SteamOfferRisk.SafeIncoming => "安全收货",
                SteamOfferRisk.YouPinVerified => "悠悠已校验",
                _ => "未校验"
            },
            offer.Status switch
            {
                SteamOfferStatus.Accepted => "已同意",
                SteamOfferStatus.Denied => "已拒绝",
                _ => "待处理"
            },
            offer.VerifiedByYouPin,
            offer.CanAcceptSafely,
            offer.SafeReason,
            offer.FailureReason,
            offer.CreatedAt,
            offer.ItemsToGive.Count,
            offer.ItemsToReceive.Count,
            offer.ItemsToGive.Select(ItemName).Where(name => name.Length > 0).Take(6).ToArray(),
            offer.ItemsToReceive.Select(ItemName).Where(name => name.Length > 0).Take(6).ToArray());

    private static string ItemName(TradeAsset item)
        => string.IsNullOrWhiteSpace(item.MarketHashName) ? "未知饰品" : item.MarketHashName.Trim();

    private static string PreviewSteamId(string steamId)
    {
        string value = steamId?.Trim() ?? "";
        return value.Length <= 6 ? value : $"***{value[^6..]}";
    }

    private static string StateMessage(SteamOfferState state)
        => !string.IsNullOrWhiteSpace(state.LastError)
            ? state.LastError
            : !string.IsNullOrWhiteSpace(state.LastStatus)
                ? state.LastStatus
                : "Steam 报价服务已连接。";

    private static long Version(SteamOfferState state)
        => state.LastRefresh == default ? 0 : state.LastRefresh.ToUniversalTime().Ticks;
}
