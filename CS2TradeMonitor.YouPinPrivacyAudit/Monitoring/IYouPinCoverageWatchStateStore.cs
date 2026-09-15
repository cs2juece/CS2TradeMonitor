namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring
{
    /// <summary>
    /// Persists only the redacted state keyed by Watch ID. The Audit Subject is supplied by the
    /// authorized host for every run and is deliberately absent from this seam.
    /// </summary>
    public interface IYouPinCoverageWatchStateStore
    {
        Task<YouPinCoverageWatchState?> LoadAsync(
            Guid watchId,
            CancellationToken cancellationToken = default);

        Task SaveAsync(
            YouPinCoverageWatchState state,
            CancellationToken cancellationToken = default);
    }
}
