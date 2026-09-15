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
        private readonly Func<string, string> _suspendRepricing;
        private readonly Func<TimeSpan, CancellationToken, Task> _recheckDelay;

        public YouPinLandlordRepriceExecutor(
            IYouPinLandlordGateway gateway,
            IYouPinLandlordAuditStore auditStore,
            IClock clock,
            Func<Settings> getSettings,
            Func<YouPinRentalShelfType, bool> isEnabled,
            CancellationToken lifetimeCancellation,
            YouPinLandlordWriteCoordinator writeCoordinator,
            Func<string, string> suspendRepricing,
            Func<TimeSpan, CancellationToken, Task>? recheckDelay = null)
        {
            _gateway = gateway;
            _auditStore = auditStore;
            _clock = clock;
            _getSettings = getSettings;
            _isEnabled = isEnabled;
            _lifetimeCancellation = lifetimeCancellation;
            _writeCoordinator = writeCoordinator ?? throw new ArgumentNullException(nameof(writeCoordinator));
            _suspendRepricing = suspendRepricing ?? throw new ArgumentNullException(nameof(suspendRepricing));
            _recheckDelay = recheckDelay ?? Task.Delay;
        }

        public async Task<YouPinLandlordPlannedAction> ExecuteAsync(
            YouPinLandlordPolicy runPolicy,
            YouPinLandlordRemoteListing listing,
            YouPinLandlordPlannedAction action,
            Stopwatch stopwatch,
            Action<YouPinLandlordPlannedAction> reportProgress,
            CancellationToken cancellationToken)
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation, cancellationToken);
            cancellationToken = lifetime.Token;
            if (!action.TargetShortRent.HasValue)
                return action with { State = YouPinLandlordActionState.Skipped };
            if (!_isEnabled(listing.RentalType))
                return Disabled(action);
            using (await _writeCoordinator.AcquireAsync(cancellationToken).ConfigureAwait(false))
            {
                bool retry = false;
                while (true)
                {
                    try
                    {
                        return await ExecuteAttemptAsync(runPolicy, listing, action, stopwatch,
                            reportProgress, retry, cancellationToken).ConfigureAwait(false);
                    }
                    catch (YouPinRateLimitException ex)
                    {
                        string message = $"接口限流，等待 {Math.Ceiling(ex.RetryAfter.TotalSeconds):0} 秒后自动重试；自动改价保持开启";
                        await AppendAsync(runPolicy, action, YouPinLandlordOperationStage.WriteCompleted,
                            "限流等待", message, stopwatch.ElapsedMilliseconds, CancellationToken.None).ConfigureAwait(false);
                        reportProgress(action with { State = YouPinLandlordActionState.WaitingForRateLimit, Reason = message });
                        await _recheckDelay(ex.RetryAfter, cancellationToken).ConfigureAwait(false);
                        retry = true;
                    }
                }
            }
        }

        private async Task<YouPinLandlordPlannedAction> ExecuteAttemptAsync(
            YouPinLandlordPolicy runPolicy, YouPinLandlordRemoteListing listing,
            YouPinLandlordPlannedAction action, Stopwatch stopwatch,
            Action<YouPinLandlordPlannedAction> reportProgress, bool retry, CancellationToken cancellationToken)
        {
            if (!action.TargetShortRent.HasValue)
                return action with { State = YouPinLandlordActionState.Skipped };
            if (!_isEnabled(listing.RentalType))
                return Disabled(action);

            Settings settings = _getSettings();
            if (retry && settings.YouPinLandlordPolicyVersion != runPolicy.PolicyVersion)
                return action with { State = YouPinLandlordActionState.Skipped, Reason = "限流等待期间配置已变更，等待下一轮重新定价" };
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

            if (retry && currentListing.ShortRent != listing.ShortRent)
                return action with { State = YouPinLandlordActionState.Skipped, Reason = "限流等待期间货架租金已变化，等待下一轮重新定价" };

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
            if (result.RequiresManualReview)
                result = result with { Success = false, Message = _suspendRepricing(result.Message) };
            await AppendAsync(runPolicy, action, YouPinLandlordOperationStage.WriteCompleted,
                !result.Submitted ? "跳过" : result.Success ? "成功" : "失败", result.Message,
                stopwatch.ElapsedMilliseconds, CancellationToken.None).ConfigureAwait(false);
            if (!result.Submitted)
                return action with { State = YouPinLandlordActionState.Skipped, Reason = result.Message };
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
            (bool confirmed, string message) = await RecheckPriceAsync(
                settings, listing, action).ConfigureAwait(false);
            await AppendAsync(runPolicy, action, YouPinLandlordOperationStage.Recheck,
                confirmed ? "成功" : "待确认", message,
                stopwatch.ElapsedMilliseconds, CancellationToken.None).ConfigureAwait(false);
            return action with
            {
                State = confirmed ? YouPinLandlordActionState.Succeeded : YouPinLandlordActionState.AwaitingSynchronization,
                Reason = message
            };
        }

        private async Task<(bool Confirmed, string Message)> RecheckPriceAsync(
            Settings settings,
            YouPinLandlordRemoteListing listing,
            YouPinLandlordPlannedAction action)
        {
            // Read at approximately 0 / 2 / 5 / 10 seconds. Never resubmit the PUT
            // to compensate for a stale shelf, and bound the entire readback phase.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            int[] delays = [0, 2, 3, 5];
            string detail = "写后回查尚未完成";
            for (int attempt = 0; attempt < delays.Length; attempt++)
            {
                try
                {
                    if (delays[attempt] > 0)
                        await _recheckDelay(TimeSpan.FromSeconds(delays[attempt]), timeout.Token).ConfigureAwait(false);
                    YouPinLandlordRemoteListing? current = await _gateway.RevalidateListingAsync(
                        settings, listing.ListingId, listing.RentalType, action.RunId, action.ActionId,
                        timeout.Token).ConfigureAwait(false);
                    if (current != null && MatchesTargetPrice(current, action))
                        return (true, "写后回查已确认本次调整的租金及附带参数");
                    detail = current == null
                        ? "商品已不在原租赁货架，去向待核实"
                        : DescribePriceDifferences(current, action);
                }
                catch (Exception exception)
                {
                    detail = YouPinLandlordAutomation.FormatRepriceFailure("写后读取异常", exception);
                    if (timeout.IsCancellationRequested)
                        break;
                }
            }
            return (false, "平台已接收改价，写后回查仍待确认；" + detail + "；本次不重复提交");
        }

        private static string DescribePriceDifferences(
            YouPinLandlordRemoteListing listing,
            YouPinLandlordPlannedAction action)
        {
            var differences = new List<string>();
            if (listing.ShortRent != action.TargetShortRent)
                differences.Add($"短租金：目标 {action.TargetShortRent:0.##}，货架 {listing.ShortRent:0.##}");
            if (action.TargetLongRent.HasValue && listing.LongRent != action.TargetLongRent.Value)
                differences.Add($"长租金：目标 {action.TargetLongRent:0.##}，货架 {listing.LongRent:0.##}");
            if (action.TargetDeposit.HasValue && listing.Deposit != action.TargetDeposit.Value)
                differences.Add($"押金：目标 {action.TargetDeposit:0.##}，货架 {listing.Deposit:0.##}");
            if (action.TargetLeaseMaxDays.HasValue && listing.LeaseMaxDays != action.TargetLeaseMaxDays.Value)
                differences.Add($"租期：目标 {action.TargetLeaseMaxDays} 天，货架 {listing.LeaseMaxDays} 天");
            return string.Join("；", differences);
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
