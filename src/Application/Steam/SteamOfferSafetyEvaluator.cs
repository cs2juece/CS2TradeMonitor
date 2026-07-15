using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.Domain.Steam;
using System;

namespace CS2TradeMonitor.Application.Steam
{
    public static class SteamOfferSafetyEvaluator
    {
        public static bool IsPureIncomingGift(SteamOfferItem offer)
        {
            return offer != null
                && offer.Type == SteamOfferType.IncomingGift
                && offer.ItemsToGive.Count == 0
                && offer.ItemsToReceive.Count > 0;
        }

        public static void Evaluate(SteamOfferItem offer)
        {
            if (offer == null) return;

            // Reset safety status first
            offer.CanAcceptSafely = false;
            offer.SafeReason = "";
            offer.FailureReason = "";

            if (IsPureIncomingGift(offer))
            {
                offer.CanAcceptSafely = true;
                offer.SafeReason = "纯收货报价 (我方不转出库存)";
                offer.RiskLevel = SteamOfferRisk.SafeIncoming;
                return;
            }

            if (offer.ItemsToGive.Count > 0)
            {
                offer.FailureReason = "涉及转出库存，请打开 Steam 手动处理";
                offer.RiskLevel = offer.VerifiedByYouPin ? SteamOfferRisk.YouPinVerified : SteamOfferRisk.Unverified;
                return;
            }

            switch (offer.Type)
            {
                case SteamOfferType.IncomingGift:
                    offer.FailureReason = "纯收货报价缺少收货饰品明细，请刷新后再试";
                    offer.RiskLevel = SteamOfferRisk.Unverified;
                    break;

                case SteamOfferType.Outgoing:
                    offer.FailureReason = "发出报价不属于纯收货，需人工处理";
                    offer.RiskLevel = offer.VerifiedByYouPin ? SteamOfferRisk.YouPinVerified : SteamOfferRisk.Unverified;
                    break;

                case SteamOfferType.TwoWay:
                    offer.FailureReason = "双方交易报价 (TwoWay) 涉及转出库存，需人工处理";
                    offer.RiskLevel = SteamOfferRisk.Unverified;
                    break;

                case SteamOfferType.Unknown:
                default:
                    // Unknown or unsupported types are not batch-safe.
                    offer.FailureReason = $"未知或不支持的报价类型: {offer.Type}";
                    offer.RiskLevel = SteamOfferRisk.Unverified;
                    break;
            }
        }
    }
}
