using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.SystemServices;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CS2TradeMonitor.Application.YouPin
{
    internal static class YouPinInventoryHistoryStore
    {
        // Stop-profit/loss rules can look back at most seven days. Keep one extra
        // day at full resolution so the oldest supported baseline remains exact.
        internal const int FullResolutionRetentionDays = 8;

        public static YouPinInventoryHistory Load(string path, JsonSerializerOptions options)
        {
            try
            {
                if (!File.Exists(path)) return new YouPinInventoryHistory();
                var json = File.ReadAllText(path);
                var history = Normalize(JsonSerializer.Deserialize<YouPinInventoryHistory>(json, options) ?? new YouPinInventoryHistory());
                if (Prune(history))
                    Save(path, history, options);
                return history;
            }
            catch
            {
                return new YouPinInventoryHistory();
            }
        }

        public static void Save(string path, YouPinInventoryHistory history, JsonSerializerOptions options)
        {
            try
            {
                var json = JsonSerializer.Serialize(history, options);
                RuntimeDataPaths.WriteTextAtomic(path, json);
            }
            catch
            {
                // History persistence must not block monitoring.
            }
        }

        public static YouPinInventoryHistory Normalize(YouPinInventoryHistory history)
        {
            history.Snapshots ??= new List<YouPinInventorySnapshot>();
            history.Changes ??= new List<YouPinInventoryChange>();
            history.Daily ??= new List<YouPinDailyPnl>();
            history.ValueAlerts ??= new List<YouPinInventoryValueAlert>();
            history.StopProfitLossAlerts ??= new List<YouPinStopProfitLossAlert>();
            history.LastStopProfitLossAlertTimes ??= new Dictionary<string, DateTime>();

            // 清理历史中因钱包余额等无关字段而产生的明显异常估值点。
            // 只有快照内有饰品明细，且原估值远小于饰品市场价总和时才修复，避免误改真实小额库存。
            foreach (var snapshot in history.Snapshots)
            {
                if (snapshot == null)
                    continue;

                double itemSum = snapshot.Items != null ? snapshot.Items.Sum(x => x.Price) : 0;
                if (itemSum > 100 && snapshot.TotalValue > 0 && snapshot.TotalValue < itemSum * 0.1)
                {
                    snapshot.TotalValue = itemSum;
                }
            }

            foreach (var daily in history.Daily)
            {
                if (daily == null)
                    continue;

                var matchingSnapshot = history.Snapshots
                    .Where(s => s.Time.ToString("yyyy-MM-dd") == daily.Date)
                    .OrderBy(s => s.Time)
                    .LastOrDefault();

                if (matchingSnapshot != null
                    && matchingSnapshot.TotalValue > 100
                    && daily.EndValue > 0
                    && daily.EndValue < matchingSnapshot.TotalValue * 0.1)
                    daily.EndValue = matchingSnapshot.TotalValue;
            }

            // 重新计算每日盈亏。
            var orderedDaily = history.Daily.OrderBy(x => x.Date).ToList();
            for (int i = 0; i < orderedDaily.Count; i++)
            {
                if (i == 0)
                {
                    orderedDaily[i].Pnl = 0;
                }
                else
                {
                    orderedDaily[i].Pnl = orderedDaily[i].EndValue - orderedDaily[i - 1].EndValue;
                }
            }

            foreach (var daily in orderedDaily)
            {
                if (daily == null || !daily.HasProfitAndLoss)
                    continue;

                // Older builds could persist the daily inventory valuation delta as official
                // total profit/loss, which makes the funds curve show impossible highs.
                if (daily.EndValue > 100
                    && Math.Abs(daily.ProfitAndLoss - daily.Pnl) < 0.01
                    && Math.Abs(daily.ProfitAndLoss) > Math.Max(1000, daily.EndValue * 0.5))
                {
                    daily.HasProfitAndLoss = false;
                    daily.ProfitAndLoss = 0;
                }
            }

            return history;
        }

        public static bool Prune(YouPinInventoryHistory history)
        {
            try
            {
                Normalize(history);
                int entryCountBefore = CountRetainedEntries(history);
                var now = DateTime.Now;
                var fullCutoff = now.AddDays(-FullResolutionRetentionDays);
                var retentionCutoff = now.AddDays(-180);

                var recentSnapshots = history.Snapshots
                    .Where(x => x != null && x.Time >= fullCutoff)
                    .ToList();
                var olderDailySnapshots = history.Snapshots
                    .Where(x => x != null && x.Time < fullCutoff && x.Time >= retentionCutoff)
                    .GroupBy(x => x.Time.Date)
                    .Select(g => g.OrderBy(x => x.Time).Last())
                    .ToList();

                history.Snapshots = olderDailySnapshots
                    .Concat(recentSnapshots)
                    .OrderBy(x => x.Time)
                    .ToList();

                history.Changes = history.Changes
                    .Where(x => x != null && x.Time >= retentionCutoff)
                    .OrderBy(x => x.Time)
                    .TakeLast(2000)
                    .ToList();

                history.ValueAlerts = history.ValueAlerts
                    .Where(x => x != null)
                    .OrderBy(x => x.Time)
                    .TakeLast(1000)
                    .ToList();

                history.StopProfitLossAlerts = history.StopProfitLossAlerts
                    .Where(x => x != null)
                    .OrderBy(x => x.Time)
                    .TakeLast(1000)
                    .ToList();

                string minDate = retentionCutoff.ToString("yyyy-MM-dd");
                history.Daily = history.Daily
                    .Where(x => x != null && string.CompareOrdinal(x.Date, minDate) >= 0)
                    .OrderBy(x => x.Date)
                    .TakeLast(180)
                    .ToList();

                return CountRetainedEntries(history) != entryCountBefore;
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Info("YouPinInventory", $"历史裁剪跳过: {ex.Message}");
                return false;
            }
        }

        private static int CountRetainedEntries(YouPinInventoryHistory history)
        {
            return history.Snapshots.Count
                + history.Changes.Count
                + history.Daily.Count
                + history.ValueAlerts.Count
                + history.StopProfitLossAlerts.Count;
        }
    }
}
