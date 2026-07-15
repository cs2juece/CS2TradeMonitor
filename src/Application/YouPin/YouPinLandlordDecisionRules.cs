using CS2TradeMonitor.Domain.YouPin;

namespace CS2TradeMonitor.Application.YouPin
{
    internal sealed record YouPinLandlordDecisionContext(
        YouPinLandlordWorkflow Workflow,
        YouPinLandlordRemoteListing Listing,
        IReadOnlyList<YouPinLandlordMarketListing> Market,
        int? CurrentRank,
        YouPinLandlordRentalPolicy Policy);

    internal sealed record YouPinLandlordDecision(
        YouPinLandlordDecisionCode Code,
        string Message,
        string RuleId = "custom");

    internal interface IYouPinLandlordDecisionRule
    {
        YouPinLandlordDecision? Evaluate(YouPinLandlordDecisionContext context);
    }

    internal sealed class YouPinLandlordRankDecisionRule : IYouPinLandlordDecisionRule
    {
        public YouPinLandlordDecision? Evaluate(YouPinLandlordDecisionContext context)
        {
            if (!context.CurrentRank.HasValue)
            {
                return new YouPinLandlordDecision(
                    YouPinLandlordDecisionCode.RankUnknown,
                    "未在同款市场明细中找到自有货架记录",
                    "rank");
            }

            if (context.CurrentRank.Value <= context.Policy.TargetRank)
            {
                return new YouPinLandlordDecision(
                    YouPinLandlordDecisionCode.WithinTargetRank,
                    "当前已在目标出租位",
                    "rank");
            }

            return new YouPinLandlordDecision(
                YouPinLandlordDecisionCode.OutsideTargetRank,
                $"当前第{context.CurrentRank}位，超出目标前{context.Policy.TargetRank}位",
                "rank");
        }
    }
}
