namespace CS2TradeMonitor.Application.Steam
{
    /// <summary>
    /// Pure recovery decision module shared by every host. It preserves the desktop
    /// record ordering, identity matching, recovery window, and enablement rules.
    /// </summary>
    public static class SteamAutoTradeRecoveryStateMachine
    {
        public static SteamAutoTradeRecoveryDecision Evaluate(
            IEnumerable<SteamAutoTradeRecord> records,
            SteamAutoTradeSettings settings,
            string? orderNo,
            DateTime now)
        {
            ArgumentNullException.ThrowIfNull(records);
            ArgumentNullException.ThrowIfNull(settings);

            List<SteamAutoTradeRecord> snapshot = records.ToList();
            string normalizedOrderNo = CS2TradeMonitor.Shared.Trading.TradeAutomationPolicy.NormalizeIdentity(orderNo);
            bool hasOrderNo = !string.IsNullOrWhiteSpace(normalizedOrderNo);

            IReadOnlyList<SteamAutoTradePlanItem> confirmationPlans = settings.Enabled
                ? snapshot
                    .Where(record => IsWithinRecoveryWindow(record, now)
                        && IsRecoverableConfirmationRecord(record)
                        && !string.IsNullOrWhiteSpace(record.TradeOfferId)
                        && IsRecoveryEnabledForRecord(record, settings))
                    .Select(BuildConfirmationPlan)
                    .ToList()
                : Array.Empty<SteamAutoTradePlanItem>();

            SteamAutoTradeRecord? persistedOffer = hasOrderNo
                ? snapshot.FirstOrDefault(record => IsWithinRecoveryWindow(record, now)
                    && IsTrackedTradeOfferRecordType(record.Type)
                    && IsSameOrder(record.OrderNo, normalizedOrderNo)
                    && !string.IsNullOrWhiteSpace(record.TradeOfferId))
                : null;

            bool hasRecoverablePersistedTradeOffer = hasOrderNo
                && snapshot.Any(record => IsWithinRecoveryWindow(record, now)
                    && IsRecoverableConfirmationRecord(record)
                    && IsSameOrder(record.OrderNo, normalizedOrderNo)
                    && !string.IsNullOrWhiteSpace(record.TradeOfferId));

            bool hasRecentSendAwaitingTradeOfferId = hasOrderNo
                && snapshot.Any(record => IsWithinRecoveryWindow(record, now)
                    && IsTrackedTradeOfferRecordType(record.Type)
                    && IsSameOrder(record.OrderNo, normalizedOrderNo)
                    && string.IsNullOrWhiteSpace(record.TradeOfferId));

            return new SteamAutoTradeRecoveryDecision(
                confirmationPlans,
                persistedOffer?.TradeOfferId ?? string.Empty,
                hasRecoverablePersistedTradeOffer,
                hasRecentSendAwaitingTradeOfferId);
        }

        private static bool IsWithinRecoveryWindow(SteamAutoTradeRecord record, DateTime now)
        {
            return CS2TradeMonitor.Shared.Trading.TradeAutomationPolicy.IsWithinRecoveryWindow(
                record.CreatedTime,
                now);
        }

        private static bool IsSameOrder(string? candidateOrderNo, string normalizedOrderNo)
        {
            return string.Equals(
                CS2TradeMonitor.Shared.Trading.TradeAutomationPolicy.NormalizeIdentity(candidateOrderNo),
                normalizedOrderNo,
                StringComparison.Ordinal);
        }

        private static bool IsRecoveryEnabledForRecord(
            SteamAutoTradeRecord record,
            SteamAutoTradeSettings settings)
        {
            return record.Source.Contains("出租", StringComparison.Ordinal)
                ? settings.SendYouPinRentalEnabled
                : settings.SendYouPinSaleEnabled;
        }

        private static bool IsRecoverableConfirmationRecord(SteamAutoTradeRecord record)
        {
            return record.Type is SteamAutoTradeRecordType.AutoSend
                or SteamAutoTradeRecordType.AutoYouPinConfirm
                or SteamAutoTradeRecordType.ManualSend
                || record.Type == SteamAutoTradeRecordType.Pending
                && record.PendingStage == SteamAutoTradePendingStage.MobileConfirmation;
        }

        private static bool IsTrackedTradeOfferRecordType(SteamAutoTradeRecordType type)
        {
            return type is SteamAutoTradeRecordType.AutoSend
                or SteamAutoTradeRecordType.AutoYouPinConfirm
                or SteamAutoTradeRecordType.ManualSend
                or SteamAutoTradeRecordType.Pending;
        }

        private static SteamAutoTradePlanItem BuildConfirmationPlan(SteamAutoTradeRecord record)
        {
            return new SteamAutoTradePlanItem
            {
                TradeOfferId = record.TradeOfferId,
                Direction = record.Direction,
                Category = record.Source.Contains("出租", StringComparison.Ordinal)
                    ? SteamAutoTradeCategory.YouPinRental
                    : SteamAutoTradeCategory.YouPinSale,
                ItemNames = record.ItemNames.ToList(),
                MatchedOrderNo = record.OrderNo,
                Action = SteamAutoTradeAction.ConfirmMobile,
                Allowed = true
            };
        }
    }

    public sealed record SteamAutoTradeRecoveryDecision(
        IReadOnlyList<SteamAutoTradePlanItem> ConfirmationPlans,
        string PersistedTradeOfferId,
        bool HasRecoverablePersistedTradeOffer,
        bool HasRecentSendAwaitingTradeOfferId);
}
