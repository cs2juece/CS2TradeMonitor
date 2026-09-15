using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Links;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Reports;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Safety;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage
{
    /// <summary>
    /// Runs one explicitly requested Full Coverage Audit. Every network call still receives
    /// one ordinary bounded Scan Batch and all batches execute sequentially.
    /// </summary>
    public sealed class YouPinHotCoverageAuditService
    {
        private readonly IYouPinPublicAuditClient _client;
        private readonly TimeProvider _timeProvider;

        public YouPinHotCoverageAuditService(
            IYouPinPublicAuditClient client,
            TimeProvider? timeProvider = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public Task<YouPinHotCoverageAuditSnapshot> RunFullCoverageFromShopLinkAsync(
            string shopLink,
            YouPinHotCoveragePlan plan,
            CancellationToken cancellationToken = default)
        {
            YouPinShopLinkInfo link = YouPinShopLinkParser.Parse(shopLink);
            return RunFullCoverageAsync(
                YouPinAuditSubject.FromLink(link),
                plan,
                progress: null,
                cancellationToken);
        }

        public Task<YouPinHotCoverageAuditSnapshot> RunFullCoverageFromShopLinkAsync(
            string shopLink,
            YouPinHotCoveragePlan plan,
            IProgress<YouPinHotCoverageProgress> progress,
            CancellationToken cancellationToken = default)
        {
            YouPinShopLinkInfo link = YouPinShopLinkParser.Parse(shopLink);
            return RunFullCoverageAsync(
                YouPinAuditSubject.FromLink(link),
                plan,
                progress,
                cancellationToken);
        }

        public async Task<YouPinHotCoverageAuditSnapshot> RunFullCoverageAsync(
            YouPinAuditSubject subject,
            YouPinHotCoveragePlan plan,
            CancellationToken cancellationToken = default)
            => await RunFullCoverageAsync(
                subject,
                plan,
                progress: null,
                cancellationToken).ConfigureAwait(false);

        public async Task<YouPinHotCoverageAuditSnapshot> RunFullCoverageAsync(
            YouPinAuditSubject subject,
            YouPinHotCoveragePlan plan,
            IProgress<YouPinHotCoverageProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(plan);
            if (plan.ResolvedCount == 0)
                throw new InvalidOperationException("热门覆盖计划没有可查询的已映射模板。");

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

            Dictionary<long, YouPinResolvedHotCatalogItem> itemByTemplateId = plan
                .OrderedResolvedItems
                .ToDictionary(item => item.TemplateId);
            var completedTemplateIds = new List<long>(plan.ResolvedCount);
            var incompleteTemplateIds = new List<long>();
            var observations = new List<YouPinHotPurchaseObservation>();
            var failures = new List<YouPinTemplateQueryFailure>();

            for (int cursor = 0; cursor < plan.BatchCount; cursor++)
            {
                YouPinHotCoverageBatch batch = plan.GetBatchByCursor(cursor);
                YouPinPurchaseExposureResult exposure =
                    await _client.FindPurchaseExposureAsync(
                        subject.UserId,
                        batch.Scope,
                        cancellationToken).ConfigureAwait(false);
                var failedSet = exposure.Failures
                    .Select(failure => failure.TemplateId)
                    .ToHashSet();

                completedTemplateIds.AddRange(exposure.RequestedTemplateIds
                    .Where(templateId => !failedSet.Contains(templateId)));
                incompleteTemplateIds.AddRange(failedSet);
                failures.AddRange(exposure.Failures);

                foreach (YouPinPurchaseExposureMatch match in exposure.Matches)
                {
                    YouPinResolvedHotCatalogItem item = itemByTemplateId[match.TemplateId];
                    observations.Add(new YouPinHotPurchaseObservation(
                        item.Rank,
                        item.TemplateId,
                        item.MarketHashName,
                        item.Category,
                        YouPinSafeText.Normalize(match.CommodityName, 200),
                        match.PurchasePrice,
                        match.SurplusQuantity,
                        YouPinSafeText.Normalize(match.AbradeText, 80),
                        YouPinSafeText.Normalize(match.FadeText, 80),
                        match.AutoReceived));
                }

                progress?.Report(new YouPinHotCoverageProgress(
                    cursor + 1,
                    plan.BatchCount,
                    completedTemplateIds.Count,
                    incompleteTemplateIds.Count,
                    observations.Count));
            }

            YouPinHotPurchaseObservation[] orderedObservations = observations
                .OrderBy(observation => observation.Rank)
                .ThenBy(observation => observation.PurchasePrice)
                .ThenBy(observation => observation.SurplusQuantity)
                .ToArray();

            return new YouPinHotCoverageAuditSnapshot(
                subject,
                storeRead,
                plan,
                Array.AsReadOnly(completedTemplateIds.ToArray()),
                Array.AsReadOnly(incompleteTemplateIds.ToArray()),
                Array.AsReadOnly(orderedObservations),
                Array.AsReadOnly(failures.ToArray()),
                _timeProvider.GetUtcNow());
        }
    }
}
