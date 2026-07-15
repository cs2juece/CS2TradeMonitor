using CS2TradeMonitor.Application.YouPin;
using System;
using System.Linq;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class YouPinStopProfitLossStatusPresenter
    {
        public static YouPinStopProfitLossStatusViewModel Build(
            YouPinStopProfitLossState state,
            YouPinInventoryTrendState trendState,
            bool enabled,
            bool onlySpecified,
            string specifiedItems)
        {
            ArgumentNullException.ThrowIfNull(state);
            ArgumentNullException.ThrowIfNull(trendState);

            int candidateCount = trendState.Rows.Count(row => row.HasEstimate && !string.IsNullOrWhiteSpace(row.Name));
            int keywordCount = YouPinStopProfitLossPageModel.SplitSpecifiedItems(specifiedItems).Count();

            string statusText;
            YouPinStopProfitLossStatusTone statusTone;
            if (!enabled)
            {
                statusText = "已关闭：不会扫描或报警";
                statusTone = YouPinStopProfitLossStatusTone.Off;
            }
            else if (onlySpecified && keywordCount == 0)
            {
                statusText = "等待指定单品";
                statusTone = YouPinStopProfitLossStatusTone.Warn;
            }
            else if (candidateCount == 0)
            {
                statusText = "等待库存涨跌数据";
                statusTone = YouPinStopProfitLossStatusTone.Warn;
            }
            else if (!string.IsNullOrWhiteSpace(state.LastError))
            {
                statusText = "异常：" + state.LastError;
                statusTone = YouPinStopProfitLossStatusTone.Critical;
            }
            else
            {
                statusText = $"运行中：扫描 {candidateCount} 个单品";
                statusTone = YouPinStopProfitLossStatusTone.Ok;
            }

            string lastCheckText = state.LastFetch == DateTime.MinValue
                ? "暂无"
                : state.LastFetch.ToString("yyyy-MM-dd HH:mm:ss");
            YouPinStopProfitLossStatusTone lastCheckTone = state.LastFetch == DateTime.MinValue
                ? YouPinStopProfitLossStatusTone.Subtle
                : YouPinStopProfitLossStatusTone.Info;

            int alertCount = state.RecentAlerts.Count;
            string alertText = alertCount == 0 ? "暂无报警" : $"{alertCount} 条";
            YouPinStopProfitLossStatusTone alertTone = alertCount == 0
                ? YouPinStopProfitLossStatusTone.Ok
                : YouPinStopProfitLossStatusTone.Warn;

            return new YouPinStopProfitLossStatusViewModel(
                statusText,
                statusTone,
                lastCheckText,
                lastCheckTone,
                alertText,
                alertTone,
                BuildSpecifiedSearchStatus(specifiedItems),
                CandidateStatusWarn: false);
        }

        public static string BuildSpecifiedSearchStatus(string specifiedItems)
        {
            return "输入后选择候选再添加。" +
                YouPinStopProfitLossPageModel.BuildSpecifiedSummary(specifiedItems);
        }
    }

    internal enum YouPinStopProfitLossStatusTone
    {
        Ok,
        Info,
        Warn,
        Off,
        Critical,
        Subtle
    }

    internal sealed record YouPinStopProfitLossStatusViewModel(
        string StatusText,
        YouPinStopProfitLossStatusTone StatusTone,
        string LastCheckText,
        YouPinStopProfitLossStatusTone LastCheckTone,
        string AlertCountText,
        YouPinStopProfitLossStatusTone AlertCountTone,
        string CandidateStatusText,
        bool CandidateStatusWarn);
}
