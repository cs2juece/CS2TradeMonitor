namespace CS2TradeMonitor.Application.Abstractions
{
    public interface INetworkRecoverySignal
    {
        event Action? Recovered;
    }
}
