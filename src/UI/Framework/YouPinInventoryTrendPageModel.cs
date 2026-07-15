using CS2TradeMonitor.Application.YouPin;
using System;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal enum YouPinInventoryTrendAuthStatus
    {
        Missing,
        SignedIn,
        Error
    }

    internal sealed record YouPinInventoryTrendHeaderModel(
        string LastFetchText,
        string AuthText,
        YouPinInventoryTrendAuthStatus AuthStatus);

    internal sealed record YouPinInventoryTrendSummaryModel(
        string MarketValueText,
        string MarketSubText,
        string DeltaValueText,
        string DeltaSubText,
        bool HasDeltaComparison,
        string PurchaseValueText,
        string PurchaseSubText);

    internal static class YouPinInventoryTrendPageModel
    {
        public static YouPinInventoryTrendHeaderModel BuildHeader(YouPinInventoryTrendState state, YouPinAuthState authState)
        {
            return new YouPinInventoryTrendHeaderModel(
                FormatLastFetch(state.LastFetch, DateTime.Today),
                BuildAuthText(authState),
                GetAuthStatus(authState));
        }

        public static YouPinInventoryTrendSummaryModel BuildSummary(YouPinInventoryTrendState state)
        {
            bool hasCount = state.TotalCount > 0;
            bool hasDelta = hasCount && Math.Abs(state.TotalDelta) > 0.001;
            string purchaseSub = !hasCount
                ? "完成读取后显示"
                : state.MissingPurchaseCount > 0 ? $"{state.MissingPurchaseCount} 件无购入价" : "已记录购入价";

            return new YouPinInventoryTrendSummaryModel(
                state.TotalValue > 0 ? $"¥{state.TotalValue:F2}" : "暂无数据",
                hasCount ? $"{state.TotalCount} 件" : "完成读取后显示",
                hasCount ? FormatSignedMoney(state.TotalDelta) : "暂无数据",
                hasCount ? YouPinInventoryTrendGridModel.FormatSignedPercent(state.TotalDeltaPercent) : "需要两次快照",
                hasDelta,
                state.PurchaseValue > 0 ? $"¥{state.PurchaseValue:F2}" : "暂无购入价",
                purchaseSub);
        }

        public static string FormatLastFetch(DateTime lastFetch, DateTime today)
        {
            if (lastFetch == default)
                return "上次刷新时间：暂无";

            return lastFetch.Date == today.Date
                ? $"上次刷新时间：{lastFetch:HH:mm:ss}"
                : $"上次刷新时间：{lastFetch:MM-dd HH:mm}";
        }

        public static string BuildAuthText(YouPinAuthState authState)
        {
            return authState.HasCredential
                ? string.IsNullOrWhiteSpace(authState.Error) ? "登录状态：已登录" : "登录状态：异常"
                : "登录状态：未登录";
        }

        public static YouPinInventoryTrendAuthStatus GetAuthStatus(YouPinAuthState authState)
        {
            if (!authState.HasCredential)
                return YouPinInventoryTrendAuthStatus.Missing;

            return string.IsNullOrWhiteSpace(authState.Error)
                ? YouPinInventoryTrendAuthStatus.SignedIn
                : YouPinInventoryTrendAuthStatus.Error;
        }

        public static string FormatSignedMoney(double value)
        {
            string sign = value > 0 ? "+" : string.Empty;
            return $"{sign}¥{value:F2}";
        }
    }
}
