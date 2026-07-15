using System;
using System.Collections.Generic;
using System.Linq;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class YouPinInventoryTrendCurveModel
    {
        public static List<YouPinDailyPnl> BuildCurvePoints(List<YouPinDailyPnl>? source, YouPinInventoryTrendState state)
        {
            var points = (source ?? new List<YouPinDailyPnl>())
                .Where(IsUsableOfficialProfitPoint)
                .Select(x => new YouPinDailyPnl
                {
                    Date = x.Date,
                    EndValue = x.EndValue,
                    ProfitAndLoss = x.ProfitAndLoss,
                    ProfitAndLossPercent = x.ProfitAndLossPercent,
                    HasProfitAndLoss = true,
                    Pnl = x.ProfitAndLoss,
                    Count = x.Count,
                    LastUpdate = x.LastUpdate
                })
                .OrderBy(x => x.Date)
                .ToList();

            if (state.TotalValue <= 0 || !state.HasOfficialProfitAndLoss)
                return points;

            var fetchTime = state.LastFetch == DateTime.MinValue ? DateTime.Now : state.LastFetch;
            string today = fetchTime.ToString("yyyy-MM-dd");
            var todayPoint = points.LastOrDefault(x => string.Equals(x.Date, today, StringComparison.Ordinal));
            if (todayPoint == null)
            {
                todayPoint = new YouPinDailyPnl { Date = today };
                points.Add(todayPoint);
            }

            todayPoint.EndValue = state.TotalValue;
            todayPoint.ProfitAndLoss = state.TotalDelta;
            todayPoint.ProfitAndLossPercent = state.TotalDeltaPercent;
            todayPoint.HasProfitAndLoss = true;
            todayPoint.Pnl = state.TotalDelta;
            todayPoint.Count = state.TotalCount;
            todayPoint.LastUpdate = fetchTime;

            if (state.TotalValue > 1000)
            {
                double minimumReasonableValue = state.TotalValue * 0.1;
                var cleaned = points.Where(x => x.EndValue >= minimumReasonableValue).ToList();
                if (cleaned.Count > 0)
                    points = cleaned;
            }

            return points.OrderBy(x => x.Date).ToList();
        }

        internal static bool IsUsableOfficialProfitPoint(YouPinDailyPnl point)
        {
            if (point == null || !point.HasProfitAndLoss)
                return false;

            if (double.IsNaN(point.ProfitAndLoss) || double.IsInfinity(point.ProfitAndLoss))
                return false;

            if (point.EndValue > 100
                && Math.Abs(point.ProfitAndLoss - point.Pnl) < 0.01
                && Math.Abs(point.ProfitAndLoss) > Math.Max(1000, point.EndValue * 0.5))
            {
                return false;
            }

            return true;
        }
    }
}
