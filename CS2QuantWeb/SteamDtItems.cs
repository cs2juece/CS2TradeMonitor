using CS2MarketData.Core;
using CS2QuantWeb.Core;

namespace CS2QuantWeb;

public sealed class SteamDtItemCatalogProvider
{
    private const string CatalogFileName = "steamdt_items.json.gz";
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly IHostEnvironment? _environment;
    private readonly string? _explicitCatalogPath;
    private SteamDtItemCatalog? _catalog;

    public SteamDtItemCatalogProvider(IHostEnvironment environment)
    {
        _environment = environment;
    }

    internal SteamDtItemCatalogProvider(string catalogPath)
    {
        _explicitCatalogPath = catalogPath;
    }

    public async Task<IReadOnlyList<SteamDtCatalogItem>> SearchAsync(
        string? query,
        int limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            return [];
        SteamDtItemCatalog catalog = await GetCatalogAsync(cancellationToken);
        return catalog.Search(query, Math.Clamp(limit, 1, 30));
    }

    public async Task<SteamDtCatalogItem?> FindByMarketHashNameAsync(
        string? marketHashName,
        CancellationToken cancellationToken)
    {
        SteamDtItemCatalog catalog = await GetCatalogAsync(cancellationToken);
        return catalog.FindByMarketHashName(marketHashName);
    }

    private async Task<SteamDtItemCatalog> GetCatalogAsync(CancellationToken cancellationToken)
    {
        if (_catalog is not null)
            return _catalog;

        await _loadGate.WaitAsync(cancellationToken);
        try
        {
            if (_catalog is not null)
                return _catalog;
            string? catalogPath = ResolveCatalogPath();
            if (catalogPath is null)
            {
                throw new SeriesLoadException(
                    "未找到本地饰品搜索库，请重新安装完整版本。",
                    StatusCodes.Status503ServiceUnavailable);
            }

            try
            {
                await using FileStream file = File.OpenRead(catalogPath);
                _catalog = await SteamDtItemCatalog.LoadGzipAsync(file, cancellationToken);
                return _catalog;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                throw new SeriesLoadException(
                    "本地饰品搜索库读取失败，请重新安装完整版本。",
                    StatusCodes.Status503ServiceUnavailable);
            }
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private string? ResolveCatalogPath()
    {
        string? configured = _explicitCatalogPath
            ?? Environment.GetEnvironmentVariable("CS2_QUANT_ITEM_CATALOG_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            string fullPath = Path.GetFullPath(configured.Trim());
            return File.Exists(fullPath) ? fullPath : null;
        }

        string contentRoot = _environment?.ContentRootPath ?? AppContext.BaseDirectory;
        string baseDirectory = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(contentRoot, "resources", CatalogFileName),
            Path.Combine(contentRoot, "..", "resources", CatalogFileName),
            Path.Combine(baseDirectory, "resources", CatalogFileName),
            Path.Combine(baseDirectory, "..", "resources", CatalogFileName)
        ];
        return candidates
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);
    }
}

