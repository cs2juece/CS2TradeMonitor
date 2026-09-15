using CS2TradeMonitor.Application.Abstractions;
using YouPinPurchaseMonitor.Models;

namespace YouPinPurchaseMonitor.Infrastructure;

public sealed record MonitorPaths(
    string ApplicationDirectory,
    string SettingsPath,
    string DefaultCatalogPath,
    string DefaultRawDirectory,
    string DefaultReportDirectory,
    string DefaultStateDirectory,
    string LegacyReportDirectory)
{
    public const string EmbeddedCatalog = "embedded://hot-top1000.catalog";
    public const string EmbeddedMappings = "embedded://hot-top1000.youpin-mapping";

    public static MonitorPaths Create(IAppDataPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        string applicationDirectory = Path.Combine(
            pathProvider.DataDirectory,
            "youpin-purchase-monitoring");
        return new MonitorPaths(
            applicationDirectory,
            Path.Combine(applicationDirectory, "settings.json"),
            EmbeddedCatalog,
            EmbeddedMappings,
            Path.Combine(applicationDirectory, "reports"),
            Path.Combine(applicationDirectory, "state"),
            Path.Combine(applicationDirectory, "reports"));
    }

    public AppSettings CreateDefaultSettings() => new(
        DefaultCatalogPath,
        DefaultRawDirectory,
        DefaultReportDirectory,
        DefaultStateDirectory,
        30,
        WatchPurposeChoice.SelfAudit);
}
