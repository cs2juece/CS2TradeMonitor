using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CS2MarketData.Core;

public sealed record SteamDtKlineCandle(
    DateOnly Date,
    double Open,
    double High,
    double Low,
    double Close,
    double Volume = 0);

public sealed record SteamDtKlineSeries(
    string MarketHashName,
    string Platform,
    IReadOnlyList<SteamDtKlineCandle> Candles,
    IReadOnlyList<double> ClosingPrices);

public enum SteamDtKlinePeriod
{
    Hourly = 1,
    Daily = 2,
    Weekly = 3
}

public enum SteamDtKlineFailureKind
{
    InvalidInput,
    MissingCredential,
    Authentication,
    RateLimited,
    Upstream,
    InvalidPayload,
    NoData
}

public sealed class SteamDtKlineException : Exception
{
    public SteamDtKlineException(SteamDtKlineFailureKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }

    public SteamDtKlineFailureKind Kind { get; }
}

/// <summary>
/// SteamDT official item K-line client. It owns request shape, period mapping, platform fallback and payload parsing.
/// </summary>
public sealed class SteamDtKlineClient(HttpClient httpClient)
{
    public static readonly Uri Endpoint = new("https://open.steamdt.com/open/cs2/item/v1/kline");

    public Task<SteamDtKlineSeries> FetchDailyAsync(
        string? marketHashName,
        string? apiKey,
        CancellationToken cancellationToken = default) =>
        FetchAsync(marketHashName, apiKey, SteamDtKlinePeriod.Daily, cancellationToken);

    public async Task<SteamDtKlineSeries> FetchAsync(
        string? marketHashName,
        string? apiKey,
        SteamDtKlinePeriod period,
        CancellationToken cancellationToken = default)
    {
        string itemName = marketHashName?.Trim() ?? string.Empty;
        if (itemName.Length == 0 || itemName.Length > 256)
            throw new SteamDtKlineException(SteamDtKlineFailureKind.InvalidInput, "SteamDT marketHashName is invalid.");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new SteamDtKlineException(SteamDtKlineFailureKind.MissingCredential, "SteamDT API key is missing.");
        if (period is not SteamDtKlinePeriod.Hourly
            and not SteamDtKlinePeriod.Daily
            and not SteamDtKlinePeriod.Weekly)
        {
            throw new SteamDtKlineException(SteamDtKlineFailureKind.InvalidInput, "SteamDT K-line period is invalid.");
        }

        SteamDtKlineException? lastFailure = null;
        foreach (string platform in new[] { string.Empty, "buff" })
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                request.Content = JsonContent.Create(new
                {
                    marketHashName = itemName,
                    type = (int)period,
                    platform,
                    specialStyle = string.Empty
                });
                using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    lastFailure = ClassifyHttpFailure(response.StatusCode);
                    if (lastFailure.Kind is SteamDtKlineFailureKind.Authentication
                        or SteamDtKlineFailureKind.RateLimited)
                    {
                        throw lastFailure;
                    }
                    continue;
                }

                await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                using JsonDocument document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (TryReadFailure(document.RootElement, out string message))
                {
                    lastFailure = new SteamDtKlineException(
                        SteamDtKlineFailureKind.Upstream,
                        string.IsNullOrWhiteSpace(message) ? "SteamDT returned an unsuccessful response." : message);
                    continue;
                }

                IReadOnlyList<SteamDtKlineCandle> candles = SteamDtKlinePayloadParser.Parse(document.RootElement);
                IReadOnlyList<double> closingPrices = candles.Count > 0
                    ? candles.Select(candle => candle.Close).ToArray()
                    : SteamDtKlinePayloadParser.ParseClosingPrices(document.RootElement);
                if (candles.Count == 0 && closingPrices.Count == 0)
                {
                    lastFailure = new SteamDtKlineException(
                        SteamDtKlineFailureKind.NoData,
                        "SteamDT returned no valid K-line candles.");
                    continue;
                }

