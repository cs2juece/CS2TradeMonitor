using CS2TradeMonitor.Domain.Steam;
using System;

namespace CS2TradeMonitor.Application.Steam
{
    internal static class SteamAssetNameCompletionHelper
    {
        public const string PendingSteamItemName = "待确认饰品（Steam 未返回名称）";

        public static bool NeedsExternalLookup(TradeAsset asset)
        {
            if (asset == null)
                return false;

            string name = Normalize(asset.MarketHashName);
            return string.IsNullOrWhiteSpace(name)
                || IsPlaceholderName(name)
                || IsLikelyPartialName(name);
        }

        public static bool ShouldReplaceWithDescription(string? currentName, string? descriptionName)
        {
            string current = Normalize(currentName);
            string candidate = Normalize(descriptionName);
            if (string.IsNullOrWhiteSpace(candidate))
                return false;
            if (string.IsNullOrWhiteSpace(current))
                return true;
            if (IsPlaceholderName(current))
                return true;
            if (string.Equals(current, candidate, StringComparison.Ordinal))
                return false;
            if (IsLikelyPartialName(current))
                return true;
            if (candidate.StartsWith(current, StringComparison.OrdinalIgnoreCase)
                && candidate.Length >= current.Length + 3)
            {
                return true;
            }

            return false;
        }

        public static bool IsPlaceholderName(string name)
            => name.StartsWith("未命名饰品 ", StringComparison.Ordinal)
                || name.Equals(PendingSteamItemName, StringComparison.Ordinal);

        private static bool IsLikelyPartialName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return true;

            string value = Normalize(name);
            return value.EndsWith("|", StringComparison.Ordinal)
                || value.EndsWith("｜", StringComparison.Ordinal)
                || value.EndsWith(":", StringComparison.Ordinal)
                || value.EndsWith("：", StringComparison.Ordinal);
        }

        private static string Normalize(string? value)
            => (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
    }
}
