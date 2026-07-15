using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CS2TradeMonitor.Application.YouPin
{
    internal enum YouPinProfitLossRedesignFilter
    {
        All,
        Matched,
        Profit,
        Loss,
        Failed
    }

    internal sealed class YouPinProfitLossRedesignView
    {
        public List<YouPinProfitLossRedesignRow> Rows { get; set; } = new();
        public List<YouPinProfitLossRedesignRow> MatchedRows { get; set; } = new();
        public List<YouPinProfitLossRedesignRow> FailedRows { get; set; } = new();
        public int RecordCount { get; set; }
        public int ItemCount { get; set; }
        public int FailedCount { get; set; }
        public int FailedBuyCount { get; set; }
        public int FailedSellCount { get; set; }
        public double MatchedBuyTotal { get; set; }
        public double MatchedSellTotal { get; set; }
        public double NetTotal { get; set; }
        public double NetRate { get; set; }
        public double ProfitTotal { get; set; }
        public double LossTotal { get; set; }
        public double FailedBuyAmount { get; set; }
        public double FailedSellAmount { get; set; }
    }

    internal sealed class YouPinProfitLossRedesignRow
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string TemplateId { get; set; } = "";
        public string AssetId { get; set; } = "";
        public string MatchStatus { get; set; } = "";
        public string BadgeText { get; set; } = "";
        public bool IsMatched { get; set; }
        public bool IsFailed { get; set; }
        public int BuyCount { get; set; }
        public int SellCount { get; set; }
        public double BuyAmount { get; set; }
        public double SellAmount { get; set; }
        public double NetProfit { get; set; }
        public double NetRate { get; set; }
        public DateTime LastTradeTime { get; set; } = DateTime.MinValue;
        public string SearchText { get; set; } = "";
    }

    internal static class YouPinProfitLossRedesignProjection
    {
        public static YouPinProfitLossRedesignView Build(IEnumerable<YouPinProfitLossRecord> source)
        {
            var records = (source ?? Enumerable.Empty<YouPinProfitLossRecord>())
                .Where(record => record != null && record.Amount > 0)
                .ToList();
            var rows = new List<YouPinProfitLossRedesignRow>();

            foreach (var record in records.Where(record => string.IsNullOrWhiteSpace(YouPinProfitLossRecordProjection.BuildEstimatedMatchKey(record))))
            {
                rows.Add(BuildFailedRow(
                    new[] { record },
                    "缺少匹配字段"));
            }

            foreach (var group in records
                .Where(record => !string.IsNullOrWhiteSpace(YouPinProfitLossRecordProjection.BuildEstimatedMatchKey(record)))
                .GroupBy(YouPinProfitLossRecordProjection.BuildEstimatedMatchKey, StringComparer.OrdinalIgnoreCase))
            {
                var buys = ExpandUnits(group.Where(record => record.Direction == YouPinProfitLossDirection.Buy))
                    .OrderBy(unit => unit.Record.Time)
                    .ToList();
                var sells = ExpandUnits(group.Where(record => record.Direction == YouPinProfitLossDirection.Sell))
                    .OrderBy(unit => unit.Record.Time)
                    .ToList();

                // 同一估算身份内按时间 FIFO 配对；配不上的买入/卖出保留为记录缺失，避免把不可靠记录算进盈亏。
                int pairCount = Math.Min(buys.Count, sells.Count);
                if (pairCount > 0)
                {
                    rows.Add(BuildMatchedRow(
                        group.Key,
                        buys.Take(pairCount).ToList(),
                        sells.Take(pairCount).ToList()));
                }

                if (buys.Count > pairCount)
                {
                    rows.Add(BuildFailedUnitRow(
                        group.Key,
                        buys.Skip(pairCount).ToList(),
                        YouPinProfitLossDirection.Buy,
                        "只买未售"));
                }

                if (sells.Count > pairCount)
                {
                    rows.Add(BuildFailedUnitRow(
                        group.Key,
                        sells.Skip(pairCount).ToList(),
                        YouPinProfitLossDirection.Sell,
                        "只售无买"));
                }
            }

            rows = rows
                .OrderBy(row => row.IsFailed)
                .ThenByDescending(row => row.LastTradeTime)
                .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var matched = rows.Where(row => row.IsMatched).ToList();
            var failed = rows.Where(row => row.IsFailed).ToList();
            // 记录缺失说明买卖链路不完整或关键字段不足，不纳入顶部盈亏，等待后续同步补齐后再自动重算。
            double matchedBuy = matched.Sum(row => row.BuyAmount);
            double matchedSell = matched.Sum(row => row.SellAmount);
            double net = matchedSell - matchedBuy;

            return new YouPinProfitLossRedesignView
            {
                Rows = rows,
                MatchedRows = matched,
                FailedRows = failed,
                RecordCount = records.Count,
                ItemCount = rows.Count,
                FailedCount = failed.Sum(row => row.BuyCount + row.SellCount),
                FailedBuyCount = failed.Sum(row => row.BuyCount),
                FailedSellCount = failed.Sum(row => row.SellCount),
                MatchedBuyTotal = matchedBuy,
                MatchedSellTotal = matchedSell,
                NetTotal = net,
                NetRate = matchedBuy > 0 ? net / matchedBuy * 100.0 : 0,
                ProfitTotal = matched.Where(row => row.NetProfit > 0).Sum(row => row.NetProfit),
                LossTotal = Math.Abs(matched.Where(row => row.NetProfit < 0).Sum(row => row.NetProfit)),
                FailedBuyAmount = failed.Sum(row => row.BuyAmount),
                FailedSellAmount = failed.Sum(row => row.SellAmount)
            };
        }

        public static List<YouPinProfitLossRedesignRow> Filter(
            IEnumerable<YouPinProfitLossRedesignRow> rows,
            YouPinProfitLossRedesignFilter filter,
            string keyword)
        {
            var query = rows ?? Enumerable.Empty<YouPinProfitLossRedesignRow>();
            query = filter switch
            {
                YouPinProfitLossRedesignFilter.Matched => query.Where(row => row.IsMatched),
                YouPinProfitLossRedesignFilter.Profit => query.Where(row => row.IsMatched && row.NetProfit > 0),
                YouPinProfitLossRedesignFilter.Loss => query.Where(row => row.IsMatched && row.NetProfit < 0),
                YouPinProfitLossRedesignFilter.Failed => query.Where(row => row.IsFailed),
                _ => query
            };

            keyword = (keyword ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(row =>
                    row.SearchText.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || row.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || row.TemplateId.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || row.AssetId.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            }

            return query.ToList();
        }

        private static List<RecordUnit> ExpandUnits(IEnumerable<YouPinProfitLossRecord> records)
        {
            var units = new List<RecordUnit>();
            foreach (var record in records)
            {
                int quantity = Math.Max(1, record.Quantity);
                double unitAmount = Math.Round(record.Amount / quantity, 2, MidpointRounding.AwayFromZero);
                for (int i = 0; i < quantity; i++)
                {
                    double amount = i == quantity - 1
                        ? Math.Round(record.Amount - unitAmount * (quantity - 1), 2, MidpointRounding.AwayFromZero)
                        : unitAmount;
                    units.Add(new RecordUnit(record, amount));
                }
            }

            return units;
        }

        private static YouPinProfitLossRedesignRow BuildMatchedRow(
            string matchKey,
            IReadOnlyList<RecordUnit> buys,
            IReadOnlyList<RecordUnit> sells)
        {
            var records = buys.Select(unit => unit.Record).Concat(sells.Select(unit => unit.Record)).ToList();
            var first = PickFirst(records);
            double buyAmount = buys.Sum(unit => unit.Amount);
            double sellAmount = sells.Sum(unit => unit.Amount);
            double net = sellAmount - buyAmount;
            string direction = net > 0 ? "吃米" : net < 0 ? "亏米" : "持平";
            return new YouPinProfitLossRedesignRow
            {
                Key = "M:" + matchKey,
                Name = first.Name,
                TemplateId = first.TemplateId,
                AssetId = first.AssetId,
                MatchStatus = "已匹配 · " + direction,
                BadgeText = direction,
                IsMatched = true,
                BuyCount = buys.Count,
                SellCount = sells.Count,
                BuyAmount = buyAmount,
                SellAmount = sellAmount,
                NetProfit = net,
                NetRate = buyAmount > 0 ? net / buyAmount * 100.0 : 0,
                LastTradeTime = records.Max(record => record.Time),
                SearchText = BuildSearchText(first, matchKey)
            };
        }

        private static YouPinProfitLossRedesignRow BuildFailedUnitRow(
            string matchKey,
            IReadOnlyList<RecordUnit> units,
            YouPinProfitLossDirection direction,
            string reason)
        {
            var records = units.Select(unit => unit.Record).ToList();
            var first = PickFirst(records);
            double amount = units.Sum(unit => unit.Amount);
            return new YouPinProfitLossRedesignRow
            {
                Key = "F:" + direction + ":" + matchKey + ":" + reason,
                Name = first.Name,
                TemplateId = first.TemplateId,
                AssetId = first.AssetId,
                MatchStatus = "记录缺失 · " + reason,
                BadgeText = direction == YouPinProfitLossDirection.Buy ? "只有购买记录" : "只有出售记录",
                IsFailed = true,
                BuyCount = direction == YouPinProfitLossDirection.Buy ? units.Count : 0,
                SellCount = direction == YouPinProfitLossDirection.Sell ? units.Count : 0,
                BuyAmount = direction == YouPinProfitLossDirection.Buy ? amount : 0,
                SellAmount = direction == YouPinProfitLossDirection.Sell ? amount : 0,
                LastTradeTime = records.Max(record => record.Time),
                SearchText = BuildSearchText(first, matchKey)
            };
        }

        private static YouPinProfitLossRedesignRow BuildFailedRow(
            IReadOnlyList<YouPinProfitLossRecord> records,
            string reason)
        {
            var first = PickFirst(records);
            var direction = first.Direction;
            int count = records.Sum(record => Math.Max(1, record.Quantity));
            double amount = records.Sum(record => record.Amount);
            return new YouPinProfitLossRedesignRow
            {
                Key = "F:" + direction + ":" + first.OrderNo + ":" + first.DetailNo,
                Name = first.Name,
                TemplateId = first.TemplateId,
                AssetId = first.AssetId,
                MatchStatus = "记录缺失 · " + reason,
                BadgeText = reason,
                IsFailed = true,
                BuyCount = direction == YouPinProfitLossDirection.Buy ? count : 0,
                SellCount = direction == YouPinProfitLossDirection.Sell ? count : 0,
                BuyAmount = direction == YouPinProfitLossDirection.Buy ? amount : 0,
                SellAmount = direction == YouPinProfitLossDirection.Sell ? amount : 0,
                LastTradeTime = records.Max(record => record.Time),
                SearchText = BuildSearchText(first, "")
            };
        }

        private static YouPinProfitLossRecord PickFirst(IReadOnlyList<YouPinProfitLossRecord> records)
        {
            return records.FirstOrDefault(record => !string.IsNullOrWhiteSpace(record.Name)) ?? records[0];
        }

        private static string BuildSearchText(YouPinProfitLossRecord record, string assetId)
        {
            return string.Join(" ", new[]
            {
                record.Name,
                record.TemplateId,
                record.CommodityHashName,
                record.Abrade,
                assetId,
                record.OrderNo,
                record.DetailNo
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private sealed record RecordUnit(YouPinProfitLossRecord Record, double Amount);
    }
}
