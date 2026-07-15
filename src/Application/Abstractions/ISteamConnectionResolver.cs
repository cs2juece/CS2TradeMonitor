using CS2TradeMonitor.Domain.Steam;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface ISteamConnectionResolver
    {
        event Action? StatusChanged;

        SteamConnectionProfile GetSnapshot();

        SteamConnectionProfile GetBestKnownProfile();

        string GetManualProxyDisplay();

        bool SaveManualProxy(string proxyUri, out string message);

        void EnsureResolvedInBackground();

        Task<SteamConnectionProfile> ResolveAsync(bool force = false, CancellationToken cancellationToken = default);

        void ReportFailure(string reason);

        void ReportSuccess();
    }
}
