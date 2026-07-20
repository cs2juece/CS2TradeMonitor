using System.Globalization;
using System.Text.Json;
using CS2QuantWeb.Core;

namespace CS2QuantWeb;

public sealed record LoadedSeries(
    string Symbol,
    string Source,
    IReadOnlyList<QuantCandle> Candles,
    CandleInterval Interval = CandleInterval.Day);

public sealed class SeriesLoadException : Exception
{
    public SeriesLoadException(string message, int statusCode = StatusCodes.Status400BadRequest)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

public sealed class MarketSeriesService(
    SteamDtItemSeriesAdapter item,
    CsvSeriesAdapter csv,
    CsqaqIndexSeriesAdapter csqaq)
{
    public Task<LoadedSeries> LoadAsync(
        string? source,
        string? symbol,
        string? range,
        CancellationToken cancellationToken)
    {
        string normalizedSource = string.IsNullOrWhiteSpace(source)
            ? "item"
            : source.Trim().ToLowerInvariant();
        return normalizedSource switch
        {
            "item" => item.LoadAsync(symbol, range, cancellationToken),
            "csv" => csv.LoadAsync(symbol, cancellationToken),
            "csqaq" => csqaq.LoadAsync(cancellationToken),
            _ => throw new SeriesLoadException("未知数据源，仅支持单品 K 线、CSQAQ 大盘日线和本地 CSV。")
        };
    }
}

public sealed class CsvSeriesAdapter(IHostEnvironment environment)
{
    public async Task<LoadedSeries> LoadAsync(string? fileName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new SeriesLoadException("CSV 数据源需要填写文件名，例如 my-data.csv。");

        string cleanName = Path.GetFileName(fileName.Trim());
        if (!string.Equals(cleanName, fileName.Trim(), StringComparison.Ordinal)
            || !string.Equals(Path.GetExtension(cleanName), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new SeriesLoadException("只允许读取 data 目录内的 .csv 文件名，不接受路径。");
        }

        string dataRoot = Environment.GetEnvironmentVariable("CS2_QUANT_DATA_DIR")
            ?? Path.Combine(environment.ContentRootPath, "data");
        string root = Path.GetFullPath(dataRoot);
        string path = Path.GetFullPath(Path.Combine(root, cleanName));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(path))
        {
            throw new SeriesLoadException($"未在 data 目录找到 {cleanName}。", StatusCodes.Status404NotFound);
        }

        string[] lines = await File.ReadAllLinesAsync(path, cancellationToken);
        if (lines.Length < 2)
            throw new SeriesLoadException("CSV 至少需要表头和一行数据。");

        string[] headers = SplitCsvLine(lines[0]).Select(value => value.Trim().ToLowerInvariant()).ToArray();
        int dateIndex = FindHeader(headers, "date", "日期", "time");
        int openIndex = FindHeader(headers, "open", "开盘");
        int highIndex = FindHeader(headers, "high", "最高");
        int lowIndex = FindHeader(headers, "low", "最低");
        int closeIndex = FindHeader(headers, "close", "收盘");
        int volumeIndex = FindOptionalHeader(headers, "volume", "vol", "成交量");
        var candles = new List<QuantCandle>();
        for (int lineNumber = 1; lineNumber < lines.Length; lineNumber++)
        {
            if (string.IsNullOrWhiteSpace(lines[lineNumber]))
                continue;

            string[] fields = SplitCsvLine(lines[lineNumber]);
            try
            {
                candles.Add(new QuantCandle(
                    ParseDate(fields[dateIndex]),
                    ParseNumber(fields[openIndex]),
                    ParseNumber(fields[highIndex]),
                    ParseNumber(fields[lowIndex]),
                    ParseNumber(fields[closeIndex]),
                    volumeIndex < 0 || volumeIndex >= fields.Length ? 0 : ParseNumber(fields[volumeIndex])));
            }
            catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException)
            {
                throw new SeriesLoadException($"CSV 第 {lineNumber + 1} 行格式无效：{ex.Message}");
            }
        }

        return new LoadedSeries(Path.GetFileNameWithoutExtension(cleanName), "csv", candles);
    }

    private static int FindHeader(string[] headers, params string[] names)
    {
        int index = FindOptionalHeader(headers, names);
        return index >= 0 ? index : throw new SeriesLoadException($"CSV 缺少必要列：{names[0]}。");
    }

    private static int FindOptionalHeader(string[] headers, params string[] names)
    {
        return Array.FindIndex(headers, header => names.Contains(header, StringComparer.OrdinalIgnoreCase));
    }

    private static string[] SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char character = line[i];
            if (character == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        fields.Add(current.ToString().Trim());
        return fields.ToArray();
    }

    private static DateOnly ParseDate(string value)
    {
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
            || DateOnly.TryParse(value, CultureInfo.GetCultureInfo("zh-CN"), DateTimeStyles.None, out date))
        {
            return date;
        }

        throw new FormatException($"无法解析日期 {value}");
    }

    private static double ParseNumber(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
            return result;

        throw new FormatException($"无法解析数字 {value}");
    }
}

