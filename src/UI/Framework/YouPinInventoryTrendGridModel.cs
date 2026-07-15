using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class PreparedTrendGridRows
    {
        public PreparedTrendGridRows(string signature, List<TrendGridRow> rows)
        {
            Signature = signature;
            Rows = rows;
        }

        public string Signature { get; }
        public List<TrendGridRow> Rows { get; }
    }

    internal sealed class TrendGridRow
    {
        public string Name { get; init; } = "";
        public string MetaText { get; init; } = "";
        public string PriceText { get; init; } = "";
        public string PercentText { get; init; } = "";
        public string DeltaText { get; init; } = "";
        public double Delta { get; init; }
        public bool HasComparison { get; init; }

        public static TrendGridRow From(YouPinInventoryTrendRow item)
        {
            bool hasComparison = item.HasEstimate && item.PreviousPrice > 0;
            return new TrendGridRow
            {
                Name = item.Name,
                MetaText = BuildMetaText(item),
                PriceText = item.HasEstimate ? $"¥{item.CurrentPrice:0.##}" : "暂无估值",
                PercentText = hasComparison ? YouPinInventoryTrendGridModel.FormatSignedPercent(item.Percent) : "暂无",
                DeltaText = hasComparison ? YouPinInventoryTrendGridModel.FormatSignedNumber(item.Delta) : "暂无",
                Delta = item.Delta,
                HasComparison = hasComparison
            };
        }

        private static string BuildMetaText(YouPinInventoryTrendRow item)
        {
            string quantity = $"{item.Quantity} 件";
            string purchase = item.HasPurchasePrice ? $"购入 ¥{item.PurchasePrice:0.##}" : "无购入价";
            return $"{quantity} / {purchase}";
        }
    }

    internal static class YouPinInventoryTrendGridModel
    {
        public static PreparedTrendGridRows Prepare(
            List<YouPinInventoryTrendRow> sourceRows,
            string keyword,
            string filter,
            string sortColumn,
            bool sortDescending)
        {
            string signature = BuildSignature(sourceRows, keyword, filter, sortColumn, sortDescending);
            var rows = FilterAndSort(sourceRows, keyword, filter, sortColumn, sortDescending)
                .Select(TrendGridRow.From)
                .ToList();
            return new PreparedTrendGridRows(signature, rows);
        }

        public static IEnumerable<YouPinInventoryTrendRow> FilterAndSort(
            IEnumerable<YouPinInventoryTrendRow> source,
            string keyword,
            string filter,
            string sortColumn,
            bool sortDescending)
        {
            var rows = source;
            if (!string.IsNullOrWhiteSpace(keyword))
                rows = rows.Where(x => x.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));

            rows = filter switch
            {
                "up" => rows.Where(x => x.Delta > 0),
                "down" => rows.Where(x => x.Delta < 0),
                "missing-price" => rows.Where(x => !x.HasEstimate),
                "missing-purchase" => rows.Where(x => !x.HasPurchasePrice),
                _ => rows
            };

            return Sort(rows, sortColumn, sortDescending);
        }

        public static string BuildSignature(
            IReadOnlyList<YouPinInventoryTrendRow> rows,
            string keyword,
            string filter,
            string sortColumn,
            bool sortDescending)
        {
            var hash = new HashCode();
            hash.Add(keyword, StringComparer.OrdinalIgnoreCase);
            hash.Add(filter, StringComparer.OrdinalIgnoreCase);
            hash.Add(sortColumn, StringComparer.OrdinalIgnoreCase);
            hash.Add(sortDescending);
            hash.Add(rows.Count);
            foreach (YouPinInventoryTrendRow row in rows)
            {
                hash.Add(row.TemplateId, StringComparer.OrdinalIgnoreCase);
                hash.Add(row.Name, StringComparer.OrdinalIgnoreCase);
                hash.Add(row.Quantity);
                hash.Add(row.CurrentPrice);
                hash.Add(row.PreviousPrice);
                hash.Add(row.Delta);
                hash.Add(row.Percent);
                hash.Add(row.PurchasePrice);
                hash.Add(row.MissingEstimateCount);
                hash.Add(row.MissingPurchaseCount);
            }

            return hash.ToHashCode().ToString();
        }

        public static IEnumerable<YouPinInventoryTrendRow> Sort(
            IEnumerable<YouPinInventoryTrendRow> rows,
            string columnName,
            bool descending)
        {
            return columnName switch
            {
                "Meta" => descending
                    ? rows.OrderByDescending(x => x.Quantity).ThenByDescending(x => x.PurchasePrice).ThenBy(x => x.Name)
                    : rows.OrderBy(x => x.Quantity).ThenBy(x => x.PurchasePrice).ThenBy(x => x.Name),
                "Percent" => descending
                    ? rows.OrderByDescending(x => x.Percent).ThenByDescending(x => x.CurrentPrice).ThenBy(x => x.Name)
                    : rows.OrderBy(x => x.Percent).ThenBy(x => x.CurrentPrice).ThenBy(x => x.Name),
                "Delta" => descending
                    ? rows.OrderByDescending(x => x.Delta).ThenByDescending(x => x.CurrentPrice).ThenBy(x => x.Name)
                    : rows.OrderBy(x => x.Delta).ThenBy(x => x.CurrentPrice).ThenBy(x => x.Name),
                _ => descending
                    ? rows.OrderByDescending(x => x.CurrentPrice).ThenBy(x => x.Name)
                    : rows.OrderBy(x => x.CurrentPrice).ThenBy(x => x.Name)
            };
        }

        public static string FormatSignedNumber(double value)
        {
            string sign = value > 0 ? "+" : string.Empty;
            return $"{sign}{value:F2}";
        }

        public static string FormatSignedPercent(double value)
        {
            string sign = value > 0 ? "+" : string.Empty;
            return $"{sign}{value:F2}%";
        }
    }
}
