using CS2TradeMonitor.Application.Steam.Auth;
using CS2TradeMonitor.Domain.Steam;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CS2TradeMonitor.Application.Steam
{
    internal static class SteamOfferStateHelper
    {
        public static SteamOfferState BuildState(
            SteamAuthStoreStatus authStatus,
            IEnumerable<SteamOfferItem> offers,
            DateTime lastRefresh,
            string lastStatus,
            string lastError,
            string highlightTradeOfferId,
            SteamAutoConfirmState autoConfirm,
            SteamAutoTradeState? autoTrade = null)
        {
            return new SteamOfferState
            {
                AuthStatus = authStatus,
                Offers = offers.Select(SteamOfferMappingHelper.CloneOffer).ToList(),
                LastRefresh = lastRefresh,
                LastStatus = lastStatus,
                LastError = lastError,
                HighlightTradeOfferId = highlightTradeOfferId,
                AutoConfirm = autoConfirm,
                AutoTrade = autoTrade ?? new SteamAutoTradeState()
            };
        }

        public static SteamAutoConfirmState BuildAutoConfirmState(AutoConfirmationService autoConfirmationService)
        {
            return new SteamAutoConfirmState
            {
                IsRunning = autoConfirmationService.IsRunning,
                LastCheckTime = autoConfirmationService.LastCheckTime,
                TotalAccepted = autoConfirmationService.TotalAccepted,
                LastStatus = autoConfirmationService.LastStatus,
                IntervalSeconds = autoConfirmationService.IntervalSeconds,
                AutoAcceptSafe = autoConfirmationService.AutoAcceptSafe,
                AllowYouPinVerifiedAccept = autoConfirmationService.AllowYouPinVerifiedAccept
            };
        }

        public static SteamAutoTradeState BuildAutoTradeState(AutoConfirmationService autoConfirmationService)
        {
            return autoConfirmationService.GetAutoTradeState();
        }

        public static SteamOfferItem? BuildManualOfferIfMissing(
            IEnumerable<SteamOfferItem> offers,
            string tradeOfferId,
            string source,
            string summary,
            DateTime now)
        {
            string id = (tradeOfferId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(id))
                return null;

            if (offers.Any(x => string.Equals(x.TradeOfferId, id, StringComparison.OrdinalIgnoreCase)))
                return null;

            return new SteamOfferItem
            {
                TradeOfferId = id,
                Title = "待核对 Steam 报价",
                ItemSummary = summary,
                Type = SteamOfferType.Unknown,
                Source = source,
                Status = SteamOfferStatus.Pending,
                RiskLevel = SteamOfferRisk.Unverified,
                CanAcceptSafely = false,
                CreatedAt = now
            };
        }

        public static bool MarkOfferStatus(IEnumerable<SteamOfferItem> offers, string tradeOfferId, SteamOfferStatus status)
        {
            var offer = offers.FirstOrDefault(x => x.TradeOfferId == tradeOfferId);
            if (offer == null)
                return false;

            offer.Status = status;
            return true;
        }

        public static bool MarkOfferStatuses(IEnumerable<SteamOfferItem> offers, IEnumerable<string> tradeOfferIds, SteamOfferStatus status)
        {
            var set = new HashSet<string>(tradeOfferIds.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.Ordinal);
            if (set.Count == 0)
                return false;

            foreach (var offer in offers)
            {
                if (set.Contains(offer.TradeOfferId))
                    offer.Status = status;
            }

            return true;
        }
    }
}
