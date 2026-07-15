namespace CS2TradeMonitor.Application.YouPin
{
    internal static class YouPinLandlordUserNotice
    {
        internal const string TradingNoticeMarker = "悠悠《交易撤回须知》";
        internal const string TradingNoticeCompactStatus =
            "● 交易须知确认失败 · 请在悠悠有品同意后重试";

        internal static string CreateTradingNoticeFailure(string detail)
        {
            string normalizedDetail = string.IsNullOrWhiteSpace(detail)
                ? "平台未返回明确原因"
                : detail.Trim().TrimEnd('。', '；', ';');
            return $"检测到{TradingNoticeMarker}，交易须知确认失败（自动确认未完成）：{normalizedDetail}。"
                + "请打开悠悠有品完成同意后，再点“立即执行一次”。";
        }

        internal static bool IsTradingNoticeFailure(string text)
        {
            return !string.IsNullOrWhiteSpace(text)
                && text.Contains(TradingNoticeMarker, StringComparison.Ordinal);
        }
    }
}
