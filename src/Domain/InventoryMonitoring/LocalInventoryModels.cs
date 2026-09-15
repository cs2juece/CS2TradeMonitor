using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CS2TradeMonitor.Domain.InventoryMonitoring
{
    public enum LocalInventoryChangeKind
    {
        Baseline = 0,
        Purchase = 1,
        Sale = 2,
        Deposit = 3,
        Withdraw = 4,
        CooldownRecovered = 5,
        WithdrawOrRecovered = 6,
        SaleOrDeposit = 7,
        Unknown = 99
    }

    public sealed record LocalInventoryWatchTargetSpec(string SteamId, string Alias)
    {
        public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? SteamId : Alias;
    }

    public sealed record LocalInventoryChangeEvent(
        string EventKey,
        int TaskId,
        string SteamId,
        string SteamName,
        int GoodId,
        string MarketName,
        int Count,
        bool Tradable,
        LocalInventoryChangeKind Kind,
        string KindText,
        DateTimeOffset OccurredAt,
        string Source);

    public sealed record LocalInventoryTargetSnapshot(
        int TaskId,
        string SteamId,
        string DisplayName,
        string SteamName,
        int TotalItemCount,
        DateTimeOffset? LastChangedAt,
        DateTimeOffset? LastCheckedAt,
        string Status,
        string Error);

    public sealed record LocalInventoryMonitorSnapshot(
        bool Enabled,
        string Status,
        string Error,
        DateTimeOffset? LastRefreshAt,
        IReadOnlyList<LocalInventoryTargetSnapshot> Targets,
        IReadOnlyList<LocalInventoryChangeEvent> RecentEvents)
    {
        public static LocalInventoryMonitorSnapshot Empty { get; } = new(
            false,
            "未启动",
            string.Empty,
            null,
            Array.Empty<LocalInventoryTargetSnapshot>(),
            Array.Empty<LocalInventoryChangeEvent>());
    }

    public static partial class LocalInventoryWatchListParser
    {
        public const int MaximumTargets = 20;

        [GeneratedRegex(@"(?<!\d)(7656119\d{10})(?!\d)", RegexOptions.CultureInvariant)]
        private static partial Regex SteamIdRegex();

        public static IReadOnlyList<LocalInventoryWatchTargetSpec> Parse(string? raw)
        {
            var result = new List<LocalInventoryWatchTargetSpec>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string sourceLine in (raw ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = sourceLine.Trim();
                if (line.Length == 0)
                    continue;

                Match match = SteamIdRegex().Match(line);
                if (!match.Success || !seen.Add(match.Groups[1].Value))
                    continue;

                string steamId = match.Groups[1].Value;
                string alias = ExtractAlias(line, match).Trim();
                result.Add(new LocalInventoryWatchTargetSpec(steamId, alias));
                if (result.Count >= MaximumTargets)
                    break;
            }

            return result;
        }

        public static string Normalize(string? raw)
        {
            return string.Join(
                Environment.NewLine,
                Parse(raw).Select(target => string.IsNullOrWhiteSpace(target.Alias)
                    ? target.SteamId
                    : $"{target.SteamId} | {target.Alias}"));
        }

        private static string ExtractAlias(string line, Match steamIdMatch)
        {
            int separator = line.IndexOf('|');
            if (separator >= 0 && separator + 1 < line.Length)
                return line[(separator + 1)..];

            string before = line[..steamIdMatch.Index].Trim(' ', '-', '—', ':', '：');
            string after = line[(steamIdMatch.Index + steamIdMatch.Length)..].Trim(' ', '-', '—', ':', '：');
            return after.Length > 0 ? after : before;
        }
    }
}
