using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CS2MarketData.Core;
using CS2TradeMonitor.Shared.Core;

namespace CS2TradeMonitor.Shared.Market;

/// <summary>
/// Platform-neutral implementation of the desktop SteamDT and QAQ wire
/// contracts. Android and Windows supply the HTTP transport only.
/// </summary>
public sealed class PortableMarketDataClient : IMarketDataClient
{
    private const string SteamDtOpenBase = "https://open.steamdt.com";
    private const string SteamDtWebBase = "https://www.steamdt.com";
    private const string QaqApiBase = "https://api.csqaq.com";
    private readonly HttpClient _http;
    private readonly SteamDtItemCatalog? _suppliedLocalCatalog;
    private readonly bool _useEmbeddedCatalog;
    private readonly SemaphoreSlim _steamDtGate = new(1, 1);
    private readonly SemaphoreSlim _qaqGate = new(1, 1);
    private Task<SteamDtItemCatalog?>? _localCatalogTask;
    private IReadOnlyList<OfficialBaseItem> _officialBaseItems = [];
    private DateTime _officialBaseUpdatedUtc = DateTime.MinValue;

    public PortableMarketDataClient(HttpClient http)
        : this(http, localCatalog: null, useEmbeddedCatalog: true)
    {
    }

    internal PortableMarketDataClient(
        HttpClient http,
        SteamDtItemCatalog? localCatalog,
        bool useEmbeddedCatalog)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _suppliedLocalCatalog = localCatalog;
        _useEmbeddedCatalog = useEmbeddedCatalog;
        if (_http.Timeout == Timeout.InfiniteTimeSpan || _http.Timeout > TimeSpan.FromSeconds(20))
            _http.Timeout = TimeSpan.FromSeconds(20);
    }

    public async Task<IReadOnlyList<MarketItemCandidate>> SearchItemsAsync(
        string keyword,
        string steamDtApiKey,
        CancellationToken cancellationToken = default)
    {
        keyword = keyword?.Trim() ?? "";
        if (keyword.Length == 0)
            return [];

        await _steamDtGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var results = new List<MarketItemCandidate>();
            var seenMarketHashNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(steamDtApiKey))
            {
                IReadOnlyList<OfficialBaseItem> officialItems = await GetOfficialBaseItemsAsync(
                    steamDtApiKey.Trim(),
                    cancellationToken).ConfigureAwait(false);
                foreach (OfficialBaseItem item in officialItems)
                {
                    if (string.IsNullOrWhiteSpace(item.MarketHashName)
                        || !SteamDtItemCatalog.Matches(item.Name, item.MarketHashName, keyword)
                        || !seenMarketHashNames.Add(item.MarketHashName))
                    {
                        continue;
                    }

                    OfficialPlatformItem? platform = item.Platforms.FirstOrDefault(candidate =>
                        candidate.Name.Contains("BUFF", StringComparison.OrdinalIgnoreCase));
                    platform ??= item.Platforms.FirstOrDefault();
                    string platformItemId = platform?.ItemId ?? "";
                    string itemId = string.IsNullOrWhiteSpace(platformItemId)
                        ? item.MarketHashName
                        : platformItemId;
                    results.Add(new MarketItemCandidate(
                        itemId,
                        item.Name,
                        item.MarketHashName,
                        platformItemId,
                        "官方 API"));
                }
            }

            SteamDtItemCatalog? localCatalog = await GetLocalCatalogAsync(cancellationToken).ConfigureAwait(false);
            if (localCatalog is not null)
            {
                foreach (SteamDtCatalogItem item in localCatalog.Search(keyword, 100))
                {
                    if (!seenMarketHashNames.Add(item.MarketHashName))
                        continue;
                    results.Add(new MarketItemCandidate(
                        item.MarketHashName,
                        item.Name,
                        item.MarketHashName,
                        item.ItemId,
                        "本地库"));
                }
            }

            if (results.Count == 0)
            {
                IReadOnlyList<MarketItemCandidate> publicResults = await FetchPublicSearchCandidatesAsync(
                    keyword,
                    cancellationToken).ConfigureAwait(false);
                results.AddRange(publicResults);
            }

            return FinalizeSearchCandidates(results, keyword);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
        finally
        {
            _steamDtGate.Release();
        }
    }

    public async Task<MarketSourceSnapshot> RefreshSteamDtAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        await _steamDtGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string officialError = "";
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                MarketSourceSnapshot official = await FetchOfficialSteamDtAsync(apiKey.Trim(), cancellationToken)
                    .ConfigureAwait(false);
                if (official.HasData)
                    return official;
                officialError = official.Error;
            }

            MarketSourceSnapshot fallback = await FetchPublicSteamDtAsync(cancellationToken).ConfigureAwait(false);
            if (fallback.HasData || string.IsNullOrWhiteSpace(officialError))
                return fallback;

            return fallback with
            {
                Error = $"官方 API 失败（{officialError}）；公开接口失败（{fallback.Error}）"
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SourceFailure(MarketDataSourceIds.SteamDt, "SteamDT", "SteamDT 请求超时。");
        }
        catch (HttpRequestException exception)
        {
            return SourceFailure(MarketDataSourceIds.SteamDt, "SteamDT", "SteamDT 网络访问失败：" + SafeError(exception.Message));
        }
        catch (JsonException)
        {
            return SourceFailure(MarketDataSourceIds.SteamDt, "SteamDT", "SteamDT 返回数据无法解析。");
        }
        finally
        {
            _steamDtGate.Release();
        }
    }

    public async Task<MarketSourceSnapshot> RefreshQaqAsync(
        string apiToken,
        CancellationToken cancellationToken = default)
    {
        await _qaqGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, QaqApiBase + "/api/v1/current_data?type=init");
            if (!string.IsNullOrWhiteSpace(apiToken))
                request.Headers.TryAddWithoutValidation("ApiToken", apiToken.Trim());

            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return SourceFailure(
                    MarketDataSourceIds.Qaq,
                    "QAQ",
                    $"QAQ 请求失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}".Trim());
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (GetInt(root, "code") != 200
                || !TryGetNestedArray(root, "data", "sub_index_data", out JsonElement items))
            {
                return SourceFailure(MarketDataSourceIds.Qaq, "QAQ", "QAQ 返回数据为空或格式异常。");
            }

            foreach (JsonElement item in items.EnumerateArray())
            {
                if (!string.Equals(GetString(item, "name_key"), "init", StringComparison.OrdinalIgnoreCase))
                    continue;

                return new MarketSourceSnapshot(
                    MarketDataSourceIds.Qaq,
                    "QAQ",
                    GetDouble(item, "market_index"),
                    GetDouble(item, "chg_num"),
                    GetDouble(item, "chg_rate"),
                    GetDateTime(item, "updated_at", DateTime.Now),
                    string.IsNullOrWhiteSpace(apiToken) ? "公开接口" : "官方 API",
                    "正常",
                    "",
                    true,
                    false);
            }

            return SourceFailure(MarketDataSourceIds.Qaq, "QAQ", "QAQ 未返回 init 指数。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SourceFailure(MarketDataSourceIds.Qaq, "QAQ", "QAQ 请求超时。");
        }
        catch (HttpRequestException exception)
        {
            return SourceFailure(MarketDataSourceIds.Qaq, "QAQ", "QAQ 网络访问失败：" + SafeError(exception.Message));
        }
        catch (JsonException)
        {
            return SourceFailure(MarketDataSourceIds.Qaq, "QAQ", "QAQ 返回数据无法解析。");
        }
        finally
        {
            _qaqGate.Release();
        }
    }

    public async Task<MarketItemRefreshResult> RefreshItemAsync(
        ItemMonitorConfig item,
        string steamDtApiKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        string itemKey = FirstText(item.ItemKey, item.MarketHashName, item.ItemId, item.PlatformItemId);
        if (string.IsNullOrWhiteSpace(itemKey))
            return MarketItemRefreshResult.Failed("", "饰品标识为空。");

        await _steamDtGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrWhiteSpace(steamDtApiKey))
            {
                MarketItemRefreshResult official = await FetchOfficialItemAsync(item, steamDtApiKey.Trim(), cancellationToken)
                    .ConfigureAwait(false);
                if (official.Success)
                    return official;
            }

            if (string.IsNullOrWhiteSpace(item.MarketHashName))
                return MarketItemRefreshResult.Failed(itemKey, "缺少 MarketHashName，无法使用公开接口。");

            return await FetchPublicItemAsync(item, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return MarketItemRefreshResult.Failed(itemKey, "SteamDT 单品请求超时。");
        }
        catch (HttpRequestException exception)
        {
            return MarketItemRefreshResult.Failed(itemKey, "SteamDT 单品网络访问失败：" + SafeError(exception.Message));
        }
        catch (JsonException)
        {
            return MarketItemRefreshResult.Failed(itemKey, "SteamDT 单品数据无法解析。");
        }
        finally
        {
            _steamDtGate.Release();
        }
    }

    private async Task<MarketSourceSnapshot> FetchOfficialSteamDtAsync(
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, SteamDtOpenBase + "/open/cs2/broad/v1/index");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return SourceFailure(
                MarketDataSourceIds.SteamDt,
                "SteamDT",
                $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".Trim());
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        if (!GetBool(root, "success") || !root.TryGetProperty("data", out JsonElement data))
        {
            return SourceFailure(
                MarketDataSourceIds.SteamDt,
                "SteamDT",
                FirstText(GetString(root, "errorMsg"), GetString(root, "errorCodeStr"), "SteamDT API 返回数据为空。"));
        }

        return new MarketSourceSnapshot(
            MarketDataSourceIds.SteamDt,
            "SteamDT",
            GetDouble(data, "broadMarketIndex"),
            GetDouble(data, "diffYesterday"),
            GetDouble(data, "diffYesterdayRatio"),
            UnixToLocal(GetLong(data, "updateTime"), DateTime.Now),
            "官方 API",
            "正常",
            "",
            true,
            false);
    }

    private async Task<MarketSourceSnapshot> FetchPublicSteamDtAsync(CancellationToken cancellationToken)
    {
        long offset = await GetSteamDtTimeOffsetAsync(cancellationToken).ConfigureAwait(false);
        string timestamp = CreateSteamDtTimestamp(offset);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{SteamDtWebBase}/api/user/item/block/v1/summary?timestamp={timestamp}");
        request.Content = JsonContent(new { type = "BROAD", level = 0, platform = "ALL", typeVal = "" });
        ApplySteamDtPublicHeaders(request, SteamDtWebBase + "/section?type=BROAD");

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return SourceFailure(
                MarketDataSourceIds.SteamDt,
                "SteamDT",
                $"公开接口 HTTP {(int)response.StatusCode} {response.ReasonPhrase}".Trim());
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        if (!GetBool(root, "success") || !root.TryGetProperty("data", out JsonElement data))
        {
            return SourceFailure(
                MarketDataSourceIds.SteamDt,
                "SteamDT",
                FirstText(GetString(root, "errorMsg"), GetString(root, "errorCodeStr"), "SteamDT 公开接口返回数据为空。"));
        }

        return new MarketSourceSnapshot(
            MarketDataSourceIds.SteamDt,
            "SteamDT",
            GetDouble(data, "index"),
            GetDouble(data, "riseFallDiff"),
            GetDouble(data, "riseFallRate"),
            UnixToLocal(GetLong(data, "updateTime"), DateTime.Now),
            "公开接口",
            "正常",
            "",
            true,
            false);
    }

    private async Task<long> GetSteamDtTimeOffsetAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await _http.GetAsync(
                SteamDtWebBase + "/api/user/system-config/v1/default-config",
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return 0;
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("data", out JsonElement data))
            {
                long serverTime = GetLong(data, "systemTime");
                if (serverTime > 0)
                    return serverTime - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            return 0;
        }

        return 0;
    }

    private async Task<MarketItemRefreshResult> FetchOfficialItemAsync(
        ItemMonitorConfig item,
        string apiKey,
        CancellationToken cancellationToken)
    {
        string endpoint = !string.IsNullOrWhiteSpace(item.MarketHashName)
            ? $"{SteamDtOpenBase}/open/cs2/v1/price/single?marketHashName={Uri.EscapeDataString(item.MarketHashName.Trim())}"
            : $"{SteamDtOpenBase}/open/cs2/item/v1/price?itemId={Uri.EscapeDataString(item.ItemId.Trim())}";
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return MarketItemRefreshResult.Failed(
                FirstText(item.ItemKey, item.ItemId),
                $"官方 API HTTP {(int)response.StatusCode} {response.ReasonPhrase}".Trim());
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        if (!GetBool(root, "success") || !root.TryGetProperty("data", out JsonElement data))
        {
            return MarketItemRefreshResult.Failed(
                FirstText(item.ItemKey, item.ItemId),
                FirstText(GetString(root, "errorMsg"), GetString(root, "errorCodeStr"), "官方 API 返回数据为空。"));
        }

        return data.ValueKind == JsonValueKind.Array
            ? ParseOfficialPlatformRows(item, data)
            : ParseOfficialItemObject(item, data);
    }

    private static MarketItemRefreshResult ParseOfficialPlatformRows(ItemMonitorConfig item, JsonElement data)
    {
        JsonElement selected = default;
        bool found = false;
        if (!string.IsNullOrWhiteSpace(item.PlatformItemId))
        {
            foreach (JsonElement row in data.EnumerateArray())
            {
                if (string.Equals(GetString(row, "platformItemId"), item.PlatformItemId, StringComparison.OrdinalIgnoreCase))
                {
                    selected = row;
                    found = true;
                    break;
                }
            }
        }

        if (!found)
        {
            foreach (JsonElement row in data.EnumerateArray())
            {
                if (GetDouble(row, "sellPrice") > 0)
                {
                    selected = row;
                    found = true;
                    break;
                }
            }
        }

        if (!found && data.GetArrayLength() > 0)
        {
            selected = data[0];
            found = true;
        }

        if (!found)
            return MarketItemRefreshResult.Failed(FirstText(item.ItemKey, item.ItemId), "官方 API 返回价格列表为空。");

        (double youPinBid, long youPinTime) = SelectYouPinBid(data);
        double sellPrice = GetDouble(selected, "sellPrice");
        double bidPrice = GetDouble(selected, "biddingPrice");
        double price = sellPrice > 0 ? sellPrice : bidPrice;
        if (price <= 0)
            return MarketItemRefreshResult.Failed(FirstText(item.ItemKey, item.ItemId), "官方 API 未返回有效价格。");

        return MarketItemRefreshResult.Succeeded(
            FirstText(item.ItemKey, item.ItemId),
            price,
            youPinBid,
            0,
            0,
            GetLong(selected, "updateTime", "systemTime", "timestamp"),
            youPinTime,
            false,
            "官方 API");
    }

    private static MarketItemRefreshResult ParseOfficialItemObject(ItemMonitorConfig item, JsonElement data)
    {
        double price = GetDouble(data, "price", "index", "lastPrice", "value");
        if (price <= 0)
            return MarketItemRefreshResult.Failed(FirstText(item.ItemKey, item.ItemId), "官方 API 未返回有效价格。");
        (double youPinBid, long youPinTime) = SelectYouPinBid(data);
        bool hasChange = HasAnyProperty(data, "change", "diffYesterday", "riseFallDiff");
        return MarketItemRefreshResult.Succeeded(
            FirstText(item.ItemKey, item.ItemId),
            price,
            youPinBid,
            GetDouble(data, "change", "diffYesterday", "riseFallDiff"),
            GetDouble(data, "changeRatio", "diffYesterdayRatio", "riseFallRate", "rate"),
            GetLong(data, "updateTime", "systemTime", "timestamp"),
            youPinTime,
            hasChange,
            "官方 API");
    }

    private async Task<MarketItemRefreshResult> FetchPublicItemAsync(
        ItemMonitorConfig item,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{SteamDtWebBase}/api/user/skin/v1/item?timestamp={CreateSteamDtTimestamp()}");
        request.Content = JsonContent(new { appId = 730, marketHashName = item.MarketHashName.Trim() });
        ApplySteamDtPublicHeaders(
            request,
            SteamDtWebBase + "/cs2/" + Uri.EscapeDataString(item.MarketHashName.Trim()));
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return MarketItemRefreshResult.Failed(
                FirstText(item.ItemKey, item.ItemId),
                $"公开接口 HTTP {(int)response.StatusCode} {response.ReasonPhrase}".Trim());
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        if (!GetBool(root, "success")
            || !root.TryGetProperty("data", out JsonElement data)
            || data.ValueKind != JsonValueKind.Object)
        {
            return MarketItemRefreshResult.Failed(FirstText(item.ItemKey, item.ItemId), "公开接口未返回可用单品价格。");
        }

        (double price, long updateTime, string platformName) = SelectPublicSellPrice(data);
        if (price <= 0)
            price = GetDouble(data, "price", "lastPrice", "evaluatePrice", "increasePrice");
        if (price <= 0)
            return MarketItemRefreshResult.Failed(FirstText(item.ItemKey, item.ItemId), "公开接口未返回有效价格。");
        (double youPinBid, long youPinTime) = SelectYouPinBid(data);
        bool hasChange = HasAnyProperty(data, "diff1DayPrice", "diff1Day");
        return MarketItemRefreshResult.Succeeded(
            FirstText(item.ItemKey, item.ItemId),
            price,
            youPinBid,
            GetDouble(data, "diff1DayPrice", "increasePrice"),
            GetDouble(data, "diff1Day"),
            updateTime > 0 ? updateTime : GetLong(data, "updateTime", "systemTime", "timestamp"),
            youPinTime,
            hasChange,
            string.IsNullOrWhiteSpace(platformName) ? "公开页面接口" : "公开页面接口/" + platformName);
    }

    private async Task<IReadOnlyList<OfficialBaseItem>> GetOfficialBaseItemsAsync(
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (_officialBaseItems.Count > 0
            && DateTime.UtcNow - _officialBaseUpdatedUtc < TimeSpan.FromHours(24))
        {
            return _officialBaseItems;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, SteamDtOpenBase + "/open/cs2/v1/base");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return _officialBaseItems;

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (!GetBool(root, "success")
                || !root.TryGetProperty("data", out JsonElement data)
                || data.ValueKind != JsonValueKind.Array)
            {
                return _officialBaseItems;
            }

            var items = new List<OfficialBaseItem>();
            foreach (JsonElement element in data.EnumerateArray())
            {
                string name = GetString(element, "name");
                string marketHashName = FirstText(
                    GetString(element, "marketHashName"),
                    GetString(element, "market_hash_name"));
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(marketHashName))
                    continue;

                var platforms = new List<OfficialPlatformItem>();
                if (element.TryGetProperty("platformList", out JsonElement platformList)
                    && platformList.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement platform in platformList.EnumerateArray())
                    {
                        string itemId = GetStringOrNumber(platform, "itemId", "item_id", "id");
                        if (string.IsNullOrWhiteSpace(itemId))
                            continue;
                        platforms.Add(new OfficialPlatformItem(GetString(platform, "name"), itemId));
                    }
                }

                items.Add(new OfficialBaseItem(name, marketHashName, platforms));
            }

            if (items.Count > 0)
            {
                _officialBaseItems = items;
                _officialBaseUpdatedUtc = DateTime.UtcNow;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Best-effort official base refresh timed out; keep the last usable snapshot.
        }
        catch (HttpRequestException)
        {
            // Best-effort official base refresh lost network access; keep the last usable snapshot.
        }
        catch (JsonException)
        {
            // Best-effort official base refresh returned malformed data; keep the last usable snapshot.
        }

        return _officialBaseItems;
    }

    private async Task<SteamDtItemCatalog?> GetLocalCatalogAsync(CancellationToken cancellationToken)
    {
        if (_suppliedLocalCatalog is not null)
            return _suppliedLocalCatalog;
        if (!_useEmbeddedCatalog)
            return null;

        _localCatalogTask ??= LoadEmbeddedCatalogAsync();
        return await _localCatalogTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<SteamDtItemCatalog?> LoadEmbeddedCatalogAsync()
    {
        try
        {
            await using Stream? stream = typeof(PortableMarketDataClient).Assembly.GetManifestResourceStream(
                "CS2TradeMonitor.Shared.Resources.steamdt_items.json.gz");
            return stream is null
                ? null
                : await SteamDtItemCatalog.LoadGzipAsync(stream).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<MarketItemCandidate>> FetchPublicSearchCandidatesAsync(
        string keyword,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{SteamDtWebBase}/api/user/item/block/v1/suggest?timestamp={CreateSteamDtTimestamp()}");
            request.Content = JsonContent(new { keyword });
            ApplySteamDtPublicHeaders(request, SteamDtWebBase + "/item/");
            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return [];

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseSearchCandidates(body, keyword);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<MarketItemCandidate> FinalizeSearchCandidates(
        IEnumerable<MarketItemCandidate> candidates,
        string keyword)
    {
        string normalizedKeyword = SteamDtItemCatalog.Normalize(keyword);
        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Name)
                && !string.IsNullOrWhiteSpace(candidate.ItemId))
            .DistinctBy(
                candidate => FirstText(candidate.MarketHashName, candidate.ItemId),
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(candidate => SearchScore(candidate, normalizedKeyword))
            .ThenBy(candidate => candidate.Name.Length)
            .ThenBy(candidate => candidate.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(300)
            .ToArray();
    }

    private static IReadOnlyList<MarketItemCandidate> ParseSearchCandidates(string body, string keyword)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        var results = new List<MarketItemCandidate>();
        CollectCandidateArrays(document.RootElement, results);
        return FinalizeSearchCandidates(results, keyword);
    }

    private static void CollectCandidateArrays(JsonElement element, List<MarketItemCandidate> results)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                if (child.ValueKind == JsonValueKind.Object)
                    TryAddCandidate(child, results);
            }
            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                CollectCandidateArrays(property.Value, results);
        }
    }

    private static void TryAddCandidate(JsonElement element, List<MarketItemCandidate> results)
    {
        string id = GetStringOrNumber(element, "itemId", "id", "goodsId", "goods_id", "item_id");
        string name = FirstText(
            GetString(element, "name"),
            GetString(element, "goodsName"),
            GetString(element, "itemName"),
            GetString(element, "nameCn"),
            GetString(element, "name_cn"));
        string marketHashName = FirstText(
            GetString(element, "marketHashName"),
            GetString(element, "market_hash_name"),
            name);
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            return;
        results.Add(new MarketItemCandidate(id, name, marketHashName, id, "公开接口"));
    }

    private static int SearchScore(MarketItemCandidate candidate, string normalizedKeyword)
    {
        string name = NormalizeSearchText(candidate.Name);
        string hash = NormalizeSearchText(candidate.MarketHashName);
        if (name.Equals(normalizedKeyword, StringComparison.OrdinalIgnoreCase)
            || hash.Equals(normalizedKeyword, StringComparison.OrdinalIgnoreCase))
            return 0;
        if (name.StartsWith(normalizedKeyword, StringComparison.OrdinalIgnoreCase))
            return 1;
        if (hash.StartsWith(normalizedKeyword, StringComparison.OrdinalIgnoreCase))
            return 2;
        if (name.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase))
            return 3;
        if (hash.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase))
            return 4;
        return 9;
    }

    private static string NormalizeSearchText(string value)
        => SteamDtItemCatalog.Normalize(value);

    private sealed record OfficialBaseItem(
        string Name,
        string MarketHashName,
        IReadOnlyList<OfficialPlatformItem> Platforms);

    private sealed record OfficialPlatformItem(string Name, string ItemId);

    private static (double Price, long UpdateTime, string PlatformName) SelectPublicSellPrice(JsonElement data)
    {
        if (!data.TryGetProperty("sellingPriceList", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array)
            return (0, 0, "");
        double bestPrice = 0;
        long bestTime = 0;
        string bestPlatform = "";
        foreach (JsonElement row in rows.EnumerateArray())
        {
            double price = GetDouble(row, "price", "sellPrice");
            if (price <= 0)
                continue;
            string platform = GetString(row, "platform");
            bool isSteam = string.Equals(platform, "steam", StringComparison.OrdinalIgnoreCase);
            if (isSteam && bestPrice > 0)
                continue;
            if (bestPrice <= 0 || (!isSteam && price < bestPrice))
            {
                bestPrice = price;
                bestTime = GetLong(row, "updateTime", "timestamp", "systemTime");
                bestPlatform = FirstText(GetString(row, "platformName"), platform);
            }
        }

        return (bestPrice, bestTime, bestPlatform);
    }

    private static (double Price, long UpdateTime) SelectYouPinBid(JsonElement payload)
    {
        double bestPrice = 0;
        long bestTime = 0;
        foreach (JsonElement row in EnumeratePlatformRows(payload))
        {
            if (!IsYouPinRow(row))
                continue;
            double price = GetDouble(row, "biddingPrice", "bidPrice", "buyPrice", "purchasePrice");
            long time = GetLong(row, "updateTime", "timestamp", "systemTime");
            if (price > bestPrice || price == bestPrice && time >= bestTime)
            {
                bestPrice = price;
                bestTime = time;
            }
        }

        return (bestPrice, bestTime);
    }

    private static IEnumerable<JsonElement> EnumeratePlatformRows(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement row in payload.EnumerateArray())
                yield return row;
            yield break;
        }

        if (payload.ValueKind != JsonValueKind.Object)
            yield break;
        foreach (string propertyName in new[] { "sellingPriceList", "priceList", "platformList" })
        {
            if (payload.TryGetProperty(propertyName, out JsonElement rows) && rows.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement row in rows.EnumerateArray())
                    yield return row;
            }
        }
    }

    private static bool IsYouPinRow(JsonElement row)
    {
        foreach (string propertyName in new[] { "platform", "platformCode", "platformName", "name" })
        {
            string value = GetString(row, propertyName);
            if (value.Contains("悠悠", StringComparison.OrdinalIgnoreCase)
                || value.Contains("youpin", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yp", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void ApplySteamDtPublicHeaders(HttpRequestMessage request, string referer)
    {
        request.Headers.TryAddWithoutValidation("x-device-id", "");
        request.Headers.TryAddWithoutValidation("x-device", "1");
        request.Headers.TryAddWithoutValidation("x-app-version", "1.0.0");
        request.Headers.TryAddWithoutValidation("access-token", "");
        request.Headers.TryAddWithoutValidation("x-currency", "CNY");
        request.Headers.TryAddWithoutValidation("language", "zh_CN");
        request.Headers.TryAddWithoutValidation("Referer", referer);
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36");
    }

    private static StringContent JsonContent<T>(T value)
        => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static string CreateSteamDtTimestamp(long offsetMilliseconds = 0)
    {
        string milliseconds = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + offsetMilliseconds)
            .ToString(CultureInfo.InvariantCulture);
        if (milliseconds.Length < 12)
            return milliseconds;
        string first12 = milliseconds[..12];
        int sum = first12.Where(char.IsDigit).Sum(character => character - '0');
        return first12 + (sum % 10).ToString(CultureInfo.InvariantCulture);
    }

    private static MarketSourceSnapshot SourceFailure(string id, string displayName, string error)
        => new(id, displayName, 0, 0, 0, DateTime.MinValue, "未获取", "刷新失败", error, false, false);

    private static bool TryGetNestedArray(JsonElement root, string objectName, string arrayName, out JsonElement value)
    {
        value = default;
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(objectName, out JsonElement container)
            && container.ValueKind == JsonValueKind.Object
            && container.TryGetProperty(arrayName, out value)
            && value.ValueKind == JsonValueKind.Array;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out JsonElement property))
            return "";
        return property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : "";
    }

    private static string GetStringOrNumber(JsonElement element, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out JsonElement property))
                continue;
            if (property.ValueKind == JsonValueKind.String)
                return property.GetString() ?? "";
            if (property.ValueKind == JsonValueKind.Number)
                return property.ToString();
        }
        return "";
    }

    private static double GetDouble(JsonElement element, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out JsonElement property))
                continue;
            if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out double number))
                return number;
            if (property.ValueKind == JsonValueKind.String
                && double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                return number;
        }
        return 0;
    }

    private static int GetInt(JsonElement element, string propertyName)
        => (int)GetLong(element, propertyName);

    private static long GetLong(JsonElement element, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out JsonElement property))
                continue;
            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out long number))
                return number;
            if (property.ValueKind == JsonValueKind.String
                && long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                return number;
        }
        return 0;
    }

    private static bool GetBool(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.True;

    private static DateTime GetDateTime(JsonElement element, string propertyName, DateTime fallback)
        => DateTime.TryParse(
            GetString(element, propertyName),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out DateTime parsed)
            ? parsed
            : fallback;

    private static DateTime UnixToLocal(long value, DateTime fallback)
    {
        if (value <= 0)
            return fallback;
        try
        {
            if (value < 100_000_000_000L)
                value *= 1000;
            return DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return fallback;
        }
    }

    private static bool HasAnyProperty(JsonElement element, params string[] propertyNames)
        => element.ValueKind == JsonValueKind.Object
            && propertyNames.Any(propertyName => element.TryGetProperty(propertyName, out _));

    private static string FirstText(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static string SafeError(string value)
    {
        value = value?.Trim() ?? "";
        return value.Length <= 160 ? value : value[..160] + "...";
    }
}
