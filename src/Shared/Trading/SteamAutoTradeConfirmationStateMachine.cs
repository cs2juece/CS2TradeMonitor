using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.Shared.Trading;

namespace CS2TradeMonitor.Application.Steam
{
    /// <summary>
    /// Pure interpretation rules for authoritative YouPin confirmation readback.
    /// Callers retain the desktop ordering of success, failure, and timeout handling.
    /// </summary>
    public static class SteamAutoTradeConfirmationStateMachine
    {
        public static SteamAutoTradeConfirmationReadback EvaluateYouPinReadback(string? message)
        {
            string text = message ?? string.Empty;
            bool indicatesSuccess = text.Contains("待对方确认", StringComparison.Ordinal)
                || text.Contains("等待对方确认", StringComparison.Ordinal)
                || text.Contains("确认报价成功", StringComparison.Ordinal)
                || text.Contains("报价确认成功", StringComparison.Ordinal)
                || text.Contains("已完成", StringComparison.Ordinal);
            bool indicatesRentalCompletion = indicatesSuccess
                || text.Contains("已发货", StringComparison.Ordinal)
                || text.Contains("转交成功", StringComparison.Ordinal)
                || text.Contains("出租成功", StringComparison.Ordinal);
            bool indicatesFailure = text.Contains("报价发送失败", StringComparison.Ordinal)
                || text.Contains("确认报价失败", StringComparison.Ordinal)
                || text.Contains("已取消", StringComparison.Ordinal)
                || text.Contains("已拒绝", StringComparison.Ordinal);
            bool indicatesWaitingForOurConfirmation = text.Contains("待您确认", StringComparison.Ordinal)
                || text.Contains("等待您回应", StringComparison.Ordinal)
                || text.Contains("等待Steam令牌确认", StringComparison.Ordinal)
                || text.Contains("待您令牌验证", StringComparison.Ordinal);

            return new SteamAutoTradeConfirmationReadback(
                indicatesSuccess,
                indicatesRentalCompletion,
                indicatesFailure,
                indicatesWaitingForOurConfirmation);
        }

        public static bool HasTradeOfferIdConflict(
            string? expectedTradeOfferId,
            string? candidateTradeOfferId)
        {
            string candidate = TradeAutomationPolicy.NormalizeIdentity(candidateTradeOfferId);
            return candidate.Length > 0
                && !string.Equals(
                    TradeAutomationPolicy.NormalizeIdentity(expectedTradeOfferId),
                    candidate,
                    StringComparison.Ordinal);
        }

        public static bool MatchesOrderNo(YouPinSaleOrder order, string? orderNo)
        {
            ArgumentNullException.ThrowIfNull(order);
            return TradeAutomationPolicy.IsExactOrderMatch(orderNo, order.OrderNo, order.OrderNos);
        }
    }

    public sealed record SteamAutoTradeConfirmationReadback(
        bool IndicatesSuccess,
        bool IndicatesRentalCompletion,
        bool IndicatesFailure,
        bool IndicatesWaitingForOurConfirmation);
}
