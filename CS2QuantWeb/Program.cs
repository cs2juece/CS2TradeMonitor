using System.Text;
using System.Text.Json.Serialization;
using System.Diagnostics;
using System.Globalization;
using CS2QuantWeb;
using CS2QuantWeb.Core;
using CS2MarketData.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(
    QuantResearchServerOptions.ResolveListenUrl(
        Environment.GetEnvironmentVariable("CS2_QUANT_LISTEN_URL")));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<IQuantResearchModule, QuantResearchModule>();
builder.Services.AddSingleton<CsvSeriesAdapter>();
builder.Services.AddSingleton<SteamDtItemCatalogProvider>();
builder.Services.AddSingleton(_ => new SteamDtKlineClient(new HttpClient
{
    Timeout = TimeSpan.FromSeconds(15)
}));
builder.Services.AddSingleton<SteamDtItemSeriesAdapter>();
builder.Services.AddSingleton(_ => new CsqaqIndexSeriesAdapter(new HttpClient
{
    Timeout = TimeSpan.FromSeconds(12)
}));
builder.Services.AddSingleton<MarketSeriesService>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.CacheControl = "no-cache";
        context.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    }
});

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "CS2QuantWeb",
    time = DateTimeOffset.UtcNow
}));

app.MapGet("/api/sources", () => Results.Ok(new[]
{
    new { key = "item", name = "SteamDT 单品 K 线", needsInput = true, description = "一年以内读取日线，两年和全部读取周线" },
    new { key = "csqaq", name = "CSQAQ 大盘日线", needsInput = false, description = "需在服务进程设置 QAQ_API_KEY" },
    new { key = "csv", name = "本地 CSV", needsInput = true, description = "从受限 data 目录读取 OHLCV 文件" }
}));

app.MapGet("/api/items/search", async (
    string? q,
    SteamDtItemCatalogProvider catalog,
    CancellationToken cancellationToken) =>
{
    try
    {
        IReadOnlyList<SteamDtCatalogItem> results = await catalog.SearchAsync(q, 20, cancellationToken);
        return Results.Ok(results);
    }
    catch (SeriesLoadException ex)
    {
        return Results.Problem(ex.Message, statusCode: ex.StatusCode, title: "单品搜索失败");
    }
});

app.MapGet("/api/analyze", async (
    string? source,
    string? symbol,
    string? range,
    MarketSeriesService seriesService,
    IQuantResearchModule module,
    CancellationToken cancellationToken) =>
{
    try
    {
        LoadedSeries loaded = await seriesService.LoadAsync(source, symbol, range, cancellationToken);
        QuantResearchResult result = module.Analyze(loaded.Symbol, loaded.Source, loaded.Candles) with
        {
            Interval = loaded.Interval
        };
        return Results.Ok(result);
    }
    catch (SeriesLoadException ex)
    {
        return Results.Problem(ex.Message, statusCode: ex.StatusCode, title: "数据读取失败");
    }
    catch (ArgumentException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity, title: "分析输入无效");
    }
});

app.MapGet("/api/export/signals.csv", async (
    string? source,
    string? symbol,
    string? range,
    MarketSeriesService seriesService,
    IQuantResearchModule module,
    CancellationToken cancellationToken) =>
{
    try
    {
        LoadedSeries loaded = await seriesService.LoadAsync(source, symbol, range, cancellationToken);
        QuantResearchResult result = module.Analyze(loaded.Symbol, loaded.Source, loaded.Candles);
        byte[] csv = BuildSignalCsv(result);
        string safeName = string.Concat(result.Symbol.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        return Results.File(csv, "text/csv; charset=utf-8", $"{safeName}-signals.csv");
    }
    catch (SeriesLoadException ex)
    {
        return Results.Problem(ex.Message, statusCode: ex.StatusCode, title: "导出失败");
    }
    catch (ArgumentException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity, title: "导出输入无效");
    }
});

app.MapFallbackToFile("index.html");
_ = QuantResearchParentProcessMonitor.RunAsync(
    Environment.GetEnvironmentVariable("CS2_QUANT_PARENT_PID"),
    Environment.GetEnvironmentVariable("CS2_QUANT_PARENT_START_UTC_TICKS"),
    app.Lifetime,
    app.Logger,
    app.Lifetime.ApplicationStopping);
app.Run();

static byte[] BuildSignalCsv(QuantResearchResult result)
{
    var builder = new StringBuilder();
    builder.AppendLine("date,category,strategy,side,price,level,reason");
    IEnumerable<(string Category, ResearchSignal Signal)> rows = result.StrategySignals
        .Select(signal => ("strategy", signal))
        .Concat(result.Chan.Signals.Select(signal => ("chan", signal)))
        .OrderBy(row => row.signal.Date);
    foreach (var row in rows)
    {
        builder.Append(row.Signal.Date.ToString("yyyy-MM-dd")).Append(',')
            .Append(Escape(row.Category)).Append(',')
            .Append(Escape(row.Signal.Strategy)).Append(',')
            .Append(row.Signal.Side).Append(',')
            .Append(row.Signal.Price.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
            .Append(Escape(row.Signal.Level)).Append(',')
            .Append(Escape(row.Signal.Reason)).AppendLine();
    }

    return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(builder.ToString());
}

static string Escape(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

public partial class Program;

internal static class QuantResearchServerOptions
{
    internal const string DefaultListenUrl = "http://127.0.0.1:5078";

    internal static string ResolveListenUrl(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)
            && Uri.TryCreate(configured.Trim(), UriKind.Absolute, out Uri? candidate)
            && candidate.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && candidate.IsLoopback
            && string.IsNullOrEmpty(candidate.UserInfo)
            && candidate.AbsolutePath == "/"
            && string.IsNullOrEmpty(candidate.Query)
            && string.IsNullOrEmpty(candidate.Fragment))
        {
            return candidate.GetLeftPart(UriPartial.Authority);
        }

        return DefaultListenUrl;
    }
}

internal static class QuantResearchParentProcessMonitor
{
    internal static async Task RunAsync(
        string? parentPidText,
        string? parentStartTicksText,
        IHostApplicationLifetime lifetime,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(parentPidText, NumberStyles.None, CultureInfo.InvariantCulture, out int parentPid)
            || parentPid <= 0)
        {
            return;
        }

        try
        {
            using Process parentProcess = Process.GetProcessById(parentPid);
            if (long.TryParse(
                    parentStartTicksText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out long expectedStartTicks)
                && parentProcess.StartTime.ToUniversalTime().Ticks != expectedStartTicks)
            {
                logger.LogWarning("Configured quant parent process identity no longer matches; stopping service.");
                lifetime.StopApplication();
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                if (parentProcess.HasExited)
                {
                    logger.LogInformation("Quant parent process exited; stopping service.");
                    lifetime.StopApplication();
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Quant parent process cannot be monitored; stopping service.");
            lifetime.StopApplication();
        }
    }
}
