using CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring
{
    /// <summary>
    /// Executes one bounded Scan Cycle. Scheduling and persistence remain host responsibilities.
    /// </summary>
    public sealed class YouPinPurchaseWatchService
    {
        private readonly IYouPinPublicAuditClient _client;
        private readonly TimeProvider _timeProvider;

        public YouPinPurchaseWatchService(
            IYouPinPublicAuditClient client,
            TimeProvider? timeProvider = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public async Task<YouPinPurchaseWatchSnapshot> ScanAsync(
            YouPinAuthorizedWatch watch,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(watch);

            YouPinPurchaseExposureResult exposure = await _client.FindPurchaseExposureAsync(
                watch.Subject.UserId,
                watch.Scope,
                cancellationToken).ConfigureAwait(false);

            return YouPinPurchaseWatchSnapshot.Create(
                watch,
                exposure,
                _timeProvider.GetUtcNow());
        }
    }
}
