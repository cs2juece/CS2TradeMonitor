using System.Net;

namespace CS2TradeMonitor.Domain.Steam
{
    public sealed class SteamEndpointConnectionSnapshot
    {
        public string Host { get; init; } = "";
        public IPAddress? EndpointAddress { get; init; }
        public string AddressSource { get; init; } = "";
        public bool UsedFallbackDns { get; init; }
        public long ConnectionGeneration { get; init; }
        public int AttemptCount { get; init; }
        public DateTime LastAttemptAt { get; init; } = DateTime.MinValue;
        public string FailureReason { get; init; } = "";
        public bool IsConnected => EndpointAddress is not null && string.IsNullOrWhiteSpace(FailureReason);
    }

    public sealed record SteamEndpointConnectionIdentity(
        string Host,
        IPAddress Address,
        long Generation);
}
