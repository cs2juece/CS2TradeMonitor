using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;
using System.Diagnostics;

namespace CS2TradeMonitor.Application.YouPin
{
    internal sealed class YouPinLandlordRepriceExecutor
    {
        private readonly IYouPinLandlordGateway _gateway;
        private readonly IYouPinLandlordAuditStore _auditStore;
        private readonly IClock _clock;
        private readonly Func<Settings> _getSettings;
        private readonly Func<YouPinRentalShelfType, bool> _isEnabled;
        private readonly CancellationToken _lifetimeCancellation;
        private readonly YouPinLandlordWriteCoordinator _writeCoordinator;

        public YouPinLandlordRepriceExecutor(
            IYouPinLandlordGateway gateway,
            IYouPinLandlordAuditStore auditStore,
            IClock clock,
            Func<Settings> getSettings,
            Func<YouPinRentalShelfType, bool> isEnabled,
            CancellationToken lifetimeCancellation,
            YouPinLandlordWriteCoordinator writeCoordinator)
        {
            _gateway = gateway;
            _auditStore = auditStore;
            _clock = clock;
            _getSettings = getSettings;
            _isEnabled = isEnabled;
            _lifetimeCancellation = lifetimeCancellation;
            _writeCoordinator = writeCoordinator ?? throw new ArgumentNullException(nameof(writeCoordinator));
        }

        public async Task<YouPinLandlordPlannedAction> ExecuteAsync(
            YouPinLandlordPolicy runPolicy,
            YouPinLandlordRemoteListing listing,
            YouPinLandlordPlannedAction action,
            Stopwatch stopwatch,
            Action<YouPinLandlordPlannedAction> reportProgress,
            CancellationToken cancellationToken)
        {
            if (!action.TargetShortRent.HasValue)
                return action with { State = YouPinLandlordActionState.Skipped };
            if (!_isEnabled(listing.RentalType))
                return Disabled(action);
            using (await _writeCoordinator.AcquireAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!_isEnabled(listing.RentalType))
                    return Disabled(action);

                Settings settings = _getSettings();
                YouPinLandlordRemoteListing? currentListing = await _gateway.RevalidateListingAsync(
                    settings,
                    listing.ListingId,
                    listing.RentalType,
                    action.RunId,
                    action.ActionId,
                    cancellationToken).ConfigureAwait(false);
                if (currentListing == null || !currentListing.IsCanLease)
                {
                    string reason = currentListing == null
                        ? "写前复核发现商品已不在当前租赁货架，已跳过"
                        : "写前复核发现商品已不具备出租资格，已跳过";
                    await AppendAsync(runPolicy, action, YouPinLandlordOperationStage.WriteCompleted,
                        "跳过", reason, stopwatch.ElapsedMilliseconds, cancellationToken).ConfigureAwait(false);
                    return action with { State = YouPinLandlordActionState.Skipped, Reason = reason };
                }

                if (!_isEnabled(listing.RentalType))
                {
                    const string reason = "自动改价已关闭，写前复核后停止执行新任务";
                    await AppendAsync(runPolicy, action, YouPinLandlordOperationStage.WriteCompleted,
                        "跳过", reason, stopwatch.ElapsedMilliseconds, CancellationToken.None).ConfigureAwait(false);
                    return action with { State = YouPinLandlordActionState.Skipped, Reason = reason };
                }

                if (MatchesTargetPrice(currentListing, action))
                {
                    const string reason = "写前复核发现当前已是目标租金，避免重复提交";
                    await AppendAsync(runPolicy, action, YouPinLandlordOperationStage.WriteCompleted,
                        "跳过", reason, stopwatch.ElapsedMilliseconds, cancellationToken).ConfigureAwait(false);
                    return action with { State = YouPinLandlordActionState.Skipped, Reason = reason };
                }

                await AppendAsync(runPolicy, action, YouPinLandlordOperationStage.WriteStarted,
                    "执行中", $"准备将短租金调整为 {action.TargetShortRent:0.##}",
                    stopwatch.ElapsedMilliseconds, cancellationToken).ConfigureAwait(false);
                reportProgress(action with
                {
                    State = YouPinLandlordActionState.Executing,
                    Reason = $"正在提交目标短租金 {action.TargetShortRent:0.##}"
                });
                YouPinLandlordWriteResult result = await _gateway.ChangeLeasePriceAsync(
                    settings,
                    new YouPinLandlordRepriceCommand(
                        listing.ListingId,
                        action.TargetShortRent.Value,
                        action.TargetLongRent ?? currentListing.LongRent,
                        action.TargetDeposit ?? currentListing.Deposit,
                        action.TargetLeaseMaxDays ?? currentListing.LeaseMaxDays,
                        currentListing.IsCanLease,
                        currentListing.IsCanSold,
                        currentListing.SellPrice),
                    action.RunId,
                    action.ActionId,
                    cancellationToken).ConfigureAwait(false);
                await AppendAsync(runPolicy, action, YouPinLandlordOperationStage.WriteCompleted,
                    result.Success ? "成功" : "失败", result.Message,
                    stopwatch.ElapsedMilliseconds, CancellationToken.None).ConfigureAwait(false);
                if (!result.Success)
                    return action with { State = YouPinLandlordActionState.Failed, Reason = result.Message };

                reportProgress(action with
                {
                    State = YouPinLandlordActionState.AwaitingSynchronization,
                    Reason = "平台已接收改价，正在写后回查"
                });
                await AppendAsync(runPolicy, action, YouPinLandlordOperationStage.RecheckStarted,
                    "回查中", "平台已接收改价，开始强制写后回查",
                    stopwatch.ElapsedMilliseconds, CancellationToken.None).ConfigureAwait(false);
                reportProgress(action with
                {
                    State = YouPinLandlordActionState.Rechecking,
                    Reason = "正在确认平台货架的实际租金"
                });
                YouPinLandlordRemoteListing? recheckedListing = await _gateway.RevalidateListingAsync(
                    settings,
                    listing.ListingId,
                    listing.RentalType,
                    action.RunId,
                    action.ActionId,
                    _lifetimeCancellation).ConfigureAwait(false);
                bool confirmed = recheckedListing != null && MatchesTargetPrice(recheckedListing, action);
                string message = confirmed
                    ? "写后回查已确认目标租金"
                    : "写后回查未确认目标租金，平台可能仍在同步";
                await AppendAsync(runPolicy, action, YouPinLandlordOperationStage.Recheck,
                    confirmed ? "成功" : "失败", message,
                    stopwatch.ElapsedMilliseconds, CancellationToken.None).ConfigureAwait(false);
                return action with
                {
                    State = confirmed ? YouPinLandlordActionState.Succeeded : YouPinLandlordActionState.Failed,
                    Reason = message
                };
            }
        }