public sealed class CsqaqIndexSeriesAdapter(HttpClient httpClient)
{
    private const string Endpoint = "https://api.csqaq.com/api/v1/sub/kline?id=1&type=1day";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);
    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private LoadedSeries? _cachedSeries;
    private DateTimeOffset _cacheExpiresAt;

    public async Task<LoadedSeries> LoadAsync(CancellationToken cancellationToken)
    {
        await _cacheGate.WaitAsync(cancellationToken);
        try
        {
            if (_cachedSeries is not null && DateTimeOffset.UtcNow < _cacheExpiresAt)
                return _cachedSeries;

            string? token = Environment.GetEnvironmentVariable("QAQ_API_KEY");
            if (string.IsNullOrWhiteSpace(token))
                throw new SeriesLoadException("未配置 QAQ_API_KEY。请在启动网页服务的进程环境中设置，页面不会保存密钥。");

            using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
            request.Headers.TryAddWithoutValidation("ApiToken", token.Trim());
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new SeriesLoadException(
                    $"CSQAQ 返回 HTTP {(int)response.StatusCode}。请检查密钥、IP 绑定和调用频率。",
                    StatusCodes.Status502BadGateway);
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            IReadOnlyList<QuantCandle> candles = CsqaqPayloadParser.Parse(document.RootElement);
            if (candles.Count == 0)
                throw new SeriesLoadException("CSQAQ 响应中未识别到有效 OHLC 数据。", StatusCodes.Status502BadGateway);

            _cachedSeries = new LoadedSeries("CSQAQ 大盘指数", "csqaq", candles);
            _cacheExpiresAt = DateTimeOffset.UtcNow.Add(CacheDuration);
            return _cachedSeries;
        }
        finally
        {
            _cacheGate.Release();
        }
    }
}

internal static class CsqaqPayloadParser
{
    public static IReadOnlyList<QuantCandle> Parse(JsonElement root)
    {
        var candidates = new List<IReadOnlyList<QuantCandle>>();
        Visit(root, candidates, 0);
        return candidates.OrderByDescending(candidate => candidate.Count).FirstOrDefault() ?? [];
    }

    private static void Visit(JsonElement element, ICollection<IReadOnlyList<QuantCandle>> candidates, int depth)
    {
        if (depth > 8)
            return;

        if (element.ValueKind == JsonValueKind.Array)
        {
            var candles = new List<QuantCandle>();
            foreach (JsonElement child in element.EnumerateArray())
            {
                if (TryParseCandle(child, out QuantCandle candle))
                    candles.Add(candle);
                else
                    Visit(child, candidates, depth + 1);
            }

            if (candles.Count > 0)
                candidates.Add(candles);
            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return;
        foreach (JsonProperty property in element.EnumerateObject())
            Visit(property.Value, candidates, depth + 1);
    }

    private static bool TryParseCandle(JsonElement element, out QuantCandle candle)
    {
        candle = null!;
        if (element.ValueKind != JsonValueKind.Object
            || !TryGetNumber(element, out double open, "open", "o")
            || !TryGetNumber(element, out double high, "high", "h")
            || !TryGetNumber(element, out double low, "low", "l")
            || !TryGetNumber(element, out double close, "close", "c")
            || !TryGetDate(element, out DateOnly date))
        {
            return false;
        }

        TryGetNumber(element, out double volume, "volume", "vol", "v");
        candle = new QuantCandle(date, open, high, low, close, volume);
        return true;
    }

    private static bool TryGetNumber(JsonElement element, out double value, params string[] names)
    {
        foreach (string name in names)
        {
            if (!TryGetPropertyIgnoreCase(element, name, out JsonElement property))
                continue;
            if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out value))
                return true;
            if (property.ValueKind == JsonValueKind.String
                && double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static bool TryGetDate(JsonElement element, out DateOnly date)
    {
        foreach (string name in new[] { "date", "day", "time", "datetime", "timestamp", "t" })
        {
            if (!TryGetPropertyIgnoreCase(element, name, out JsonElement property))
                continue;

            if (property.ValueKind == JsonValueKind.String)
            {
                string? value = property.GetString();
                if (DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out DateTimeOffset parsed))
                {
                    date = DateOnly.FromDateTime(parsed.DateTime);
                    return true;
                }

                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long stringTimestamp)
                    && TryParseUnixTimestamp(stringTimestamp, out date))
                {
                    return true;
                }
            }

            if (property.ValueKind == JsonValueKind.Number
                && property.TryGetInt64(out long timestamp)
                && TryParseUnixTimestamp(timestamp, out date))
            {
                return true;
            }
        }

        date = default;
        return false;
    }

    private static bool TryParseUnixTimestamp(long timestamp, out DateOnly date)
    {
        try
        {
            DateTimeOffset parsed = timestamp > 10_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
                : DateTimeOffset.FromUnixTimeSeconds(timestamp);
            date = DateOnly.FromDateTime(parsed.LocalDateTime);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            date = default;
            return false;
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
