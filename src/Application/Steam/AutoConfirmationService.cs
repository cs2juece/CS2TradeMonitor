using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.Steam;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.SystemServices;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CS2TradeMonitor.Application.Steam
{
    public sealed class AutoConfirmationService : IDisposable
    {
        private const int MaxRecordCount = 100;
        private const string RecordSnapshotFileName = "steam_auto_trade_records.json";
        private static readonly TimeSpan RecoverableConfirmationAge = TimeSpan.FromHours(24);
        private static readonly TimeSpan[] RecoveryRetryDelays =
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
        };

        private readonly object _sync = new();
        private readonly ISteamOfferService _offerService;
        private readonly IYouPinSaleReminderService _youPinSaleReminders;
        private readonly Action? _stateChanged;
        private readonly string _recordSnapshotPath;
        private readonly List<SteamAutoTradeRecord> _records = new();
        private readonly Dictionary<string, SteamAutoTradeRecordType> _countedRecordTypesByKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _completedRecordKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _skipRecordKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _confirmationExecutionGate = new(1, 1);
        private readonly SemaphoreSlim _pollCycleGate = new(1, 1);
        private PeriodicTimer? _timer;
        private CancellationTokenSource? _cts;
        private SteamAutoTradeSettings _settings = SteamAutoTradeSettings.ReadOnly(300);
        private bool _lastLoggedAutoTradeEnabled;
        private int _lastLoggedAutoTradeIntervalSeconds;
        private DateTime _lastAutoTradeStartLogTime;
        private bool _disposed;
        private DateTime _today = DateTime.Today;
        internal Func<TimeSpan, CancellationToken, Task> RecoveryDelayAsync { get; set; } = Task.Delay;

        public bool IsRunning { get; private set; }
        public DateTime LastCheckTime { get; private set; }
        public DateTime LastProcessTime { get; private set; }
        public DateTime NextCheckTime { get; private set; }
        public int TotalAccepted { get; private set; }
        public int TodaySuccess { get; private set; }
        public int TodayFailure { get; private set; }
        public string LastStatus { get; private set; } = "";
        public string LastFailureReason { get; private set; } = "";
        public int IntervalSeconds => _settings.IntervalSeconds;
        public bool AutoAcceptSafe => _settings.AcceptPureIncomingEnabled;
        public bool AllowYouPinVerifiedAccept => _settings.AcceptYouPinPurchaseEnabled;

        public AutoConfirmationService(
            ISteamOfferService offerService,
            IYouPinSaleReminderService youPinSaleReminders,
            Action? stateChanged = null,
            string? recordSnapshotPath = null)
        {
            _offerService = offerService ?? throw new ArgumentNullException(nameof(offerService));
            _youPinSaleReminders = youPinSaleReminders ?? throw new ArgumentNullException(nameof(youPinSaleReminders));
            _stateChanged = stateChanged;
            _recordSnapshotPath = string.IsNullOrWhiteSpace(recordSnapshotPath)
                ? RuntimeDataPaths.GetDataFilePath(RecordSnapshotFileName)
                : recordSnapshotPath;
            _youPinSaleReminders.NewWaitDeliverOrdersDetected += OnNewWaitDeliverOrdersDetected;
            LoadRecordSnapshot();
        }

        public void Start(int intervalSeconds, bool autoAcceptSafe, bool allowYouPinVerifiedAccept)
        {
            StartAutoTrade(new SteamAutoTradeSettings
            {
                Enabled = autoAcceptSafe,
                AcceptPureIncomingEnabled = autoAcceptSafe,
                AcceptYouPinPurchaseEnabled = autoAcceptSafe && allowYouPinVerifiedAccept,
                SendYouPinSaleEnabled = false,
                SendYouPinRentalEnabled = false,
                IntervalSeconds = intervalSeconds
            });
        }

        public void StartAutoTrade(SteamAutoTradeSettings settings)
        {
            ThrowIfDisposed();
            Stop();

            settings ??= SteamAutoTradeSettings.ReadOnly(300);
            settings.IntervalSeconds = Math.Clamp(settings.IntervalSeconds <= 0 ? 300 : settings.IntervalSeconds, 30, 3600);

            var cts = new CancellationTokenSource();
            var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.IntervalSeconds));
            bool shouldLogStart;

            lock (_sync)
            {
                _settings = CloneSettings(settings);
                _cts = cts;
                _timer = timer;
                IsRunning = true;
                LastStatus = settings.Enabled ? "自动处理交易已启动。" : "Steam 报价后台读取已启动。";
                NextCheckTime = DateTime.Now;
                _ = Task.Run(() => RunLoopAsync(timer, cts));
                shouldLogStart = ShouldLogAutoTradeStartNoLock(settings);
            }

            if (shouldLogStart)
                SteamOfferAuditLog.LogAutoTradeStarted(settings.Enabled, settings.IntervalSeconds);
            NotifyStateChanged();
        }

        public void Stop()
        {
            CancellationTokenSource? cts;
            PeriodicTimer? timer;

            lock (_sync)
            {
                cts = _cts;
                timer = _timer;
                _cts = null;
                _timer = null;
                IsRunning = false;
                if (!_disposed && cts != null)
                    LastStatus = "Steam 报价后台轮询已停止。";
            }

            try
            {
                cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already stopped.
            }

            timer?.Dispose();
            NotifyStateChanged();
        }

        public SteamAutoTradeState GetAutoTradeState()
        {
            lock (_sync)
            {
                return new SteamAutoTradeState
                {
                    IsRunning = IsRunning,
                    ProcessingEnabled = _settings.Enabled,
                    LastCheckTime = LastCheckTime,
                    LastProcessTime = LastProcessTime,
                    NextCheckTime = NextCheckTime,
                    TodaySuccess = TodaySuccess,
                    TodayFailure = TodayFailure,
                    StatusText = BuildStatusTextNoLock(),
                    LastStatus = LastStatus,
                    LastFailureReason = LastFailureReason,
                    IntervalSeconds = _settings.IntervalSeconds,
                    RecentRecords = _records
                        .OrderByDescending(x => x.Time)
                        .Take(5)
                        .Select(CloneRecord)
                        .ToList()
                };
            }
        }

        public void Record(SteamAutoTradeRecord record)
        {
            AddRecord(record, countResult: false);
        }

        private async Task RunLoopAsync(PeriodicTimer timer, CancellationTokenSource cts)
        {
            var ct = cts.Token;
            try
            {
                await RecoverPersistedMobileConfirmationsAsync(ct).ConfigureAwait(false);
                await PollCycleAsync(ct).ConfigureAwait(false);

                while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                {
                    await RecoverPersistedMobileConfirmationsOnceAsync(ct, normalCycle: true).ConfigureAwait(false);
                    await PollCycleAsync(ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Normal stop path.
            }
            catch (Exception ex)
            {
                SetFailure("Steam 报价后台轮询异常：" + SteamOfferAuditLog.RedactSecrets(ex.Message));
                SteamOfferAuditLog.Error("Steam auto trade service loop failed", ex);
            }
            finally
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_cts, cts))
                    {
                        _cts = null;
                        _timer = null;
                        IsRunning = false;
                    }
                }

                timer.Dispose();
                cts.Dispose();
                NotifyStateChanged();
            }
        }

        private async Task PollCycleAsync(CancellationToken ct)
        {
            await _pollCycleGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await PollCycleCoreAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _pollCycleGate.Release();
            }
        }

        private async Task PollCycleCoreAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ResetDailyCountersIfNeeded();

            SteamAutoTradeSettings settings;
            lock (_sync)
            {
                settings = CloneSettings(_settings);
                NextCheckTime = DateTime.Now.AddSeconds(settings.IntervalSeconds);
            }

            try
            {
                SteamOfferActionResult loadResult = await _offerService.LoadOffersForAutoTradeAsync().ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

                lock (_sync)
                {
                    LastCheckTime = DateTime.Now;
                    LastStatus = loadResult.Message;
                    if (!IsLatestCountedRecordFailureNoLock())
                        LastFailureReason = "";
                }

                if (!loadResult.Ok)
                {
                    SetFailure(loadResult.Message);
                    return;
                }

                if (!settings.Enabled)
                {
                    lock (_sync)
                    {
                        LastFailureReason = "";
                        LastStatus = "后台读取完成，自动处理交易已关闭。";
                    }
                    NotifyStateChanged();
                    return;
                }

                await ProcessLoadedOffersAsync(settings, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                SetFailure("轮询失败：" + SteamOfferAuditLog.RedactSecrets(ex.Message));
                SteamOfferAuditLog.Error("Steam auto trade poll cycle failed", ex);
            }
        }

        internal async Task ProcessLoadedOffersNowAsync(CancellationToken ct = default)
        {
            await _pollCycleGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                SteamAutoTradeSettings settings;
                lock (_sync)
                    settings = CloneSettings(_settings);
                if (!settings.Enabled)
                    return;

                await ProcessLoadedOffersAsync(settings, ct).ConfigureAwait(false);
            }
            finally
            {
                _pollCycleGate.Release();
            }
        }

        private async Task ProcessLoadedOffersAsync(SteamAutoTradeSettings settings, CancellationToken ct)
        {
            SteamOfferState state = _offerService.GetState();
            YouPinSaleReminderState youPinState = _youPinSaleReminders.GetState();
            IReadOnlyList<SteamAutoTradePlanItem> offerPlans = SteamAutoTradePlanner.BuildOfferPlans(state.Offers, youPinState, settings);
            IReadOnlyList<SteamAutoTradePlanItem> sendPlans = SteamAutoTradePlanner.BuildYouPinSendPlans(youPinState, settings);

            foreach (SteamAutoTradePlanItem plan in offerPlans)
            {
                ct.ThrowIfCancellationRequested();
                await ExecuteOfferPlanAsync(plan).ConfigureAwait(false);
            }

            foreach (SteamAutoTradePlanItem plan in sendPlans)
            {
                ct.ThrowIfCancellationRequested();
                if (HasRecoverablePersistedTradeOffer(plan))
                    continue;
                await ExecuteSendPlanAsync(plan, ct).ConfigureAwait(false);
            }

            lock (_sync)
            {
                LastProcessTime = DateTime.Now;
                if (string.IsNullOrWhiteSpace(LastFailureReason))
                    LastStatus = "自动处理检查完成。";
            }
            NotifyStateChanged();
        }

        private async Task ExecuteOfferPlanAsync(SteamAutoTradePlanItem plan)
        {
            if (!plan.Allowed)
            {
                RecordSkipOnce(plan);
                return;
            }

            if (plan.Action != SteamAutoTradeAction.AcceptOffer)
                return;

            SteamOfferActionResult result = await _offerService.AcceptAutoTradeOfferAsync(plan).ConfigureAwait(false);
            AddRecord(new SteamAutoTradeRecord
            {
                Type = result.Ok ? SteamAutoTradeRecordType.AutoAccept : SteamAutoTradeRecordType.Failed,
                Direction = plan.Direction,
                ItemNames = plan.ItemNames,
                Source = SteamAutoTradePlanner.FormatCategory(plan.Category),
                Result = result.Ok ? "成功" : "失败",
                Reason = result.Message,
                TradeOfferId = plan.TradeOfferId,
                OrderNo = plan.MatchedOrderNo
            }, countResult: true);
        }

        internal async Task ExecuteSendPlanAsync(SteamAutoTradePlanItem plan, CancellationToken ct = default)
        {
            if (!plan.Allowed)
                return;

            if (IsPlanCompleted(plan))
                return;

            if (plan.Action != SteamAutoTradeAction.ConfirmYouPinOffer)
                RestorePersistedTradeOfferId(plan);

            if (plan.Action == SteamAutoTradeAction.ConfirmYouPinOffer)
            {
                await ExecuteYouPinOfferConfirmationAsync(plan, ct).ConfigureAwait(false);
                return;
            }

            if (plan.Action == SteamAutoTradeAction.AcceptOffer)
            {
                await ExecuteKnownYouPinSteamOfferAsync(plan, ct).ConfigureAwait(false);
                return;
            }

            if (plan.Action == SteamAutoTradeAction.ConfirmMobile
                && string.IsNullOrWhiteSpace(plan.TradeOfferId))
            {
                YouPinSaleActionResult queryResult = await _youPinSaleReminders
                    .QueryTradeOfferIdAsync(plan.MatchedOrderNo)
                    .ConfigureAwait(false);
                if (queryResult.Ok && !string.IsNullOrWhiteSpace(queryResult.TradeOfferId))
                {
                    plan.TradeOfferId = queryResult.TradeOfferId.Trim();
                    await RecordExistingMobileConfirmationStateAsync(plan, ct).ConfigureAwait(false);
                    return;
                }

                AddPendingRecord(
                    plan,
                    SteamAutoTradePendingStage.TradeOfferSync,
                    FirstText(
                        queryResult.Message,
                        "租赁报价已生成，正在等待 Steam 报价号同步。"),
                    "等待 Steam 同步");
                return;
            }

            if (!string.IsNullOrWhiteSpace(plan.TradeOfferId))
            {
                await RecordExistingMobileConfirmationStateAsync(plan, ct).ConfigureAwait(false);
                return;
            }

            if (HasRecentSendAwaitingTradeOfferId(plan))
            {
                YouPinSaleActionResult queryResult = await _youPinSaleReminders.QueryTradeOfferIdAsync(plan.MatchedOrderNo).ConfigureAwait(false);
                if (queryResult.Ok && !string.IsNullOrWhiteSpace(queryResult.TradeOfferId))
                {
                    plan.TradeOfferId = queryResult.TradeOfferId.Trim();
                    await RecordExistingMobileConfirmationStateAsync(plan, ct).ConfigureAwait(false);
                    return;
                }

                AddPendingRecord(
                    plan,
                    SteamAutoTradePendingStage.TradeOfferSync,
                    FirstText(
                        queryResult.Message,
                        "Steam 报价已发送，等待平台同步报价号。"),
                    "等待 Steam 同步");
                return;
            }

            YouPinSaleActionResult sendResult = await _youPinSaleReminders.SendOfferAsync(plan.MatchedOrderNo, SteamOfferAuditLog.TriggerBackgroundAuto).ConfigureAwait(false);
            string tradeOfferId = string.IsNullOrWhiteSpace(sendResult.TradeOfferId) ? plan.TradeOfferId : sendResult.TradeOfferId;

            if (!sendResult.Ok && IsAlreadySentOrWaitingConfirmation(sendResult.Message))
            {
                YouPinSaleActionResult queryResult = await _youPinSaleReminders.QueryTradeOfferIdAsync(plan.MatchedOrderNo).ConfigureAwait(false);
                if (queryResult.Ok && !string.IsNullOrWhiteSpace(queryResult.TradeOfferId))
                {
                    plan.TradeOfferId = queryResult.TradeOfferId.Trim();
                    await RecordExistingMobileConfirmationStateAsync(plan, ct).ConfigureAwait(false);
                    return;
                }

                AddPendingRecord(
                    plan,
                    SteamAutoTradePendingStage.TradeOfferSync,
                    FirstText(
                        queryResult.Message,
                        "Steam 报价可能已生成，等待平台同步报价号。请稍后刷新或打开 Steam 手机确认。"));
                return;
            }

            AddRecord(new SteamAutoTradeRecord
            {
                Type = sendResult.Ok ? SteamAutoTradeRecordType.AutoSend : SteamAutoTradeRecordType.Failed,
                Direction = plan.Direction,
                ItemNames = plan.ItemNames,
                Source = SteamAutoTradePlanner.FormatCategory(plan.Category),
                Result = sendResult.Ok ? "已发送，等待确认" : "失败",
                Reason = sendResult.Message,
                TradeOfferId = tradeOfferId,
                OrderNo = plan.MatchedOrderNo
            }, countResult: true);

            if (!sendResult.Ok || string.IsNullOrWhiteSpace(tradeOfferId))
                return;

            plan.TradeOfferId = tradeOfferId;
            await RecordExistingMobileConfirmationStateAsync(plan, ct).ConfigureAwait(false);
        }

        private async Task ExecuteKnownYouPinSteamOfferAsync(
            SteamAutoTradePlanItem plan,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(plan.TradeOfferId))
            {
                YouPinSaleActionResult queryResult = await _youPinSaleReminders
                    .QueryTradeOfferIdAsync(plan.MatchedOrderNo)
                    .ConfigureAwait(false);
                if (!queryResult.Ok || string.IsNullOrWhiteSpace(queryResult.TradeOfferId))
                {
                    AddPendingRecord(
                        plan,
                        SteamAutoTradePendingStage.TradeOfferSync,
                        FirstText(queryResult.Message, "租赁报价已生成，正在等待 Steam 报价号同步。"),
                        "等待 Steam 同步");
                    return;
                }

                plan.TradeOfferId = queryResult.TradeOfferId.Trim();
            }

            SteamTradeOfferStatusResult status = await _offerService
                .QueryTradeOfferStatusAsync(plan.TradeOfferId)
                .ConfigureAwait(false);
            if (status.Kind == SteamTradeOfferStatusKind.Accepted)
            {
                AddConfirmationOutcomeRecord(
                    plan,
                    SteamAutoTradeRecordType.AutoAccept,
                    "成功",
                    status.Message,
                    countResult: true);
                return;
            }

            if (status.Kind == SteamTradeOfferStatusKind.Failed)
            {
                AddConfirmationOutcomeRecord(
                    plan,
                    SteamAutoTradeRecordType.TerminalFailure,
                    "失败",
                    status.Message,
                    countResult: true);
                return;
            }

            if (status.Kind == SteamTradeOfferStatusKind.NeedsMobileConfirmation)
            {
                await RecordExistingMobileConfirmationStateAsync(plan, ct).ConfigureAwait(false);
                return;
            }

            if (status.Kind is SteamTradeOfferStatusKind.NotFound or SteamTradeOfferStatusKind.QueryFailed)
            {
                AddPendingRecord(
                    plan,
                    SteamAutoTradePendingStage.TradeOfferSync,
                    FirstText(status.Message, "Steam 尚未同步该租赁报价，稍后自动重试。"),
                    "等待 Steam 同步");
                return;
            }

            SteamOfferActionResult loadResult = await _offerService.LoadOffersForAutoTradeAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (!loadResult.Ok)
            {
                AddPendingRecord(
                    plan,
                    SteamAutoTradePendingStage.TradeOfferSync,
                    FirstText(loadResult.Message, "暂时无法刷新 Steam 报价，稍后自动重试。"),
                    "等待 Steam 同步");
                return;
            }

            SteamOfferActionResult acceptResult = await _offerService
                .AcceptAutoTradeOfferAsync(plan)
                .ConfigureAwait(false);
            if (acceptResult.Ok)
            {
                AddConfirmationOutcomeRecord(
                    plan,
                    SteamAutoTradeRecordType.AutoAccept,
                    "成功",
                    acceptResult.Message,
                    countResult: true);
                return;
            }

            if (string.Equals(acceptResult.Code, "need_login", StringComparison.OrdinalIgnoreCase))
            {
                AddConfirmationOutcomeRecord(
                    plan,
                    SteamAutoTradeRecordType.Failed,
                    "失败",
                    acceptResult.Message,
                    countResult: true);
                return;
            }

            AddPendingRecord(
                plan,
                SteamAutoTradePendingStage.TradeOfferSync,
                FirstText(acceptResult.Message, "Steam 报价当前不可接收，稍后将按真实状态重试。"),
                "等待 Steam 处理");
        }

        private async Task ExecuteYouPinOfferConfirmationAsync(SteamAutoTradePlanItem plan, CancellationToken ct)
        {
            YouPinSaleActionResult result = await _youPinSaleReminders.ConfirmOfferAsync(
                plan.MatchedOrderNo,
                plan.TradeOfferId,
                SteamOfferAuditLog.TriggerBackgroundAuto).ConfigureAwait(false);
            string tradeOfferId = FirstText(result.TradeOfferId, plan.TradeOfferId);
            AddRecord(new SteamAutoTradeRecord
            {
                Type = result.Ok ? SteamAutoTradeRecordType.AutoYouPinConfirm : SteamAutoTradeRecordType.Failed,
                Direction = plan.Direction,
                ItemNames = plan.ItemNames,
                Source = SteamAutoTradePlanner.FormatCategory(plan.Category),
                Result = result.Ok ? "已确认报价，等待 Steam 确认" : "失败",
                Reason = result.Message,
                TradeOfferId = tradeOfferId,
                OrderNo = plan.MatchedOrderNo
            }, countResult: true);

            if (!result.Ok || string.IsNullOrWhiteSpace(tradeOfferId))
                return;

            plan.TradeOfferId = tradeOfferId;
            await RecordExistingMobileConfirmationStateAsync(plan, ct).ConfigureAwait(false);
        }

        private void OnNewWaitDeliverOrdersDetected(IReadOnlyList<YouPinSaleOrder> orders)
        {
            if (orders == null || orders.Count == 0)
                return;

            CancellationToken ct;
            lock (_sync)
            {
                if (_disposed || !IsRunning || !_settings.Enabled || _cts == null)
                    return;
                ct = _cts.Token;
            }

            _ = Task.Run(() => RunImmediateLoadedOrdersAsync(ct), CancellationToken.None);
        }

        private async Task RunImmediateLoadedOrdersAsync(CancellationToken ct)
        {
            try
            {
                await ProcessLoadedOffersNowAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Normal stop or restart path.
            }
            catch (Exception ex)
            {
                SetFailure("悠悠新订单即时处理异常：" + SteamOfferAuditLog.RedactSecrets(ex.Message));
                SteamOfferAuditLog.Error("Immediate YouPin loaded-order processing failed", ex);
            }
        }

        internal async Task HandleManuallySentYouPinOfferAsync(
            YouPinSaleOrder order,
            YouPinSaleActionResult sendResult,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(order);
            ArgumentNullException.ThrowIfNull(sendResult);
            if (!sendResult.Ok
                || string.IsNullOrWhiteSpace(order.OrderNo)
                || string.IsNullOrWhiteSpace(sendResult.TradeOfferId))
                return;

            SteamAutoTradeSettings settings;
            lock (_sync)
                settings = CloneSettings(_settings);

            SteamAutoTradePlanItem plan = SteamAutoTradePlanner.BuildManuallySentYouPinPlan(
                order,
                sendResult.TradeOfferId,
                settings);
            if (!plan.Allowed)
                return;

            AddRecord(new SteamAutoTradeRecord
            {
                Type = SteamAutoTradeRecordType.ManualSend,
                Direction = plan.Direction,
                ItemNames = plan.ItemNames,
                Source = SteamAutoTradePlanner.FormatCategory(plan.Category),
                Result = "已手动发送，等待确认",
                Reason = sendResult.Message,
                TradeOfferId = plan.TradeOfferId,
                OrderNo = plan.MatchedOrderNo
            }, countResult: true);
            await RecordExistingMobileConfirmationStateAsync(plan, ct).ConfigureAwait(false);
        }

        private async Task<SteamOfferActionResult> RecordExistingMobileConfirmationStateAsync(
            SteamAutoTradePlanItem plan,
            CancellationToken ct = default)
        {
            await _confirmationExecutionGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                bool requiresYouPinOrderReadback = plan.Category == SteamAutoTradeCategory.YouPinRental
                    && !string.IsNullOrWhiteSpace(plan.MatchedOrderNo);
                bool confirmationAttemptCompleted = false;
                bool confirmationSubmitted = false;
                for (int attempt = 0; ; attempt++)
                {
                    lock (_sync)
                    {
                        if (!IsConfirmationEnabledForPlanNoLock(plan))
                            return SteamOfferActionResult.Failed("对应的悠悠自动发送规则已关闭，已停止自动手机确认。", "disabled");
                    }

                    if (IsPlanCompleted(plan))
                        return SteamOfferActionResult.Success("该 Steam 报价已经完成确认。");

                    bool finalCheck = attempt >= RecoveryRetryDelays.Length;
                    if (requiresYouPinOrderReadback && (confirmationSubmitted || finalCheck))
                    {
                        SteamOfferActionResult? youPinResolution = await TryResolveRentalOrderFromYouPinAsync(
                            plan,
                            finalCheck,
                            confirmationSubmitted).ConfigureAwait(false);
                        if (youPinResolution != null)
                            return youPinResolution;
                    }

                    SteamTradeOfferStatusResult status = await _offerService.QueryTradeOfferStatusAsync(plan.TradeOfferId).ConfigureAwait(false);
                    if (status.Kind == SteamTradeOfferStatusKind.Accepted)
                    {
                        if (requiresYouPinOrderReadback)
                        {
                            confirmationAttemptCompleted = true;
                            confirmationSubmitted = true;
                        }
                        else
                        {
                            AddConfirmationOutcomeRecord(plan, SteamAutoTradeRecordType.AutoMobileConfirm, "成功", status.Message, countResult: true);
                            return SteamOfferActionResult.Success(status.Message);
                        }
                    }

                    if (status.Kind == SteamTradeOfferStatusKind.Failed)
                    {
                        if (!requiresYouPinOrderReadback)
                        {
                            AddConfirmationOutcomeRecord(plan, SteamAutoTradeRecordType.TerminalFailure, "失败", status.Message, countResult: true);
                            return SteamOfferActionResult.Failed(status.Message, "terminal_offer_state");
                        }

                        confirmationAttemptCompleted = true;
                    }

                    bool steamStateUnavailable = status.Kind is SteamTradeOfferStatusKind.NotFound or SteamTradeOfferStatusKind.QueryFailed;
                    if (!requiresYouPinOrderReadback
                        && steamStateUnavailable
                        && (attempt == 0 || finalCheck))
                    {
                        SteamOfferActionResult? youPinResolution = await TryResolveFromYouPinAsync(
                            plan,
                            finalCheck).ConfigureAwait(false);
                        if (youPinResolution != null)
                            return youPinResolution;
                    }

                    if ((status.Kind == SteamTradeOfferStatusKind.NeedsMobileConfirmation || steamStateUnavailable)
                        && !confirmationAttemptCompleted)
                    {
                        SteamOfferActionResult confirm = await _offerService.ConfirmMatchedMobileTradeAsync(plan).ConfigureAwait(false);
                        if (confirm.Ok)
                        {
                            confirmationAttemptCompleted = true;
                            confirmationSubmitted = true;
                            if (!requiresYouPinOrderReadback)
                                continue;
                        }
                        else if (confirm.Code != "not_found")
                        {
                            confirmationAttemptCompleted = true;
                            continue;
                        }
                    }

                    if (finalCheck)
                    {
                        string unresolved = requiresYouPinOrderReadback
                            ? "悠悠订单回读在 30 秒内未返回明确终态，自动确认已停止。"
                            : "Steam 与悠悠在 30 秒内均未返回明确终态，自动确认已停止。";
                        AddConfirmationOutcomeRecord(plan, SteamAutoTradeRecordType.Unresolved, "需人工核对", unresolved, countResult: false);
                        return SteamOfferActionResult.Failed(unresolved, "offer_state_unresolved");
                    }

                    TimeSpan delay = RecoveryRetryDelays[attempt];
                    AddPendingRecord(
                        plan,
                        SteamAutoTradePendingStage.MobileConfirmation,
                        requiresYouPinOrderReadback
                            ? $"正在回读悠悠订单真实状态，{delay.TotalSeconds:0} 秒后复查。"
                            : $"正在查询 Steam 报价真实状态，{delay.TotalSeconds:0} 秒后复查。",
                        "正在核验");
                    await RecoveryDelayAsync(delay, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                _confirmationExecutionGate.Release();
            }
        }

        private async Task<SteamOfferActionResult?> TryResolveRentalOrderFromYouPinAsync(
            SteamAutoTradePlanItem plan,
            bool finalCheck,
            bool allowSuccess)
        {
            YouPinSaleReminderCheckResult refresh = await _youPinSaleReminders
                .CheckQuoteNowAsync(SteamOfferAuditLog.TriggerBackgroundAuto)
                .ConfigureAwait(false);
            if (!refresh.Ok)
                return null;

            YouPinSaleOrder? order = _youPinSaleReminders
                .GetState()
                .RecentWaitDeliverOrders
                .FirstOrDefault(candidate => MatchesOrderNo(candidate, plan.MatchedOrderNo));
            if (order == null)
            {
                if (!allowSuccess)
                    return null;

                const string success = "悠悠订单回读确认：该租赁订单已离开待处理列表。";
                AddConfirmationOutcomeRecord(plan, SteamAutoTradeRecordType.AutoMobileConfirm, "成功", success, countResult: true);
                return SteamOfferActionResult.Success(success);
            }

            string tradeOfferId = FirstText(order.TradeOfferId, order.OfferId);
            if (!string.IsNullOrWhiteSpace(tradeOfferId)
                && !string.Equals(tradeOfferId.Trim(), plan.TradeOfferId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                const string mismatch = "悠悠订单回读的 Steam 报价号与当前自动确认目标不一致，已停止自动处理。";
                AddConfirmationOutcomeRecord(plan, SteamAutoTradeRecordType.Unresolved, "需人工核对", mismatch, countResult: false);
                return SteamOfferActionResult.Failed(mismatch, "trade_offer_id_mismatch");
            }

            string message = string.Join(" / ", new[] { order.OrderStatusDesc, order.Message }
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Distinct(StringComparer.Ordinal));
            if (LooksLikeYouPinOfferFailed(message))
            {
                string failure = FirstText(message, "悠悠订单回读确认该租赁报价已失败或取消。");
                AddConfirmationOutcomeRecord(plan, SteamAutoTradeRecordType.TerminalFailure, "失败", failure, countResult: true);
                return SteamOfferActionResult.Failed(failure, "youpin_terminal_order_state");
            }

            if (allowSuccess && LooksLikeYouPinRentalCompleted(message))
            {
                string success = FirstText(message, "悠悠订单回读确认该租赁报价已完成。");
                AddConfirmationOutcomeRecord(plan, SteamAutoTradeRecordType.AutoMobileConfirm, "成功", success, countResult: true);
                return SteamOfferActionResult.Success(success);
            }

            if (finalCheck && LooksLikeYouPinWaitingForOurConfirmation(message))
            {
                const string failure = "Steam 手机确认未在 30 秒内完成，悠悠订单仍在等待我方令牌确认。";
                AddConfirmationOutcomeRecord(plan, SteamAutoTradeRecordType.TerminalFailure, "失败", failure, countResult: true);
                return SteamOfferActionResult.Failed(failure, "mobile_confirmation_timeout");
            }

            return null;
        }

        private static bool MatchesOrderNo(YouPinSaleOrder order, string orderNo)
        {
            return string.Equals(order.OrderNo?.Trim(), orderNo.Trim(), StringComparison.OrdinalIgnoreCase)
                || (order.OrderNos?.Any(candidate => string.Equals(
                    candidate?.Trim(),
                    orderNo.Trim(),
                    StringComparison.OrdinalIgnoreCase)) ?? false);
        }

        private async Task<SteamOfferActionResult?> TryResolveFromYouPinAsync(
            SteamAutoTradePlanItem plan,
            bool finalCheck)
        {
            if (string.IsNullOrWhiteSpace(plan.MatchedOrderNo))
                return null;

            YouPinSaleActionResult result = await _youPinSaleReminders
                .QueryOfferStatusAsync(plan.MatchedOrderNo, SteamOfferAuditLog.TriggerBackgroundAuto)
                .ConfigureAwait(false);
            if (!result.Ok)
                return null;

            if (!string.IsNullOrWhiteSpace(result.TradeOfferId)
                && !string.Equals(result.TradeOfferId.Trim(), plan.TradeOfferId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                const string mismatch = "悠悠返回的 Steam 报价号与当前自动确认目标不一致，已停止自动处理。";
                AddConfirmationOutcomeRecord(plan, SteamAutoTradeRecordType.Unresolved, "需人工核对", mismatch, countResult: false);
                return SteamOfferActionResult.Failed(mismatch, "trade_offer_id_mismatch");
            }

            string message = result.Message ?? string.Empty;
            if (LooksLikeYouPinConfirmationSucceeded(message))
            {
                string success = FirstText(message, "悠悠已确认该 Steam 报价。等待交易对方接收。");
                AddConfirmationOutcomeRecord(plan, SteamAutoTradeRecordType.AutoMobileConfirm, "成功", success, countResult: true);
                return SteamOfferActionResult.Success(success);
            }

            if (LooksLikeYouPinOfferFailed(message))
            {
                string failure = FirstText(message, "悠悠确认该报价已失败或取消。");
                AddConfirmationOutcomeRecord(plan, SteamAutoTradeRecordType.TerminalFailure, "失败", failure, countResult: true);
                return SteamOfferActionResult.Failed(failure, "youpin_terminal_offer_state");
            }

            if (finalCheck && LooksLikeYouPinWaitingForOurConfirmation(message))
            {
                const string failure = "Steam 手机确认未在 30 秒内完成，悠悠订单仍在等待我方令牌确认。";
                AddConfirmationOutcomeRecord(plan, SteamAutoTradeRecordType.TerminalFailure, "失败", failure, countResult: true);
                return SteamOfferActionResult.Failed(failure, "mobile_confirmation_timeout");
            }

            return null;
        }

        private static bool LooksLikeYouPinConfirmationSucceeded(string message)
        {
            string text = message ?? string.Empty;
            return text.Contains("待对方确认", StringComparison.Ordinal)
                || text.Contains("等待对方确认", StringComparison.Ordinal)
                || text.Contains("确认报价成功", StringComparison.Ordinal)
                || text.Contains("报价确认成功", StringComparison.Ordinal)
                || text.Contains("已完成", StringComparison.Ordinal);
        }

        private static bool LooksLikeYouPinRentalCompleted(string message)
        {
            string text = message ?? string.Empty;
            return LooksLikeYouPinConfirmationSucceeded(text)
                || text.Contains("已发货", StringComparison.Ordinal)
                || text.Contains("转交成功", StringComparison.Ordinal)
                || text.Contains("出租成功", StringComparison.Ordinal);
        }

        private static bool LooksLikeYouPinOfferFailed(string message)
        {
            string text = message ?? string.Empty;
            return text.Contains("报价发送失败", StringComparison.Ordinal)
                || text.Contains("确认报价失败", StringComparison.Ordinal)
                || text.Contains("已取消", StringComparison.Ordinal)
                || text.Contains("已拒绝", StringComparison.Ordinal);
        }

        private static bool LooksLikeYouPinWaitingForOurConfirmation(string message)
        {
            string text = message ?? string.Empty;
            return text.Contains("待您确认", StringComparison.Ordinal)
                || text.Contains("等待您回应", StringComparison.Ordinal)
                || text.Contains("等待Steam令牌确认", StringComparison.Ordinal)
                || text.Contains("待您令牌验证", StringComparison.Ordinal);
        }

        private void AddConfirmationOutcomeRecord(
            SteamAutoTradePlanItem plan,
            SteamAutoTradeRecordType type,
            string result,
            string reason,
            bool countResult)
        {
            AddRecord(new SteamAutoTradeRecord
            {
                Type = type,
                Direction = plan.Direction,
                ItemNames = plan.ItemNames,
                Source = SteamAutoTradePlanner.FormatCategory(plan.Category),
                Result = result,
                Reason = reason,
                TradeOfferId = plan.TradeOfferId,
                OrderNo = plan.MatchedOrderNo
            }, countResult);
        }

        private async Task RecoverPersistedMobileConfirmationsAsync(CancellationToken ct)
        {
            for (int attempt = 0; ; attempt++)
            {
                bool hasRemaining = await RecoverPersistedMobileConfirmationsOnceAsync(ct, normalCycle: false).ConfigureAwait(false);
                if (!hasRemaining)
                    return;
                if (attempt >= RecoveryRetryDelays.Length)
                {
                    foreach (SteamAutoTradePlanItem plan in BuildRecoverableConfirmationPlans())
                        AddPendingRecord(
                            plan,
                            SteamAutoTradePendingStage.MobileConfirmation,
                            "Steam 确认项尚未同步，稍后按正常检查周期继续重试。",
                            "等待 Steam 同步");
                    return;
                }

                TimeSpan delay = RecoveryRetryDelays[attempt];
                foreach (SteamAutoTradePlanItem plan in BuildRecoverableConfirmationPlans())
                    AddPendingRecord(
                        plan,
                        SteamAutoTradePendingStage.MobileConfirmation,
                        $"Steam 确认项尚未同步，{delay.TotalSeconds:0} 秒后重试。",
                        "等待 Steam 同步");
                lock (_sync)
                {
                    LastStatus = $"等待 Steam 同步确认项，{delay.TotalSeconds:0} 秒后重试。";
                    NextCheckTime = DateTime.Now + delay;
                }
                NotifyStateChanged();
                await RecoveryDelayAsync(delay, ct).ConfigureAwait(false);
            }
        }

        internal async Task<bool> RecoverPersistedMobileConfirmationsOnceAsync(CancellationToken ct, bool normalCycle)
        {
            IReadOnlyList<SteamAutoTradePlanItem> plans = BuildRecoverableConfirmationPlans();
            if (plans.Count == 0)
                return false;

            foreach (SteamAutoTradePlanItem plan in plans)
            {
                ct.ThrowIfCancellationRequested();
                AddPendingRecord(
                    plan,
                    SteamAutoTradePendingStage.MobileConfirmation,
                    normalCycle ? "正在按正常检查周期重试 Steam 手机确认。" : "正在恢复未完成的 Steam 手机确认。",
                    "正在自动确认");
                await RecordExistingMobileConfirmationStateAsync(plan, ct).ConfigureAwait(false);
            }

            IReadOnlyList<SteamAutoTradePlanItem> remaining = BuildRecoverableConfirmationPlans();
            if (normalCycle)
            {
                foreach (SteamAutoTradePlanItem plan in remaining)
                    AddPendingRecord(
                        plan,
                        SteamAutoTradePendingStage.MobileConfirmation,
                        "Steam 确认项尚未同步，等待下次正常检查。",
                        "等待 Steam 同步");
            }
            return remaining.Count > 0;
        }

        private IReadOnlyList<SteamAutoTradePlanItem> BuildRecoverableConfirmationPlans()
        {
            DateTime cutoff = DateTime.Now - RecoverableConfirmationAge;
            lock (_sync)
            {
                if (!_settings.Enabled)
                    return Array.Empty<SteamAutoTradePlanItem>();

                return _records
                    .Where(record => record.CreatedTime >= cutoff
                        && IsRecoverableConfirmationRecord(record)
                        && !string.IsNullOrWhiteSpace(record.TradeOfferId)
                        && IsRecoveryEnabledForRecord(record, _settings))
                    .Select(BuildConfirmationPlan)
                    .ToList();
            }
        }

        private static bool IsRecoveryEnabledForRecord(SteamAutoTradeRecord record, SteamAutoTradeSettings settings)
        {
            return record.Source.Contains("出租", StringComparison.Ordinal)
                ? settings.SendYouPinRentalEnabled
                : settings.SendYouPinSaleEnabled;
        }

        private bool IsConfirmationEnabledForPlanNoLock(SteamAutoTradePlanItem plan)
        {
            if (!IsRunning)
                return plan.Allowed;
            if (!_settings.Enabled)
                return false;

            return plan.Category == SteamAutoTradeCategory.YouPinRental
                ? _settings.SendYouPinRentalEnabled
                : plan.Category == SteamAutoTradeCategory.YouPinSale && _settings.SendYouPinSaleEnabled;
        }

        private void RestorePersistedTradeOfferId(SteamAutoTradePlanItem plan)
        {
            if (!string.IsNullOrWhiteSpace(plan.TradeOfferId))
                return;

            string orderNo = NormalizeRecordKeyPart(plan.MatchedOrderNo);
            if (string.IsNullOrWhiteSpace(orderNo))
                return;

            lock (_sync)
            {
                DateTime cutoff = DateTime.Now - RecoverableConfirmationAge;
                SteamAutoTradeRecord? pending = _records.FirstOrDefault(record =>
                    record.CreatedTime >= cutoff
                    && IsTrackedTradeOfferRecordType(record.Type)
                    && string.Equals(NormalizeRecordKeyPart(record.OrderNo), orderNo, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(record.TradeOfferId));
                if (pending != null)
                    plan.TradeOfferId = pending.TradeOfferId;
            }
        }

        private bool HasRecoverablePersistedTradeOffer(SteamAutoTradePlanItem plan)
        {
            string orderNo = NormalizeRecordKeyPart(plan.MatchedOrderNo);
            if (string.IsNullOrWhiteSpace(orderNo))
                return false;

            DateTime cutoff = DateTime.Now - RecoverableConfirmationAge;
            lock (_sync)
            {
                return _records.Any(record =>
                    record.CreatedTime >= cutoff
                    && IsRecoverableConfirmationRecord(record)
                    && string.Equals(NormalizeRecordKeyPart(record.OrderNo), orderNo, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(record.TradeOfferId));
            }
        }

        private bool HasRecentSendAwaitingTradeOfferId(SteamAutoTradePlanItem plan)
        {
            string orderNo = NormalizeRecordKeyPart(plan.MatchedOrderNo);
            if (string.IsNullOrWhiteSpace(orderNo))
                return false;

            DateTime cutoff = DateTime.Now - RecoverableConfirmationAge;
            lock (_sync)
            {
                return _records.Any(record =>
                    record.CreatedTime >= cutoff
                    && IsTrackedTradeOfferRecordType(record.Type)
                    && string.Equals(NormalizeRecordKeyPart(record.OrderNo), orderNo, StringComparison.Ordinal)
                    && string.IsNullOrWhiteSpace(record.TradeOfferId));
            }
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

        private void AddPendingRecord(
            SteamAutoTradePlanItem plan,
            SteamAutoTradePendingStage pendingStage,
            string reason,
            string result = "待确认")
        {
            AddRecord(new SteamAutoTradeRecord
            {
                Type = SteamAutoTradeRecordType.Pending,
                Direction = plan.Direction,
                ItemNames = plan.ItemNames,
                Source = SteamAutoTradePlanner.FormatCategory(plan.Category),
                Result = result,
                Reason = reason,
                TradeOfferId = plan.TradeOfferId,
                OrderNo = plan.MatchedOrderNo,
                PendingStage = pendingStage
            }, countResult: false);
        }

        private void RecordSkipOnce(SteamAutoTradePlanItem plan)
        {
            if (IsPlanCompleted(plan))
                return;

            string key = BuildPrimaryTransactionKey(plan.MatchedOrderNo, plan.TradeOfferId);
            if (string.IsNullOrWhiteSpace(key))
                return;

            lock (_sync)
            {
                if (!_skipRecordKeys.Add(key))
                    return;
            }

            AddRecord(new SteamAutoTradeRecord
            {
                Type = SteamAutoTradeRecordType.Skip,
                Direction = plan.Direction,
                ItemNames = plan.ItemNames,
                Source = SteamAutoTradePlanner.FormatCategory(plan.Category),
                Result = plan.SkipReason,
                Reason = plan.SkipReason,
                TradeOfferId = plan.TradeOfferId,
                OrderNo = plan.MatchedOrderNo
            }, countResult: false);
        }

        internal void AddRecord(SteamAutoTradeRecord record, bool countResult)
        {
            record ??= new SteamAutoTradeRecord();
            record.Time = record.Time == default ? DateTime.Now : record.Time;
            record.Reason = SteamOfferAuditLog.RedactSecrets(record.Reason);
            record.Result = SteamOfferAuditLog.RedactSecrets(record.Result);
            string recordKey = BuildRecordDedupeKey(record);
            bool countable = countResult && IsCountedRecordType(record.Type);
            List<SteamAutoTradeRecord> snapshot;

            lock (_sync)
            {
                ResetDailyCountersIfNeededNoLock();
                if (!string.IsNullOrWhiteSpace(recordKey))
                {
                    SteamAutoTradeRecord? existing = _records.FirstOrDefault(existing => string.Equals(BuildRecordDedupeKey(existing), recordKey, StringComparison.OrdinalIgnoreCase));
                    if (record.CreatedTime == default)
                    {
                        record.CreatedTime = existing != null && existing.CreatedTime != default
                            ? existing.CreatedTime
                            : existing?.Time ?? record.Time;
                    }
                    if (existing != null && ShouldKeepExistingRecord(existing, record))
                        return;

                    _records.RemoveAll(existing => string.Equals(BuildRecordDedupeKey(existing), recordKey, StringComparison.OrdinalIgnoreCase));
                    if (_countedRecordTypesByKey.Remove(recordKey, out SteamAutoTradeRecordType oldType))
                        ApplyCounterDeltaNoLock(oldType, -1);
                }

                if (record.CreatedTime == default)
                    record.CreatedTime = record.Time;

                _records.Insert(0, CloneRecord(record));
                if (_records.Count > MaxRecordCount)
                    _records.RemoveRange(MaxRecordCount, _records.Count - MaxRecordCount);

                if (IsTerminalRecordType(record.Type))
                    AddCompletedRecordKeysNoLock(record);

                if (countable)
                {
                    if (!string.IsNullOrWhiteSpace(recordKey))
                        _countedRecordTypesByKey[recordKey] = record.Type;
                    ApplyCounterDeltaNoLock(record.Type, 1);
                    if (record.Type == SteamAutoTradeRecordType.AutoAccept)
                        TotalAccepted++;
                    LastFailureReason = record.Type is SteamAutoTradeRecordType.Failed or SteamAutoTradeRecordType.TerminalFailure
                        ? FirstText(record.Reason, record.Result)
                        : "";
                }
                else if (!string.IsNullOrWhiteSpace(recordKey) && !HasCountedFailuresNoLock())
                {
                    LastFailureReason = "";
                }

                LastStatus = FirstText(record.Reason, record.Result, LastStatus);
                snapshot = _records.Select(CloneRecord).ToList();
            }

            SaveRecordSnapshot(snapshot);
            NotifyStateChanged();
        }

        private void SetFailure(string message)
        {
            string clean = SteamOfferAuditLog.RedactSecrets(message);
            lock (_sync)
            {
                ResetDailyCountersIfNeededNoLock();
                LastCheckTime = DateTime.Now;
                LastFailureReason = clean;
                LastStatus = clean;
            }

            SteamOfferAuditLog.LogAutoTradeFailure(clean);
            NotifyStateChanged();
        }

        private string BuildStatusTextNoLock()
        {
            bool hasActiveFailure = !string.IsNullOrWhiteSpace(LastFailureReason)
                && (string.Equals(LastStatus, LastFailureReason, StringComparison.Ordinal)
                    || IsLatestCountedRecordFailureNoLock());
            if (hasActiveFailure
                && !_settings.Enabled
                && (LooksLikeConfigurationRequired(LastFailureReason)
                    || LastFailureReason.Contains("未登录", StringComparison.Ordinal)))
            {
                return "待配置";
            }
            if (hasActiveFailure && LooksLikeConfigurationRequired(LastFailureReason))
                return "待配置";
            if (hasActiveFailure && LooksLikeLoginExpired(LastFailureReason))
                return "需要登录";
            if (hasActiveFailure)
                return "最近失败";
            if (!_settings.Enabled)
                return "已关闭";
            return IsRunning ? "运行中" : "已关闭";
        }

        private void ResetDailyCountersIfNeeded()
        {
            lock (_sync)
            {
                ResetDailyCountersIfNeededNoLock();
            }
        }

        private void ResetDailyCountersIfNeededNoLock()
        {
            DateTime today = DateTime.Today;
            if (today == _today)
                return;

            _today = today;
            RebuildDailyCountersNoLock();
            _skipRecordKeys.Clear();
            _completedRecordKeys.Clear();
            foreach (SteamAutoTradeRecord record in _records.Where(record => IsTerminalRecordType(record.Type)))
                AddCompletedRecordKeysNoLock(record);
        }

        private bool ShouldLogAutoTradeStartNoLock(SteamAutoTradeSettings settings)
        {
            DateTime now = DateTime.Now;
            bool changed = _lastAutoTradeStartLogTime == default
                || _lastLoggedAutoTradeEnabled != settings.Enabled
                || _lastLoggedAutoTradeIntervalSeconds != settings.IntervalSeconds;
            if (!changed && now - _lastAutoTradeStartLogTime < TimeSpan.FromMinutes(10))
                return false;

            _lastAutoTradeStartLogTime = now;
            _lastLoggedAutoTradeEnabled = settings.Enabled;
            _lastLoggedAutoTradeIntervalSeconds = settings.IntervalSeconds;
            return true;
        }

        private void NotifyStateChanged()
        {
            try
            {
                _stateChanged?.Invoke();
            }
            catch
            {
                // UI refresh subscribers must not break the background loop.
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _youPinSaleReminders.NewWaitDeliverOrdersDetected -= OnNewWaitDeliverOrdersDetected;
            Stop();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AutoConfirmationService));
        }

        private static SteamAutoTradeSettings CloneSettings(SteamAutoTradeSettings settings)
        {
            return new SteamAutoTradeSettings
            {
                Enabled = settings.Enabled,
                AcceptPureIncomingEnabled = settings.AcceptPureIncomingEnabled,
                AcceptYouPinPurchaseEnabled = settings.AcceptYouPinPurchaseEnabled,
                SendYouPinSaleEnabled = settings.SendYouPinSaleEnabled,
                SendYouPinRentalEnabled = settings.SendYouPinRentalEnabled,
                IntervalSeconds = Math.Clamp(settings.IntervalSeconds <= 0 ? 300 : settings.IntervalSeconds, 30, 3600)
            };
        }

        private static SteamAutoTradeRecord CloneRecord(SteamAutoTradeRecord record)
        {
            return new SteamAutoTradeRecord
            {
                Time = record.Time,
                CreatedTime = record.CreatedTime,
                Type = record.Type,
                Direction = record.Direction,
                ItemNames = record.ItemNames.ToList(),
                Source = record.Source,
                Result = record.Result,
                Reason = record.Reason,
                TradeOfferId = record.TradeOfferId,
                OrderNo = record.OrderNo,
                PendingStage = record.PendingStage
            };
        }

        private void LoadRecordSnapshot()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_recordSnapshotPath) || !File.Exists(_recordSnapshotPath))
                    return;

                string json = File.ReadAllText(_recordSnapshotPath);
                var snapshot = JsonSerializer.Deserialize<AutoTradeRecordSnapshot>(json, ServiceInfra.DefaultJsonOptions);
                IEnumerable<SteamAutoTradeRecord> loaded = snapshot?.Records is { } records
                    ? records
                    : Array.Empty<SteamAutoTradeRecord>();

                lock (_sync)
                {
                    _records.Clear();
                    _completedRecordKeys.Clear();
                    foreach (SteamAutoTradeRecord record in NormalizeAndDedupeRecords(loaded))
                    {
                        _records.Add(record);
                        if (IsTerminalRecordType(record.Type))
                            AddCompletedRecordKeysNoLock(record);
                    }

                    RebuildDailyCountersNoLock();
                }
            }
            catch (Exception ex)
            {
                SteamOfferAuditLog.DiagnosticError("Loading Steam auto trade record snapshot failed.", ex);
            }
        }

        private void SaveRecordSnapshot(IReadOnlyList<SteamAutoTradeRecord> records)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_recordSnapshotPath))
                    return;

                var snapshot = new AutoTradeRecordSnapshot
                {
                    Records = records
                        .OrderByDescending(x => x.Time)
                        .Take(MaxRecordCount)
                        .Select(CloneRecord)
                        .ToList()
                };
                string json = JsonSerializer.Serialize(snapshot, ServiceInfra.DefaultJsonOptions);
                RuntimeDataPaths.WriteTextAtomic(_recordSnapshotPath, json);
            }
            catch (Exception ex)
            {
                SteamOfferAuditLog.DiagnosticError("Saving Steam auto trade record snapshot failed.", ex);
            }
        }

        private void RebuildDailyCountersNoLock()
        {
            _countedRecordTypesByKey.Clear();
            TodaySuccess = 0;
            TodayFailure = 0;
            LastFailureReason = "";

            foreach (SteamAutoTradeRecord record in _records.OrderBy(x => x.Time))
            {
                if (record.Time.Date != _today || !IsCountedRecordType(record.Type))
                    continue;

                string key = BuildRecordDedupeKey(record);
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                _countedRecordTypesByKey[key] = record.Type;
            }

            foreach (SteamAutoTradeRecordType type in _countedRecordTypesByKey.Values)
                ApplyCounterDeltaNoLock(type, 1);

            SteamAutoTradeRecord? latestCounted = _records
                .Where(record => record.Time.Date == _today && IsCountedRecordType(record.Type))
                .OrderByDescending(record => record.Time)
                .FirstOrDefault();
            if (latestCounted?.Type == SteamAutoTradeRecordType.Failed)
                LastFailureReason = FirstText(latestCounted.Reason, latestCounted.Result);
        }

        private static List<SteamAutoTradeRecord> NormalizeAndDedupeRecords(IEnumerable<SteamAutoTradeRecord> records)
        {
            var result = new List<SteamAutoTradeRecord>();
            var indexByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (SteamAutoTradeRecord record in records
                .Where(record => record != null)
                .Select(NormalizeRecord)
                .OrderByDescending(record => record.Time))
            {
                string key = BuildRecordDedupeKey(record);
                if (!string.IsNullOrWhiteSpace(key) && indexByKey.TryGetValue(key, out int existingIndex))
                {
                    SteamAutoTradeRecord existing = result[existingIndex];
                    if (ShouldKeepExistingRecord(record, existing))
                        result[existingIndex] = record;

                    continue;
                }

                result.Add(record);
                if (!string.IsNullOrWhiteSpace(key))
                    indexByKey[key] = result.Count - 1;
                if (result.Count >= MaxRecordCount)
                    break;
            }

            return result;
        }

        private static SteamAutoTradeRecord NormalizeRecord(SteamAutoTradeRecord record)
        {
            var clone = CloneRecord(record);
            clone.Time = clone.Time == default ? DateTime.Now : clone.Time;
            clone.CreatedTime = clone.CreatedTime == default ? clone.Time : clone.CreatedTime;
            clone.Reason = SteamOfferAuditLog.RedactSecrets(clone.Reason);
            clone.Result = SteamOfferAuditLog.RedactSecrets(clone.Result);
            clone.Source = clone.Source?.Trim() ?? "";
            clone.TradeOfferId = clone.TradeOfferId?.Trim() ?? "";
            clone.OrderNo = clone.OrderNo?.Trim() ?? "";
            clone.ItemNames = clone.ItemNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .ToList();
            if (ShouldRecoverLegacySteamNotFoundRecord(clone))
            {
                clone.Type = SteamAutoTradeRecordType.Pending;
                clone.Result = "等待 Steam 同步";
                clone.Reason = "升级后重新核验 Steam 与悠悠报价状态。";
            }
            if (clone.Type == SteamAutoTradeRecordType.Pending
                && clone.PendingStage == SteamAutoTradePendingStage.Unknown)
            {
                clone.PendingStage = clone.Source.Contains("出租", StringComparison.Ordinal)
                    ? SteamAutoTradePendingStage.TradeOfferSync
                    : SteamAutoTradePendingStage.MobileConfirmation;
            }
            return clone;
        }

        private static bool ShouldRecoverLegacySteamNotFoundRecord(SteamAutoTradeRecord record)
        {
            return record.Type == SteamAutoTradeRecordType.Unresolved
                && record.CreatedTime >= DateTime.Now - RecoverableConfirmationAge
                && !string.IsNullOrWhiteSpace(record.TradeOfferId)
                && !string.IsNullOrWhiteSpace(record.OrderNo)
                && (record.Reason.Contains("Steam 未返回该报价", StringComparison.Ordinal)
                    || record.Reason.Contains("Steam 与悠悠在 30 秒内均未返回明确终态", StringComparison.Ordinal));
        }

        private static string BuildRecordDedupeKey(SteamAutoTradeRecord record)
        {
            return BuildPrimaryTransactionKey(record.OrderNo, record.TradeOfferId);
        }

        private static string BuildPrimaryTransactionKey(string? orderNo, string? tradeOfferId)
        {
            return BuildTransactionKeys(orderNo, tradeOfferId).FirstOrDefault() ?? "";
        }

        private static IEnumerable<string> BuildTransactionKeys(string? orderNo, string? tradeOfferId)
        {
            string normalizedOrderNo = NormalizeRecordKeyPart(orderNo);
            if (!string.IsNullOrWhiteSpace(normalizedOrderNo))
                yield return "order:" + normalizedOrderNo;

            string normalizedTradeOfferId = NormalizeRecordKeyPart(tradeOfferId);
            if (!string.IsNullOrWhiteSpace(normalizedTradeOfferId))
                yield return "offer:" + normalizedTradeOfferId;
        }

        private static string NormalizeRecordKeyPart(string? value)
        {
            return (value ?? "").Trim().ToUpperInvariant();
        }

        private static bool IsCountedRecordType(SteamAutoTradeRecordType type)
        {
            return type is SteamAutoTradeRecordType.Failed or SteamAutoTradeRecordType.TerminalFailure || IsSuccessRecordType(type);
        }

        private static bool IsSuccessRecordType(SteamAutoTradeRecordType type)
        {
            return type is SteamAutoTradeRecordType.AutoAccept
                or SteamAutoTradeRecordType.AutoSend
                or SteamAutoTradeRecordType.AutoYouPinConfirm
                or SteamAutoTradeRecordType.AutoMobileConfirm
                or SteamAutoTradeRecordType.ManualAccept
                or SteamAutoTradeRecordType.ManualSend
                or SteamAutoTradeRecordType.ManualMobileConfirm;
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

        private static bool IsTerminalSuccessRecordType(SteamAutoTradeRecordType type)
        {
            return type is SteamAutoTradeRecordType.AutoAccept
                or SteamAutoTradeRecordType.AutoMobileConfirm
                or SteamAutoTradeRecordType.ManualAccept
                or SteamAutoTradeRecordType.ManualMobileConfirm;
        }

        private static bool IsTerminalRecordType(SteamAutoTradeRecordType type)
        {
            return IsTerminalSuccessRecordType(type)
                || type is SteamAutoTradeRecordType.Unresolved or SteamAutoTradeRecordType.TerminalFailure;
        }

        private static bool ShouldKeepExistingRecord(SteamAutoTradeRecord existing, SteamAutoTradeRecord next)
        {
            return IsTerminalRecordType(existing.Type) && !IsTerminalRecordType(next.Type);
        }

        private bool IsLatestCountedRecordFailureNoLock()
        {
            SteamAutoTradeRecord? latestCounted = _records
                .Where(record => record.Time.Date == _today && IsCountedRecordType(record.Type))
                .OrderByDescending(record => record.Time)
                .FirstOrDefault();
            return latestCounted?.Type is SteamAutoTradeRecordType.Failed or SteamAutoTradeRecordType.TerminalFailure;
        }

        private bool IsPlanCompleted(SteamAutoTradePlanItem plan)
        {
            foreach (string key in BuildPlanLookupKeys(plan))
            {
                lock (_sync)
                {
                    if (_completedRecordKeys.Contains(key))
                        return true;
                }
            }

            return false;
        }

        private void AddCompletedRecordKeysNoLock(SteamAutoTradeRecord record)
        {
            foreach (string key in BuildRecordLookupKeys(record))
                _completedRecordKeys.Add(key);
        }

        private static IEnumerable<string> BuildRecordLookupKeys(SteamAutoTradeRecord record)
        {
            foreach (string key in BuildTransactionKeys(record.OrderNo, record.TradeOfferId))
                yield return key;
        }

        private static IEnumerable<string> BuildPlanLookupKeys(SteamAutoTradePlanItem plan)
        {
            foreach (string key in BuildTransactionKeys(plan.MatchedOrderNo, plan.TradeOfferId))
                yield return key;
        }

        private void ApplyCounterDeltaNoLock(SteamAutoTradeRecordType type, int delta)
        {
            if (type is SteamAutoTradeRecordType.Failed or SteamAutoTradeRecordType.TerminalFailure)
            {
                TodayFailure = Math.Max(0, TodayFailure + delta);
                return;
            }

            if (IsSuccessRecordType(type))
                TodaySuccess = Math.Max(0, TodaySuccess + delta);
        }

        private bool HasCountedFailuresNoLock()
        {
            return _countedRecordTypesByKey.Values.Any(x => x is SteamAutoTradeRecordType.Failed or SteamAutoTradeRecordType.TerminalFailure);
        }

        private static bool IsAlreadySentOrWaitingConfirmation(string? message)
        {
            string text = message ?? "";
            return text.Contains("已处理过", StringComparison.Ordinal)
                || text.Contains("不是待发送状态", StringComparison.Ordinal)
                || text.Contains("不能发送报价", StringComparison.Ordinal)
                || text.Contains("待您令牌验证", StringComparison.Ordinal)
                || text.Contains("待手机确认", StringComparison.Ordinal)
                || text.Contains("待确认报价", StringComparison.Ordinal)
                || text.Contains("等待确认", StringComparison.Ordinal);
        }

        private static bool LooksLikeLoginExpired(string message)
        {
            return message.Contains("登录", StringComparison.Ordinal)
                || message.Contains("login", StringComparison.OrdinalIgnoreCase)
                || message.Contains("auth", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeConfigurationRequired(string message)
        {
            return message.Contains("未绑定 Steam 令牌", StringComparison.Ordinal)
                || message.Contains("请先导入 Steam 令牌", StringComparison.Ordinal)
                || message.Contains("令牌字段不完整", StringComparison.Ordinal)
                || message.Contains("重新导入 maFile", StringComparison.Ordinal)
                || message.Contains("绑定/管理令牌", StringComparison.Ordinal);
        }

        private static string FirstText(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        private sealed class AutoTradeRecordSnapshot
        {
            public List<SteamAutoTradeRecord> Records { get; set; } = new();
        }
    }
}
