using CS2TradeMonitor.Application.Steam.Auth;
using CS2TradeMonitor.Domain.Steam;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CS2TradeMonitor.Application.Steam
{
    internal sealed class SteamOfferStateStore
    {
        private readonly object _lock = new();
        private readonly Action<SteamOfferItem> _prepareManualOffer;
        private List<SteamOfferItem> _offers = new();
        private DateTime _lastRefresh = DateTime.MinValue;
        private string _lastStatus = "未刷新";
        private string _lastError = "";
        private string _highlightTradeOfferId = "";

        public SteamOfferStateStore(Action<SteamOfferItem> prepareManualOffer)
        {
            _prepareManualOffer = prepareManualOffer ?? throw new ArgumentNullException(nameof(prepareManualOffer));
        }

        public SteamOfferState BuildState(
            SteamAuthStoreStatus authStatus,
            SteamAutoConfirmState autoConfirmState,
            SteamAutoTradeState? autoTradeState = null)
        {
            lock (_lock)
            {
                return SteamOfferStateHelper.BuildState(
                    authStatus,
                    _offers,
                    _lastRefresh,
                    _lastStatus,
                    _lastError,
                    _highlightTradeOfferId,
                    autoConfirmState,
                    autoTradeState);
            }
        }

        public void HighlightTradeOffer(string tradeOfferId, string source, string summary)
        {
            lock (_lock)
            {
                _highlightTradeOfferId = (tradeOfferId ?? "").Trim();
                EnsureManualOfferNoLock(_highlightTradeOfferId, source, summary);
            }
        }

        public void ClearAll(string status)
        {
            lock (_lock)
            {
                _offers.Clear();
                _lastRefresh = DateTime.MinValue;
                _lastStatus = status;
                _lastError = "";
                _highlightTradeOfferId = "";
            }
        }

        public void SetStatus(string status, string error)
        {
            lock (_lock)
            {
                _lastStatus = status;
                _lastError = error;
            }
        }

        public void SetOffers(List<SteamOfferItem> offers, string status, string error)
        {
            lock (_lock)
            {
                _offers = offers;
                EnsureManualOfferNoLock(
                    _highlightTradeOfferId,
                    "外部跳转",
                    "从外部页面跳转的 Steam 报价，请刷新或打开 Steam 页面核对。");
                _lastRefresh = DateTime.Now;
                _lastStatus = status;
                _lastError = error;
            }
        }

        public List<SteamOfferItem> GetEligibleOffers(Func<SteamOfferItem, bool> predicate)
        {
            lock (_lock)
            {
                return _offers
                    .Where(predicate)
                    .Select(SteamOfferMappingHelper.CloneOffer)
                    .ToList();
            }
        }

        public SteamOfferItem? FindOffer(string tradeOfferId)
        {
            lock (_lock)
            {
                SteamOfferItem? offer = _offers.FirstOrDefault(x => x.TradeOfferId == tradeOfferId);
                return offer == null ? null : SteamOfferMappingHelper.CloneOffer(offer);
            }
        }

        public void MarkOfferStatus(string tradeOfferId, SteamOfferStatus status)
        {
            lock (_lock)
            {
                SteamOfferStateHelper.MarkOfferStatus(_offers, tradeOfferId, status);
            }
        }

        public bool MarkOfferStatuses(IEnumerable<string> tradeOfferIds, SteamOfferStatus status)
        {
            lock (_lock)
            {
                return SteamOfferStateHelper.MarkOfferStatuses(_offers, tradeOfferIds, status);
            }
        }

        private void EnsureManualOfferNoLock(string tradeOfferId, string source, string summary)
        {
            SteamOfferItem? manualOffer = SteamOfferStateHelper.BuildManualOfferIfMissing(
                _offers,
                tradeOfferId,
                source,
                summary,
                DateTime.Now);
            if (manualOffer == null)
                return;

            _prepareManualOffer(manualOffer);
            _offers.Insert(0, manualOffer);
        }
    }
}
