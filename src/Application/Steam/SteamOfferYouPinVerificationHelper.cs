using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.Steam;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CS2TradeMonitor.Application.Steam
{
    internal static class SteamOfferYouPinVerificationHelper
    {
        public static void EnrichWithYouPinVerification(List<SteamOfferItem> offers, YouPinSaleReminderState state)
        {
            var orders = state.RecentOrders
                .Concat(state.RecentMsgCenterOrders)
                .Concat(state.RecentWaitDeliverOrders)
                .Where(x => !string.IsNullOrWhiteSpace(x.TradeOfferId))
                .GroupBy(x => x.TradeOfferId.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var offer in offers)
            {
                string tradeOfferId = (offer.TradeOfferId ?? "").Trim();
                if (!orders.TryGetValue(tradeOfferId, out var order))
                    continue;

                offer.VerifiedByYouPin = true;
                offer.YouPinOrderNo = order.OrderNo;
                offer.PlatformOrderNo = order.OrderNo;
                offer.YouPinItemName = order.Name;
                offer.YouPinPrice = order.Price;
                offer.Amount = order.Price;
                bool rental = order.OrderType == 2
                    || ContainsRentalText(order.Message)
                    || ContainsRentalText(order.OrderStatusDesc);
                offer.Source = rental ? "悠悠出租" : "悠悠有品待办";
                offer.RiskLevel = SteamOfferRisk.YouPinVerified;
                offer.CanAcceptSafely = false;
                if (!string.IsNullOrWhiteSpace(order.Name))
                    offer.ItemSummary = order.Price > 0 ? $"{order.Name} ¥{order.Price:F2}" : order.Name;
                offer.Title = rental ? "悠悠出租已校验报价" : "悠悠有品已校验报价";
            }
        }

        private static bool ContainsRentalText(string? text)
        {
            return text?.Contains("出租", StringComparison.OrdinalIgnoreCase) == true
                || text?.Contains("租赁", StringComparison.OrdinalIgnoreCase) == true
                || text?.Contains("转交", StringComparison.OrdinalIgnoreCase) == true;
        }

        public static bool IsEligibleForSafeBatch(SteamOfferItem offer, bool allowYouPinVerified)
        {
            if (offer.Status != SteamOfferStatus.Pending || !offer.CanAcceptSafely)
                return false;
            return SteamOfferSafetyEvaluator.IsPureIncomingGift(offer)
                && offer.RiskLevel == SteamOfferRisk.SafeIncoming;
        }
    }
}
