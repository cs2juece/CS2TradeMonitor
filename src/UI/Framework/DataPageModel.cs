using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.Core.Actions;
using System;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class DataPageModel
    {
        public static IReadOnlyList<DataPageMarketFormatOption> MarketFormatOptions { get; } =
            Array.AsReadOnly(new[]
            {
                new DataPageMarketFormatOption("价格 + 涨跌幅", 0),
                new DataPageMarketFormatOption("价格 + 涨跌值", 1),
                new DataPageMarketFormatOption("仅涨跌幅", 2),
                new DataPageMarketFormatOption("仅价格", 3),
                new DataPageMarketFormatOption("完整显示", 4),
                new DataPageMarketFormatOption("短标签模式", 5)
            });

        public static int NormalizeMarketFormatIndex(int value)
        {
            return Math.Clamp(value, 0, MarketFormatOptions.Count - 1);
        }

        public static int NormalizeMarketRefreshValue(int value)
        {
            return value <= 0 ? Settings.DefaultMarketRefreshSec : Math.Max(Settings.DefaultMarketRefreshSec, value);
        }

        public static string BuildNoDataReason(string error, string nextStep)
        {
            return string.IsNullOrWhiteSpace(error)
                ? $"状态：尚未刷新成功  原因：还没有可用数据  下一步：{nextStep}"
                : $"状态：刷新失败  原因：{error}  下一步：{nextStep}";
        }

        public static string BuildCachedReason(string error, string nextStep)
        {
            return string.IsNullOrWhiteSpace(error)
                ? $"状态：缓存可用  原因：本次刷新未成功  下一步：{nextStep}"
                : $"状态：缓存可用  原因：{error}  下一步：{nextStep}";
        }

        public static string NormalizeSourceText(string? source, string fallback)
        {
            if (string.IsNullOrWhiteSpace(source))
                return fallback;

            string text = source.Trim();
            return text == "公开页面接口" ? "公开接口" : text;
        }

        public static DataSourceStatusViewModel BuildSourceStatus(MarketSourceStateSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            int interval = NormalizeMarketRefreshValue(snapshot.RefreshIntervalSeconds);
            string normalizedError = AppActions.SanitizeError(snapshot.LastError);
            string nextStep = $"点击顶部“立即刷新”，仍失败再检查 {snapshot.CredentialName}";
            if (!snapshot.HasData)
            {
                bool hasError = !string.IsNullOrWhiteSpace(normalizedError);
                DataSourceStatusTone tone = hasError ? DataSourceStatusTone.Warning : DataSourceStatusTone.Muted;
                return new DataSourceStatusViewModel(
                    new DataSourceStatusText(hasError ? "刷新失败" : "未刷新成功", tone),
                    new DataSourceStatusText(BuildNoDataReason(normalizedError, nextStep), tone),
                    new DataSourceStatusText(snapshot.HasCredential ? "官方 API" : "公开接口", DataSourceStatusTone.Muted),
                    new DataSourceStatusText($"{interval} 秒", DataSourceStatusTone.Normal));
            }

            string source = NormalizeSourceText(snapshot.Source, "公开接口");
            if (snapshot.IsStale)
            {
                return new DataSourceStatusViewModel(
                    new DataSourceStatusText("缓存可用", DataSourceStatusTone.Warning),
                    new DataSourceStatusText(BuildCachedReason(normalizedError, nextStep), DataSourceStatusTone.Warning),
                    new DataSourceStatusText(source, DataSourceStatusTone.Normal),
                    new DataSourceStatusText($"{interval} 秒", DataSourceStatusTone.Normal));
            }

            return new DataSourceStatusViewModel(
                new DataSourceStatusText("正常", DataSourceStatusTone.Success),
                new DataSourceStatusText(
                    $"状态：正常  原因：已刷新成功  下一步：无需处理  上次：{snapshot.RetrievedAt:yyyy-MM-dd HH:mm:ss}",
                    DataSourceStatusTone.Muted),
                new DataSourceStatusText(source, DataSourceStatusTone.Normal),
                new DataSourceStatusText($"{interval} 秒", DataSourceStatusTone.Normal));
        }
    }

    internal sealed record DataSourceStatusViewModel(
        DataSourceStatusText CardStatus,
        DataSourceStatusText LastRefresh,
        DataSourceStatusText Source,
        DataSourceStatusText Interval);

    internal sealed record DataSourceStatusText(
        string Text,
        DataSourceStatusTone Tone);

    internal sealed record DataPageMarketFormatOption(
        string Text,
        int Value);

    internal enum DataSourceStatusTone
    {
        Success,
        Warning,
        Muted,
        Normal
    }
}
