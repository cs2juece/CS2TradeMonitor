using System;
using System.Collections.Generic;

namespace CS2TradeMonitor.Domain.YouPin
{
    public enum YouPinLandlordWorkflow
    {
        RentalReprice,
        InventoryAutoRent
    }

    public enum YouPinLandlordRunMode
    {
        Execute,
        ScanOnly
    }

    public enum YouPinLandlordExecutionLane
    {
        ZeroCdReprice,
        InventoryRentalReprice,
        InventoryAutoRent
    }

    public enum YouPinRentalShelfType
    {
        ZeroCd,
        InventoryRental
    }

    public enum YouPinLandlordRepriceConfigurationMode
    {
        Unified,
        Separate
    }

    public enum YouPinLandlordInventoryListMode
    {
        Whitelist,
        Blacklist
    }

    public enum YouPinLandlordSelectionScope
    {
        PerAsset,
        SameItemName
    }

    public sealed record YouPinLandlordSelectionRule(
        bool Initialized,
        YouPinLandlordSelectionScope Scope,
        IReadOnlyList<string> SelectedAssetIds,
        IReadOnlyList<string> SelectedItemNames)
    {
        public static YouPinLandlordSelectionRule AllowAll { get; } = new(
            false,
            YouPinLandlordSelectionScope.PerAsset,
            Array.Empty<string>(),
            Array.Empty<string>());

        public bool IsSelected(string assetId, string itemName)
        {
            return Matches(
                Initialized,
                Scope,
                SelectedAssetIds,
                SelectedItemNames,
                assetId,
                itemName);
        }

        public static bool Matches(
            bool initialized,
            YouPinLandlordSelectionScope scope,
            IReadOnlyList<string>? selectedAssetIds,
            IReadOnlyList<string>? selectedItemNames,
            string assetId,
            string itemName)
        {
            if (!initialized)
                return true;

            return scope == YouPinLandlordSelectionScope.SameItemName
                ? (selectedItemNames ?? Array.Empty<string>()).Contains(itemName, StringComparer.Ordinal)
                : (selectedAssetIds ?? Array.Empty<string>()).Contains(assetId, StringComparer.Ordinal);
        }
    }

    public enum YouPinLandlordInventoryEligibilityCode
    {
        Eligible,
        SteamNotTradable,
        SteamNotMarketable,
        LeaseBanned,
        InventoryUnavailable,
        TradeProtected,
        StoreOffline,
        PlatformProhibited,
        ListingConfigurationUnavailable,
        PlatformExcluded,
        SteamTradeUnavailable
    }

    [Flags]
    public enum YouPinRentalScanScope
    {
        None = 0,
        ZeroCd = 1,
        InventoryRental = 2,
        All = ZeroCd | InventoryRental
    }

    public enum YouPinLandlordDecisionCode
    {
        RankUnknown,
        WithinTargetRank,
        OutsideTargetRank
    }

    public enum YouPinLandlordOperationStage
    {
        RunStarted,
        RunSkipped,
        Decision,
        PricingObtained,
        WriteStarted,
        WriteCompleted,
        RecheckStarted,
        Recheck,
        RunCompleted,
        RunFailed
    }

    public enum YouPinLandlordActionKind
    {
        ObserveOnly,
        Reprice,
        AlignOwnPrice,
        ListInventory
    }

    public enum YouPinLandlordActionState
    {
        Evaluating,
        PricingReady,
        Planned,
        Executing,
        AwaitingSynchronization,
        Rechecking,
        Observed,
        Succeeded,
        Failed,
        Skipped
    }

    public enum YouPinLandlordCallbackState
    {
        NotRequired,
        Pending,
        Confirmed,
        Rejected
    }

    public sealed record YouPinLandlordRentalPolicy(
        int TargetRank,
        int ScanIntervalMinutes,
        bool Enabled = false)
    {
        public int ExecutionIntervalMinutes { get; init; } = 30;

        public YouPinLandlordWeeklyFreeRule WeeklyFree { get; init; } = YouPinLandlordWeeklyFreeRule.Disabled;

        public YouPinLandlordCooldownWindow Cooldown { get; init; } = YouPinLandlordCooldownWindow.Disabled;

        public YouPinLandlordSelectionRule Selection { get; init; }
            = YouPinLandlordSelectionRule.AllowAll;

        public bool Allows(string assetId, string itemName)
        {
            return Selection.IsSelected(assetId, itemName);
        }

        public static YouPinLandlordRentalPolicy ZeroCdDefault { get; } = new(3, 30);

        public static YouPinLandlordRentalPolicy InventoryRentalDefault { get; } = new(5, 30);
    }