public sealed class SteamDtItemSeriesAdapter
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);
    private readonly SteamDtKlineClient _client;
    private readonly SteamDtItemCatalogProvider _catalog;
    private readonly Func<string?> _apiKeyProvider;
    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private readonly Dictionary<CacheKey, CacheEntry> _cache = [];

    public SteamDtItemSeriesAdapter(
        SteamDtKlineClient client,
        SteamDtItemCatalogProvider catalog)
        : this(client, catalog, ResolveApiKey)
    {
    }

    internal SteamDtItemSeriesAdapter(
        SteamDtKlineClient client,
        SteamDtItemCatalogProvider catalog,
        Func<string?> apiKeyProvider)
    {
        _client = client;
        _catalog = catalog;
        _apiKeyProvider = apiKeyProvider;
    }

    public async Task<LoadedSeries> LoadAsync(
        string? marketHashName,
        string? range,
        CancellationToken cancellationToken)
    {
        string normalizedName = marketHashName?.Trim() ?? string.Empty;
        if (normalizedName.Length == 0 || normalizedName.Length > 256)
            throw new SeriesLoadException("请先搜索并选择一个单品。");

        SteamDtCatalogItem? item = await _catalog.FindByMarketHashNameAsync(
            normalizedName,
            cancellationToken);
        if (item is null)
            throw new SeriesLoadException("未在本地饰品库找到该单品，请重新搜索后选择。", StatusCodes.Status404NotFound);

        SteamDtKlinePeriod period = ResolvePeriod(range);
        var cacheKey = new CacheKey(item.MarketHashName, period);

        await _cacheGate.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(cacheKey, out CacheEntry? cached)
                && DateTimeOffset.UtcNow < cached.ExpiresAt)
            {
                return cached.Series;
            }

            SteamDtKlineSeries response;
            try
            {
                response = await _client.FetchAsync(
                    item.MarketHashName,
                    _apiKeyProvider(),
                    period,
                    cancellationToken);
            }
            catch (SteamDtKlineException ex)
            {
                throw MapFailure(ex);
            }

            if (response.Candles.Count < 5)
            {
                throw new SeriesLoadException(
                    "SteamDT 响应中未识别到至少 5 根有效 OHLC K 线。",
                    StatusCodes.Status502BadGateway);
            }

            var series = new LoadedSeries(
                item.Name,
                "steamdt-item",
                response.Candles.Select(candle => new QuantCandle(
                    candle.Date,
                    candle.Open,
                    candle.High,
                    candle.Low,
                    candle.Close,
                    candle.Volume)).ToArray(),
                period == SteamDtKlinePeriod.Weekly ? CandleInterval.Week : CandleInterval.Day);
            RemoveExpiredCacheEntries();
            _cache[cacheKey] = new CacheEntry(series, DateTimeOffset.UtcNow.Add(CacheDuration));
            return series;
        }
        finally
        {
            _cacheGate.Release();
        }
    }

    private static string? ResolveApiKey()
    {
        return Environment.GetEnvironmentVariable("CS2_QUANT_STEAMDT_API_KEY");
    }

    private static SteamDtKlinePeriod ResolvePeriod(string? range)
    {
        string normalizedRange = string.IsNullOrWhiteSpace(range) ? "30" : range.Trim().ToLowerInvariant();
        return normalizedRange switch
        {
            "30" or "60" or "90" or "180" or "365" => SteamDtKlinePeriod.Daily,
            "730" or "all" => SteamDtKlinePeriod.Weekly,
            _ => throw new SeriesLoadException("显示范围无效，仅支持 30 天、60 天、90 天、半年、一年、两年和全部。")
        };
    }

    private static SeriesLoadException MapFailure(SteamDtKlineException exception)
    {
        return exception.Kind switch
        {
            SteamDtKlineFailureKind.MissingCredential => new SeriesLoadException(
                "未配置 SteamDT API Key。请先在桌面端“数据源”页面填写，再重新打开量化研究。",
                StatusCodes.Status503ServiceUnavailable),
            SteamDtKlineFailureKind.Authentication => new SeriesLoadException(
                "SteamDT API Key 无效或没有 K 线权限。",
                StatusCodes.Status502BadGateway),
            SteamDtKlineFailureKind.RateLimited => new SeriesLoadException(
                "SteamDT K 线调用频率已达上限，请稍后再试。",
                StatusCodes.Status429TooManyRequests),
            SteamDtKlineFailureKind.NoData => new SeriesLoadException(
                "SteamDT 未返回该单品的 K 线数据。",
                StatusCodes.Status404NotFound),
            _ => new SeriesLoadException(
                "SteamDT K 线读取失败，请稍后重试。",
                StatusCodes.Status502BadGateway)
        };
    }

    private void RemoveExpiredCacheEntries()
    {
        if (_cache.Count < 64)
            return;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (CacheKey key in _cache
            .Where(pair => pair.Value.ExpiresAt <= now)
            .Select(pair => pair.Key)
            .ToArray())
        {
            _cache.Remove(key);
        }
    }

    private readonly record struct CacheKey(string MarketHashName, SteamDtKlinePeriod Period)
    {
        public bool Equals(CacheKey other) =>
            Period == other.Period
            && StringComparer.OrdinalIgnoreCase.Equals(MarketHashName, other.MarketHashName);

        public override int GetHashCode() => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(MarketHashName),
            Period);
    }

    private sealed record CacheEntry(LoadedSeries Series, DateTimeOffset ExpiresAt);
}
