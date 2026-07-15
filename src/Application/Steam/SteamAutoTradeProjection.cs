using System;
using System.Collections.Generic;
using System.Linq;

namespace CS2TradeMonitor.Application.Steam
{
    internal enum SteamAutoTradeProjectionView
    {
        SteamOffers = 0,
        YouPinAutoQuote = 1
    }

    internal enum SteamAutoTradeResultTone
    {
        Neutral = 0,
        Success = 1,
        Warning = 2,
        Failure = 3
    }

    internal sealed class SteamAutoTradeRecordRow
    {
        public DateTime Time { get; init; }
        public string TimeText { get; init; } = "";
        public SteamAutoTradeRecordType Type { get; init; }
        public string TypeText { get; init; } = "";
        public SteamAutoTradeDirection Direction { get; init; }
        public string DirectionText { get; init; } = "";
        public string ItemsText { get; init; } = "";
        public string SourceText { get; init; } = "";
        public string ResultText { get; init; } = "";
        public SteamAutoTradeResultTone ResultTone { get; init; }
    }

    internal static class SteamAutoTradeProjection
    {
        private static readonly HashSet<string> YouPinSources = new(StringComparer.Ordinal)
        {
            "悠悠购买",
            "悠悠出售",
            "悠悠出租"
        };

        public static SteamAutoTradeSettings BuildSteamSettings(
            bool acceptPureIncoming,
            bool acceptYouPinPurchase,
            bool sendYouPinSale,
            bool sendYouPinRental,
            int intervalSeconds)
        {
            return SteamAutoTradeSettingsPersistence.Normalize(new SteamAutoTradeSettings
            {
                AcceptPureIncomingEnabled = acceptPureIncoming,
                AcceptYouPinPurchaseEnabled = acceptYouPinPurchase,
                SendYouPinSaleEnabled = sendYouPinSale,
                SendYouPinRentalEnabled = sendYouPinRental,
                IntervalSeconds = intervalSeconds
            });
        }

        public static SteamAutoTradeSettings BuildYouPinSettings(
            SteamAutoTradeSettings current,
            bool acceptPurchase,
            bool sendSale,
            bool sendRental)
        {
            ArgumentNullException.ThrowIfNull(current);

            return SteamAutoTradeSettingsPersistence.Normalize(new SteamAutoTradeSettings
            {
                AcceptPureIncomingEnabled = current.AcceptPureIncomingEnabled,
                AcceptYouPinPurchaseEnabled = acceptPurchase,
                SendYouPinSaleEnabled = sendSale,
                SendYouPinRentalEnabled = sendRental,
                IntervalSeconds = current.IntervalSeconds
            });
        }

        public static IReadOnlyList<SteamAutoTradeRecordRow> BuildRecordRows(
            IEnumerable<SteamAutoTradeRecord> records,
            SteamAutoTradeProjectionView view,
            int limit = 5)
        {
            ArgumentNullException.ThrowIfNull(records);
            if (limit <= 0)
                return Array.Empty<SteamAutoTradeRecordRow>();

            IEnumerable<SteamAutoTradeRecord> filtered = records.Where(record => record is not null);
            if (view == SteamAutoTradeProjectionView.YouPinAutoQuote)
                filtered = filtered.Where(record => YouPinSources.Contains(record.Source ?? ""));

            return filtered
                .OrderByDescending(record => record.Time)
                .Take(limit)
                .Select(record => BuildRecordRow(record, view))
                .ToList();
        }

        private static SteamAutoTradeRecordRow BuildRecordRow(
            SteamAutoTradeRecord record,
            SteamAutoTradeProjectionView view)
        {
            IEnumerable<string> itemNames = record.ItemNames ?? new List<string>();
            if (view == SteamAutoTradeProjectionView.SteamOffers)
                itemNames = itemNames.Take(3);

            string source = record.Source ?? "";
            string result = record.Result ?? "";
            return new SteamAutoTradeRecordRow
            {
                Time = record.Time,
                TimeText = view == SteamAutoTradeProjectionView.YouPinAutoQuote && record.Time == default
                    ? "--"
                    : record.Time.ToString("HH:mm:ss"),
                Type = record.Type,
                TypeText = view == SteamAutoTradeProjectionView.YouPinAutoQuote
                    && record.Type == SteamAutoTradeRecordType.AutoMobileConfirm
                        ? "Steam 手机确认"
                        : SteamAutoTradePlanner.FormatRecordType(record.Type),
                Direction = record.Direction,
                DirectionText = SteamAutoTradePlanner.FormatDirection(record.Direction),
                ItemsText = string.Join("、", itemNames),
                SourceText = view == SteamAutoTradeProjectionView.SteamOffers && string.IsNullOrWhiteSpace(source)
                    ? "-"
                    : source,
                ResultText = string.IsNullOrWhiteSpace(result) ? record.Reason ?? "" : result,
                ResultTone = ResolveResultTone(record, view)
            };
        }

        private static SteamAutoTradeResultTone ResolveResultTone(
            SteamAutoTradeRecord record,
            SteamAutoTradeProjectionView view)
        {
            string result = record.Result ?? "";
            if (record.Type is SteamAutoTradeRecordType.Failed or SteamAutoTradeRecordType.TerminalFailure
                || string.Equals(result, "失败", StringComparison.Ordinal))
            {
                return SteamAutoTradeResultTone.Failure;
            }

            if (record.Type is SteamAutoTradeRecordType.Pending or SteamAutoTradeRecordType.Unresolved
                || string.Equals(result, "待确认", StringComparison.Ordinal)
                || (view == SteamAutoTradeProjectionView.SteamOffers
                    && result.Contains("等待", StringComparison.Ordinal)))
            {
                return SteamAutoTradeResultTone.Warning;
            }

            if (record.Type == SteamAutoTradeRecordType.Skip
                || string.Equals(result, "跳过", StringComparison.Ordinal))
            {
                return SteamAutoTradeResultTone.Neutral;
            }

            if (result.Contains("成功", StringComparison.Ordinal)
                || view == SteamAutoTradeProjectionView.YouPinAutoQuote)
            {
                return SteamAutoTradeResultTone.Success;
            }

            return SteamAutoTradeResultTone.Neutral;
        }
    }
}