    public sealed record YouPinLandlordWeeklyFreeRule(
        bool Enabled,
        decimal MinimumItemValue,
        decimal MaximumItemValue)
    {
        public const decimal ExclusiveRentLimit = 0.72m;
        public const decimal MaximumAllowedRent = 0.71m;

        public static YouPinLandlordWeeklyFreeRule Disabled { get; } = new(false, 0m, 0m);

        public bool Matches(decimal itemValue)
        {
            return Enabled
                && itemValue >= MinimumItemValue
                && itemValue <= MaximumItemValue;
        }
    }

    public sealed record YouPinLandlordCooldownWindow(
        bool Enabled,
        int StartMinuteOfDay,
        int EndMinuteOfDay)
    {
        public static YouPinLandlordCooldownWindow Disabled { get; } = new(false, 0, 8 * 60);

        public bool Contains(DateTime localTime)
        {
            if (!Enabled)
                return false;

            int minute = (localTime.Hour * 60) + localTime.Minute;
            if (StartMinuteOfDay == EndMinuteOfDay)
                return true;
            return StartMinuteOfDay < EndMinuteOfDay
                ? minute >= StartMinuteOfDay && minute < EndMinuteOfDay
                : minute >= StartMinuteOfDay || minute < EndMinuteOfDay;
        }
    }

    public sealed record YouPinLandlordPolicy(
        int SchemaVersion,
        int PolicyVersion,
        YouPinLandlordRentalPolicy ZeroCd,
        YouPinLandlordRentalPolicy InventoryRental)
    {
        public YouPinLandlordRepriceConfigurationMode RepriceConfigurationMode { get; init; }
            = YouPinLandlordRepriceConfigurationMode.Separate;

        public YouPinLandlordRentalPolicy UnifiedRental { get; init; }
            = YouPinLandlordRentalPolicy.ZeroCdDefault;

        public YouPinLandlordInventoryPolicy InventoryAutoRent { get; init; }
            = YouPinLandlordInventoryPolicy.Default;

        public static YouPinLandlordPolicy Default { get; } = new(
            1,
            1,
            YouPinLandlordRentalPolicy.ZeroCdDefault,
            YouPinLandlordRentalPolicy.InventoryRentalDefault);

        public YouPinLandlordRentalPolicy For(YouPinRentalShelfType type)
        {
            return type == YouPinRentalShelfType.ZeroCd ? ZeroCd : InventoryRental;
        }

        public YouPinLandlordRentalPolicy EffectiveFor(YouPinRentalShelfType type)
        {
            return RepriceConfigurationMode == YouPinLandlordRepriceConfigurationMode.Unified
                ? UnifiedRental
                : For(type);
        }
    }

    public sealed record YouPinLandlordInventoryPolicy(
        bool Enabled,
        int ScanIntervalMinutes,
        YouPinLandlordInventoryListMode ListMode,
        IReadOnlyList<string> SelectedAssetIds)
    {
        public int ExecutionIntervalMinutes { get; init; } = 30;

        public YouPinLandlordWeeklyFreeRule WeeklyFree { get; init; } = YouPinLandlordWeeklyFreeRule.Disabled;

        public YouPinLandlordCooldownWindow Cooldown { get; init; } = YouPinLandlordCooldownWindow.Disabled;

        public YouPinLandlordSelectionScope SelectionScope { get; init; }
            = YouPinLandlordSelectionScope.PerAsset;

        public IReadOnlyList<string> SelectedItemNames { get; init; } = Array.Empty<string>();

        public static YouPinLandlordInventoryPolicy Default { get; } = new(
            false,
            30,
            YouPinLandlordInventoryListMode.Whitelist,
            Array.Empty<string>());

        public bool IsSelected(string assetId)
        {
            return (SelectedAssetIds ?? Array.Empty<string>()).Contains(assetId, StringComparer.Ordinal);
        }

        public bool Allows(string assetId)
        {
            bool selected = IsSelected(assetId);
            return ListMode == YouPinLandlordInventoryListMode.Whitelist ? selected : !selected;
        }

        public bool IsSelected(string assetId, string itemName)
        {
            return YouPinLandlordSelectionRule.Matches(
                true,
                SelectionScope,
                SelectedAssetIds,
                SelectedItemNames,
                assetId,
                itemName);
        }

        public bool Allows(string assetId, string itemName)
        {
            bool selected = IsSelected(assetId, itemName);
            return ListMode == YouPinLandlordInventoryListMode.Whitelist ? selected : !selected;
        }
    }

