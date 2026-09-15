using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage;
using YouPinPurchaseMonitor.Models;

namespace YouPinPurchaseMonitor.Infrastructure;

public sealed class CoveragePlanLoader
{
    public async Task<YouPinHotCoveragePlan> LoadAsync(
        string catalogPath,
        string rawDirectory,
        CancellationToken cancellationToken = default)
    {
        CoveragePlanInputs inputs = await LoadInputsAsync(
            catalogPath,
            rawDirectory,
            cancellationToken).ConfigureAwait(false);
        return inputs.CreatePlan(ObservationScopeChoice.Top1000);
    }

    public Task<CoveragePlanInputs> LoadInputsAsync(
        string catalogPath,
        string rawDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(catalogPath, MonitorPaths.EmbeddedCatalog, StringComparison.Ordinal)
            || !string.Equals(rawDirectory, MonitorPaths.EmbeddedMappings, StringComparison.Ordinal))
        {
            throw new InvalidDataException("集成版本仅接受随程序发布并经过校验的内嵌 Top 1000 覆盖计划。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return LoadEmbeddedAsync(cancellationToken);
    }

    private static async Task<CoveragePlanInputs> LoadEmbeddedAsync(
        CancellationToken cancellationToken)
    {
        YouPinEmbeddedCoveragePlanAsset asset = await YouPinEmbeddedCoveragePlanLoader.LoadAsync(
            cancellationToken).ConfigureAwait(false);
        return new CoveragePlanInputs(asset.Catalog, asset.Mappings);
    }
}

public sealed class CoveragePlanInputs
{
    private readonly IReadOnlyList<YouPinTemplateMapping> _mappings;

    internal CoveragePlanInputs(
        YouPinHotCatalog catalog,
        IReadOnlyList<YouPinTemplateMapping> mappings)
    {
        Catalog = catalog;
        _mappings = mappings;
    }

    public YouPinHotCatalog Catalog { get; }

    public YouPinHotCoveragePlan CreatePlan(
        ObservationScopeChoice scope,
        IReadOnlyCollection<long>? observedTemplateIds = null)
        => scope switch
        {
            ObservationScopeChoice.Top100 => YouPinHotCoveragePlan.CreateTopRanked(
                Catalog,
                _mappings,
                100),
            ObservationScopeChoice.Top300 => YouPinHotCoveragePlan.CreateTopRanked(
                Catalog,
                _mappings,
                300),
            ObservationScopeChoice.Top1000 => YouPinHotCoveragePlan.Create(
                Catalog,
                _mappings,
                YouPinHotCoverageOrdering.PopularityRank),
            ObservationScopeChoice.ObservedOnly when observedTemplateIds is { Count: > 0 }
                => YouPinHotCoveragePlan.CreateForTemplateIds(
                    Catalog,
                    _mappings,
                    observedTemplateIds),
            ObservationScopeChoice.ObservedOnly => throw new InvalidOperationException(
                "尚未观察到可建立优先范围的模板。"),
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };
}
