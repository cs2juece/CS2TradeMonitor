using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Links;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Reports;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit
{
    /// <summary>
    /// Coordinates one bounded audit while keeping public transport and report policy separate.
    /// </summary>
    public sealed class YouPinPrivacyAuditService
    {
        private readonly IYouPinPublicAuditClient _client;
        private readonly TimeProvider _timeProvider;

        public YouPinPrivacyAuditService(
            IYouPinPublicAuditClient client,
            TimeProvider? timeProvider = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public async Task<YouPinPrivacyAuditSnapshot> RunAsync(
            YouPinAuditSubject subject,
            IReadOnlyCollection<long> templateIds,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(subject);
            YouPinPurchaseAuditScope scope = YouPinPurchaseAuditScope.Create(templateIds);

            YouPinStoreReadResult storeRead;
            try
            {
                YouPinPublicStoreSummary summary = await _client.GetStoreSummaryAsync(
                    subject.UserId,
                    cancellationToken).ConfigureAwait(false);
                storeRead = YouPinStoreReadResult.Completed(summary);
            }
            catch (YouPinPublicApiException error)
            {
                storeRead = YouPinStoreReadResult.Failed(subject.UserId, error);
            }

            YouPinPurchaseExposureResult purchaseExposure =
                await _client.FindPurchaseExposureAsync(
                    subject.UserId,
                    scope,
                    cancellationToken).ConfigureAwait(false);

            return new YouPinPrivacyAuditSnapshot(
                subject,
                storeRead,
                purchaseExposure,
                _timeProvider.GetUtcNow());
        }

        public Task<YouPinPrivacyAuditSnapshot> DiscoverFromShopLinkAsync(
            string shopLink,
            IReadOnlyCollection<long> templateIds,
            CancellationToken cancellationToken = default)
        {
            YouPinShopLinkInfo link = YouPinShopLinkParser.Parse(shopLink);
            YouPinAuditSubject subject = YouPinAuditSubject.FromLink(link);
            return RunAsync(subject, templateIds, cancellationToken);
        }
    }
}
