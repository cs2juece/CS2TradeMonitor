namespace CS2TradeMonitor.Application.Abstractions
{
    public interface ISteamManualProxyStore
    {
        string Load();

        void Save(string proxyUri);

        void Clear();
    }
}
