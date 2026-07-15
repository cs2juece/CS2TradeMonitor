using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;
using System.Diagnostics;

namespace CS2TradeMonitor.Application.YouPin
{
    internal sealed class YouPinLandlordInventoryRentExecutor
    {
        private readonly IYouPinLandlordGateway _gateway;
        private readonly IYouPinLandlordAuditStore _auditStore;
        private readonly IClock _clock;
        private readonly Func<Settings> _getSettings;
        private readonly Func<bool> _isEnabled;
        private readonly CancellationToken _lifetimeCancellation;
        private readonly YouPinLandlordWriteCoordinator _writeCoordinator;

        public YouPinLandlordInventoryRentExecutor(
            IYouPinLandlordGateway gateway,
            IYouPinLandlordAuditStore auditStore,
            IClock clock,
            Func<Settings> getSettings,
            Func<bool> isEnabled,
            CancellationToken lifetimeCancellation,
            YouPinLandlordWriteCoordinator writeCoordinator)
        {
            _gateway = gateway;
            _auditStore = auditStore;
            _clock = clock;
            _getSettings = getSettings;
            _isEnabled = isEnabled;
            _lifetimeCancellation = lifetimeCancellation;
            _writeCoordinator = writeCoordinator;
        }

        public async Task<YouPinLandlordPlannedAction> ExecuteAsync(
            YouPinLandlordPolicy runPolicy,
            YouPinLandlordRemoteInventoryItem inventory,
            YouPinLandlordPlannedAction action,
            Stopwatch stopwatch,
            Action<YouPinLandlordPlannedAction> reportProgress,
            CancellationToken cancellationToken)
        {
            if (!action.TargetShortRent.HasValue || !_isEnabled())
                return Disabled(action);

            using (await _writeCoordinator.AcquireAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!_isEnabled())
                    return Disabled(action);

                Settings settings = _getSettings();
                await AppendAsync(runPolicy, action, YouPinLandlordOperationStage.WriteStarted,
                    "执行中", $"资格已复核，准备按短租金 {action.TargetShortRent:0.##} 自动上架",
                    stopwatch.ElapsedMilliseconds, cancellationToken).ConfigureAwait(false);
                reportProgress(action with
                {
                    State = YouPinLandlordActionState.Executing,
                    Reason = $"正在提交普通出租上架，目标短租金 {action.TargetShortRent:0.##}"
                });
                YouPinLandlordInventoryWriteResult result = await _gateway.ListInventoryAsync(
                    settings,
                    new YouPinLandlordInventoryListCommand(
                        inventory.AssetId,
                        action.TargetShortRent.Value,
                        action.TargetLongRent ?? 0m,
                        action.TargetDeposit ?? 0m,
                        action.TargetLeaseMaxDays ?? 0,
                        action.TargetSellPrice ?? inventory.ReferencePrice,
                        inventory.CompensationType,
                        inventory.NormalChargePercent,
                        inventory.VipChargePercent,
                        inventory.VipSwitchStatus),
                    action.RunId,
                    action.ActionId,
                    cancellationToken).ConfigureAwait(false);
                await AppendAsync(runPolicy, action, YouPinLandlordOperationStage.WriteCompleted,
                    result.Success ? "成功" : "失败", result.Message,
                    stopwatch.ElapsedMilliseconds, CancellationToken.None).ConfigureAwait(false);
                if (!result.Success || string.IsNullOrWhiteSpace(result.ListingId))
                    return action with { State = YouPinLandlordActionState.Failed, Reason = result.Message };

                reportProgress(action with
                {
                    State = YouPinLandlordActionState.AwaitingSynchronization,
                    Reason = "平台已接收上架，正在强制回查普通出租货架"
                });
                await AppendAsync(runPolicy, action, YouPinLandlordOperationStage.RecheckStarted,
                    "回查中", "平台已接收上架，开始强制写后回查",
                    stopwatch.ElapsedMilliseconds, CancellationToken.None).ConfigureAwait(false);
                reportProgress(action with
                {
                    State = YouPinLandlordActionState.Rechecking,
                    Reason = "正在确认饰品已进入普通出租货架"
                });
                YouPinLandlordRemoteListing? listing = await _gateway.RevalidateListingAsync(
                    settings,
                    result.ListingId,
                    YouPinRentalShelfType.InventoryRental,
                    action.RunId,
                    action.ActionId,
                    _lifetimeCancellation).ConfigureAwait(false);
                bool confirmed = listing != null && listing.ShortRent == action.TargetShortRent.Value;
                string message = confirmed
                    ? "写后回查已确认普通出租上架及目标短租金"
                    : "写后回查未确认上架结果，平台可能仍在同步";
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
                    action.ItemName, YouPinRentalShelfType.InventoryRental, action.DecisionCode,
                    result, message, elapsedMilliseconds)
                {
                    PolicyVersion = policy.PolicyVersion,
                    ResourceKeyHash = action.ResourceKeyHash
                },
                cancellationToken);
        }

        private static YouPinLandlordPlannedAction Disabled(YouPinLandlordPlannedAction action)
        {
            return action with
            {
                State = YouPinLandlordActionState.Skipped,
                Reason = "库存自动出租已关闭，停止创建和执行新任务"
            };
        }
    }
}
