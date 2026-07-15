using System;
using System.Drawing;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed record DataRedesignPageView(
        DataRedesignSummaryView Summary,
        DataRedesignSourceView SteamDt,
        DataRedesignSourceView Csqaq,
        string FormatText,
        string SteamDtPreviewText,
        string CsqaqPreviewText,
        Color SteamDtPreviewColor,
        Color CsqaqPreviewColor,
        bool SteamDtApiConfigured,
        bool CsqaqApiConfigured);

    internal sealed record DataRedesignSummaryView(
        string HealthText,
        Color HealthColor,
        string LastRefreshText,
        string FormatText,
        string SteamDtConfigText,
        string CsqaqConfigText);

    internal sealed record DataRedesignSourceView(
        string Title,
        string StatusText,
        Color StatusColor,
        string SourceText,
        string IndexText,
        string ChangeText,
        Color ChangeColor,
        string IntervalText,
        string LastRefreshText,
        string DetailText,
        bool TrendPositive);

    internal readonly record struct DataRedesignSourceCardsLayout(
        int HostHeight,
        Rectangle SteamDtBounds,
        Rectangle CsqaqBounds,
        bool Stacked);

    internal readonly record struct DataRedesignSourceCardLayout(
        Rectangle TitleBounds,
        Rectangle StatusPillBounds,
        Rectangle SourcePillBounds,
        Rectangle IndexTitleBounds,
        Rectangle IndexBounds,
        Rectangle ChangeBounds,
        Rectangle SparklineBounds,
        Rectangle IntervalTitleBounds,
        Rectangle IntervalBounds,
        Rectangle LastTitleBounds,
        Rectangle LastRefreshBounds,
        Rectangle DividerBounds,
        Rectangle DetailBounds);

    internal static class DataRedesignPageModel
    {
        public const string ApiHintLine1 = "建议在下方接口配置 Steam DT API和CSQAQ API";
        public const string ApiHintLine2 = "配置完成后  数据源更加稳定  各种功能也更加稳定";
        public const string ApiHelpText = "无 API 时走公开接口；填写后优先使用官方 API。";

        private static readonly Color RiseColor = Color.FromArgb(220, 70, 90);
        private static readonly Color FallColor = Color.FromArgb(80, 160, 135);

        public static DataRedesignPageView Build(
            Settings cfg,
            MarketSourceStatesSnapshot sources)
        {
            ArgumentNullException.ThrowIfNull(cfg);
            ArgumentNullException.ThrowIfNull(sources);

            bool hasSteamDtApi = sources.SteamDt.HasCredential;
            bool hasCsqaqApi = sources.Csqaq.HasCredential;
            string formatText = GetMarketFormatText(cfg.MarketFormat);

            var steamDtView = BuildSource(sources.SteamDt);
            var csqaqView = BuildSource(sources.Csqaq);
            var summary = BuildSummary(steamDtView, csqaqView, sources, formatText);

            return new DataRedesignPageView(
                summary,
                steamDtView,
                csqaqView,
                formatText,
                BuildPreviewText("DT", steamDtView.IndexText, steamDtView.ChangeText),
                BuildPreviewText("QAQ", csqaqView.IndexText, csqaqView.ChangeText),
                steamDtView.ChangeColor,
                csqaqView.ChangeColor,
                hasSteamDtApi,
                hasCsqaqApi);
        }

        public static string GetMarketFormatText(int marketFormat)
        {
            int index = DataPageModel.NormalizeMarketFormatIndex(marketFormat);
            return DataPageModel.MarketFormatOptions[index].Text;
        }

        public static Color GetChangeColor(double percent)
        {
            if (percent > 0) return RiseColor;
            if (percent < 0) return FallColor;
            return UIColors.TextMain;
        }

        public static DataRedesignSourceCardsLayout BuildSourceCardsLayout(int hostWidth)
        {
            int gap = UIUtils.S(16);
            int width = Math.Max(1, (Math.Max(1, hostWidth) - gap) / 2);
            int secondWidth = Math.Max(1, hostWidth - width - gap);
            int height = UIUtils.S(224);
            return new DataRedesignSourceCardsLayout(
                height,
                new Rectangle(0, 0, width, height),
                new Rectangle(width + gap, 0, secondWidth, height),
                false);
        }

        public static DataRedesignSourceCardLayout BuildSourceCardLayout(int cardWidth)
        {
            int width = Math.Max(1, cardWidth);
            int pad = width < UIUtils.S(460) ? UIUtils.S(18) : UIUtils.S(24);
            int right = Math.Max(pad + 1, width - pad);
            int statusWidth = width < UIUtils.S(360) ? UIUtils.S(48) : UIUtils.S(54);
            int sourceWidth = width < UIUtils.S(360) ? UIUtils.S(62) : UIUtils.S(72);
            int pillGap = UIUtils.S(8);

            var sourcePill = new Rectangle(Math.Max(pad, right - sourceWidth), UIUtils.S(18), Math.Min(sourceWidth, Math.Max(1, right - pad)), UIUtils.S(24));
            var statusPill = new Rectangle(Math.Max(pad, sourcePill.Left - pillGap - statusWidth), UIUtils.S(18), Math.Min(statusWidth, Math.Max(1, sourcePill.Left - pillGap - pad)), UIUtils.S(24));
            int titleRight = Math.Max(pad + 1, statusPill.Left - UIUtils.S(10));
            var title = new Rectangle(pad, UIUtils.S(18), Math.Max(1, titleRight - pad), UIUtils.S(28));

            if (width < UIUtils.S(520))
            {
                int metricTop = UIUtils.S(66);
                int intervalWidth = Math.Min(UIUtils.S(118), Math.Max(UIUtils.S(86), right - pad));
                int intervalLeft = Math.Max(pad, right - intervalWidth);
                int metricRight = Math.Max(pad + UIUtils.S(72), intervalLeft - UIUtils.S(12));
                int indexWidth = Math.Min(UIUtils.S(118), Math.Max(UIUtils.S(76), metricRight - pad - UIUtils.S(58)));
                var indexTitle = new Rectangle(pad, metricTop - UIUtils.S(4), Math.Max(1, metricRight - pad), UIUtils.S(22));
                var index = new Rectangle(pad, metricTop + UIUtils.S(24), indexWidth, UIUtils.S(34));
                int changeLeft = index.Right + UIUtils.S(8);
                var change = new Rectangle(changeLeft, index.Top + UIUtils.S(4), Math.Max(1, metricRight - changeLeft), UIUtils.S(28));
                var sparkline = new Rectangle(pad, UIUtils.S(132), Math.Max(1, metricRight - pad), UIUtils.S(34));
                var intervalTitle = new Rectangle(intervalLeft, metricTop - UIUtils.S(4), intervalWidth, UIUtils.S(22));
                var interval = new Rectangle(intervalLeft, metricTop + UIUtils.S(24), intervalWidth, UIUtils.S(34));
                var lastTitle = new Rectangle(intervalLeft, interval.Bottom + UIUtils.S(12), intervalWidth, UIUtils.S(20));
                var lastRefresh = new Rectangle(intervalLeft, lastTitle.Bottom, Math.Max(1, right - intervalLeft), UIUtils.S(22));
                var divider = new Rectangle(pad, UIUtils.S(178), Math.Max(1, width - pad * 2), 1);
                var detail = new Rectangle(pad, divider.Bottom + UIUtils.S(8), Math.Max(1, width - pad * 2), UIUtils.S(26));

                return new DataRedesignSourceCardLayout(
                    title,
                    statusPill,
                    sourcePill,
                    indexTitle,
                    index,
                    change,
                    sparkline,
                    intervalTitle,
                    interval,
                    lastTitle,
                    lastRefresh,
                    divider,
                    detail);
            }

            int wideMetricTop = UIUtils.S(70);
            int sideLeft = Math.Max(UIUtils.S(330), width - pad - UIUtils.S(218));
            var wideIndexTitle = new Rectangle(pad, wideMetricTop - UIUtils.S(4), UIUtils.S(120), UIUtils.S(22));
            var wideIndex = new Rectangle(pad, wideMetricTop + UIUtils.S(24), UIUtils.S(128), UIUtils.S(34));
            var wideChange = new Rectangle(wideIndex.Right + UIUtils.S(14), wideIndex.Top + UIUtils.S(4), Math.Max(1, sideLeft - wideIndex.Right - UIUtils.S(24)), UIUtils.S(28));
            var wideSparkline = new Rectangle(pad, UIUtils.S(138), Math.Max(1, sideLeft - pad - UIUtils.S(20)), UIUtils.S(34));
            var wideIntervalTitle = new Rectangle(sideLeft, wideMetricTop - UIUtils.S(4), UIUtils.S(80), UIUtils.S(22));
            var wideInterval = new Rectangle(sideLeft, wideMetricTop + UIUtils.S(24), UIUtils.S(118), UIUtils.S(34));
            var wideLastTitle = new Rectangle(sideLeft, wideInterval.Bottom + UIUtils.S(16), UIUtils.S(80), UIUtils.S(22));
            var wideLastRefresh = new Rectangle(wideLastTitle.Right + UIUtils.S(10), wideLastTitle.Top, Math.Max(1, right - wideLastTitle.Right - UIUtils.S(10)), UIUtils.S(22));
            var wideDivider = new Rectangle(pad, UIUtils.S(178), Math.Max(1, width - pad * 2), 1);
            var wideDetail = new Rectangle(pad, wideDivider.Bottom + UIUtils.S(8), Math.Max(1, width - pad * 2), UIUtils.S(26));

            return new DataRedesignSourceCardLayout(
                title,
                statusPill,
                sourcePill,
                wideIndexTitle,
                wideIndex,
                wideChange,
                wideSparkline,
                wideIntervalTitle,
                wideInterval,
                wideLastTitle,
                wideLastRefresh,
                wideDivider,
                wideDetail);
        }

        private static DataRedesignSourceView BuildSource(MarketSourceStateSnapshot source)
        {
            DataSourceStatusViewModel status = DataPageModel.BuildSourceStatus(source);
            string index = source.HasData ? MarketDisplayFormatter.FormatIndex(source.Index) : "--";
            string change = source.HasData ? MarketDisplayFormatter.FormatSignedPercent(source.Percent) : "--";
            string refresh = !source.HasData || source.RetrievedAt == default
                ? "--"
                : source.RetrievedAt.ToString("yyyy-MM-dd HH:mm:ss");
            return new DataRedesignSourceView(
                $"{source.DisplayName} 市场监控",
                status.CardStatus.Text,
                DataPageStatusRenderer.StatusToneColor(status.CardStatus.Tone),
                status.Source.Text,
                index,
                change,
                source.HasData ? GetChangeColor(source.Percent) : UIColors.TextSub,
                $"{DataPageModel.NormalizeMarketRefreshValue(source.RefreshIntervalSeconds)}",
                refresh,
                TrimLastRefreshSuffix(status.LastRefresh.Text),
                source.Percent >= 0);
        }

        private static DataRedesignSummaryView BuildSummary(
            DataRedesignSourceView steamDtView,
            DataRedesignSourceView csqaqView,
            MarketSourceStatesSnapshot sources,
            string formatText)
        {
            int healthy = (steamDtView.StatusText == "正常" ? 1 : 0) + (csqaqView.StatusText == "正常" ? 1 : 0);
            Color healthColor = healthy == 2 ? UIColors.Positive : healthy == 0 ? UIColors.TextWarn : UIColors.TextMain;
            DateTime latest = Max(sources.SteamDt.RetrievedAt, sources.Csqaq.RetrievedAt);
            string latestText = latest == default ? "--" : latest.ToString("yyyy-MM-dd HH:mm:ss");
            return new DataRedesignSummaryView(
                $"{healthy} / 2 正常",
                healthColor,
                latestText,
                formatText,
                sources.SteamDt.HasCredential ? "SteamDT API 已配置" : "SteamDT API 未配置",
                sources.Csqaq.HasCredential ? "CSQAQ API 已配置" : "CSQAQ API 未配置");
        }

        private static string BuildPreviewText(string label, string index, string change)
        {
            return $"{label}  {index}  {change}";
        }

        private static DateTime Max(DateTime left, DateTime right)
        {
            return left >= right ? left : right;
        }

        private static string TrimLastRefreshSuffix(string text)
        {
            string value = text ?? string.Empty;
            int index = value.IndexOf("  上次：", StringComparison.Ordinal);
            return index < 0 ? value : value[..index];
        }
    }
}