        private Task AppendAsync(
            YouPinLandlordPolicy policy,
            YouPinLandlordPlannedAction action,
            YouPinLandlordOperationStage stage,
            string result,
            string message,
            long elapsedMilliseconds,
            CancellationToken cancellationToken)
        {
            return _auditStore.AppendAsync(
                new YouPinLandlordOperationRecord(
                    1, action.RunId, action.ActionId, action.Workflow, stage, _clock.Now,
                    action.ItemName, action.RentalType, action.DecisionCode,
                    result, message, elapsedMilliseconds)
                {
                    PolicyVersion = policy.PolicyVersion
                },
                cancellationToken);
        }

        private static YouPinLandlordPlannedAction Disabled(YouPinLandlordPlannedAction action)
        {
            return action with
            {
                State = YouPinLandlordActionState.Skipped,
                Reason = "自动改价已关闭，停止创建和执行新任务"
            };
        }

        private static bool MatchesTargetPrice(
            YouPinLandlordRemoteListing listing,
            YouPinLandlordPlannedAction action)
        {
            return listing.ShortRent == action.TargetShortRent
                && (!action.TargetLongRent.HasValue || listing.LongRent == action.TargetLongRent.Value)
                && (!action.TargetDeposit.HasValue || listing.Deposit == action.TargetDeposit.Value)
                && (!action.TargetLeaseMaxDays.HasValue
                    || listing.LeaseMaxDays == action.TargetLeaseMaxDays.Value);
        }

    }
}