    public sealed record YouPinLandlordInventoryItem(
        string ActionId,
        string AssetId,
        string TemplateId,
        string ItemName,
        decimal ReferencePrice,
        bool IsSelected,
        bool IsEligible,
        YouPinLandlordInventoryEligibilityCode EligibilityCode,
        string EligibilityReason,
        DateTime CheckedAt);

    public sealed record YouPinLandlordPricingPreference(
        int PricingType,
        bool ZeroCdRentEnabled,
        bool FillRentEnabled,
        bool FillDepositEnabled,
        IReadOnlyList<int> LeaseDays)
    {
        public int TransactionMode { get; init; }

        public bool DefaultRentalActivityEnabled { get; init; }

        public int DepositCompensationType { get; init; }

        public decimal LongRentCoefficient { get; init; }

        public static YouPinLandlordPricingPreference Empty { get; } = new(
            0,
            false,
            false,
            false,
            Array.Empty<int>());
    }

    public sealed record YouPinLandlordShelfItem(
        string ActionId,
        string ItemName,
        YouPinRentalShelfType RentalType,
        decimal CurrentRent,
        int? CurrentRank,
        int TargetRank,
        YouPinLandlordDecisionCode DecisionCode,
        string DecisionText,
        DateTime CheckedAt)
    {
        public string AssetId { get; init; } = string.Empty;
    }

    public sealed record YouPinLandlordScanItem(
        string ListingId,
        string AssetId,
        string TemplateId,
        string ItemName,
        YouPinRentalShelfType RentalType,
        decimal ShortRent);

    public sealed record YouPinLandlordScanSnapshot(
        int SchemaVersion,
        int PolicyVersion,
        string RunId,
        DateTime CapturedAt,
        IReadOnlyList<YouPinLandlordScanItem> Listings,
        YouPinLandlordPricingPreference PricingPreference);

    public sealed record YouPinLandlordPlannedAction(
        int SchemaVersion,
        int PolicyVersion,
        string RunId,
        string ActionId,
        YouPinLandlordWorkflow Workflow,
        string ItemName,
        YouPinRentalShelfType RentalType,
        YouPinLandlordActionKind Kind,
        YouPinLandlordActionState State,
        YouPinLandlordDecisionCode DecisionCode,
        string Reason)
    {
        public decimal? TargetShortRent { get; init; }

        public decimal? TargetLongRent { get; init; }

        public decimal? TargetDeposit { get; init; }

        public int? TargetLeaseMaxDays { get; init; }

        public decimal? TargetSellPrice { get; init; }

        public string ResourceKeyHash { get; init; } = string.Empty;
    }

    public sealed record YouPinLandlordPlan(
        int SchemaVersion,
        int PolicyVersion,
        string RunId,
        DateTime CreatedAt,
        IReadOnlyList<YouPinLandlordPlannedAction> Actions)
    {
        public static YouPinLandlordPlan Empty { get; } = new(
            1,
            YouPinLandlordPolicy.Default.PolicyVersion,
            string.Empty,
            DateTime.MinValue,
            Array.Empty<YouPinLandlordPlannedAction>());
    }

    public sealed record YouPinLandlordActionExecution(
        int SchemaVersion,
        int PolicyVersion,
        string RunId,
        string ActionId,
        YouPinLandlordActionKind Kind,
        YouPinLandlordActionState State,
        DateTime Time,
        string Message);

    public sealed record YouPinLandlordCallbackResult(
        int SchemaVersion,
        int PolicyVersion,
        string RunId,
        string ActionId,
        YouPinLandlordCallbackState State,
        DateTime CheckedAt,
        string Message);

    public sealed record YouPinLandlordStateMerge(
        int SchemaVersion,
        int PolicyVersion,
        string RunId,
        string ActionId,
        YouPinLandlordActionState PreviousState,
        YouPinLandlordActionState CurrentState,
        DateTime MergedAt,
        string Reason);

