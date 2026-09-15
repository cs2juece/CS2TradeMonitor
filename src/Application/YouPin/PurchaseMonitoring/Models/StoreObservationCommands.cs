namespace YouPinPurchaseMonitor.Models;

internal sealed record StoreObservationDraft(
    string RawLink,
    string SafeNote,
    WatchPurposeChoice Purpose,
    ObservationScopeChoice Scope,
    int IntervalMinutes);

internal sealed class StoreObservationDraftEventArgs : EventArgs
{
    internal StoreObservationDraftEventArgs(StoreObservationDraft draft) => Draft = draft;
    internal StoreObservationDraft Draft { get; }
}

internal sealed class StoreObservationActionEventArgs : EventArgs
{
    internal StoreObservationActionEventArgs(Guid watchId) => WatchId = watchId;
    internal Guid WatchId { get; }
}

internal sealed class StoreObservationReauthorizationEventArgs : EventArgs
{
    internal StoreObservationReauthorizationEventArgs(Guid watchId, string rawLink)
    {
        WatchId = watchId;
        RawLink = rawLink;
    }

    internal Guid WatchId { get; }
    internal string RawLink { get; }
}
