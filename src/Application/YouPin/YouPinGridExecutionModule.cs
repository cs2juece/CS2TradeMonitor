using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.SystemServices;
using System.Globalization;

namespace CS2TradeMonitor.Application.YouPin
{
    internal sealed class YouPinGridExecutionModule
    {
        private readonly IYouPinGridExecutionJournal _journal;
        private readonly IYouPinGridExecutionGateway _gateway;
        private readonly Func<DateTime> _now;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public YouPinGridExecutionModule(
            IYouPinGridExecutionJournal journal,
            IYouPinGridExecutionGateway gateway)
            : this(journal, gateway, () => DateTime.Now)
        {
        }

        internal YouPinGridExecutionModule(
            IYouPinGridExecutionJournal journal,
            IYouPinGridExecutionGateway gateway,
            Func<DateTime> now)
        {
            _journal = journal ?? throw new ArgumentNullException(nameof(journal));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _now = now ?? throw new ArgumentNullException(nameof(now));
        }

        public async Task<YouPinGridExecutionOutcome> ExecuteOrReconcileAsync(
            Settings settings,
            YouPinGridStrategy strategy,
            YouPinGridPlan plan,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(strategy);
            ArgumentNullException.ThrowIfNull(plan);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                YouPinGridExecutionRecord? active = _journal.FindActive(strategy.Id);
                if (active != null)
                    return await ReconcileAsync(settings, active, cancellationToken).ConfigureAwait(false);

                if (!strategy.Enabled
                    || strategy.ObserveOnly
                    || !plan.ExecutionPermitted
                    || plan.Action == YouPinGridAction.None)
                {
                    return Outcome(
                        YouPinGridExecutionStage.None,
                        plan.Action,
                        plan.Quantity,
                        plan.ObservationPrice,
                        "策略当前仅观察，不会提交真实订单");
                }

                YouPinGridExecutionRevalidation revalidation = await _gateway.RevalidateAsync(
                    settings,
                    strategy,
                    plan,
                    cancellationToken).ConfigureAwait(false);
                string validation = ValidateRevalidation(strategy, plan, revalidation);
                if (validation.Length > 0)
                {
                    return Outcome(
                        YouPinGridExecutionStage.None,
                        plan.Action,
                        revalidation.Quantity,
                        revalidation.UnitPrice,
                        validation);
                }

                DateTime now = _now();
                var prepared = new YouPinGridExecutionRecord
                {
                    StrategyId = strategy.Id,
                    TemplateId = strategy.TemplateId.Trim(),
                    Fingerprint = BuildFingerprint(strategy, plan),
                    Action = plan.Action,
                    Stage = YouPinGridExecutionStage.Prepared,
                    Quantity = revalidation.Quantity,
                    UnitPrice = revalidation.UnitPrice,
                    TriggerPrice = plan.TriggerPrice,
                    TargetReference = revalidation.TargetReference.Trim(),
                    Message = "远端写入前已完成本地占位",
                    CreatedAt = now,
                    UpdatedAt = now
                };
                if (!_journal.Save(prepared))
                {
                    return Outcome(
                        YouPinGridExecutionStage.Failed,
                        plan.Action,
                        revalidation.Quantity,
                        revalidation.UnitPrice,
                        "无法持久化执行占位，已停止远端写入");
                }

                YouPinGridRemoteMutationResult remote;
                try
                {
                    remote = await _gateway.SubmitAsync(
                        settings,
                        prepared,
                        revalidation,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    MarkManualReview(prepared, "远端请求被取消，结果未知，禁止自动重试");
                    throw;
                }
                catch (Exception ex)
                {
                    MarkManualReview(prepared, $"远端请求结果未知：{Sanitize(ex.Message)}");
                    return Outcome(
                        YouPinGridExecutionStage.RequiresManualReview,
                        plan.Action,
                        prepared.Quantity,
                        prepared.UnitPrice,
                        prepared.Message);
                }

                prepared.RemoteReference = remote.RemoteReference ?? string.Empty;
                prepared.Message = string.IsNullOrWhiteSpace(remote.Message)
                    ? "悠悠未返回执行说明"
                    : remote.Message.Trim();
                prepared.UpdatedAt = _now();
                if (remote.Accepted)
                    prepared.Stage = remote.Settled
                        ? YouPinGridExecutionStage.Completed
                        : YouPinGridExecutionStage.AwaitingSettlement;
                else
                    prepared.Stage = remote.MayHaveChangedRemoteState
                        ? YouPinGridExecutionStage.RequiresManualReview
                        : YouPinGridExecutionStage.Failed;

                if (!_journal.Save(prepared))
                {
                    return Outcome(
                        YouPinGridExecutionStage.RequiresManualReview,
                        plan.Action,
                        prepared.Quantity,
                        prepared.UnitPrice,
                        "悠悠已返回结果，但本地执行状态保存失败，请人工核对");
                }

                return OutcomeFromRecord(prepared);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<YouPinGridExecutionOutcome> ReconcileAsync(
            Settings settings,
            YouPinGridExecutionRecord active,
            CancellationToken cancellationToken)
        {
            if (active.Stage == YouPinGridExecutionStage.RequiresManualReview)
                return OutcomeFromRecord(active);

            if (active.Stage == YouPinGridExecutionStage.Prepared)
            {
                active.Stage = YouPinGridExecutionStage.RequiresManualReview;
                active.Message = "上次执行停在远端写入前后边界，禁止自动重试";
                active.UpdatedAt = _now();
                _journal.Save(active);
                return OutcomeFromRecord(active);
            }

            YouPinGridRemoteSettlementResult settlement;
            try
            {
                settlement = await _gateway.ReconcileAsync(
                    settings,
                    active,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                active.Message = $"状态回读失败：{Sanitize(ex.Message)}";
                active.UpdatedAt = _now();
                _journal.Save(active);
                return OutcomeFromRecord(active);
            }

            active.Stage = NormalizeSettlementStage(settlement.Stage);
            if (settlement.UnitPrice > 0m)
                active.UnitPrice = settlement.UnitPrice;
            active.Message = string.IsNullOrWhiteSpace(settlement.Message)
                ? "等待悠悠订单状态更新"
                : settlement.Message.Trim();
            active.UpdatedAt = _now();
            if (!_journal.Save(active))
            {
                return Outcome(
                    YouPinGridExecutionStage.RequiresManualReview,
                    active.Action,
                    active.Quantity,
                    active.UnitPrice,
                    "悠悠状态已回读，但本地状态保存失败，请人工核对");
            }

            return OutcomeFromRecord(active);
        }

        private void MarkManualReview(YouPinGridExecutionRecord record, string message)
        {
            record.Stage = YouPinGridExecutionStage.RequiresManualReview;
            record.Message = message;
            record.UpdatedAt = _now();
            _journal.Save(record);
        }

        private static string ValidateRevalidation(
            YouPinGridStrategy strategy,
            YouPinGridPlan plan,
            YouPinGridExecutionRevalidation revalidation)
        {
            if (!revalidation.Ready)
                return string.IsNullOrWhiteSpace(revalidation.Message)
                    ? "悠悠写入前核验未通过"
                    : revalidation.Message.Trim();
            if (revalidation.Action != plan.Action)
                return "写入前核验方向与原计划不一致";
            if (revalidation.Quantity <= 0 || revalidation.Quantity > plan.Quantity)
                return "写入前核验数量超出原计划";
            if (revalidation.UnitPrice <= 0m)
                return "写入前核验价格无效";
            if (plan.Action == YouPinGridAction.Buy && revalidation.UnitPrice > plan.TriggerPrice)
                return "悠悠最低有效在售价已高于买入触发价，本次不追价";
            if (plan.Action == YouPinGridAction.Sell && revalidation.UnitPrice < plan.TriggerPrice)
                return "悠悠最低有效在售价已低于卖出触发价，本次不降价";
            if (plan.Action == YouPinGridAction.Buy
                && strategy.MaxCapital > 0m
                && revalidation.ReservedCapital + (revalidation.UnitPrice * revalidation.Quantity) > strategy.MaxCapital)
            {
                return "写入前核验发现本次买入会超过最大占用资金";
            }

            return string.Empty;
        }

        private static YouPinGridExecutionStage NormalizeSettlementStage(YouPinGridExecutionStage stage)
        {
            return stage is YouPinGridExecutionStage.Completed
                or YouPinGridExecutionStage.Failed
                or YouPinGridExecutionStage.RequiresManualReview
                ? stage
                : YouPinGridExecutionStage.AwaitingSettlement;
        }

        private static string BuildFingerprint(YouPinGridStrategy strategy, YouPinGridPlan plan)
        {
            return string.Join(
                "|",
                strategy.Id.Trim(),
                plan.Action,
                strategy.BasePrice.ToString("0.00", CultureInfo.InvariantCulture),
                plan.TriggerPrice.ToString("0.00", CultureInfo.InvariantCulture));
        }

        private static YouPinGridExecutionOutcome OutcomeFromRecord(YouPinGridExecutionRecord record)
        {
            return Outcome(
                record.Stage,
                record.Action,
                record.Quantity,
                record.UnitPrice,
                record.Message);
        }

        private static YouPinGridExecutionOutcome Outcome(
            YouPinGridExecutionStage stage,
            YouPinGridAction action,
            int quantity,
            decimal unitPrice,
            string message)
        {
            return new YouPinGridExecutionOutcome
            {
                Stage = stage,
                Action = action,
                Quantity = quantity,
                UnitPrice = unitPrice,
                CompletedBasePrice = stage == YouPinGridExecutionStage.Completed && unitPrice > 0m
                    ? unitPrice
                    : null,
                Message = message
            };
        }

        private static string Sanitize(string? value)
        {
            string text = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return text.Length <= 180 ? text : text[..180];
        }
    }
}