    public sealed record YouPinLandlordOperationRecord(
        int SchemaVersion,
        string RunId,
        string ActionId,
        YouPinLandlordWorkflow Workflow,
        YouPinLandlordOperationStage Stage,
        DateTime Time,
        string ItemName,
        YouPinRentalShelfType? RentalType,
        YouPinLandlordDecisionCode? DecisionCode,
        string Result,
        string Message,
        long ElapsedMilliseconds)
    {
        public int PolicyVersion { get; init; } = 1;

        public YouPinLandlordRunMode RunMode { get; init; } = YouPinLandlordRunMode.Execute;

        public string ResourceKeyHash { get; init; } = string.Empty;
    }

    public sealed record YouPinLandlordExecutionState(
        YouPinLandlordExecutionLane Lane,
        DateTime LastStartedAtUtc,
        DateTime NextAutomaticAtUtc)
    {
        public static YouPinLandlordExecutionState Never(YouPinLandlordExecutionLane lane)
            => new(lane, DateTime.MinValue, DateTime.MaxValue);
    }

    public sealed record YouPinLandlordAuditQuery(
        DateTime? From,
        DateTime? To,
        YouPinLandlordWorkflow? Workflow,
        YouPinRentalShelfType? RentalType,
        string ItemName,
        string Result,
        string RunId,
        string ActionId,
        int Limit = 500)
    {
        public static YouPinLandlordAuditQuery Recent { get; } = new(
            null, null, null, null, string.Empty, string.Empty, string.Empty, string.Empty, 100);
    }

    public sealed record YouPinLandlordAuditHealth(
        bool IsHealthy,
        DateTime LastSuccessfulWriteAt,
        DateTime LastFailureAt,
        string LastError)
    {
        public static YouPinLandlordAuditHealth Healthy { get; } = new(
            true,
            DateTime.MinValue,
            DateTime.MinValue,
            string.Empty);
    }

    public sealed record YouPinLandlordSnapshot(
        int SchemaVersion,
        int PolicyVersion,
        string LastRunId,
        DateTime LastCheckedAt,
        string Status,
        string LastError,
        bool IsRunning,
        IReadOnlyList<YouPinLandlordShelfItem> Shelf,
        YouPinLandlordPricingPreference PricingPreference,
        IReadOnlyList<YouPinLandlordOperationRecord> RecentOperations)
    {
        public YouPinLandlordScanSnapshot? LastScan { get; init; }

        public YouPinLandlordPlan CurrentPlan { get; init; } = YouPinLandlordPlan.Empty;

        public IReadOnlyList<YouPinLandlordInventoryItem> Inventory { get; init; }
            = Array.Empty<YouPinLandlordInventoryItem>();

        public DateTime InventoryLastCheckedAt { get; init; } = DateTime.MinValue;

        public string InventoryStatus { get; init; } = "未扫描";

        public DateTime ZeroCdLastCheckedAt { get; init; } = DateTime.MinValue;

        public DateTime InventoryRentalLastCheckedAt { get; init; } = DateTime.MinValue;

        public YouPinLandlordExecutionState ZeroCdExecution { get; init; }
            = YouPinLandlordExecutionState.Never(YouPinLandlordExecutionLane.ZeroCdReprice);

        public YouPinLandlordExecutionState InventoryRentalExecution { get; init; }
            = YouPinLandlordExecutionState.Never(YouPinLandlordExecutionLane.InventoryRentalReprice);

        public YouPinLandlordExecutionState InventoryAutoRentExecution { get; init; }
            = YouPinLandlordExecutionState.Never(YouPinLandlordExecutionLane.InventoryAutoRent);

        public static YouPinLandlordSnapshot Empty { get; } = new(
            1,
            YouPinLandlordPolicy.Default.PolicyVersion,
            string.Empty,
            DateTime.MinValue,
            "未检查",
            string.Empty,
            false,
            Array.Empty<YouPinLandlordShelfItem>(),
            YouPinLandlordPricingPreference.Empty,
            Array.Empty<YouPinLandlordOperationRecord>());
    }

    public sealed record YouPinLandlordRunResult(
        bool Success,
        bool Skipped,
        string RunId,
        string Message,
        int ListingCount)
    {
        public static YouPinLandlordRunResult Completed(string runId, string message, int listingCount)
            => new(true, false, runId, message, listingCount);

        public static YouPinLandlordRunResult Skip(string message, string runId = "")
            => new(false, true, runId, message, 0);

        public static YouPinLandlordRunResult Failed(string runId, string message)
            => new(false, false, runId, message, 0);
    }
}
