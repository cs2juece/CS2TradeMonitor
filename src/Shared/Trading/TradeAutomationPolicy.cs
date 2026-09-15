using System.Collections.ObjectModel;

namespace CS2TradeMonitor.Shared.Trading
{
    public enum TradeAutomationOfferCategory
    {
        Unknown = 0,
        PureIncoming = 1,
        YouPinPurchase = 2,
        YouPinSale = 3,
        YouPinRental = 4
    }

    public enum TradeAutomationState
    {
        Unknown = 0,
        Pending = 1,
        Success = 2,
        Failed = 3,
        NeedsUserAction = 4
    }

    /// <summary>
    /// Platform-neutral safety and recovery rules shared by every host.
    /// Values in this module are the authoritative desktop trading contract.
    /// </summary>
    public static class TradeAutomationPolicy
    {
        private static readonly ReadOnlyCollection<TimeSpan> RetryDelays = Array.AsReadOnly(new[]
        {
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(3)
        });

        public static TimeSpan RecoverableWindow { get; } = TimeSpan.FromHours(24);
        public static IReadOnlyList<TimeSpan> RecoveryRetryDelays => RetryDelays;
        public static TimeSpan MinimumWriteInterval { get; } = TimeSpan.FromMilliseconds(1800);
        public static TimeSpan TransientRetryDelay { get; } = TimeSpan.FromSeconds(5);
        public const int MaximumWriteAttempts = 3;

        public static bool CanAutoExecuteOffer(
            TradeAutomationOfferCategory category,
            bool youPinPurchaseEnabled,
            bool youPinRentalEnabled)
        {
            return category switch
            {
                TradeAutomationOfferCategory.YouPinPurchase => youPinPurchaseEnabled,
                TradeAutomationOfferCategory.YouPinRental => youPinRentalEnabled,
                _ => false
            };
        }

        public static bool IsExactTradeOfferMatch(
            string? expectedTradeOfferId,
            string? candidateTradeOfferId,
            bool candidateIsTradeConfirmation)
        {
            if (!candidateIsTradeConfirmation)
                return false;

            string expected = NormalizeIdentity(expectedTradeOfferId);
            string candidate = NormalizeIdentity(candidateTradeOfferId);
            return expected.Length > 0
                && candidate.Length > 0
                && string.Equals(expected, candidate, StringComparison.Ordinal);
        }

        public static bool IsExactOrderMatch(
            string? expectedOrderNo,
            string? candidateOrderNo,
            IEnumerable<string>? candidateOrderAliases = null)
        {
            string expected = NormalizeIdentity(expectedOrderNo);
            if (expected.Length == 0)
                return false;

            if (string.Equals(expected, NormalizeIdentity(candidateOrderNo), StringComparison.Ordinal))
                return true;

            return candidateOrderAliases?.Any(alias =>
                string.Equals(expected, NormalizeIdentity(alias), StringComparison.Ordinal)) == true;
        }

        public static bool CanConfirmPlatformWithoutTradeOfferId(bool isRental)
            => isRental;

        public static bool IsWithinRecoveryWindow(
            DateTimeOffset createdAt,
            DateTimeOffset now)
        {
            if (createdAt == default)
                return false;

            return createdAt >= now - RecoverableWindow;
        }

        public static bool IsWithinRecoveryWindow(DateTime createdAt, DateTime now)
        {
            if (createdAt == default)
                return false;

            return createdAt >= now - RecoverableWindow;
        }

        public static bool ShouldPreserveStateOnStartup(TradeAutomationState state)
        {
            return state is TradeAutomationState.Failed or TradeAutomationState.NeedsUserAction;
        }

        public static string NormalizeIdentity(string? value)
            => (value ?? string.Empty).Trim().ToUpperInvariant();

        public static IReadOnlyList<string> BuildTransactionKeys(
            string? orderNo,
            string? tradeOfferId)
        {
            var keys = new List<string>(capacity: 2);
            string normalizedOrderNo = NormalizeIdentity(orderNo);
            if (normalizedOrderNo.Length > 0)
                keys.Add("order:" + normalizedOrderNo);

            string normalizedTradeOfferId = NormalizeIdentity(tradeOfferId);
            if (normalizedTradeOfferId.Length > 0)
                keys.Add("offer:" + normalizedTradeOfferId);

            return keys;
        }
    }
}
