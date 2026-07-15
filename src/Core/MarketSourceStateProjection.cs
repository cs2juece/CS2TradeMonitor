using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Market;

namespace CS2TradeMonitor.src.Core
{
    internal sealed record MarketSourceStateSnapshot
    {
        public string Id { get; init; } = string.Empty;

        public string DisplayKey { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string CredentialName { get; init; } = string.Empty;

        public string TypeDescription { get; init; } = string.Empty;

        public string ConfigState { get; init; } = string.Empty;

        public string Status { get; init; } = "未获取";

        public string LastRefresh { get; init; } = string.Empty;

        public string LastError { get; init; } = string.Empty;

        public string Source { get; init; } = string.Empty;

        public bool HasCredential { get; init; }

        public bool HasData { get; init; }

        public bool IsStale { get; init; }

        public int RefreshIntervalSeconds { get; init; } = Settings.DefaultMarketRefreshSec;

        public double Index { get; init; }

        public double Change { get; init; }

        public double Percent { get; init; }

        public DateTime RetrievedAt { get; init; }
    }

    internal sealed record MarketSourceStatesSnapshot(
        MarketSourceStateSnapshot SteamDt,
        MarketSourceStateSnapshot Csqaq)
    {
        public IReadOnlyList<MarketSourceStateSnapshot> All { get; } =
            Array.AsReadOnly(new[] { Csqaq, SteamDt });
    }

    internal readonly record struct MarketSourceStateConfig
    {
        public MarketSourceStateConfig(bool hasCredential, int refreshIntervalSeconds)
        {
            HasCredential = hasCredential;
            RefreshIntervalSeconds = NormalizeRefreshInterval(refreshIntervalSeconds);
        }

        public bool HasCredential { get; }

        public int RefreshIntervalSeconds { get; }

        private static int NormalizeRefreshInterval(int seconds)
        {
            return seconds <= 0
                ? Settings.DefaultMarketRefreshSec
                : Math.Max(Settings.DefaultMarketRefreshSec, seconds);
        }
    }

    internal interface IMarketSourceStateAdapter
    {
        string Id { get; }

        MarketSourceStateSnapshot Capture(MarketSourceStateConfig config);
    }

    internal sealed class MarketSourceStateProjection
    {
        private readonly IMarketSourceStateAdapter _steamDt;
        private readonly IMarketSourceStateAdapter _csqaq;

        public MarketSourceStateProjection(
            IMarketSourceStateAdapter steamDt,
            IMarketSourceStateAdapter csqaq)
        {
            _steamDt = steamDt ?? throw new ArgumentNullException(nameof(steamDt));
            _csqaq = csqaq ?? throw new ArgumentNullException(nameof(csqaq));

            if (!string.Equals(_steamDt.Id, MarketDataSourceManager.SteamDtId, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("SteamDT adapter id is invalid.", nameof(steamDt));
            if (!string.Equals(_csqaq.Id, MarketDataSourceManager.QaqId, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("CSQAQ adapter id is invalid.", nameof(csqaq));
        }

        public MarketSourceStatesSnapshot Capture(
            MarketSourceStateConfig steamDtConfig,
            MarketSourceStateConfig csqaqConfig)
        {
            return new MarketSourceStatesSnapshot(
                _steamDt.Capture(steamDtConfig),
                _csqaq.Capture(csqaqConfig));
        }

        public MarketSourceStateSnapshot Capture(string sourceId, MarketSourceStateConfig config)
        {
            if (string.Equals(sourceId, MarketDataSourceManager.SteamDtId, StringComparison.OrdinalIgnoreCase))
                return _steamDt.Capture(config);
            if (string.Equals(sourceId, MarketDataSourceManager.QaqId, StringComparison.OrdinalIgnoreCase))
                return _csqaq.Capture(config);

            throw new ArgumentOutOfRangeException(nameof(sourceId), sourceId, "Unknown market source id.");
        }
    }

    internal sealed class SteamDtMarketSourceStateAdapter : IMarketSourceStateAdapter
    {
        private readonly ISteamDtService _service;

        public SteamDtMarketSourceStateAdapter(ISteamDtService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public string Id => MarketDataSourceManager.SteamDtId;

        public MarketSourceStateSnapshot Capture(MarketSourceStateConfig config)
        {
            SteamDtData? latest = _service.Latest;
            string lastError = _service.LastError ?? string.Empty;
            string status = latest != null
                ? latest.IsStale ? "异常" : "正常"
                : string.IsNullOrEmpty(lastError) ? "未获取" : "异常";
            string typeDescription = latest?.Source
                ?? (config.HasCredential ? "官方 API" : "公开页面接口");

            return new MarketSourceStateSnapshot
            {
                Id = Id,
                DisplayKey = MarketDataSourceManager.SteamDtDisplayKey,
                DisplayName = "SteamDT",
                CredentialName = "SteamDT API Key",
                TypeDescription = typeDescription,
                ConfigState = config.HasCredential ? "已配置 API Key" : "未配置 API Key",
                Status = status,
                LastRefresh = latest?.RetrievedAt.ToString("HH:mm:ss") ?? string.Empty,
                LastError = lastError,
                Source = latest?.Source ?? (config.HasCredential ? "官方 API" : "公开接口"),
                HasCredential = config.HasCredential,
                HasData = latest != null,
                IsStale = latest?.IsStale ?? false,
                RefreshIntervalSeconds = config.RefreshIntervalSeconds,
                Index = latest?.Index ?? 0,
                Change = latest?.DiffYesterday ?? 0,
                Percent = latest?.DiffYesterdayRatio ?? 0,
                RetrievedAt = latest?.RetrievedAt ?? DateTime.MinValue
            };
        }
    }

    internal sealed class CsqaqMarketSourceStateAdapter : IMarketSourceStateAdapter
    {
        private readonly ICsqaqService _service;

        public CsqaqMarketSourceStateAdapter(ICsqaqService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public string Id => MarketDataSourceManager.QaqId;

        public MarketSourceStateSnapshot Capture(MarketSourceStateConfig config)
        {
            CsqaqData? latest = _service.Latest;
            string lastError = _service.LastError ?? string.Empty;
            string status = !string.IsNullOrEmpty(lastError)
                ? "异常"
                : latest != null ? "正常" : "未获取";
            string typeDescription = config.HasCredential ? "API Token 接口" : "公开接口/无需 API";

            return new MarketSourceStateSnapshot
            {
                Id = Id,
                DisplayKey = MarketDataSourceManager.QaqDisplayKey,
                DisplayName = "QAQ",
                CredentialName = "QAQ API Token",
                TypeDescription = typeDescription,
                ConfigState = config.HasCredential ? "已配置 Token" : "未配置 Token",
                Status = status,
                LastRefresh = latest?.RetrievedAt.ToString("HH:mm:ss") ?? string.Empty,
                LastError = lastError,
                Source = latest?.Source ?? (config.HasCredential ? "官方 API" : "公开接口"),
                HasCredential = config.HasCredential,
                HasData = latest != null,
                IsStale = latest?.IsStale ?? false,
                RefreshIntervalSeconds = config.RefreshIntervalSeconds,
                Index = latest?.MarketIndex ?? 0,
                Change = latest?.Change ?? 0,
                Percent = latest?.ChangeRate ?? 0,
                RetrievedAt = latest?.RetrievedAt ?? DateTime.MinValue
            };
        }
    }
}
