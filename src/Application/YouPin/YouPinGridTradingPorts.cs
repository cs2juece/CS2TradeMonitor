using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Application.YouPin
{
    internal interface IYouPinGridStrategyStore
    {
        YouPinGridState Load();

        bool Save(YouPinGridState state);
    }

    internal interface IYouPinGridMarketGateway
    {
        Task<YouPinGridMarketQuote> ReadLowestValidListingAsync(
            Settings settings,
            YouPinGridStrategy strategy,
            CancellationToken cancellationToken);
    }

    internal interface IYouPinGridExecutionJournal
    {
        YouPinGridExecutionRecord? FindActive(string strategyId);

        YouPinGridExecutionRecord? FindLatest(string strategyId);

        bool Save(YouPinGridExecutionRecord record);
    }

    internal interface IYouPinGridExecutionGateway
    {
        Task<YouPinGridExecutionRevalidation> RevalidateAsync(
            Settings settings,
            YouPinGridStrategy strategy,
            YouPinGridPlan plan,
            CancellationToken cancellationToken);

        Task<YouPinGridRemoteMutationResult> SubmitAsync(
            Settings settings,
            YouPinGridExecutionRecord prepared,
            YouPinGridExecutionRevalidation revalidation,
            CancellationToken cancellationToken);

        Task<YouPinGridRemoteSettlementResult> ReconcileAsync(
            Settings settings,
            YouPinGridExecutionRecord active,
            CancellationToken cancellationToken);
    }

    internal sealed record YouPinGridExecutionRevalidation(
        bool Ready,
        YouPinGridAction Action,
        int Quantity,
        decimal UnitPrice,
        decimal ReservedCapital,
        string TargetReference = "",
        string Message = "");

    internal sealed record YouPinGridRemoteMutationResult(
        bool Accepted,
        bool Settled,
        bool MayHaveChangedRemoteState,
        string RemoteReference,
        string Message);

    internal sealed record YouPinGridRemoteSettlementResult(
        YouPinGridExecutionStage Stage,
        decimal UnitPrice,
        string Message);
}
