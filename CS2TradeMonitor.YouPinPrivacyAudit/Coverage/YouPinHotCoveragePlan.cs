using CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi;
using System.Security.Cryptography;
using System.Text;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage
{
    public enum YouPinHotCoverageOrdering
    {
        PopularityRank,
        CategoryRoundRobin
    }

    /// <summary>
    /// Offline, immutable plan that resolves a frozen Hot Catalog into bounded Scan Batches.
    /// It has no Audit Subject and cannot perform network requests by itself.
    /// </summary>
    public sealed class YouPinHotCoveragePlan
    {
        private YouPinHotCoveragePlan(
            YouPinHotCatalog catalog,
            int candidateCount,
            YouPinHotCoverageOrdering ordering,
            int batchSize,
            string fingerprint,
            IReadOnlyList<YouPinResolvedHotCatalogItem> orderedResolvedItems,
            IReadOnlyList<YouPinHotCatalogItem> unresolvedItems)
        {
            Catalog = catalog;
            CandidateCount = candidateCount;
            Ordering = ordering;
            BatchSize = batchSize;
            Fingerprint = fingerprint;
            OrderedResolvedItems = orderedResolvedItems;
            UnresolvedItems = unresolvedItems;
            BatchCount = (orderedResolvedItems.Count + batchSize - 1) / batchSize;
        }

        public YouPinHotCatalog Catalog { get; }
        public YouPinHotCoverageOrdering Ordering { get; }
        public int BatchSize { get; }
        public string Fingerprint { get; }
        public IReadOnlyList<YouPinResolvedHotCatalogItem> OrderedResolvedItems { get; }
        public IReadOnlyList<YouPinHotCatalogItem> UnresolvedItems { get; }
        public int CandidateCount { get; }
        public int ResolvedCount => OrderedResolvedItems.Count;
        public int UnresolvedCount => UnresolvedItems.Count;
        public int BatchCount { get; }
        public bool IsFullyResolved => UnresolvedCount == 0;

        public static YouPinHotCoveragePlan Create(
            YouPinHotCatalog catalog,
            IReadOnlyCollection<YouPinTemplateMapping> mappings,
            YouPinHotCoverageOrdering ordering = YouPinHotCoverageOrdering.CategoryRoundRobin,
            int batchSize = YouPinPurchaseAuditScope.MaximumTemplateCount)
            => CreateSelected(
                catalog,
                mappings,
                catalog.Items,
                "all",
                ordering,
                batchSize);

        public static YouPinHotCoveragePlan CreateTopRanked(
            YouPinHotCatalog catalog,
            IReadOnlyCollection<YouPinTemplateMapping> mappings,
            int maximumRank,
            YouPinHotCoverageOrdering ordering = YouPinHotCoverageOrdering.PopularityRank,
            int batchSize = YouPinPurchaseAuditScope.MaximumTemplateCount)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            if (maximumRank <= 0 || maximumRank > catalog.Items.Count)
                throw new ArgumentOutOfRangeException(nameof(maximumRank));
            return CreateSelected(
                catalog,
                mappings,
                catalog.Items.Where(item => item.Rank <= maximumRank).ToArray(),
                $"top:{maximumRank}",
                ordering,
                batchSize);
        }

        public static YouPinHotCoveragePlan CreateForTemplateIds(
            YouPinHotCatalog catalog,
            IReadOnlyCollection<YouPinTemplateMapping> mappings,
            IReadOnlyCollection<long> templateIds,
            YouPinHotCoverageOrdering ordering = YouPinHotCoverageOrdering.PopularityRank,
            int batchSize = YouPinPurchaseAuditScope.MaximumTemplateCount)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            ArgumentNullException.ThrowIfNull(mappings);
            ArgumentNullException.ThrowIfNull(templateIds);
            long[] selectedIds = templateIds.Distinct().Order().ToArray();
            if (selectedIds.Length == 0 || selectedIds.Any(templateId => templateId <= 0))
                throw new ArgumentException("优先模板范围必须包含正整数模板 ID。", nameof(templateIds));

            var selectedSet = selectedIds.ToHashSet();
            var nameByTemplateId = mappings
                .Where(mapping => selectedSet.Contains(mapping.TemplateId))
                .ToDictionary(mapping => mapping.TemplateId, mapping => mapping.MarketHashName);
            if (nameByTemplateId.Count != selectedIds.Length)
                throw new ArgumentException("优先模板范围包含未精确映射的模板。", nameof(templateIds));
            var selectedNames = nameByTemplateId.Values.ToHashSet(StringComparer.Ordinal);
            YouPinHotCatalogItem[] selectedItems = catalog.Items
                .Where(item => selectedNames.Contains(item.MarketHashName))
                .ToArray();
            if (selectedItems.Length != selectedIds.Length)
                throw new ArgumentException("优先模板范围包含冻结热门目录之外的模板。", nameof(templateIds));

            return CreateSelected(
                catalog,
                mappings,
                selectedItems,
                "templates:" + string.Join(',', selectedIds),
                ordering,
                batchSize);
        }

        private static YouPinHotCoveragePlan CreateSelected(
            YouPinHotCatalog catalog,
            IReadOnlyCollection<YouPinTemplateMapping> mappings,
            IReadOnlyCollection<YouPinHotCatalogItem> selectedItems,
            string selectionDescriptor,
            YouPinHotCoverageOrdering ordering,
            int batchSize)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            ArgumentNullException.ThrowIfNull(mappings);
            ArgumentNullException.ThrowIfNull(selectedItems);
            ArgumentException.ThrowIfNullOrWhiteSpace(selectionDescriptor);
            if (selectedItems.Count == 0)
                throw new ArgumentException("覆盖计划至少需要一个候选模板。", nameof(selectedItems));
            if (!Enum.IsDefined(ordering))
                throw new ArgumentOutOfRangeException(nameof(ordering));
            if (batchSize <= 0 || batchSize > YouPinPurchaseAuditScope.MaximumTemplateCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(batchSize),
                    $"覆盖批次必须包含 1 到 {YouPinPurchaseAuditScope.MaximumTemplateCount} 个模板。");
            }

            var catalogNames = selectedItems
                .Select(item => item.MarketHashName)
                .ToHashSet(StringComparer.Ordinal);
            var mappingByName = new Dictionary<string, long>(StringComparer.Ordinal);

            foreach (YouPinTemplateMapping mapping in mappings)
            {
                ArgumentNullException.ThrowIfNull(mapping);
                if (!catalogNames.Contains(mapping.MarketHashName))
                    continue;

                if (mappingByName.TryGetValue(mapping.MarketHashName, out long existingId)
                    && existingId != mapping.TemplateId)
                {
                    throw new ArgumentException(
                        "同一 market_hash_name 对应了不同的悠悠模板 ID。",
                        nameof(mappings));
                }

                mappingByName[mapping.MarketHashName] = mapping.TemplateId;
            }

            var templateOwners = new Dictionary<long, string>();
            var resolvedByRank = new List<YouPinResolvedHotCatalogItem>(mappingByName.Count);
            var unresolved = new List<YouPinHotCatalogItem>();

            foreach (YouPinHotCatalogItem item in selectedItems.OrderBy(item => item.Rank))
            {
                if (!mappingByName.TryGetValue(item.MarketHashName, out long templateId))
                {
                    unresolved.Add(item);
                    continue;
                }

                if (templateOwners.TryGetValue(templateId, out string? owner)
                    && !string.Equals(owner, item.MarketHashName, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "同一悠悠模板 ID 对应了不同的 market_hash_name。",
                        nameof(mappings));
                }

                templateOwners[templateId] = item.MarketHashName;
                resolvedByRank.Add(new YouPinResolvedHotCatalogItem(item, templateId));
            }

            IReadOnlyList<YouPinResolvedHotCatalogItem> ordered = ordering switch
            {
                YouPinHotCoverageOrdering.PopularityRank => resolvedByRank,
                YouPinHotCoverageOrdering.CategoryRoundRobin =>
                    OrderByCategoryRoundRobin(resolvedByRank),
                _ => throw new ArgumentOutOfRangeException(nameof(ordering))
            };
            string fingerprint = CreateFingerprint(
                catalog,
                selectionDescriptor,
                ordering,
                batchSize,
                resolvedByRank);

            return new YouPinHotCoveragePlan(
                catalog,
                selectedItems.Count,
                ordering,
                batchSize,
                fingerprint,
                Array.AsReadOnly(ordered.ToArray()),
                Array.AsReadOnly(unresolved.ToArray()));
        }

        public YouPinHotCoverageBatch GetBatchByCursor(int cursor)
        {
            if (BatchCount == 0)
                throw new InvalidOperationException("热门覆盖计划尚无已映射模板。");
            if (cursor < 0 || cursor >= BatchCount)
                throw new ArgumentOutOfRangeException(nameof(cursor));

            YouPinResolvedHotCatalogItem[] entries = OrderedResolvedItems
                .Skip(cursor * BatchSize)
                .Take(BatchSize)
                .ToArray();
            return new YouPinHotCoverageBatch(
                Fingerprint,
                cursor,
                BatchCount,
                Array.AsReadOnly(entries));
        }

        private static IReadOnlyList<YouPinResolvedHotCatalogItem> OrderByCategoryRoundRobin(
            IReadOnlyCollection<YouPinResolvedHotCatalogItem> items)
        {
            Queue<YouPinResolvedHotCatalogItem>[] categories = items
                .GroupBy(item => item.Category, StringComparer.Ordinal)
                .OrderBy(group => group.Min(item => item.Rank))
                .Select(group => new Queue<YouPinResolvedHotCatalogItem>(
                    group.OrderBy(item => item.Rank)))
                .ToArray();
            var ordered = new List<YouPinResolvedHotCatalogItem>(items.Count);

            while (ordered.Count < items.Count)
            {
                foreach (Queue<YouPinResolvedHotCatalogItem> category in categories)
                {
                    if (category.TryDequeue(out YouPinResolvedHotCatalogItem? item))
                        ordered.Add(item);
                }
            }

            return ordered;
        }

        private static string CreateFingerprint(
            YouPinHotCatalog catalog,
            string selectionDescriptor,
            YouPinHotCoverageOrdering ordering,
            int batchSize,
            IReadOnlyCollection<YouPinResolvedHotCatalogItem> resolvedByRank)
        {
            var canonical = new StringBuilder();
            canonical
                .Append(catalog.SourceSha256)
                .Append('|')
                .Append(selectionDescriptor)
                .Append('|')
                .Append(ordering)
                .Append('|')
                .Append(batchSize)
                .Append('\n');

            foreach (YouPinResolvedHotCatalogItem item in resolvedByRank.OrderBy(item => item.Rank))
            {
                canonical
                    .Append(item.Rank)
                    .Append('|')
                    .Append(item.TemplateId)
                    .Append('|')
                    .Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(item.MarketHashName)))
                    .Append('\n');
            }

            return Convert
                .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
                .ToLowerInvariant();
        }
    }
}