                return new SteamDtKlineSeries(itemName, platform, candles, closingPrices);
            }
            catch (SteamDtKlineException)
            {
                throw;
            }
            catch (JsonException ex)
            {
                lastFailure = new SteamDtKlineException(
                    SteamDtKlineFailureKind.InvalidPayload,
                    $"SteamDT returned invalid JSON: {ex.Message}");
            }
            catch (HttpRequestException ex)
            {
                lastFailure = new SteamDtKlineException(
                    SteamDtKlineFailureKind.Upstream,
                    $"SteamDT request failed: {ex.Message}");
            }
        }

        throw lastFailure ?? new SteamDtKlineException(
            SteamDtKlineFailureKind.NoData,
            "SteamDT returned no K-line data.");
    }

    private static SteamDtKlineException ClassifyHttpFailure(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new(
                SteamDtKlineFailureKind.Authentication,
                $"SteamDT rejected the credential (HTTP {(int)statusCode})."),
            HttpStatusCode.TooManyRequests => new(
                SteamDtKlineFailureKind.RateLimited,
                "SteamDT rate limit reached (HTTP 429)."),
            _ => new(
                SteamDtKlineFailureKind.Upstream,
                $"SteamDT returned HTTP {(int)statusCode}.")
        };
    }

    private static bool TryReadFailure(JsonElement root, out string message)
    {
        message = string.Empty;
        if (root.ValueKind != JsonValueKind.Object)
            return false;
        if (TryGetPropertyIgnoreCase(root, "errorMsg", out JsonElement errorMessage)
            && errorMessage.ValueKind == JsonValueKind.String)
        {
            message = (errorMessage.GetString() ?? string.Empty).Trim();
            if (message.Length > 160)
                message = message[..160];
        }

        return TryGetPropertyIgnoreCase(root, "success", out JsonElement success)
            && success.ValueKind == JsonValueKind.False;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}

public static class SteamDtKlinePayloadParser
{
    private static readonly TimeSpan SteamDtTimeOffset = TimeSpan.FromHours(8);

    private sealed record ParsedCandle(SteamDtKlineCandle Candle, long SortKey);

    public static IReadOnlyList<SteamDtKlineCandle> Parse(string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        return Parse(document.RootElement);
    }

    public static IReadOnlyList<SteamDtKlineCandle> Parse(JsonElement root)
    {
        var samples = new List<ParsedCandle>();
        JsonElement data = root.ValueKind == JsonValueKind.Object
            && TryGetPropertyIgnoreCase(root, "data", out JsonElement payload)
                ? payload
                : root;
        Visit(data, samples, 0);
        return samples
            .Where(sample => IsValid(sample.Candle))
            .OrderBy(sample => sample.Candle.Date)
            .ThenBy(sample => sample.SortKey)
            .GroupBy(sample => sample.Candle.Date)
            .Select(AggregateByDate)
            .ToArray();
    }

    public static IReadOnlyList<double> ParseClosingPrices(string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        return ParseClosingPrices(document.RootElement);
    }

    public static IReadOnlyList<double> ParseClosingPrices(JsonElement root)
    {
        IReadOnlyList<SteamDtKlineCandle> candles = Parse(root);
        if (candles.Count > 0)
            return candles.Select(candle => candle.Close).ToArray();

        var prices = new List<double>();
        JsonElement data = root.ValueKind == JsonValueKind.Object
            && TryGetPropertyIgnoreCase(root, "data", out JsonElement payload)
                ? payload
                : root;
        CollectStandaloneClosingPrices(data, prices, 0);
        return prices.Where(price => price > 0 && price < 100_000_000).ToArray();
    }

    private static SteamDtKlineCandle AggregateByDate(IEnumerable<ParsedCandle> group)
    {
        SteamDtKlineCandle[] candles = group.Select(sample => sample.Candle).ToArray();
        return new SteamDtKlineCandle(
            candles[0].Date,
            candles[0].Open,
            candles.Max(candle => candle.High),
            candles.Min(candle => candle.Low),
            candles[^1].Close,
            candles.Sum(candle => candle.Volume));
    }

    private static void CollectStandaloneClosingPrices(
        JsonElement element,
        ICollection<double> prices,
        int depth)
    {
        if (depth > 8)
            return;
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (TryGetNumber(
                element,
                out double close,
                "close",
                "c",
                "closePrice",
                "closingPrice",
                "endPrice",
                "price",
                "avgPrice",
                "value",
                "index"))
            {
                prices.Add(close);
                return;
            }

            foreach (JsonProperty property in element.EnumerateObject())
                CollectStandaloneClosingPrices(property.Value, prices, depth + 1);
            return;
        }

        if (element.ValueKind != JsonValueKind.Array)
            return;

        JsonElement[] children = element.EnumerateArray().ToArray();
        if (children.Length == 2
            && TryParseDate(children[0], out _, out _)
            && TryReadNumber(children[1], out double pointPrice))
        {
            prices.Add(pointPrice);
            return;
        }

