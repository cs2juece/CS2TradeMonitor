using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace CS2TradeMonitor.Application.YouPin
{
    public sealed class YouPinLandlordAutomation : IYouPinLandlordAutomation
    {
        private const int RecentOperationLimit = 100;
        private const int MarketReadMaxConcurrency = 3;

        private readonly IYouPinLandlordGateway _gateway;
        private readonly IYouPinLandlordAuditStore _auditStore;
        private readonly IClock _clock;
        private readonly IReadOnlyList<IYouPinLandlordDecisionRule> _decisionRules;
        private readonly SemaphoreSlim _runGate = new(1, 1);
        private readonly YouPinLandlordWriteCoordinator _writeCoordinator;
        private readonly YouPinLandlordRepriceExecutor _repriceExecutor;
        private readonly YouPinLandlordInventoryRentExecutor _inventoryRentExecutor;
        private readonly YouPinLandlordExecutionCadence _executionCadence = new();
        private readonly Action<Settings>? _persistSettings;
        private readonly CancellationTokenSource _lifetimeCancellation = new();
        private readonly System.Threading.Timer _backgroundTimer;
        private readonly object _stateLock = new();
        private readonly object _lifecycleLock = new();

        private Settings _settings = new();
        private YouPinLandlordPolicy _policy = YouPinLandlordPolicy.Default;
        private YouPinLandlordSnapshot _snapshot = YouPinLandlordSnapshot.Empty;
        private DateTime _nextZeroCdCheckUtc = DateTime.MaxValue;
        private DateTime _nextInventoryRentalCheckUtc = DateTime.MaxValue;
        private DateTime _nextInventoryAutoRentCheckUtc = DateTime.MaxValue;
        private int _zeroCdFailureStreak;
        private int _inventoryRentalFailureStreak;
        private int _inventoryAutoRentFailureStreak;
        private Task _backgroundTask = Task.CompletedTask;
        private volatile bool _disposed;

        internal YouPinLandlordAutomation(
            IYouPinLandlordGateway gateway,
            IYouPinLandlordAuditStore auditStore,
            IClock clock,
            IReadOnlyList<IYouPinLandlordDecisionRule>? decisionRules = null,
            TimeSpan? writeInterval = null,
            Action<Settings>? persistSettings = null)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _decisionRules = decisionRules is { Count: > 0 }
                ? decisionRules.ToArray()
                : new IYouPinLandlordDecisionRule[] { new YouPinLandlordRankDecisionRule() };
            _persistSettings = persistSettings;
            _writeCoordinator = new YouPinLandlordWriteCoordinator(writeInterval ?? TimeSpan.Zero);
            _repriceExecutor = new YouPinLandlordRepriceExecutor(
                _gateway,
                _auditStore,
                _clock,
                () => _settings,
                IsRentalTypeEnabled,
                _lifetimeCancellation.Token,
                _writeCoordinator);
            _inventoryRentExecutor = new YouPinLandlordInventoryRentExecutor(
                _gateway,
                _auditStore,
                _clock,
                () => _settings,
                () => GetPolicy().InventoryAutoRent.Enabled,
                _lifetimeCancellation.Token,
                _writeCoordinator);
            _backgroundTimer = new System.Threading.Timer(
                HandleBackgroundTimer,
                null,
                Timeout.Infinite,
                Timeout.Infinite);
        }

        public event Action? SnapshotChanged;

        public void Configure(Settings settings)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _settings = settings ?? new Settings();
            if (!_settings.YouPinLandlordUnifiedRepriceInitialized)
            {
                _settings.YouPinLandlordUnifiedEnabled = _settings.YouPinLandlordZeroCdEnabled;
                _settings.YouPinLandlordUnifiedTargetRank = _settings.YouPinLandlordZeroCdTargetRank;
                _settings.YouPinLandlordUnifiedScanIntervalMinutes =
                    _settings.YouPinLandlordZeroCdScanIntervalMinutes;
                _settings.YouPinLandlordUnifiedExecutionIntervalMinutes =
                    _settings.YouPinLandlordZeroCdExecutionIntervalMinutes;
                _settings.YouPinLandlordUnifiedSelectionInitialized =
                    _settings.YouPinLandlordZeroCdSelectionInitialized;
                _settings.YouPinLandlordUnifiedSelectionScope =
                    _settings.YouPinLandlordZeroCdSelectionScope;
                _settings.YouPinLandlordUnifiedSelectedAssetIds =
                    _settings.YouPinLandlordZeroCdSelectedAssetIds;
                _settings.YouPinLandlordUnifiedSelectedItemNames =
                    _settings.YouPinLandlordZeroCdSelectedItemNames;
                _settings.YouPinLandlordUnifiedWeeklyFreeEnabled =
                    _settings.YouPinLandlordZeroCdWeeklyFreeEnabled;
                _settings.YouPinLandlordUnifiedWeeklyFreeMinimumValue =
                    _settings.YouPinLandlordZeroCdWeeklyFreeMinimumValue;
                _settings.YouPinLandlordUnifiedWeeklyFreeMaximumValue =
                    _settings.YouPinLandlordZeroCdWeeklyFreeMaximumValue;
                _settings.YouPinLandlordUnifiedCooldownEnabled =
                    _settings.YouPinLandlordZeroCdCooldownEnabled;
                _settings.YouPinLandlordUnifiedCooldownStartMinute =
                    _settings.YouPinLandlordZeroCdCooldownStartMinute;
                _settings.YouPinLandlordUnifiedCooldownEndMinute =
                    _settings.YouPinLandlordZeroCdCooldownEndMinute;
                _settings.YouPinLandlordUnifiedRepriceInitialized = true;
            }
            ApplyPolicy(new YouPinLandlordPolicy(
                1,
                Math.Max(1, _settings.YouPinLandlordPolicyVersion),
                new YouPinLandlordRentalPolicy(
                    _settings.YouPinLandlordZeroCdTargetRank,
                    _settings.YouPinLandlordZeroCdScanIntervalMinutes,
                    _settings.YouPinLandlordZeroCdEnabled)
                {
                    ExecutionIntervalMinutes = _settings.YouPinLandlordZeroCdExecutionIntervalMinutes,
                    WeeklyFree = new YouPinLandlordWeeklyFreeRule(
                        _settings.YouPinLandlordZeroCdWeeklyFreeEnabled,
                        _settings.YouPinLandlordZeroCdWeeklyFreeMinimumValue,
                        _settings.YouPinLandlordZeroCdWeeklyFreeMaximumValue),
                    Cooldown = new YouPinLandlordCooldownWindow(
                        _settings.YouPinLandlordZeroCdCooldownEnabled,
                        _settings.YouPinLandlordZeroCdCooldownStartMinute,
                        _settings.YouPinLandlordZeroCdCooldownEndMinute),
                    Selection = BuildSelectionRule(
                        _settings.YouPinLandlordZeroCdSelectionInitialized,
                        _settings.YouPinLandlordZeroCdSelectionScope,
                        _settings.YouPinLandlordZeroCdSelectedAssetIds,
                        _settings.YouPinLandlordZeroCdSelectedItemNames)
                },
                new YouPinLandlordRentalPolicy(
                    _settings.YouPinLandlordInventoryRentalTargetRank,
                    _settings.YouPinLandlordInventoryRentalScanIntervalMinutes,
                    _settings.YouPinLandlordInventoryRentalEnabled)
                {
                    ExecutionIntervalMinutes = _settings.YouPinLandlordInventoryRentalExecutionIntervalMinutes,
                    WeeklyFree = new YouPinLandlordWeeklyFreeRule(
                        _settings.YouPinLandlordInventoryRentalWeeklyFreeEnabled,
                        _settings.YouPinLandlordInventoryRentalWeeklyFreeMinimumValue,
                        _settings.YouPinLandlordInventoryRentalWeeklyFreeMaximumValue),
                    Cooldown = new YouPinLandlordCooldownWindow(
                        _settings.YouPinLandlordInventoryRentalCooldownEnabled,
                        _settings.YouPinLandlordInventoryRentalCooldownStartMinute,
                        _settings.YouPinLandlordInventoryRentalCooldownEndMinute),
                    Selection = BuildSelectionRule(
                        _settings.YouPinLandlordInventoryRentalSelectionInitialized,
                        _settings.YouPinLandlordInventoryRentalSelectionScope,
                        _settings.YouPinLandlordInventoryRentalSelectedAssetIds,
                        _settings.YouPinLandlordInventoryRentalSelectedItemNames)
                })
            {
                RepriceConfigurationMode = _settings.YouPinLandlordUseUnifiedRepriceSettings
                    ? YouPinLandlordRepriceConfigurationMode.Unified
                    : YouPinLandlordRepriceConfigurationMode.Separate,
                UnifiedRental = new YouPinLandlordRentalPolicy(
                    _settings.YouPinLandlordUnifiedTargetRank,
                    _settings.YouPinLandlordUnifiedScanIntervalMinutes,
                    _settings.YouPinLandlordUnifiedEnabled)
                {
                    ExecutionIntervalMinutes = _settings.YouPinLandlordUnifiedExecutionIntervalMinutes,
                    WeeklyFree = new YouPinLandlordWeeklyFreeRule(
                        _settings.YouPinLandlordUnifiedWeeklyFreeEnabled,
                        _settings.YouPinLandlordUnifiedWeeklyFreeMinimumValue,
                        _settings.YouPinLandlordUnifiedWeeklyFreeMaximumValue),
                    Cooldown = new YouPinLandlordCooldownWindow(
                        _settings.YouPinLandlordUnifiedCooldownEnabled,
                        _settings.YouPinLandlordUnifiedCooldownStartMinute,
                        _settings.YouPinLandlordUnifiedCooldownEndMinute),
                    Selection = BuildSelectionRule(
                        _settings.YouPinLandlordUnifiedSelectionInitialized,
                        _settings.YouPinLandlordUnifiedSelectionScope,
                        _settings.YouPinLandlordUnifiedSelectedAssetIds,
                        _settings.YouPinLandlordUnifiedSelectedItemNames)
                },
                InventoryAutoRent = new YouPinLandlordInventoryPolicy(
                    _settings.YouPinLandlordInventoryAutoRentEnabled,
                    _settings.YouPinLandlordInventoryAutoRentScanIntervalMinutes,
                    Enum.TryParse(
                        _settings.YouPinLandlordInventoryAutoRentListMode,
                        true,
                        out YouPinLandlordInventoryListMode listMode)
                            ? listMode
                            : YouPinLandlordInventoryListMode.Whitelist,
                    ParseSelectedAssetIds(_settings.YouPinLandlordInventoryAutoRentSelectedAssetIds))
                {
                    ExecutionIntervalMinutes = _settings.YouPinLandlordInventoryAutoRentExecutionIntervalMinutes,
                    SelectionScope = Enum.TryParse(
                        _settings.YouPinLandlordInventoryAutoRentSelectionScope,
                        true,
                        out YouPinLandlordSelectionScope selectionScope)
                            ? selectionScope
                            : YouPinLandlordSelectionScope.PerAsset,
                    SelectedItemNames = ParseSelectedItemNames(
                        _settings.YouPinLandlordInventoryAutoRentSelectedItemNames),
                    WeeklyFree = new YouPinLandlordWeeklyFreeRule(
                        _settings.YouPinLandlordInventoryAutoRentWeeklyFreeEnabled,
                        _settings.YouPinLandlordInventoryAutoRentWeeklyFreeMinimumValue,
                        _settings.YouPinLandlordInventoryAutoRentWeeklyFreeMaximumValue),
                    Cooldown = new YouPinLandlordCooldownWindow(
                        _settings.YouPinLandlordInventoryAutoRentCooldownEnabled,
                        _settings.YouPinLandlordInventoryAutoRentCooldownStartMinute,
                        _settings.YouPinLandlordInventoryAutoRentCooldownEndMinute)
                }
            });
        }

        public YouPinLandlordPolicy ApplyPolicy(YouPinLandlordPolicy policy)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(policy);

            var normalized = policy with
            {
                SchemaVersion = Math.Max(1, policy.SchemaVersion),
                PolicyVersion = Math.Max(1, policy.PolicyVersion),
                ZeroCd = NormalizeRentalPolicy(policy.ZeroCd, YouPinLandlordRentalPolicy.ZeroCdDefault),
                InventoryRental = NormalizeRentalPolicy(
                    policy.InventoryRental,
                    YouPinLandlordRentalPolicy.InventoryRentalDefault),
                UnifiedRental = NormalizeRentalPolicy(
                    policy.UnifiedRental,
                    YouPinLandlordRentalPolicy.ZeroCdDefault),
                InventoryAutoRent = NormalizeInventoryPolicy(policy.InventoryAutoRent)
            };

            lock (_stateLock)
            {
                _policy = normalized;
                _snapshot = _snapshot with { PolicyVersion = normalized.PolicyVersion };
                _settings.YouPinLandlordPolicyVersion = normalized.PolicyVersion;
                _settings.YouPinLandlordZeroCdEnabled = normalized.ZeroCd.Enabled;
                _settings.YouPinLandlordZeroCdTargetRank = normalized.ZeroCd.TargetRank;
                _settings.YouPinLandlordZeroCdScanIntervalMinutes = normalized.ZeroCd.ScanIntervalMinutes;
                _settings.YouPinLandlordZeroCdExecutionIntervalMinutes =
                    normalized.ZeroCd.ExecutionIntervalMinutes;
                _settings.YouPinLandlordZeroCdSelectionInitialized = normalized.ZeroCd.Selection.Initialized;
                _settings.YouPinLandlordZeroCdSelectionScope = normalized.ZeroCd.Selection.Scope.ToString();
                _settings.YouPinLandlordZeroCdSelectedAssetIds =
                    string.Join('\n', normalized.ZeroCd.Selection.SelectedAssetIds);
                _settings.YouPinLandlordZeroCdSelectedItemNames =
                    string.Join('\n', normalized.ZeroCd.Selection.SelectedItemNames);
                _settings.YouPinLandlordZeroCdWeeklyFreeEnabled = normalized.ZeroCd.WeeklyFree.Enabled;
                _settings.YouPinLandlordZeroCdWeeklyFreeMinimumValue = normalized.ZeroCd.WeeklyFree.MinimumItemValue;
                _settings.YouPinLandlordZeroCdWeeklyFreeMaximumValue = normalized.ZeroCd.WeeklyFree.MaximumItemValue;
                _settings.YouPinLandlordZeroCdCooldownEnabled = normalized.ZeroCd.Cooldown.Enabled;
                _settings.YouPinLandlordZeroCdCooldownStartMinute = normalized.ZeroCd.Cooldown.StartMinuteOfDay;
                _settings.YouPinLandlordZeroCdCooldownEndMinute = normalized.ZeroCd.Cooldown.EndMinuteOfDay;
                _settings.YouPinLandlordInventoryRentalEnabled = normalized.InventoryRental.Enabled;
                _settings.YouPinLandlordInventoryRentalTargetRank = normalized.InventoryRental.TargetRank;
                _settings.YouPinLandlordInventoryRentalScanIntervalMinutes =
                    normalized.InventoryRental.ScanIntervalMinutes;
                _settings.YouPinLandlordInventoryRentalExecutionIntervalMinutes =
                    normalized.InventoryRental.ExecutionIntervalMinutes;
                _settings.YouPinLandlordInventoryRentalSelectionInitialized =
                    normalized.InventoryRental.Selection.Initialized;
                _settings.YouPinLandlordInventoryRentalSelectionScope =
                    normalized.InventoryRental.Selection.Scope.ToString();
                _settings.YouPinLandlordInventoryRentalSelectedAssetIds =
                    string.Join('\n', normalized.InventoryRental.Selection.SelectedAssetIds);
                _settings.YouPinLandlordInventoryRentalSelectedItemNames =
                    string.Join('\n', normalized.InventoryRental.Selection.SelectedItemNames);
                _settings.YouPinLandlordInventoryRentalWeeklyFreeEnabled = normalized.InventoryRental.WeeklyFree.Enabled;
                _settings.YouPinLandlordInventoryRentalWeeklyFreeMinimumValue = normalized.InventoryRental.WeeklyFree.MinimumItemValue;
                _settings.YouPinLandlordInventoryRentalWeeklyFreeMaximumValue = normalized.InventoryRental.WeeklyFree.MaximumItemValue;
                _settings.YouPinLandlordInventoryRentalCooldownEnabled = normalized.InventoryRental.Cooldown.Enabled;
                _settings.YouPinLandlordInventoryRentalCooldownStartMinute = normalized.InventoryRental.Cooldown.StartMinuteOfDay;
                _settings.YouPinLandlordInventoryRentalCooldownEndMinute = normalized.InventoryRental.Cooldown.EndMinuteOfDay;
                _settings.YouPinLandlordUseUnifiedRepriceSettings =
                    normalized.RepriceConfigurationMode == YouPinLandlordRepriceConfigurationMode.Unified;
                _settings.YouPinLandlordUnifiedRepriceInitialized = true;
                _settings.YouPinLandlordUnifiedEnabled = normalized.UnifiedRental.Enabled;
                _settings.YouPinLandlordUnifiedTargetRank = normalized.UnifiedRental.TargetRank;
                _settings.YouPinLandlordUnifiedScanIntervalMinutes = normalized.UnifiedRental.ScanIntervalMinutes;
                _settings.YouPinLandlordUnifiedExecutionIntervalMinutes =
                    normalized.UnifiedRental.ExecutionIntervalMinutes;
                _settings.YouPinLandlordUnifiedSelectionInitialized =
                    normalized.UnifiedRental.Selection.Initialized;
                _settings.YouPinLandlordUnifiedSelectionScope =
                    normalized.UnifiedRental.Selection.Scope.ToString();
                _settings.YouPinLandlordUnifiedSelectedAssetIds =
                    string.Join('\n', normalized.UnifiedRental.Selection.SelectedAssetIds);
                _settings.YouPinLandlordUnifiedSelectedItemNames =
                    string.Join('\n', normalized.UnifiedRental.Selection.SelectedItemNames);
                _settings.YouPinLandlordUnifiedWeeklyFreeEnabled = normalized.UnifiedRental.WeeklyFree.Enabled;
                _settings.YouPinLandlordUnifiedWeeklyFreeMinimumValue = normalized.UnifiedRental.WeeklyFree.MinimumItemValue;
                _settings.YouPinLandlordUnifiedWeeklyFreeMaximumValue = normalized.UnifiedRental.WeeklyFree.MaximumItemValue;
                _settings.YouPinLandlordUnifiedCooldownEnabled = normalized.UnifiedRental.Cooldown.Enabled;
                _settings.YouPinLandlordUnifiedCooldownStartMinute = normalized.UnifiedRental.Cooldown.StartMinuteOfDay;
                _settings.YouPinLandlordUnifiedCooldownEndMinute = normalized.UnifiedRental.Cooldown.EndMinuteOfDay;
                _settings.YouPinLandlordInventoryAutoRentEnabled = normalized.InventoryAutoRent.Enabled;
                _settings.YouPinLandlordInventoryAutoRentScanIntervalMinutes =
                    normalized.InventoryAutoRent.ScanIntervalMinutes;
                _settings.YouPinLandlordInventoryAutoRentExecutionIntervalMinutes =
                    normalized.InventoryAutoRent.ExecutionIntervalMinutes;
                _settings.YouPinLandlordInventoryAutoRentListMode = normalized.InventoryAutoRent.ListMode.ToString();
                _settings.YouPinLandlordInventoryAutoRentSelectionScope =
                    normalized.InventoryAutoRent.SelectionScope.ToString();
                _settings.YouPinLandlordInventoryAutoRentSelectedAssetIds =
                    string.Join('\n', normalized.InventoryAutoRent.SelectedAssetIds);
                _settings.YouPinLandlordInventoryAutoRentSelectedItemNames =
                    string.Join('\n', normalized.InventoryAutoRent.SelectedItemNames);
                _settings.YouPinLandlordInventoryAutoRentWeeklyFreeEnabled = normalized.InventoryAutoRent.WeeklyFree.Enabled;
                _settings.YouPinLandlordInventoryAutoRentWeeklyFreeMinimumValue =
                    normalized.InventoryAutoRent.WeeklyFree.MinimumItemValue;
                _settings.YouPinLandlordInventoryAutoRentWeeklyFreeMaximumValue =
                    normalized.InventoryAutoRent.WeeklyFree.MaximumItemValue;
                _settings.YouPinLandlordInventoryAutoRentCooldownEnabled = normalized.InventoryAutoRent.Cooldown.Enabled;
                _settings.YouPinLandlordInventoryAutoRentCooldownStartMinute =
                    normalized.InventoryAutoRent.Cooldown.StartMinuteOfDay;
                _settings.YouPinLandlordInventoryAutoRentCooldownEndMinute =
                    normalized.InventoryAutoRent.Cooldown.EndMinuteOfDay;
            }

            ResetBackgroundSchedule();
            SnapshotChanged?.Invoke();
            return normalized;
        }

        public YouPinLandlordSnapshot GetSnapshot()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            lock (_stateLock)
            {
                return _snapshot with
                {
                    Shelf = _snapshot.Shelf.ToArray(),
                    RecentOperations = _snapshot.RecentOperations.ToArray(),
                    PricingPreference = _snapshot.PricingPreference with
                    {
                        LeaseDays = _snapshot.PricingPreference.LeaseDays.ToArray()
                    },
                    LastScan = _snapshot.LastScan is null
                        ? null
                        : _snapshot.LastScan with
                        {
                            Listings = _snapshot.LastScan.Listings.ToArray(),
                            PricingPreference = _snapshot.LastScan.PricingPreference with
                            {
                                LeaseDays = _snapshot.LastScan.PricingPreference.LeaseDays.ToArray()
                            }
                        },
                    CurrentPlan = _snapshot.CurrentPlan with
                    {
                        Actions = _snapshot.CurrentPlan.Actions.ToArray()
                    },
                    Inventory = _snapshot.Inventory.ToArray()
                };
            }
        }

        public YouPinLandlordPolicy GetPolicy()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            lock (_stateLock)
            {
                return _policy;
            }
        }

        public Task<YouPinLandlordRunResult> RunNowAsync(
            YouPinLandlordWorkflow workflow,
            string trigger = "用户立即检查",
            CancellationToken cancellationToken = default)
        {
            if (workflow == YouPinLandlordWorkflow.InventoryAutoRent)
                return RunInventoryNowAsync(trigger, cancellationToken);

            YouPinLandlordPolicy policy = GetPolicy();
            bool anyEnabled = policy.EffectiveFor(YouPinRentalShelfType.ZeroCd).Enabled
                || policy.EffectiveFor(YouPinRentalShelfType.InventoryRental).Enabled;
            return RunScopedAsync(
                workflow,
                YouPinRentalScanScope.All,
                trigger,
                anyEnabled ? YouPinLandlordRunMode.Execute : YouPinLandlordRunMode.ScanOnly,
                manualExecution: anyEnabled,
                cancellationToken);
        }

        public Task<YouPinLandlordRunResult> RunRentalTypeNowAsync(
            YouPinRentalShelfType rentalType,
            string trigger = "用户立即检查",
            CancellationToken cancellationToken = default)
        {
            return GetPolicy().EffectiveFor(rentalType).Enabled
                ? ExecuteRentalTypeNowAsync(rentalType, trigger, cancellationToken)
                : ScanRentalTypeNowAsync(rentalType, trigger, cancellationToken);
        }

        public Task<YouPinLandlordRunResult> RunInventoryNowAsync(
            string trigger = "用户立即扫描库存",
            CancellationToken cancellationToken = default)
        {
            return GetPolicy().InventoryAutoRent.Enabled
                ? ExecuteInventoryNowAsync(trigger, cancellationToken)
                : ScanInventoryNowAsync(trigger, cancellationToken);
        }

        public Task<YouPinLandlordRunResult> ScanRentalTypeNowAsync(
            YouPinRentalShelfType rentalType,
            string trigger = "用户立即扫描货架",
            CancellationToken cancellationToken = default)
        {
            return ScanRentalNowAsync(
                ToScope(rentalType),
                trigger,
                cancellationToken);
        }

        public Task<YouPinLandlordRunResult> ScanRentalNowAsync(
            YouPinRentalScanScope scope,
            string trigger = "用户立即扫描货架",
            CancellationToken cancellationToken = default)
        {
            return RunManualRentalAsync(
                scope,
                trigger,
                YouPinLandlordRunMode.ScanOnly,
                cancellationToken);
        }

        public Task<YouPinLandlordRunResult> ExecuteRentalTypeNowAsync(
            YouPinRentalShelfType rentalType,
            string trigger = "用户立即执行改价",
            CancellationToken cancellationToken = default)
        {
            return ExecuteRentalNowAsync(
                ToScope(rentalType),
                trigger,
                cancellationToken);
        }

        public Task<YouPinLandlordRunResult> ExecuteRentalNowAsync(
            YouPinRentalScanScope scope,
            string trigger = "用户立即执行改价",
            CancellationToken cancellationToken = default)
        {
            return RunManualRentalAsync(
                scope,
                trigger,
                YouPinLandlordRunMode.Execute,
                cancellationToken);
        }

        public Task<YouPinLandlordRunResult> ScanInventoryNowAsync(
            string trigger = "用户立即扫描库存",
            CancellationToken cancellationToken = default)
        {
            return RunManualInventoryAsync(
                trigger,
                YouPinLandlordRunMode.ScanOnly,
                cancellationToken);
        }

        public Task<YouPinLandlordRunResult> ExecuteInventoryNowAsync(
            string trigger = "用户立即执行库存自动出租",
            CancellationToken cancellationToken = default)
        {
            return RunManualInventoryAsync(
                trigger,
                YouPinLandlordRunMode.Execute,
                cancellationToken);
        }

        private async Task<YouPinLandlordRunResult> RunManualRentalAsync(
            YouPinRentalScanScope scope,
            string trigger,
            YouPinLandlordRunMode runMode,
            CancellationToken cancellationToken)
        {
            YouPinLandlordRunResult result = await RunScopedAsync(
                YouPinLandlordWorkflow.RentalReprice,
                scope,
                trigger,
                runMode,
                manualExecution: runMode == YouPinLandlordRunMode.Execute,
                cancellationToken).ConfigureAwait(false);
            if (!result.Skipped)
                AdvanceBackgroundSchedule(scope, result.Success);
            return result;
        }

        private async Task<YouPinLandlordRunResult> RunManualInventoryAsync(
            string trigger,
            YouPinLandlordRunMode runMode,
            CancellationToken cancellationToken)
        {
            YouPinLandlordRunResult result = await RunInventoryScanAsync(
                trigger,
                runMode,
                manualExecution: runMode == YouPinLandlordRunMode.Execute,
                cancellationToken).ConfigureAwait(false);
            if (!result.Skipped)
                AdvanceInventoryBackgroundSchedule(result.Success);
            return result;
        }

        public async Task<YouPinLandlordPricingPreference> RefreshPricingPreferenceAsync(
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!await _runGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("已有包租公检查正在执行，请稍后刷新。");

            string runId = Guid.NewGuid().ToString("N");
            try
            {
                await _gateway.ValidateLoginAsync(_settings, cancellationToken).ConfigureAwait(false);
                YouPinLandlordRemoteSnapshot remote = await _gateway.ReadSnapshotAsync(
                    _settings,
                    YouPinRentalScanScope.None,
                    runId,
                    cancellationToken).ConfigureAwait(false);
                YouPinLandlordPricingPreference preference = remote.PricingPreference with
                {
                    LeaseDays = remote.PricingPreference.LeaseDays.ToArray()
                };
                lock (_stateLock)
                {
                    _snapshot = _snapshot with { PricingPreference = preference };
                }
                SnapshotChanged?.Invoke();
                return preference with { LeaseDays = preference.LeaseDays.ToArray() };
            }
            finally
            {
                _runGate.Release();
            }
        }

        public Task<IReadOnlyList<YouPinLandlordOperationRecord>> QueryHistoryAsync(
            YouPinLandlordAuditQuery query,
            CancellationToken cancellationToken = default)
        {
            return _auditStore.QueryAsync(query, cancellationToken);
        }

        public YouPinLandlordAuditHealth GetAuditHealth() => _auditStore.GetHealth();

        private async Task<YouPinLandlordRunResult> RunInventoryScanAsync(
            string trigger,
            YouPinLandlordRunMode runMode,
            bool manualExecution,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!await _runGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                string activeRunId = GetActiveRunId();
                const string mergedMessage = "已有包租公检查正在执行，本次请求已合并。";
                await AppendRunRecordAsync(
                    activeRunId,
                    YouPinLandlordWorkflow.InventoryAutoRent,
                    GetPolicy().PolicyVersion,
                    YouPinLandlordOperationStage.RunSkipped,
                    "合并",
                    mergedMessage,
                    0,
                    CancellationToken.None,
                    runMode).ConfigureAwait(false);
                return YouPinLandlordRunResult.Skip(mergedMessage, activeRunId);
            }

            string runId = Guid.NewGuid().ToString("N");
            YouPinLandlordPolicy runPolicy = GetPolicy();
            var stopwatch = Stopwatch.StartNew();
            try
            {
                if (runMode == YouPinLandlordRunMode.Execute)
                {
                    YouPinLandlordExecutionStartResult executionStart = _executionCadence.TryStart(
                        YouPinLandlordExecutionLane.InventoryAutoRent,
                        _clock.UtcNow,
                        ignoreAutomaticInterval: manualExecution);
                    if (!executionStart.Started)
                    {
                        string skippedMessage = FormatExecutionStartBlocked(
                            executionStart,
                            "库存自动出租");
                        await AppendRunRecordAsync(
                            runId,
                            YouPinLandlordWorkflow.InventoryAutoRent,
                            runPolicy.PolicyVersion,
                            YouPinLandlordOperationStage.RunSkipped,
                            "跳过",
                            skippedMessage,
                            stopwatch.ElapsedMilliseconds,
                            CancellationToken.None,
                            runMode).ConfigureAwait(false);
                        return YouPinLandlordRunResult.Skip(skippedMessage, runId);
                    }

                    PersistExecutionStart(executionStart.State);
                }

                SetRunning(runId);
                await AppendRunRecordAsync(
                    runId,
                    YouPinLandlordWorkflow.InventoryAutoRent,
                    runPolicy.PolicyVersion,
                    YouPinLandlordOperationStage.RunStarted,
                    "开始",
                    string.IsNullOrWhiteSpace(trigger) ? "开始扫描普通出租库存" : trigger.Trim(),
                    0,
                    cancellationToken,
                    runMode).ConfigureAwait(false);
                await _gateway.ValidateLoginAsync(_settings, cancellationToken).ConfigureAwait(false);
                IReadOnlyList<YouPinLandlordRemoteInventoryItem> remote = await _gateway
                    .ReadInventoryAsync(_settings, runId, cancellationToken)
                    .ConfigureAwait(false);

                YouPinLandlordInventoryPolicy inventoryPolicy = runPolicy.InventoryAutoRent;
                var rows = new List<YouPinLandlordInventoryItem>(remote.Count);
                var plannedActions = new List<YouPinLandlordPlannedAction>(remote.Count);
                YouPinLandlordRemoteSnapshot shelfSnapshot = inventoryPolicy.Enabled
                    ? await _gateway.ReadSnapshotAsync(
                        _settings,
                        YouPinRentalScanScope.InventoryRental,
                        runId,
                        cancellationToken).ConfigureAwait(false)
                    : new YouPinLandlordRemoteSnapshot(
                        Array.Empty<YouPinLandlordRemoteListing>(),
                        YouPinLandlordPricingPreference.Empty);
                var pricingByName = new Dictionary<string, YouPinLandlordPricingQuote>(StringComparer.Ordinal);
                HashSet<string> pendingInventoryHashes = await LoadPendingInventoryHashesAsync(
                    cancellationToken).ConfigureAwait(false);
                IReadOnlyDictionary<string, IReadOnlyList<YouPinLandlordMarketListing>> marketByName =
                    inventoryPolicy.Enabled && !inventoryPolicy.Cooldown.Contains(_clock.Now)
                        ? await ReadMarketsByNameAsync(
                            remote
                                .Where(item => item.IsEligible
                                    && inventoryPolicy.Allows(item.AssetId, item.ItemName)
                                    && !pendingInventoryHashes.Contains(HashResourceKey(item.AssetId)))
                                .Select(item => (item.TemplateId, item.ItemName)),
                            runId,
                            cancellationToken).ConfigureAwait(false)
                        : new Dictionary<string, IReadOnlyList<YouPinLandlordMarketListing>>(
                            StringComparer.Ordinal);
                foreach (YouPinLandlordRemoteInventoryItem item in remote)
                {
                    string actionId = Guid.NewGuid().ToString("N");
                    bool selected = inventoryPolicy.IsSelected(item.AssetId, item.ItemName);
                    bool allowedByList = inventoryPolicy.Allows(item.AssetId, item.ItemName);
                    string listDecision = BuildInventoryListDecision(
                        inventoryPolicy,
                        selected,
                        allowedByList);
                    rows.Add(new YouPinLandlordInventoryItem(
                        actionId,
                        item.AssetId,
                        item.TemplateId,
                        item.ItemName,
                        item.ReferencePrice,
                        selected,
                        item.IsEligible,
                        item.EligibilityCode,
                        item.EligibilityReason,
                        _clock.Now));
                    await _auditStore.AppendAsync(
                        new YouPinLandlordOperationRecord(
                            1,
                            runId,
                            actionId,
                            YouPinLandlordWorkflow.InventoryAutoRent,
                            YouPinLandlordOperationStage.Decision,
                            _clock.Now,
                            item.ItemName,
                            null,
                            null,
                            item.IsEligible ? "资格通过" : "资格不通过",
                            $"{item.EligibilityReason}；{listDecision}",
                            stopwatch.ElapsedMilliseconds)
                        {
                            PolicyVersion = runPolicy.PolicyVersion,
                            RunMode = runMode
                        },
                        cancellationToken).ConfigureAwait(false);

                    YouPinLandlordActionState state = YouPinLandlordActionState.Observed;
                    string reason = $"{item.EligibilityReason}；{listDecision}";
                    YouPinLandlordPricingQuote? quote = null;
                    decimal? targetShortRent = null;
                    if (!inventoryPolicy.Enabled)
                    {
                        reason += "；库存自动出租未开启，仅扫描资格";
                    }
                    else if (!item.IsEligible || !inventoryPolicy.Allows(item.AssetId, item.ItemName))
                    {
                        state = YouPinLandlordActionState.Skipped;
                    }
                    else if (pendingInventoryHashes.Contains(HashResourceKey(item.AssetId)))
                    {
                        state = YouPinLandlordActionState.Skipped;
                        reason = "上次上架已被平台接收但回查尚未确认；为避免重复提交，本次等待平台库存与货架同步";
                    }
                    else if (inventoryPolicy.Cooldown.Contains(_clock.Now))
                    {
                        state = YouPinLandlordActionState.Skipped;
                        reason = "当前处于冷却时段，只更新库存资格，不执行自动上架";
                    }
                    else
                    {
                        IReadOnlyList<YouPinLandlordMarketListing> market = marketByName.TryGetValue(
                            item.ItemName,
                            out IReadOnlyList<YouPinLandlordMarketListing>? cachedMarket)
                                ? cachedMarket
                                : Array.Empty<YouPinLandlordMarketListing>();
                        try
                        {
                            if (!pricingByName.TryGetValue(item.ItemName, out quote))
                            {
                                quote = await _gateway.ReadOneClickPricingAsync(
                                    _settings,
                                    ToInventoryPricingListing(item),
                                    runId,
                                    cancellationToken).ConfigureAwait(false);
                                pricingByName[item.ItemName] = quote;
                            }
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            state = YouPinLandlordActionState.Failed;
                            reason = FormatInventoryPricingFailure(ex);
                        }

                        if (quote != null)
                        {
                            decimal? protectedOwnPrice = FindProtectedOwnPriceForInventory(
                                shelfSnapshot.Listings,
                                item.ItemName,
                                market);
                            bool weeklyFreeMatched = inventoryPolicy.WeeklyFree.Matches(item.ReferencePrice);
                            if (quote.ShortRent <= 0m || quote.Deposit <= 0m || quote.LeaseMaxDays <= 0)
                            {
                                state = YouPinLandlordActionState.Skipped;
                                reason = "悠悠一键定价返回不完整，未取得有效短租、押金或租期，本次不自动上架";
                            }
                            else if (protectedOwnPrice.HasValue
                                && weeklyFreeMatched
                                && protectedOwnPrice.Value > YouPinLandlordWeeklyFreeRule.MaximumAllowedRent)
                            {
                                state = YouPinLandlordActionState.Skipped;
                                reason = "自有同名货架租金高于周周免租上限；为避免自压，本次不自动上架";
                            }
                            else
                            {
                                targetShortRent = protectedOwnPrice
                                    ?? (weeklyFreeMatched
                                        ? Math.Min(quote.ShortRent, YouPinLandlordWeeklyFreeRule.MaximumAllowedRent)
                                        : quote.ShortRent);
                                state = YouPinLandlordActionState.Planned;
                                reason = protectedOwnPrice.HasValue
                                    ? $"自有同名商品已在货架，按最佳自有出租位租金 {protectedOwnPrice:0.##} 对齐"
                                    : weeklyFreeMatched
                                        ? $"采用悠悠一键定价并限制短租金严格小于 {YouPinLandlordWeeklyFreeRule.ExclusiveRentLimit:0.00}"
                                        : $"采用悠悠一键定价 {quote.ShortRent:0.##}";
                            }
                        }
                    }

                    var action = new YouPinLandlordPlannedAction(
                        1,
                        runPolicy.PolicyVersion,
                        runId,
                        actionId,
                        YouPinLandlordWorkflow.InventoryAutoRent,
                        item.ItemName,
                        YouPinRentalShelfType.InventoryRental,
                        state == YouPinLandlordActionState.Planned
                            ? YouPinLandlordActionKind.ListInventory
                            : YouPinLandlordActionKind.ObserveOnly,
                        state,
                        YouPinLandlordDecisionCode.RankUnknown,
                        reason)
                    {
                        TargetShortRent = targetShortRent,
                        TargetLongRent = quote?.LongRent,
                        TargetDeposit = quote?.Deposit,
                        TargetLeaseMaxDays = quote?.LeaseMaxDays,
                        TargetSellPrice = quote is { SellPrice: > 0m } ? quote.SellPrice : null,
                        ResourceKeyHash = HashResourceKey(item.AssetId)
                    };
                    if (quote != null)
                    {
                        await AppendActionRecordAsync(
                            runPolicy,
                            action,
                            YouPinLandlordOperationStage.PricingObtained,
                            "已取得定价",
                            $"悠悠一键定价：短租 {quote.ShortRent:0.##}，长租 {quote.LongRent:0.##}，押金 {quote.Deposit:0.##}",
                            stopwatch.ElapsedMilliseconds,
                            cancellationToken,
                            runMode).ConfigureAwait(false);
                    }
                    else if (state == YouPinLandlordActionState.Failed)
                    {
                        await AppendActionRecordAsync(
                            runPolicy,
                            action,
                            YouPinLandlordOperationStage.PricingObtained,
                            "失败",
                            reason,
                            stopwatch.ElapsedMilliseconds,
                            cancellationToken,
                            runMode).ConfigureAwait(false);
                    }
                    plannedActions.Add(action);
                }

                YouPinLandlordRemoteInventoryItem[] revalidatedItems = runMode == YouPinLandlordRunMode.Execute
                    && plannedActions.Any(action => action.State == YouPinLandlordActionState.Planned)
                    ? (await _gateway.ReadInventoryAsync(_settings, runId, cancellationToken).ConfigureAwait(false)).ToArray()
                    : Array.Empty<YouPinLandlordRemoteInventoryItem>();
                var revalidatedByAsset = revalidatedItems.ToDictionary(item => item.AssetId, StringComparer.Ordinal);
                for (int index = 0; index < plannedActions.Count; index++)
                {
                    YouPinLandlordPlannedAction action = plannedActions[index];
                    if (action.State != YouPinLandlordActionState.Planned)
                        continue;
                    if (runMode == YouPinLandlordRunMode.ScanOnly)
                    {
                        plannedActions[index] = action with
                        {
                            Reason = action.Reason + "；扫描完成，等待执行"
                        };
                        continue;
                    }
                    YouPinLandlordRemoteInventoryItem original = remote[index];
                    if (!revalidatedByAsset.TryGetValue(original.AssetId, out YouPinLandlordRemoteInventoryItem? current)
                        || !current.IsEligible
                        || !inventoryPolicy.Allows(current.AssetId, current.ItemName))
                    {
                        string recheckReason = current == null
                            ? "写前复核发现饰品已不在可用库存，已跳过"
                            : !current.IsEligible
                                ? "写前复核发现出租资格已变化，已跳过：" + current.EligibilityReason
                                : "写前复核发现名单规则已变化，已跳过";
                        action = action with { State = YouPinLandlordActionState.Skipped, Reason = recheckReason };
                        await AppendActionRecordAsync(
                            runPolicy,
                            action,
                            YouPinLandlordOperationStage.WriteCompleted,
                            "跳过",
                            recheckReason,
                            stopwatch.ElapsedMilliseconds,
                            cancellationToken,
                            runMode).ConfigureAwait(false);
                    }
                    else
                    {
                        PublishInventoryPlanProgress(runPolicy, runId, rows, plannedActions, index, action);
                        try
                        {
                            action = await _inventoryRentExecutor.ExecuteAsync(
                                runPolicy,
                                current,
                                action,
                                stopwatch,
                                progress => PublishInventoryPlanProgress(
                                    runPolicy,
                                    runId,
                                    rows,
                                    plannedActions,
                                    index,
                                    progress),
                                cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            string failureReason = $"单件自动上架异常：{ex.GetType().Name}";
                            await AppendActionRecordAsync(
                                runPolicy,
                                action,
                                YouPinLandlordOperationStage.WriteCompleted,
                                "失败",
                                failureReason,
                                stopwatch.ElapsedMilliseconds,
                                cancellationToken,
                                runMode).ConfigureAwait(false);
                            action = action with
                            {
                                State = YouPinLandlordActionState.Failed,
                                Reason = failureReason
                            };
                        }
                    }
                    plannedActions[index] = action;
                    PublishInventoryPlanProgress(runPolicy, runId, rows, plannedActions, index, action);
                }

                stopwatch.Stop();
                int eligibleCount = rows.Count(item => item.IsEligible);
                int allowedCount = rows.Count(item =>
                    item.IsEligible && inventoryPolicy.Allows(item.AssetId, item.ItemName));
                int successCount = plannedActions.Count(action => action.State == YouPinLandlordActionState.Succeeded);
                int failedCount = plannedActions.Count(action => action.State == YouPinLandlordActionState.Failed);
                int skippedCount = plannedActions.Count(action => action.State == YouPinLandlordActionState.Skipped);
                int plannedCount = plannedActions.Count(action =>
                    action.State == YouPinLandlordActionState.Planned);
                string completionMessage = runMode == YouPinLandlordRunMode.ScanOnly
                    ? $"库存扫描完成，名单方式 {FormatInventorySelectionScope(inventoryPolicy.SelectionScope)}；共 {rows.Count} 件；资格通过 {eligibleCount}，名单允许 {allowedCount}；待执行 {plannedCount}。"
                    : inventoryPolicy.Enabled
                        ? $"库存执行完成，名单方式 {FormatInventorySelectionScope(inventoryPolicy.SelectionScope)}；共 {rows.Count} 件；资格通过 {eligibleCount}，名单允许 {allowedCount}；上架成功 {successCount}，失败 {failedCount}，跳过 {skippedCount}。"
                        : $"库存扫描完成，名单方式 {FormatInventorySelectionScope(inventoryPolicy.SelectionScope)}；共 {rows.Count} 件；符合出租资格 {eligibleCount} 件；名单允许 {allowedCount} 件。";
                string? tradingNoticeFailure = plannedActions
                    .Where(action => action.State == YouPinLandlordActionState.Failed)
                    .Select(action => action.Reason)
                    .FirstOrDefault(YouPinLandlordUserNotice.IsTradingNoticeFailure);
                if (tradingNoticeFailure != null)
                    completionMessage += "；" + tradingNoticeFailure;
                await AppendRunRecordAsync(
                    runId,
                    YouPinLandlordWorkflow.InventoryAutoRent,
                    runPolicy.PolicyVersion,
                    YouPinLandlordOperationStage.RunCompleted,
                    failedCount > 0 ? "部分失败" : "成功",
                    completionMessage,
                    stopwatch.ElapsedMilliseconds,
                    cancellationToken,
                    runMode).ConfigureAwait(false);
                IReadOnlyList<YouPinLandlordOperationRecord> recentOperations = await _auditStore
                    .ReadRecentAsync(RecentOperationLimit, cancellationToken)
                    .ConfigureAwait(false);
                lock (_stateLock)
                {
                    _snapshot = _snapshot with
                    {
                        LastRunId = runId,
                        LastCheckedAt = _clock.Now,
                        Status = "检查完成",
                        LastError = string.Empty,
                        IsRunning = false,
                        Inventory = rows,
                        InventoryLastCheckedAt = _clock.Now,
                        InventoryStatus = completionMessage,
                        RecentOperations = recentOperations.ToArray(),
                        PricingPreference = shelfSnapshot.PricingPreference with
                        {
                            LeaseDays = shelfSnapshot.PricingPreference.LeaseDays.ToArray()
                        },
                        CurrentPlan = new YouPinLandlordPlan(
                            1,
                            runPolicy.PolicyVersion,
                            runId,
                            _clock.Now,
                            _snapshot.CurrentPlan.Actions
                                .Where(action => action.Workflow != YouPinLandlordWorkflow.InventoryAutoRent)
                                .Concat(plannedActions)
                                .ToArray())
                    };
                }
                SnapshotChanged?.Invoke();
                return YouPinLandlordRunResult.Completed(runId, completionMessage, rows.Count);
            }
            catch (OperationCanceledException)
            {
                await SetInventoryFailureAsync(runId, "库存扫描已取消").ConfigureAwait(false);
                return YouPinLandlordRunResult.Skip("库存扫描已取消", runId);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                string message = string.IsNullOrWhiteSpace(ex.Message) ? "库存资格扫描失败。" : ex.Message.Trim();
                await AppendRunRecordAsync(
                    runId,
                    YouPinLandlordWorkflow.InventoryAutoRent,
                    runPolicy.PolicyVersion,
                    YouPinLandlordOperationStage.RunFailed,
                    "失败",
                    message,
                    stopwatch.ElapsedMilliseconds,
                    CancellationToken.None,
                    runMode).ConfigureAwait(false);
                await SetInventoryFailureAsync(runId, message).ConfigureAwait(false);
                return YouPinLandlordRunResult.Failed(runId, message);
            }
            finally
            {
                _runGate.Release();
            }
        }

        private async Task<YouPinLandlordRunResult> RunScopedAsync(
            YouPinLandlordWorkflow workflow,
            YouPinRentalScanScope scope,
            string trigger,
            YouPinLandlordRunMode runMode,
            bool manualExecution,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!await _runGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                string activeRunId = GetActiveRunId();
                await AppendRunRecordAsync(
                    activeRunId,
                    workflow,
                    GetPolicy().PolicyVersion,
                    YouPinLandlordOperationStage.RunSkipped,
                    "合并",
                    "已有包租公检查正在执行，本次请求已合并。",
                    0,
                    CancellationToken.None,
                    runMode).ConfigureAwait(false);
                return YouPinLandlordRunResult.Skip(
                    "已有包租公检查正在执行，本次请求已合并。",
                    activeRunId);
            }

            string runId = Guid.NewGuid().ToString("N");
            YouPinLandlordPolicy runPolicy = GetPolicy();
            var stopwatch = Stopwatch.StartNew();
            try
            {
                if (runMode == YouPinLandlordRunMode.Execute)
                {
                    (YouPinRentalScanScope startedScope, string blockedMessage) = BeginRentalExecution(
                        scope,
                        manualExecution);
                    if (startedScope == YouPinRentalScanScope.None)
                    {
                        string skippedMessage = string.IsNullOrWhiteSpace(blockedMessage)
                            ? "当前租赁自动改价未到执行时间。"
                            : blockedMessage;
                        await AppendRunRecordAsync(
                            runId,
                            workflow,
                            runPolicy.PolicyVersion,
                            YouPinLandlordOperationStage.RunSkipped,
                            "跳过",
                            skippedMessage,
                            stopwatch.ElapsedMilliseconds,
                            CancellationToken.None,
                            runMode).ConfigureAwait(false);
                        return YouPinLandlordRunResult.Skip(skippedMessage, runId);
                    }

                    if (!string.IsNullOrWhiteSpace(blockedMessage))
                    {
                        await AppendRunRecordAsync(
                            runId,
                            workflow,
                            runPolicy.PolicyVersion,
                            YouPinLandlordOperationStage.RunSkipped,
                            "部分跳过",
                            blockedMessage,
                            stopwatch.ElapsedMilliseconds,
                            CancellationToken.None,
                            runMode).ConfigureAwait(false);
                    }

                    scope = startedScope;
                }

                SetRunning(runId);
                await AppendRunRecordAsync(
                    runId,
                    workflow,
                    runPolicy.PolicyVersion,
                    YouPinLandlordOperationStage.RunStarted,
                    "开始",
                    string.IsNullOrWhiteSpace(trigger) ? "开始自动检查" : trigger.Trim(),
                    0,
                    cancellationToken,
                    runMode).ConfigureAwait(false);

                await _gateway.ValidateLoginAsync(_settings, cancellationToken).ConfigureAwait(false);
                YouPinLandlordRemoteSnapshot remote = await _gateway
                    .ReadSnapshotAsync(_settings, scope, runId, cancellationToken)
                    .ConfigureAwait(false);
                var scanSnapshot = new YouPinLandlordScanSnapshot(
                    1,
                    runPolicy.PolicyVersion,
                    runId,
                    _clock.Now,
                    remote.Listings.Select(listing => new YouPinLandlordScanItem(
                        listing.ListingId,
                        listing.AssetId,
                        listing.TemplateId,
                        listing.ItemName,
                        listing.RentalType,
                        listing.ShortRent)).ToArray(),
                    remote.PricingPreference with
                    {
                        LeaseDays = remote.PricingPreference.LeaseDays.ToArray()
                    });
                IReadOnlyDictionary<string, IReadOnlyList<YouPinLandlordMarketListing>> marketByName =
                    await ReadMarketsByNameAsync(
                        remote.Listings
                            .Where(listing => runPolicy
                                .EffectiveFor(listing.RentalType)
                                .Allows(listing.AssetId, listing.ItemName))
                            .Select(listing => (listing.TemplateId, listing.ItemName)),
                        runId,
                        cancellationToken).ConfigureAwait(false);

                var shelf = new List<YouPinLandlordShelfItem>(remote.Listings.Count);
                var plannedActions = new List<YouPinLandlordPlannedAction>(remote.Listings.Count);
                var pricingByGroup = new Dictionary<(string ItemName, YouPinRentalShelfType Type), YouPinLandlordPricingQuote>();
                foreach (YouPinLandlordRemoteListing listing in remote.Listings)
                {
                    YouPinLandlordRentalPolicy rentalPolicy = runPolicy.EffectiveFor(listing.RentalType);
                    if (rentalPolicy.Enabled
                        && !IsRentalTypeEnabled(listing.RentalType))
                        break;

                    string actionId = Guid.NewGuid().ToString("N");
                    if (!rentalPolicy.Allows(listing.AssetId, listing.ItemName))
                    {
                        const string skipReason = "未加入当前租赁自动改价名单";
                        var skippedShelfItem = new YouPinLandlordShelfItem(
                            actionId,
                            listing.ItemName,
                            listing.RentalType,
                            listing.ShortRent,
                            null,
                            rentalPolicy.TargetRank,
                            YouPinLandlordDecisionCode.RankUnknown,
                            skipReason,
                            _clock.Now)
                        {
                            AssetId = listing.AssetId
                        };
                        var skippedAction = new YouPinLandlordPlannedAction(
                            1,
                            runPolicy.PolicyVersion,
                            runId,
                            actionId,
                            workflow,
                            listing.ItemName,
                            listing.RentalType,
                            YouPinLandlordActionKind.ObserveOnly,
                            YouPinLandlordActionState.Skipped,
                            YouPinLandlordDecisionCode.RankUnknown,
                            skipReason);
                        shelf.Add(skippedShelfItem);
                        plannedActions.Add(skippedAction);
                        await _auditStore.AppendAsync(
                            new YouPinLandlordOperationRecord(
                                1,
                                runId,
                                actionId,
                                workflow,
                                YouPinLandlordOperationStage.Decision,
                                _clock.Now,
                                listing.ItemName,
                                listing.RentalType,
                                YouPinLandlordDecisionCode.RankUnknown,
                                "名单跳过",
                                skipReason,
                                stopwatch.ElapsedMilliseconds)
                            {
                                PolicyVersion = runPolicy.PolicyVersion,
                                RunMode = runMode
                            },
                            cancellationToken).ConfigureAwait(false);
                        PublishPlanProgress(
                            runPolicy,
                            runId,
                            shelf,
                            plannedActions,
                            skippedAction);
                        continue;
                    }

                    PublishPlanProgress(
                        runPolicy,
                        runId,
                        shelf,
                        plannedActions,
                        new YouPinLandlordPlannedAction(
                            1,
                            runPolicy.PolicyVersion,
                            runId,
                            actionId,
                            workflow,
                            listing.ItemName,
                            listing.RentalType,
                            YouPinLandlordActionKind.ObserveOnly,
                            YouPinLandlordActionState.Evaluating,
                            YouPinLandlordDecisionCode.RankUnknown,
                            "正在读取市场并判断出租位"));
                    IReadOnlyList<YouPinLandlordMarketListing> market = marketByName[listing.ItemName];
                    int? currentRank = FindRank(market, listing.ListingId);
                    YouPinLandlordDecision decision = EvaluateDecision(
                        new YouPinLandlordDecisionContext(
                            workflow,
                            listing,
                            market,
                            currentRank,
                            rentalPolicy));
                    await _auditStore.AppendAsync(
                        new YouPinLandlordOperationRecord(
                            1,
                            runId,
                            actionId,
                            workflow,
                            YouPinLandlordOperationStage.Decision,
                            _clock.Now,
                            listing.ItemName,
                            listing.RentalType,
                            decision.Code,
                            $"判断:{decision.RuleId}",
                            decision.Message,
                            stopwatch.ElapsedMilliseconds)
                        {
                            PolicyVersion = runPolicy.PolicyVersion,
                            RunMode = runMode
                        },
                        cancellationToken).ConfigureAwait(false);
                    decimal? protectedOwnPrice = FindProtectedOwnPrice(
                        remote.Listings,
                        listing,
                        market,
                        rentalPolicy.TargetRank);
                    YouPinLandlordActionKind actionKind = YouPinLandlordActionKind.ObserveOnly;
                    YouPinLandlordActionState actionState = YouPinLandlordActionState.Observed;
                    decimal? targetShortRent = null;
                    YouPinLandlordPricingQuote? pricingQuote = null;
                    string actionReason = decision.Message;
                    if (rentalPolicy.Enabled && rentalPolicy.Cooldown.Contains(_clock.Now))
                    {
                        actionState = YouPinLandlordActionState.Skipped;
                        actionReason = "当前处于冷却时段，只更新货架观察，不执行定价或写操作";
                    }
                    else if (rentalPolicy.Enabled
                        && protectedOwnPrice.HasValue
                        && protectedOwnPrice.Value != listing.ShortRent)
                    {
                        actionKind = YouPinLandlordActionKind.AlignOwnPrice;
                        actionState = YouPinLandlordActionState.Planned;
                        targetShortRent = protectedOwnPrice.Value;
                        actionReason = $"自有同款已进入目标出租位，对齐受保护租金 {protectedOwnPrice:0.##}";
                    }
                    else if (rentalPolicy.Enabled && !protectedOwnPrice.HasValue)
                    {
                        var groupKey = (listing.ItemName, listing.RentalType);
                        if (!pricingByGroup.TryGetValue(groupKey, out pricingQuote))
                        {
                            pricingQuote = await _gateway.ReadOneClickPricingAsync(
                                _settings,
                                listing,
                                runId,
                                cancellationToken).ConfigureAwait(false);
                            pricingByGroup[groupKey] = pricingQuote;
                        }

                        actionKind = YouPinLandlordActionKind.Reprice;
                        actionState = YouPinLandlordActionState.Planned;
                        bool weeklyFreeMatched = rentalPolicy.WeeklyFree.Matches(listing.ReferencePrice);
                        targetShortRent = weeklyFreeMatched
                            ? Math.Min(
                                pricingQuote.ShortRent,
                                YouPinLandlordWeeklyFreeRule.MaximumAllowedRent)
                            : pricingQuote.ShortRent;
                        actionReason = weeklyFreeMatched
                            ? $"命中周周免租价值区间，短租金限制为严格小于 {YouPinLandlordWeeklyFreeRule.ExclusiveRentLimit:0.00}"
                            : $"尚无自有同款进入目标出租位，采用悠悠一键定价 {pricingQuote.ShortRent:0.##}";
                    }
                    var row = new YouPinLandlordShelfItem(
                        actionId,
                        listing.ItemName,
                        listing.RentalType,
                        listing.ShortRent,
                        currentRank,
                        rentalPolicy.TargetRank,
                        decision.Code,
                        decision.Message,
                        _clock.Now)
                    {
                        AssetId = listing.AssetId
                    };
                    shelf.Add(row);
                    var plannedAction = new YouPinLandlordPlannedAction(
                        1,
                        runPolicy.PolicyVersion,
                        runId,
                        actionId,
                        workflow,
                        listing.ItemName,
                        listing.RentalType,
                        actionKind,
                        actionState,
                        decision.Code,
                        actionReason)
                    {
                        TargetShortRent = targetShortRent,
                        TargetLongRent = listing.LongRent > 0m
                            ? pricingQuote?.LongRent
                            : null,
                        TargetDeposit = pricingQuote?.Deposit,
                        TargetLeaseMaxDays = pricingQuote?.LeaseMaxDays
                    };

                    if (plannedAction.State == YouPinLandlordActionState.Planned)
                    {
                        if (pricingQuote != null)
                        {
                            await AppendActionRecordAsync(
                                runPolicy,
                                plannedAction,
                                YouPinLandlordOperationStage.PricingObtained,
                                "已取得定价",
                                "已读取悠悠一键定价，等待执行",
                                stopwatch.ElapsedMilliseconds,
                                cancellationToken,
                                runMode).ConfigureAwait(false);
                            PublishPlanProgress(
                                runPolicy,
                                runId,
                                shelf,
                                plannedActions,
                                plannedAction with
                                {
                                    State = YouPinLandlordActionState.PricingReady,
                                    Reason = "已取得悠悠一键定价，等待执行"
                                });
                        }
                        PublishPlanProgress(
                            runPolicy,
                            runId,
                            shelf,
                            plannedActions,
                            plannedAction);
                        if (runMode == YouPinLandlordRunMode.ScanOnly)
                        {
                            plannedAction = plannedAction with
                            {
                                Reason = plannedAction.Reason + "；扫描完成，等待执行"
                            };
                        }
                        else
                        {
                            try
                            {
                                plannedAction = await _repriceExecutor.ExecuteAsync(
                                    runPolicy,
                                    listing,
                                    plannedAction,
                                    stopwatch,
                                    progress => PublishPlanProgress(
                                        runPolicy,
                                        runId,
                                        shelf,
                                        plannedActions,
                                        progress),
                                    cancellationToken).ConfigureAwait(false);
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                string failureReason = $"单件改价异常：{ex.GetType().Name}";
                                await AppendActionRecordAsync(
                                    runPolicy,
                                    plannedAction,
                                    YouPinLandlordOperationStage.WriteCompleted,
                                    "失败",
                                    failureReason,
                                    stopwatch.ElapsedMilliseconds,
                                    cancellationToken,
                                    runMode).ConfigureAwait(false);
                                plannedAction = plannedAction with
                                {
                                    State = YouPinLandlordActionState.Failed,
                                    Reason = failureReason
                                };
                            }
                        }
                    }
                    plannedActions.Add(plannedAction);

                }

                stopwatch.Stop();
                int succeededCount = plannedActions.Count(action =>
                    action.State == YouPinLandlordActionState.Succeeded);
                int failedCount = plannedActions.Count(action =>
                    action.State == YouPinLandlordActionState.Failed);
                int skippedCount = plannedActions.Count(action =>
                    action.State == YouPinLandlordActionState.Skipped);
                int plannedCount = plannedActions.Count(action =>
                    action.State == YouPinLandlordActionState.Planned);
                string completionMessage = runMode == YouPinLandlordRunMode.ScanOnly
                    ? $"货架扫描完成，共 {shelf.Count} 件；待执行 {plannedCount}，跳过 {skippedCount}。"
                    : $"改价执行完成，共 {shelf.Count} 件；成功 {succeededCount}，失败 {failedCount}，跳过 {skippedCount}。";
                string status = runMode == YouPinLandlordRunMode.ScanOnly
                    ? "检查完成"
                    : failedCount > 0
                        ? succeededCount > 0
                            ? $"执行部分失败 · 成功 {succeededCount} / 失败 {failedCount}"
                            : $"执行失败 · 成功 0 / 失败 {failedCount}"
                        : $"执行成功 · 成功 {succeededCount} / 失败 0";
                await AppendRunRecordAsync(
                    runId,
                    workflow,
                    runPolicy.PolicyVersion,
                    YouPinLandlordOperationStage.RunCompleted,
                    failedCount > 0 ? "部分失败" : "成功",
                    completionMessage,
                    stopwatch.ElapsedMilliseconds,
                    cancellationToken,
                    runMode).ConfigureAwait(false);
                IReadOnlyList<YouPinLandlordOperationRecord> recentOperations = await _auditStore
                    .ReadRecentAsync(RecentOperationLimit, cancellationToken)
                    .ConfigureAwait(false);

                lock (_stateLock)
                {
                    YouPinLandlordScanItem[] mergedScanItems = (_snapshot.LastScan?.Listings ??
                            Array.Empty<YouPinLandlordScanItem>())
                        .Where(item => !ScopeContains(scope, item.RentalType))
                        .Concat(scanSnapshot.Listings)
                        .ToArray();
                    YouPinLandlordShelfItem[] mergedShelf = _snapshot.Shelf
                        .Where(item => !ScopeContains(scope, item.RentalType))
                        .Concat(shelf)
                        .ToArray();
                    YouPinLandlordPlannedAction[] mergedActions = _snapshot.CurrentPlan.Actions
                        .Where(action => !ScopeContains(scope, action.RentalType))
                        .Concat(plannedActions)
                        .ToArray();
                    _snapshot = new YouPinLandlordSnapshot(
                        1,
                        runPolicy.PolicyVersion,
                        runId,
                        _clock.Now,
                        status,
                        string.Empty,
                        false,
                        mergedShelf,
                        remote.PricingPreference with
                        {
                            LeaseDays = remote.PricingPreference.LeaseDays.ToArray()
                        },
                        recentOperations.ToArray())
                    {
                        LastScan = scanSnapshot with { Listings = mergedScanItems },
                        CurrentPlan = new YouPinLandlordPlan(
                            1,
                            runPolicy.PolicyVersion,
                            runId,
                            _clock.Now,
                            mergedActions),
                        Inventory = _snapshot.Inventory.ToArray(),
                        InventoryLastCheckedAt = _snapshot.InventoryLastCheckedAt,
                        InventoryStatus = _snapshot.InventoryStatus,
                        ZeroCdLastCheckedAt = scope.HasFlag(YouPinRentalScanScope.ZeroCd)
                            ? _clock.Now
                            : _snapshot.ZeroCdLastCheckedAt,
                        InventoryRentalLastCheckedAt = scope.HasFlag(
                            YouPinRentalScanScope.InventoryRental)
                                ? _clock.Now
                                : _snapshot.InventoryRentalLastCheckedAt,
                        ZeroCdExecution = _executionCadence.GetState(
                            YouPinLandlordExecutionLane.ZeroCdReprice),
                        InventoryRentalExecution = _executionCadence.GetState(
                            YouPinLandlordExecutionLane.InventoryRentalReprice),
                        InventoryAutoRentExecution = _executionCadence.GetState(
                            YouPinLandlordExecutionLane.InventoryAutoRent)
                    };
                }

                SnapshotChanged?.Invoke();
                return YouPinLandlordRunResult.Completed(runId, completionMessage, shelf.Count);
            }
            catch (OperationCanceledException)
            {
                await SetFailureAsync(runId, "检查已取消").ConfigureAwait(false);
                return YouPinLandlordRunResult.Skip("检查已取消");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                string message = string.IsNullOrWhiteSpace(ex.Message) ? "自动检查失败。" : ex.Message.Trim();
                await AppendRunRecordAsync(
                    runId,
                    workflow,
                    runPolicy.PolicyVersion,
                    YouPinLandlordOperationStage.RunFailed,
                    "失败",
                    message,
                    stopwatch.ElapsedMilliseconds,
                    CancellationToken.None,
                    runMode).ConfigureAwait(false);
                await SetFailureAsync(runId, message).ConfigureAwait(false);
                return YouPinLandlordRunResult.Failed(runId, message);
            }
            finally
            {
                _runGate.Release();
            }
        }

        private static YouPinLandlordRentalPolicy NormalizeRentalPolicy(
            YouPinLandlordRentalPolicy? policy,
            YouPinLandlordRentalPolicy fallback)
        {
            policy ??= fallback;
            if (policy.ScanIntervalMinutes < 20)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(policy),
                    policy.ScanIntervalMinutes,
                    "包租公扫描间隔不能低于 20 分钟，以保护出租环境。");
            }
            if (policy.ExecutionIntervalMinutes < YouPinLandlordExecutionCadence.MinimumIntervalMinutes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(policy),
                    policy.ExecutionIntervalMinutes,
                    "包租公执行间隔不能低于 20 分钟，以保护出租环境。");
            }
            return policy with
            {
                TargetRank = Math.Clamp(policy.TargetRank, 1, 20),
                ScanIntervalMinutes = Math.Clamp(policy.ScanIntervalMinutes, 20, 1440),
                ExecutionIntervalMinutes = Math.Clamp(
                    policy.ExecutionIntervalMinutes,
                    YouPinLandlordExecutionCadence.MinimumIntervalMinutes,
                    1440),
                WeeklyFree = NormalizeWeeklyFree(policy.WeeklyFree),
                Cooldown = NormalizeCooldown(policy.Cooldown),
                Selection = NormalizeSelectionRule(policy.Selection)
            };
        }

        private static YouPinLandlordSelectionRule NormalizeSelectionRule(
            YouPinLandlordSelectionRule? selection)
        {
            selection ??= YouPinLandlordSelectionRule.AllowAll;
            string[] selectedAssetIds = (selection.SelectedAssetIds ?? Array.Empty<string>())
                .Select(value => value?.Trim() ?? string.Empty)
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            string[] selectedItemNames = (selection.SelectedItemNames ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return selection with
            {
                SelectedAssetIds = selectedAssetIds.Length == 0
                    ? Array.Empty<string>()
                    : selectedAssetIds,
                SelectedItemNames = selectedItemNames.Length == 0
                    ? Array.Empty<string>()
                    : selectedItemNames
            };
        }

        private static YouPinLandlordWeeklyFreeRule NormalizeWeeklyFree(YouPinLandlordWeeklyFreeRule? rule)
        {
            rule ??= YouPinLandlordWeeklyFreeRule.Disabled;
            decimal minimum = Math.Max(0m, rule.MinimumItemValue);
            decimal maximum = Math.Max(minimum, rule.MaximumItemValue);
            return rule with
            {
                MinimumItemValue = minimum,
                MaximumItemValue = maximum
            };
        }

        private static YouPinLandlordCooldownWindow NormalizeCooldown(YouPinLandlordCooldownWindow? cooldown)
        {
            cooldown ??= YouPinLandlordCooldownWindow.Disabled;
            return cooldown with
            {
                StartMinuteOfDay = Math.Clamp(cooldown.StartMinuteOfDay, 0, (24 * 60) - 1),
                EndMinuteOfDay = Math.Clamp(cooldown.EndMinuteOfDay, 0, (24 * 60) - 1)
            };
        }

        private static YouPinLandlordInventoryPolicy NormalizeInventoryPolicy(
            YouPinLandlordInventoryPolicy? policy)
        {
            policy ??= YouPinLandlordInventoryPolicy.Default;
            if (policy.ScanIntervalMinutes < 20)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(policy),
                    policy.ScanIntervalMinutes,
                    "包租公扫描间隔不能低于 20 分钟，以保护出租环境。");
            }
            if (policy.ExecutionIntervalMinutes < YouPinLandlordExecutionCadence.MinimumIntervalMinutes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(policy),
                    policy.ExecutionIntervalMinutes,
                    "包租公执行间隔不能低于 20 分钟，以保护出租环境。");
            }

            return policy with
            {
                ScanIntervalMinutes = Math.Clamp(policy.ScanIntervalMinutes, 20, 1440),
                ExecutionIntervalMinutes = Math.Clamp(
                    policy.ExecutionIntervalMinutes,
                    YouPinLandlordExecutionCadence.MinimumIntervalMinutes,
                    1440),
                SelectedAssetIds = (policy.SelectedAssetIds ?? Array.Empty<string>())
                    .Select(value => value?.Trim() ?? string.Empty)
                    .Where(value => value.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                SelectedItemNames = (policy.SelectedItemNames ?? Array.Empty<string>())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                WeeklyFree = NormalizeWeeklyFree(policy.WeeklyFree),
                Cooldown = NormalizeCooldown(policy.Cooldown)
            };
        }

        private static string BuildInventoryListDecision(
            YouPinLandlordInventoryPolicy policy,
            bool selected,
            bool allowed)
        {
            string scope = FormatInventorySelectionScope(policy.SelectionScope);
            string mode = policy.ListMode == YouPinLandlordInventoryListMode.Whitelist
                ? "白名单"
                : "黑名单";
            return $"{scope}{mode}{(selected ? "已勾选" : "未勾选")}，{(allowed ? "允许自动上架" : "禁止自动上架")}";
        }

        private static string FormatInventorySelectionScope(
            YouPinLandlordSelectionScope selectionScope)
        {
            return selectionScope == YouPinLandlordSelectionScope.SameItemName
                ? "按同款"
                : "逐件选择";
        }

        private static YouPinLandlordSelectionRule BuildSelectionRule(
            bool initialized,
            string? scopeText,
            string? selectedAssetIds,
            string? selectedItemNames)
        {
            return new YouPinLandlordSelectionRule(
                initialized,
                Enum.TryParse(scopeText, true, out YouPinLandlordSelectionScope scope)
                    ? scope
                    : YouPinLandlordSelectionScope.PerAsset,
                ParseSelectedAssetIds(selectedAssetIds),
                ParseSelectedItemNames(selectedItemNames));
        }

        private static IReadOnlyList<string> ParseSelectedAssetIds(string? value)
        {
            return (value ?? string.Empty)
                .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static IReadOnlyList<string> ParseSelectedItemNames(string? value)
        {
            return (value ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private (YouPinRentalScanScope StartedScope, string BlockedMessage) BeginRentalExecution(
            YouPinRentalScanScope requestedScope,
            bool manualExecution)
        {
            YouPinRentalScanScope startedScope = YouPinRentalScanScope.None;
            var blocked = new List<string>(2);
            TryStart(YouPinRentalShelfType.ZeroCd);
            TryStart(YouPinRentalShelfType.InventoryRental);
            return (startedScope, string.Join("；", blocked));

            void TryStart(YouPinRentalShelfType rentalType)
            {
                YouPinRentalScanScope typeScope = ToScope(rentalType);
                if (!requestedScope.HasFlag(typeScope))
                    return;

                YouPinLandlordExecutionLane lane = ToExecutionLane(rentalType);
                YouPinLandlordExecutionStartResult result = _executionCadence.TryStart(
                    lane,
                    _clock.UtcNow,
                    ignoreAutomaticInterval: manualExecution);
                if (result.Started)
                {
                    startedScope |= typeScope;
                    PersistExecutionStart(result.State);
                    return;
                }

                blocked.Add(FormatExecutionStartBlocked(result, FormatRentalType(rentalType)));
            }
        }

        private void PersistExecutionStart(YouPinLandlordExecutionState state)
        {
            long unixMilliseconds = new DateTimeOffset(state.LastStartedAtUtc).ToUnixTimeMilliseconds();
            switch (state.Lane)
            {
                case YouPinLandlordExecutionLane.ZeroCdReprice:
                    _settings.YouPinLandlordZeroCdLastExecutionUnixMilliseconds = unixMilliseconds;
                    break;
                case YouPinLandlordExecutionLane.InventoryRentalReprice:
                    _settings.YouPinLandlordInventoryRentalLastExecutionUnixMilliseconds = unixMilliseconds;
                    break;
                case YouPinLandlordExecutionLane.InventoryAutoRent:
                    _settings.YouPinLandlordInventoryAutoRentLastExecutionUnixMilliseconds = unixMilliseconds;
                    break;
            }

            lock (_stateLock)
            {
                _snapshot = _snapshot with
                {
                    ZeroCdExecution = _executionCadence.GetState(
                        YouPinLandlordExecutionLane.ZeroCdReprice),
                    InventoryRentalExecution = _executionCadence.GetState(
                        YouPinLandlordExecutionLane.InventoryRentalReprice),
                    InventoryAutoRentExecution = _executionCadence.GetState(
                        YouPinLandlordExecutionLane.InventoryAutoRent)
                };
            }

            try
            {
                _persistSettings?.Invoke(_settings);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error(
                    "YouPinLandlord",
                    "Persisting the three-minute execution cooldown failed.",
                    ex);
            }
        }

        private static string FormatExecutionStartBlocked(
            YouPinLandlordExecutionStartResult result,
            string laneName)
        {
            return result.Status switch
            {
                YouPinLandlordExecutionStartStatus.Disabled => $"{laneName}开关已关闭，不能执行",
                YouPinLandlordExecutionStartStatus.NotDue => $"{laneName}尚未到自动执行时间",
                YouPinLandlordExecutionStartStatus.CoolingDown =>
                    $"{laneName}处于三分钟重复执行保护中，请在 {FormatRemaining(result.CooldownRemaining)} 后重试",
                _ => $"{laneName}暂时不能执行"
            };
        }

        private static string FormatRemaining(TimeSpan remaining)
        {
            int seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
            return $"{seconds / 60:00}:{seconds % 60:00}";
        }

        private static YouPinLandlordExecutionLane ToExecutionLane(YouPinRentalShelfType rentalType)
        {
            return rentalType == YouPinRentalShelfType.ZeroCd
                ? YouPinLandlordExecutionLane.ZeroCdReprice
                : YouPinLandlordExecutionLane.InventoryRentalReprice;
        }

        private static string FormatRentalType(YouPinRentalShelfType rentalType)
        {
            return rentalType == YouPinRentalShelfType.ZeroCd ? "0CD改价" : "普通出租改价";
        }

        private static DateTime FromUnixMilliseconds(long value)
        {
            if (value <= 0)
                return DateTime.MinValue;

            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime;
            }
            catch (ArgumentOutOfRangeException)
            {
                return DateTime.MinValue;
            }
        }

        private async Task SetInventoryFailureAsync(string runId, string message)
        {
            IReadOnlyList<YouPinLandlordOperationRecord> recentOperations = await _auditStore
                .ReadRecentAsync(RecentOperationLimit, CancellationToken.None)
                .ConfigureAwait(false);
            lock (_stateLock)
            {
                _snapshot = _snapshot with
                {
                    LastRunId = runId,
                    LastCheckedAt = _clock.Now,
                    Status = "检查失败",
                    LastError = message,
                    IsRunning = false,
                    InventoryLastCheckedAt = _clock.Now,
                    InventoryStatus = "库存扫描失败：" + message,
                    RecentOperations = recentOperations.ToArray()
                };
            }
            SnapshotChanged?.Invoke();
        }

        internal Task<YouPinLandlordRunResult> RunBackgroundCheckAsync(
            YouPinRentalScanScope scope = YouPinRentalScanScope.All,
            CancellationToken cancellationToken = default)
        {
            return RunScopedAsync(
                YouPinLandlordWorkflow.RentalReprice,
                scope,
                "后台定时检查",
                YouPinLandlordRunMode.Execute,
                manualExecution: true,
                cancellationToken);
        }

        private void HandleBackgroundTimer(object? state)
        {
            lock (_lifecycleLock)
            {
                if (_disposed)
                    return;

                _backgroundTask = RunBackgroundTimerAsync();
            }
        }

        private async Task RunBackgroundTimerAsync()
        {
            YouPinRentalScanScope dueExecutionScope = GetDueRentalExecutionScope();
            YouPinRentalScanScope dueScanScope = GetDueBackgroundScope() & ~dueExecutionScope;
            bool inventoryExecutionDue = IsInventoryExecutionDue();
            bool inventoryScanDue = IsInventoryBackgroundDue() && !inventoryExecutionDue;
            if (dueExecutionScope == YouPinRentalScanScope.None
                && dueScanScope == YouPinRentalScanScope.None
                && !inventoryExecutionDue
                && !inventoryScanDue)
            {
                ScheduleNextBackgroundCheck();
                return;
            }

            try
            {
                if (inventoryExecutionDue)
                {
                    YouPinLandlordRunResult inventoryResult = await RunInventoryScanAsync(
                        "后台定时执行库存自动出租",
                        YouPinLandlordRunMode.Execute,
                        manualExecution: false,
                        _lifetimeCancellation.Token).ConfigureAwait(false);
                    if (!inventoryResult.Skipped)
                        AdvanceInventoryBackgroundSchedule(inventoryResult.Success);
                }
                else if (inventoryScanDue)
                {
                    YouPinLandlordRunResult inventoryResult = await RunInventoryScanAsync(
                        "后台定时扫描库存",
                        YouPinLandlordRunMode.ScanOnly,
                        manualExecution: false,
                        _lifetimeCancellation.Token).ConfigureAwait(false);
                    if (!inventoryResult.Skipped)
                        AdvanceInventoryBackgroundSchedule(inventoryResult.Success);
                }

                if (dueExecutionScope != YouPinRentalScanScope.None)
                {
                    YouPinLandlordRunResult result = await RunScopedAsync(
                        YouPinLandlordWorkflow.RentalReprice,
                        dueExecutionScope,
                        "后台定时执行租赁自动改价",
                        YouPinLandlordRunMode.Execute,
                        manualExecution: false,
                        _lifetimeCancellation.Token).ConfigureAwait(false);
                    if (!result.Skipped)
                        AdvanceBackgroundSchedule(dueExecutionScope, result.Success);
                }

                if (dueScanScope != YouPinRentalScanScope.None)
                {
                    YouPinLandlordRunResult result = await RunScopedAsync(
                        YouPinLandlordWorkflow.RentalReprice,
                        dueScanScope,
                        "后台定时扫描租赁货架",
                        YouPinLandlordRunMode.ScanOnly,
                        manualExecution: false,
                        _lifetimeCancellation.Token).ConfigureAwait(false);
                    if (!result.Skipped)
                        AdvanceBackgroundSchedule(dueScanScope, result.Success);
                }
            }
            catch (ObjectDisposedException) when (_disposed)
            {
                return;
            }
            finally
            {
                ScheduleNextBackgroundCheck();
            }
        }

        private void ResetBackgroundSchedule()
        {
            lock (_stateLock)
            {
                DateTime now = _clock.UtcNow;
                _zeroCdFailureStreak = 0;
                _inventoryRentalFailureStreak = 0;
                _inventoryAutoRentFailureStreak = 0;
                YouPinLandlordRentalPolicy zeroCd = _policy.EffectiveFor(YouPinRentalShelfType.ZeroCd);
                YouPinLandlordRentalPolicy inventoryRental = _policy.EffectiveFor(YouPinRentalShelfType.InventoryRental);
                _nextZeroCdCheckUtc = zeroCd.Enabled
                    ? now.AddMinutes(zeroCd.ScanIntervalMinutes)
                    : DateTime.MaxValue;
                _nextInventoryRentalCheckUtc = inventoryRental.Enabled
                    ? now.AddMinutes(inventoryRental.ScanIntervalMinutes)
                    : DateTime.MaxValue;
                _nextInventoryAutoRentCheckUtc = _policy.InventoryAutoRent.Enabled
                    ? now.AddMinutes(_policy.InventoryAutoRent.ScanIntervalMinutes)
                    : DateTime.MaxValue;
                _executionCadence.Configure(
                    YouPinLandlordExecutionLane.ZeroCdReprice,
                    zeroCd.Enabled,
                    zeroCd.ExecutionIntervalMinutes,
                    now,
                    FromUnixMilliseconds(_settings.YouPinLandlordZeroCdLastExecutionUnixMilliseconds));
                _executionCadence.Configure(
                    YouPinLandlordExecutionLane.InventoryRentalReprice,
                    inventoryRental.Enabled,
                    inventoryRental.ExecutionIntervalMinutes,
                    now,
                    FromUnixMilliseconds(
                        _settings.YouPinLandlordInventoryRentalLastExecutionUnixMilliseconds));
                _executionCadence.Configure(
                    YouPinLandlordExecutionLane.InventoryAutoRent,
                    _policy.InventoryAutoRent.Enabled,
                    _policy.InventoryAutoRent.ExecutionIntervalMinutes,
                    now,
                    FromUnixMilliseconds(
                        _settings.YouPinLandlordInventoryAutoRentLastExecutionUnixMilliseconds));
                _snapshot = _snapshot with
                {
                    ZeroCdExecution = _executionCadence.GetState(
                        YouPinLandlordExecutionLane.ZeroCdReprice),
                    InventoryRentalExecution = _executionCadence.GetState(
                        YouPinLandlordExecutionLane.InventoryRentalReprice),
                    InventoryAutoRentExecution = _executionCadence.GetState(
                        YouPinLandlordExecutionLane.InventoryAutoRent)
                };
            }

            ScheduleNextBackgroundCheck();
        }

        private bool IsInventoryBackgroundDue()
        {
            lock (_stateLock)
            {
                return _policy.InventoryAutoRent.Enabled
                    && _nextInventoryAutoRentCheckUtc <= _clock.UtcNow;
            }
        }

        private bool IsInventoryExecutionDue()
        {
            lock (_stateLock)
            {
                return _executionCadence.IsAutomaticDue(
                    YouPinLandlordExecutionLane.InventoryAutoRent,
                    _clock.UtcNow);
            }
        }

        private void AdvanceInventoryBackgroundSchedule(bool succeeded)
        {
            lock (_stateLock)
            {
                _inventoryAutoRentFailureStreak = succeeded ? 0 : _inventoryAutoRentFailureStreak + 1;
                _nextInventoryAutoRentCheckUtc = _policy.InventoryAutoRent.Enabled
                    ? _clock.UtcNow.AddMinutes(ApplyFailureBackoff(
                        _policy.InventoryAutoRent.ScanIntervalMinutes,
                        _inventoryAutoRentFailureStreak))
                    : DateTime.MaxValue;
            }
        }

        private YouPinRentalScanScope GetDueBackgroundScope()
        {
            lock (_stateLock)
            {
                DateTime now = _clock.UtcNow;
                YouPinLandlordRentalPolicy zeroCd = _policy.EffectiveFor(YouPinRentalShelfType.ZeroCd);
                YouPinLandlordRentalPolicy inventoryRental = _policy.EffectiveFor(YouPinRentalShelfType.InventoryRental);
                YouPinRentalScanScope scope = YouPinRentalScanScope.None;
                if (zeroCd.Enabled && _nextZeroCdCheckUtc <= now)
                    scope |= YouPinRentalScanScope.ZeroCd;
                if (inventoryRental.Enabled && _nextInventoryRentalCheckUtc <= now)
                    scope |= YouPinRentalScanScope.InventoryRental;
                return scope;
            }
        }

        private YouPinRentalScanScope GetDueRentalExecutionScope()
        {
            lock (_stateLock)
            {
                DateTime now = _clock.UtcNow;
                YouPinRentalScanScope scope = YouPinRentalScanScope.None;
                if (_executionCadence.IsAutomaticDue(
                    YouPinLandlordExecutionLane.ZeroCdReprice,
                    now))
                {
                    scope |= YouPinRentalScanScope.ZeroCd;
                }
                if (_executionCadence.IsAutomaticDue(
                    YouPinLandlordExecutionLane.InventoryRentalReprice,
                    now))
                {
                    scope |= YouPinRentalScanScope.InventoryRental;
                }
                return scope;
            }
        }

        private void AdvanceBackgroundSchedule(YouPinRentalScanScope completedScope, bool succeeded)
        {
            lock (_stateLock)
            {
                DateTime now = _clock.UtcNow;
                YouPinLandlordRentalPolicy zeroCd = _policy.EffectiveFor(YouPinRentalShelfType.ZeroCd);
                YouPinLandlordRentalPolicy inventoryRental = _policy.EffectiveFor(YouPinRentalShelfType.InventoryRental);
                if (completedScope.HasFlag(YouPinRentalScanScope.ZeroCd))
                {
                    _zeroCdFailureStreak = succeeded ? 0 : _zeroCdFailureStreak + 1;
                    _nextZeroCdCheckUtc = zeroCd.Enabled
                        ? now.AddMinutes(ApplyFailureBackoff(
                            zeroCd.ScanIntervalMinutes,
                            _zeroCdFailureStreak))
                        : DateTime.MaxValue;
                }

                if (completedScope.HasFlag(YouPinRentalScanScope.InventoryRental))
                {
                    _inventoryRentalFailureStreak = succeeded ? 0 : _inventoryRentalFailureStreak + 1;
                    _nextInventoryRentalCheckUtc = inventoryRental.Enabled
                        ? now.AddMinutes(ApplyFailureBackoff(
                            inventoryRental.ScanIntervalMinutes,
                            _inventoryRentalFailureStreak))
                        : DateTime.MaxValue;
                }
            }
        }

        private static int ApplyFailureBackoff(int intervalMinutes, int failureStreak)
        {
            int multiplier = 1 << Math.Min(Math.Max(0, failureStreak), 3);
            return Math.Min(1440, checked(intervalMinutes * multiplier));
        }

        private async Task<IReadOnlyDictionary<string, IReadOnlyList<YouPinLandlordMarketListing>>>
            ReadMarketsByNameAsync(
                IEnumerable<(string TemplateId, string ItemName)> sources,
                string runId,
                CancellationToken cancellationToken)
        {
            (string TemplateId, string ItemName)[] uniqueSources = sources
                .Where(source => !string.IsNullOrWhiteSpace(source.ItemName))
                .GroupBy(source => source.ItemName, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
            if (uniqueSources.Length == 0)
            {
                return new Dictionary<string, IReadOnlyList<YouPinLandlordMarketListing>>(
                    StringComparer.Ordinal);
            }

            using var concurrencyGate = new SemaphoreSlim(MarketReadMaxConcurrency);
            Task<KeyValuePair<string, IReadOnlyList<YouPinLandlordMarketListing>>>[] tasks = uniqueSources
                .Select(ReadOneAsync)
                .ToArray();
            KeyValuePair<string, IReadOnlyList<YouPinLandlordMarketListing>>[] results =
                await Task.WhenAll(tasks).ConfigureAwait(false);
            return results.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

            async Task<KeyValuePair<string, IReadOnlyList<YouPinLandlordMarketListing>>> ReadOneAsync(
                (string TemplateId, string ItemName) source)
            {
                await concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    IReadOnlyList<YouPinLandlordMarketListing> market = await _gateway
                        .ReadMarketAsync(
                            _settings,
                            source.TemplateId,
                            source.ItemName,
                            runId,
                            cancellationToken)
                        .ConfigureAwait(false);
                    return new KeyValuePair<string, IReadOnlyList<YouPinLandlordMarketListing>>(
                        source.ItemName,
                        market);
                }
                finally
                {
                    concurrencyGate.Release();
                }
            }
        }

        private void ScheduleNextBackgroundCheck()
        {
            if (_disposed)
                return;

            DateTime nextCheckUtc;
            lock (_stateLock)
            {
                nextCheckUtc = new[]
                {
                    _nextZeroCdCheckUtc,
                    _nextInventoryRentalCheckUtc,
                    _nextInventoryAutoRentCheckUtc,
                    _executionCadence.GetState(
                        YouPinLandlordExecutionLane.ZeroCdReprice).NextAutomaticAtUtc,
                    _executionCadence.GetState(
                        YouPinLandlordExecutionLane.InventoryRentalReprice).NextAutomaticAtUtc,
                    _executionCadence.GetState(
                        YouPinLandlordExecutionLane.InventoryAutoRent).NextAutomaticAtUtc
                }.Min();
            }

            if (nextCheckUtc == DateTime.MaxValue)
            {
                _backgroundTimer.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }

            try
            {
                TimeSpan due = nextCheckUtc - _clock.UtcNow;
                if (due < TimeSpan.FromSeconds(1))
                    due = TimeSpan.FromSeconds(1);
                _backgroundTimer.Change(due, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException) when (_disposed)
            {
                // Dispose 与定时器回调竞争时无需重新安排。
            }
        }

        private static int? FindRank(
            IReadOnlyList<YouPinLandlordMarketListing> market,
            string listingId)
        {
            for (int index = 0; index < market.Count; index++)
            {
                if (string.Equals(market[index].ListingId, listingId, StringComparison.Ordinal))
                    return index + 1;
            }

            return null;
        }

        private static decimal? FindProtectedOwnPrice(
            IReadOnlyList<YouPinLandlordRemoteListing> allListings,
            YouPinLandlordRemoteListing current,
            IReadOnlyList<YouPinLandlordMarketListing> market,
            int targetRank)
        {
            var ownIds = allListings
                .Where(item => item.RentalType == current.RentalType
                    && string.Equals(item.ItemName, current.ItemName, StringComparison.Ordinal))
                .Select(item => item.ListingId)
                .ToHashSet(StringComparer.Ordinal);
            for (int index = 0; index < market.Count && index < targetRank; index++)
            {
                YouPinLandlordMarketListing row = market[index];
                if (ownIds.Contains(row.ListingId))
                    return row.ShortRent;
            }

            return null;
        }

        private static decimal? FindProtectedOwnPriceForInventory(
            IReadOnlyList<YouPinLandlordRemoteListing> existingListings,
            string itemName,
            IReadOnlyList<YouPinLandlordMarketListing> market)
        {
            var ownIds = existingListings
                .Where(item => item.RentalType == YouPinRentalShelfType.InventoryRental
                    && string.Equals(item.ItemName, itemName, StringComparison.Ordinal))
                .Select(item => item.ListingId)
                .ToHashSet(StringComparer.Ordinal);
            foreach (YouPinLandlordMarketListing row in market)
            {
                if (ownIds.Contains(row.ListingId))
                    return row.ShortRent;
            }
            return null;
        }

        private static YouPinLandlordRemoteListing ToInventoryPricingListing(
            YouPinLandlordRemoteInventoryItem inventory)
        {
            return new YouPinLandlordRemoteListing(
                string.Empty,
                inventory.AssetId,
                inventory.TemplateId,
                inventory.ItemName,
                YouPinRentalShelfType.InventoryRental,
                0m,
                ReferencePrice: inventory.ReferencePrice,
                MarketHashName: inventory.MarketHashName);
        }

        private async Task<HashSet<string>> LoadPendingInventoryHashesAsync(
            CancellationToken cancellationToken)
        {
            IReadOnlyList<YouPinLandlordOperationRecord> records = await _auditStore.QueryAsync(
                new YouPinLandlordAuditQuery(
                    _clock.Now.AddHours(-24),
                    _clock.Now,
                    YouPinLandlordWorkflow.InventoryAutoRent,
                    YouPinRentalShelfType.InventoryRental,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    1000),
                cancellationToken).ConfigureAwait(false);
            return records
                .Where(record => record.ActionId.Length > 0 && record.ResourceKeyHash.Length > 0)
                .GroupBy(record => record.ActionId, StringComparer.Ordinal)
                .Where(group => group.Any(record =>
                        record.Stage == YouPinLandlordOperationStage.WriteCompleted
                        && string.Equals(record.Result, "成功", StringComparison.Ordinal))
                    && !group.Any(record =>
                        record.Stage == YouPinLandlordOperationStage.Recheck
                        && string.Equals(record.Result, "成功", StringComparison.Ordinal)))
                .Select(group => group.First(record => record.ResourceKeyHash.Length > 0).ResourceKeyHash)
                .ToHashSet(StringComparer.Ordinal);
        }

        internal static string HashResourceKey(string value)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
            return Convert.ToHexString(bytes);
        }

        private void PublishInventoryPlanProgress(
            YouPinLandlordPolicy policy,
            string runId,
            IReadOnlyList<YouPinLandlordInventoryItem> inventory,
            IReadOnlyList<YouPinLandlordPlannedAction> actions,
            int currentIndex,
            YouPinLandlordPlannedAction currentAction)
        {
            YouPinLandlordPlannedAction[] inventoryActions = actions
                .Select((action, index) => index == currentIndex ? currentAction : action)
                .ToArray();
            lock (_stateLock)
            {
                _snapshot = _snapshot with
                {
                    PolicyVersion = policy.PolicyVersion,
                    Status = FormatActionProgress(currentAction.State),
                    Inventory = inventory.ToArray(),
                    CurrentPlan = new YouPinLandlordPlan(
                        1,
                        policy.PolicyVersion,
                        runId,
                        _clock.Now,
                        _snapshot.CurrentPlan.Actions
                            .Where(action => action.Workflow != YouPinLandlordWorkflow.InventoryAutoRent)
                            .Concat(inventoryActions)
                            .ToArray())
                };
            }
            SnapshotChanged?.Invoke();
        }

        private Task AppendActionRecordAsync(
            YouPinLandlordPolicy policy,
            YouPinLandlordPlannedAction action,
            YouPinLandlordOperationStage stage,
            string result,
            string message,
            long elapsedMilliseconds,
            CancellationToken cancellationToken,
            YouPinLandlordRunMode runMode)
        {
            return _auditStore.AppendAsync(
                new YouPinLandlordOperationRecord(
                    1,
                    action.RunId,
                    action.ActionId,
                    action.Workflow,
                    stage,
                    _clock.Now,
                    action.ItemName,
                    action.RentalType,
                    action.DecisionCode,
                    result,
                    message,
                    elapsedMilliseconds)
                {
                    PolicyVersion = policy.PolicyVersion,
                    ResourceKeyHash = action.ResourceKeyHash,
                    RunMode = runMode
                },
                cancellationToken);
        }

        private void PublishPlanProgress(
            YouPinLandlordPolicy policy,
            string runId,
            IReadOnlyList<YouPinLandlordShelfItem> shelf,
            IReadOnlyList<YouPinLandlordPlannedAction> completedActions,
            YouPinLandlordPlannedAction currentAction)
        {
            lock (_stateLock)
            {
                YouPinLandlordPlannedAction[] actions = _snapshot.CurrentPlan.Actions
                    .Where(action => action.RentalType != currentAction.RentalType)
                    .Concat(completedActions)
                    .Append(currentAction)
                    .GroupBy(action => action.ActionId, StringComparer.Ordinal)
                    .Select(group => group.Last())
                    .ToArray();
                YouPinLandlordShelfItem[] mergedShelf = _snapshot.Shelf
                    .Where(item => item.RentalType != currentAction.RentalType)
                    .Concat(shelf)
                    .GroupBy(item => item.ActionId, StringComparer.Ordinal)
                    .Select(group => group.Last())
                    .ToArray();
                _snapshot = _snapshot with
                {
                    PolicyVersion = policy.PolicyVersion,
                    Status = FormatActionProgress(currentAction.State),
                    Shelf = mergedShelf,
                    CurrentPlan = new YouPinLandlordPlan(
                        1,
                        policy.PolicyVersion,
                        runId,
                        _clock.Now,
                        actions)
                };
            }
            SnapshotChanged?.Invoke();
        }

        private static string FormatActionProgress(YouPinLandlordActionState state)
        {
            return state switch
            {
                YouPinLandlordActionState.Evaluating => "等待判断",
                YouPinLandlordActionState.PricingReady => "已取得定价",
                YouPinLandlordActionState.Planned => "等待执行",
                YouPinLandlordActionState.Executing => "正在改价",
                YouPinLandlordActionState.AwaitingSynchronization => "等待平台同步",
                YouPinLandlordActionState.Rechecking => "正在回查",
                _ => "正在处理"
            };
        }

        private static string FormatInventoryPricingFailure(Exception exception)
        {
            return exception is InvalidOperationException
                && exception.Message.Contains("未返回有效短租金", StringComparison.Ordinal)
                    ? "悠悠一键定价未返回有效短租金，本件已跳过"
                    : $"读取悠悠一键定价异常（{exception.GetType().Name}），本件已跳过";
        }

        private bool IsRentalTypeEnabled(YouPinRentalShelfType rentalType)
        {
            lock (_stateLock)
            {
                return _policy.EffectiveFor(rentalType).Enabled;
            }
        }

        private static YouPinRentalScanScope ToScope(YouPinRentalShelfType rentalType)
        {
            return rentalType == YouPinRentalShelfType.ZeroCd
                ? YouPinRentalScanScope.ZeroCd
                : YouPinRentalScanScope.InventoryRental;
        }

        private static bool ScopeContains(
            YouPinRentalScanScope scope,
            YouPinRentalShelfType rentalType)
        {
            return scope.HasFlag(ToScope(rentalType));
        }

        private YouPinLandlordDecision EvaluateDecision(YouPinLandlordDecisionContext context)
        {
            foreach (IYouPinLandlordDecisionRule rule in _decisionRules)
            {
                YouPinLandlordDecision? decision = rule.Evaluate(context);
                if (decision != null)
                    return decision;
            }

            return new YouPinLandlordDecision(
                YouPinLandlordDecisionCode.RankUnknown,
                "没有规则处理当前货架记录",
                "fallback");
        }

        private async Task AppendRunRecordAsync(
            string runId,
            YouPinLandlordWorkflow workflow,
            int policyVersion,
            YouPinLandlordOperationStage stage,
            string result,
            string message,
            long elapsedMilliseconds,
            CancellationToken cancellationToken,
            YouPinLandlordRunMode runMode = YouPinLandlordRunMode.Execute)
        {
            await _auditStore.AppendAsync(
                new YouPinLandlordOperationRecord(
                    1,
                    runId,
                    string.Empty,
                    workflow,
                    stage,
                    _clock.Now,
                    string.Empty,
                    null,
                    null,
                    result,
                    message,
                    elapsedMilliseconds)
                {
                    PolicyVersion = policyVersion,
                    RunMode = runMode
                },
                cancellationToken).ConfigureAwait(false);
        }

        private void SetRunning(string runId)
        {
            lock (_stateLock)
            {
                _snapshot = _snapshot with
                {
                    LastRunId = runId,
                    Status = "检查中",
                    LastError = string.Empty,
                    IsRunning = true
                };
            }

            SnapshotChanged?.Invoke();
        }

        private string GetActiveRunId()
        {
            lock (_stateLock)
            {
                return _snapshot.LastRunId;
            }
        }

        private async Task SetFailureAsync(string runId, string message)
        {
            IReadOnlyList<YouPinLandlordOperationRecord> recentOperations = await _auditStore
                .ReadRecentAsync(RecentOperationLimit, CancellationToken.None)
                .ConfigureAwait(false);
            lock (_stateLock)
            {
                _snapshot = _snapshot with
                {
                    LastRunId = runId,
                    LastCheckedAt = _clock.Now,
                    Status = "检查失败",
                    LastError = message,
                    IsRunning = false,
                    RecentOperations = recentOperations.ToArray()
                };
            }

            SnapshotChanged?.Invoke();
        }

        public void Dispose()
        {
            Task backgroundTask;
            lock (_lifecycleLock)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _backgroundTimer.Dispose();
                _lifetimeCancellation.Cancel();
                backgroundTask = _backgroundTask;
            }

            try
            {
                backgroundTask.GetAwaiter().GetResult();
            }
            catch (ObjectDisposedException)
            {
                // 退出过程中后台请求观察到释放状态，已安全结束。
            }

            _lifetimeCancellation.Dispose();
            _writeCoordinator.Dispose();
        }
    }
}
