using System.IO.Compression;
using System.Reflection;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage
{
    public sealed record YouPinEmbeddedCoveragePlanAsset(
        YouPinHotCatalog Catalog,
        IReadOnlyList<YouPinTemplateMapping> Mappings)
    {
        public YouPinHotCoveragePlan CreateTopRanked(int maximumItems = YouPinHotCatalog.MaximumItemCount)
            => YouPinHotCoveragePlan.CreateTopRanked(Catalog, Mappings, maximumItems);
    }

    public static class YouPinEmbeddedCoveragePlanLoader
    {
        public const string CatalogResourceName =
            "CS2TradeMonitor.YouPinPrivacyAudit.Resources.hot-top1000.catalog.json.gz";

        public const string MappingResourceName =
            "CS2TradeMonitor.YouPinPrivacyAudit.Resources.hot-top1000.youpin-mapping.json.gz";

        public static async Task<YouPinEmbeddedCoveragePlanAsset> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            Assembly assembly = typeof(YouPinEmbeddedCoveragePlanLoader).Assembly;
            await using Stream catalogResource = RequireResource(assembly, CatalogResourceName);
            await using var catalogGzip = new GZipStream(
                catalogResource,
                CompressionMode.Decompress,
                leaveOpen: false);
            YouPinHotCatalog catalog = await YouPinHotCatalog.LoadSteamDtAsync(
                catalogGzip,
                cancellationToken).ConfigureAwait(false);

            await using Stream mappingResource = RequireResource(assembly, MappingResourceName);
            await using var mappingGzip = new GZipStream(
                mappingResource,
                CompressionMode.Decompress,
                leaveOpen: false);
            IReadOnlyList<YouPinTemplateMapping> mappings =
                await YouPinTemplateMappingJsonReader.LoadAsync(
                    mappingGzip,
                    cancellationToken).ConfigureAwait(false);

            YouPinHotCoveragePlan validationPlan = YouPinHotCoveragePlan.CreateTopRanked(
                catalog,
                mappings,
                YouPinHotCatalog.MaximumItemCount);
            if (!validationPlan.IsFullyResolved
                || validationPlan.CandidateCount != YouPinHotCatalog.MaximumItemCount)
            {
                throw new InvalidDataException(
                    $"内置求购覆盖计划无效：{validationPlan.ResolvedCount}/{validationPlan.CandidateCount}。");
            }

            return new YouPinEmbeddedCoveragePlanAsset(catalog, mappings);
        }

        private static Stream RequireResource(Assembly assembly, string resourceName)
            => assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidDataException($"找不到内置求购覆盖资源：{resourceName}");
    }
}