        foreach (JsonElement child in children)
            CollectStandaloneClosingPrices(child, prices, depth + 1);
    }

    private static void Visit(JsonElement element, ICollection<ParsedCandle> samples, int depth)
    {
        if (depth > 8)
            return;
        if (TryParseCandle(element, out ParsedCandle sample))
        {
            samples.Add(sample);
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            if (TryParseArrayCandle(element, out sample))
            {
                samples.Add(sample);
                return;
            }
            foreach (JsonElement child in element.EnumerateArray())
                Visit(child, samples, depth + 1);
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
                Visit(property.Value, samples, depth + 1);
        }
    }

    private static bool TryParseCandle(JsonElement element, out ParsedCandle sample)
    {
        sample = null!;
        if (element.ValueKind != JsonValueKind.Object
            || !TryGetDate(element, out DateOnly date, out long sortKey)
            || !TryGetNumber(element, out double open, "open", "o", "openPrice", "openingPrice", "startPrice", "beginPrice")
            || !TryGetNumber(element, out double high, "high", "h", "highPrice", "highestPrice", "maxPrice")
            || !TryGetNumber(element, out double low, "low", "l", "lowPrice", "lowestPrice", "minPrice")
            || !TryGetNumber(element, out double close, "close", "c", "closePrice", "closingPrice", "endPrice", "price", "avgPrice"))
        {
            return false;
        }

        TryGetNumber(element, out double volume, "volume", "vol", "v", "tradeCount", "sellCount");
        sample = new ParsedCandle(
            new SteamDtKlineCandle(date, open, high, low, close, volume),
            sortKey);
        return true;
    }

    private static bool TryParseArrayCandle(JsonElement element, out ParsedCandle sample)
    {
        sample = null!;
        JsonElement[] values = element.EnumerateArray().ToArray();
        if (values.Length < 5 || !TryParseDate(values[0], out DateOnly date, out long sortKey))
            return false;

        double[] numbers = values.Skip(1).Select(ReadNumber).ToArray();
        if (numbers.Length < 4 || numbers.Take(4).Any(value => value <= 0))
            return false;

        double open = numbers[0];
        double high;
        double low;
        double close;
        double volume;
        if (values.Length == 5)
        {
            // SteamDT live tuples are [timestamp, open, close, high, low].
            close = numbers[1];
            high = numbers[2];
            low = numbers[3];
            volume = 0;
        }
        else
        {
            // Preserve support for conventional [timestamp, open, high, low, close, volume].
            high = numbers[1];
            low = numbers[2];
            close = numbers[3];
            volume = numbers[4] >= 0 ? numbers[4] : 0;
        }

        sample = new ParsedCandle(
            new SteamDtKlineCandle(date, open, high, low, close, volume),
            sortKey);
        return true;
    }

    private static bool IsValid(SteamDtKlineCandle candle)
    {
        return candle.Open > 0
            && candle.Close > 0
            && double.IsFinite(candle.Open)
            && double.IsFinite(candle.High)
            && double.IsFinite(candle.Low)
            && double.IsFinite(candle.Close)
            && double.IsFinite(candle.Volume)
            && candle.High >= Math.Max(candle.Open, candle.Close)
            && candle.Low > 0
            && candle.Low <= Math.Min(candle.Open, candle.Close)
            && candle.Volume >= 0;
    }

    private static bool TryGetNumber(JsonElement element, out double value, params string[] names)
    {
        foreach (string name in names)
        {
            if (TryGetPropertyIgnoreCase(element, name, out JsonElement property)
                && TryReadNumber(property, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static double ReadNumber(JsonElement element)
    {
        return TryReadNumber(element, out double value) ? value : double.NaN;
    }

    private static bool TryReadNumber(JsonElement element, out double value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value))
            return true;
        if (element.ValueKind == JsonValueKind.String
            && double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryGetDate(JsonElement element, out DateOnly date, out long sortKey)
    {
        foreach (string name in new[] { "date", "day", "time", "timestamp", "createTime", "updateTime", "t" })
        {
            if (TryGetPropertyIgnoreCase(element, name, out JsonElement property)
                && TryParseDate(property, out date, out sortKey))
            {
                return true;
            }
        }

        date = default;
        sortKey = 0;
        return false;
    }

    private static bool TryParseDate(JsonElement property, out DateOnly date, out long sortKey)
    {
        if (property.ValueKind == JsonValueKind.String)
        {
            string value = property.GetString() ?? string.Empty;
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long timestamp))
                return TryParseUnixTimestamp(timestamp, out date, out sortKey);
            if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                sortKey = 0;
                return true;
            }
            if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out DateTimeOffset parsed))
            {
                DateTimeOffset steamDtTime = parsed.ToOffset(SteamDtTimeOffset);
                date = DateOnly.FromDateTime(steamDtTime.DateTime);
                sortKey = steamDtTime.TimeOfDay.Ticks;
                return true;
            }
        }
        else if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out long timestamp))
        {
            return TryParseUnixTimestamp(timestamp, out date, out sortKey);
        }

        date = default;
        sortKey = 0;
        return false;
    }

    private static bool TryParseUnixTimestamp(long timestamp, out DateOnly date, out long sortKey)
    {
        try
        {
            DateTimeOffset parsed = timestamp > 10_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
                : DateTimeOffset.FromUnixTimeSeconds(timestamp);
            DateTimeOffset steamDtTime = parsed.ToOffset(SteamDtTimeOffset);
            date = DateOnly.FromDateTime(steamDtTime.DateTime);
            sortKey = steamDtTime.TimeOfDay.Ticks;
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            date = default;
            sortKey = 0;
            return false;
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
