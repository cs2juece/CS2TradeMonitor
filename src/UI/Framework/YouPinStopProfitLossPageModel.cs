using CS2TradeMonitor.Domain.Market;
using CS2TradeMonitor.src.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class YouPinStopProfitLossPageModel
    {
        public static string FormatSignedPercent(double value)
        {
            string sign = value > 0 ? "+" : string.Empty;
            return $"{sign}{value:F2}%";
        }

        public static string NormalizeKeywordInput(string value)
        {
            return string.Join(", ", SplitSpecifiedItems(value));
        }

        public static IEnumerable<string> SplitSpecifiedItems(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Enumerable.Empty<string>();

            return value
                .Replace('，', ',')
                .Replace('；', ',')
                .Replace('、', ',')
                .Replace(';', ',')
                .Split(new[] { ',', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        public static string BuildSpecifiedSummary(string? value)
        {
            var items = SplitSpecifiedItems(value).ToList();
            if (items.Count == 0)
                return " 当前未指定单品。";

            string text = string.Join("、", items.Take(3));
            if (items.Count > 3)
                text += $" 等 {items.Count} 个";

            return " 已指定：" + text + "。";
        }

        public static string CleanCandidateName(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        public static string GetCandidateKeyword(SteamDtSearchCandidate candidate)
        {
            string name = string.IsNullOrWhiteSpace(candidate.Name)
                ? candidate.MarketHashName
                : candidate.Name;
            return CleanCandidateName(name);
        }

        public static string GetCandidateDisplay(SteamDtSearchCandidate candidate)
        {
            string name = string.IsNullOrWhiteSpace(candidate.Name) ? "未命名单品" : candidate.Name.Trim();
            string price = candidate.Price > 0 ? "¥" + candidate.Price.ToString("0.##", CultureInfo.InvariantCulture) : "暂无价格";
            string source = string.IsNullOrWhiteSpace(candidate.Source) ? "来源未知" : candidate.Source.Trim();
            return $"{name}    {price} / {source}";
        }

        public static YouPinStopProfitLossStatusPanelLayout BuildStatusPanelLayout(Rectangle contentBounds, int buttonHeight)
        {
            int desiredGap = UIUtils.S(12);
            int buttonWidth = Math.Min(UIUtils.S(128), Math.Max(1, contentBounds.Width / 4));
            int gap = Math.Min(
                desiredGap,
                Math.Max(0, (contentBounds.Width - buttonWidth - 4) / 4));
            int tilesWidth = Math.Max(4, contentBounds.Width - buttonWidth - gap * 4);
            int tileWidth = Math.Max(1, tilesWidth / 4);
            int lastTileWidth = Math.Max(1, tilesWidth - tileWidth * 3);
            int x = contentBounds.Left;
            var enabledTile = new Rectangle(x, contentBounds.Top, tileWidth, contentBounds.Height);
            x = enabledTile.Right + gap;
            var stateTile = new Rectangle(x, contentBounds.Top, tileWidth, contentBounds.Height);
            x = stateTile.Right + gap;
            var scanTile = new Rectangle(x, contentBounds.Top, tileWidth, contentBounds.Height);
            x = scanTile.Right + gap;
            var alertTile = new Rectangle(x, contentBounds.Top, lastTileWidth, contentBounds.Height);
            var scanButton = new Rectangle(
                contentBounds.Right - buttonWidth,
                contentBounds.Top + Math.Max(0, (contentBounds.Height - buttonHeight) / 2),
                buttonWidth,
                Math.Min(buttonHeight, contentBounds.Height));
            return new YouPinStopProfitLossStatusPanelLayout(
                enabledTile,
                stateTile,
                scanTile,
                alertTile,
                scanButton);
        }
    }

    internal readonly record struct YouPinStopProfitLossStatusPanelLayout(
        Rectangle EnabledTileBounds,
        Rectangle StateTileBounds,
        Rectangle ScanTileBounds,
        Rectangle AlertTileBounds,
        Rectangle ScanButtonBounds);

    internal sealed class SpecifiedCandidateListItem
    {
        private readonly string _displayText;

        public SpecifiedCandidateListItem(SteamDtSearchCandidate candidate, string displayText)
        {
            Candidate = candidate;
            _displayText = displayText;
        }

        public SteamDtSearchCandidate Candidate { get; }

        public override string ToString()
        {
            return _displayText;
        }
    }
}
