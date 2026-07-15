using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class YouPinSaleReminderPageActionRunner
    {
        private readonly List<LiteButton> _actionButtons;
        private readonly Action _ensureSettings;
        private readonly Action _configureService;
        private readonly Action _refreshState;
        private readonly Action<Label?, string, bool> _setStatus;
        private readonly SemaphoreSlim _operationLock = new(1, 1);

        public YouPinSaleReminderPageActionRunner(
            List<LiteButton> actionButtons,
            Action ensureSettings,
            Action configureService,
            Action refreshState,
            Action<Label?, string, bool> setStatus)
        {
            _actionButtons = actionButtons ?? throw new ArgumentNullException(nameof(actionButtons));
            _ensureSettings = ensureSettings ?? throw new ArgumentNullException(nameof(ensureSettings));
            _configureService = configureService ?? throw new ArgumentNullException(nameof(configureService));
            _refreshState = refreshState ?? throw new ArgumentNullException(nameof(refreshState));
            _setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));
        }

        public bool IsBusy { get; private set; }

        public async Task RunCheckAsync(
            Control sourceButton,
            Label? statusLabel,
            string busyText,
            Func<Task<YouPinSaleReminderCheckResult>> action)
        {
            ArgumentNullException.ThrowIfNull(sourceButton);
            ArgumentNullException.ThrowIfNull(action);

            if (IsBusy || !_operationLock.Wait(0))
            {
                _setStatus(statusLabel, "上一个操作正在进行中，请稍后再试。", false);
                return;
            }

            IsBusy = true;
            SetBusy(true);
            bool refreshNeeded = false;
            try
            {
                _ensureSettings();
                _configureService();
                _setStatus(statusLabel, busyText, true);
                var result = await action().ConfigureAwait(true);
                _setStatus(statusLabel, YouPinSaleReminderPagePresenter.BuildCheckResultText(result), result.Ok || result.Skipped);
                refreshNeeded = true;
            }
            catch (Exception ex)
            {
                _setStatus(statusLabel, ex.Message, false);
                refreshNeeded = true;
            }
            finally
            {
                IsBusy = false;
                if (!sourceButton.IsDisposed)
                    SetBusy(false);
                if (refreshNeeded)
                    _refreshState();
                _operationLock.Release();
            }
        }

        public async Task RunOrderActionAsync(
            YouPinSaleOrder order,
            Label? statusLabel,
            string actionName,
            Func<YouPinSaleOrder, Task<YouPinSaleActionResult>> action)
        {
            ArgumentNullException.ThrowIfNull(order);
            ArgumentNullException.ThrowIfNull(action);

            if (string.IsNullOrWhiteSpace(order.OrderNo))
            {
                _setStatus(statusLabel, "订单号为空，无法操作。", false);
                return;
            }

            if (IsBusy || !_operationLock.Wait(0))
            {
                _setStatus(statusLabel, "上一个操作正在进行中，请稍后再试。", false);
                return;
            }

            IsBusy = true;
            SetBusy(true);
            bool refreshNeeded = false;
            try
            {
                _ensureSettings();
                _setStatus(statusLabel, $"正在{actionName}...", true);
                var result = await action(order).ConfigureAwait(true);
                _setStatus(statusLabel, result.Message, result.Ok);
                refreshNeeded = true;
            }
            catch (Exception ex)
            {
                _setStatus(statusLabel, ex.Message, false);
                refreshNeeded = true;
            }
            finally
            {
                IsBusy = false;
                SetBusy(false);
                if (refreshNeeded)
                    _refreshState();
                _operationLock.Release();
            }
        }

        public async Task RunBatchOrderActionAsync(
            IReadOnlyList<YouPinSaleOrder> orders,
            Label? statusLabel,
            string actionName,
            Func<YouPinSaleOrder, int, int, Task<YouPinSaleActionResult>> action,
            int interItemDelayMs = 0)
        {
            ArgumentNullException.ThrowIfNull(orders);
            ArgumentNullException.ThrowIfNull(action);

            var targets = orders
                .Where(order => order != null && !string.IsNullOrWhiteSpace(order.OrderNo))
                .GroupBy(order => order.OrderNo.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            if (targets.Count == 0)
            {
                _setStatus(statusLabel, "当前没有可发送报价的订单。", false);
                return;
            }

            if (IsBusy || !_operationLock.Wait(0))
            {
                _setStatus(statusLabel, "上一个操作正在进行中，请稍后再试。", false);
                return;
            }

            IsBusy = true;
            SetBusy(true);
            int ok = 0;
            int skipped = 0;
            var successes = new List<string>();
            var skips = new List<string>();
            var failures = new List<string>();
            bool refreshNeeded = false;
            try
            {
                _ensureSettings();
                for (int i = 0; i < targets.Count; i++)
                {
                    YouPinSaleOrder order = targets[i];
                    _setStatus(statusLabel, $"正在{actionName} {i + 1}/{targets.Count}：{BuildOrderLabel(order)}", true);
                    var result = await action(order, i + 1, targets.Count).ConfigureAwait(true);
                    if (result.Ok)
                    {
                        ok++;
                        successes.Add($"{BuildOrderLabel(order)}：{result.Message}");
                    }
                    else if (result.Skipped)
                    {
                        skipped++;
                        skips.Add($"{BuildOrderLabel(order)}：{result.Message}");
                    }
                    else
                    {
                        failures.Add($"{BuildOrderLabel(order)}：{result.Message}");
                    }

                    if (interItemDelayMs > 0 && i + 1 < targets.Count)
                        await Task.Delay(interItemDelayMs).ConfigureAwait(true);
                }

                int failed = targets.Count - ok - skipped;
                string message = BuildBatchSummary(actionName, ok, skipped, failed, successes, skips, failures);
                _setStatus(statusLabel, message, failed == 0);
                refreshNeeded = true;
            }
            catch (Exception ex)
            {
                _setStatus(statusLabel, ex.Message, false);
                refreshNeeded = true;
            }
            finally
            {
                IsBusy = false;
                SetBusy(false);
                if (refreshNeeded)
                    _refreshState();
                _operationLock.Release();
            }
        }

        private void SetBusy(bool busy)
        {
            foreach (LiteButton button in _actionButtons.Where(button => !button.IsDisposed))
                button.Enabled = !busy;
        }

        private static string BuildOrderLabel(YouPinSaleOrder order)
        {
            string name = (order.Name ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(name))
                return name.Length > 18 ? name[..18] + "..." : name;

            string orderNo = (order.OrderNo ?? "").Trim();
            if (orderNo.Length <= 6)
                return string.IsNullOrWhiteSpace(orderNo) ? "未知订单" : "#" + orderNo;
            return "#" + orderNo[^6..];
        }

        private static string BuildFailureSummary(IReadOnlyList<string> failures)
        {
            if (failures.Count == 0)
                return "";

            return "失败：" + string.Join("；", failures.Take(2)) + (failures.Count > 2 ? "；..." : "");
        }

        private static string BuildBatchSummary(
            string actionName,
            int ok,
            int skipped,
            int failed,
            IReadOnlyList<string> successes,
            IReadOnlyList<string> skips,
            IReadOnlyList<string> failures)
        {
            string message = $"{actionName}完成：成功 {ok} 条";
            if (skipped > 0)
                message += $"，跳过 {skipped} 条";
            if (failed > 0)
                message += $"，失败 {failed} 条";
            message += "。";

            string successSummary = BuildNamedSummary("成功", successes);
            if (!string.IsNullOrWhiteSpace(successSummary))
                message += successSummary;

            string skipSummary = BuildNamedSummary("跳过", skips);
            if (!string.IsNullOrWhiteSpace(skipSummary))
                message += skipSummary;

            string failureSummary = BuildFailureSummary(failures);
            return string.IsNullOrWhiteSpace(failureSummary) ? message : message + failureSummary;
        }

        private static string BuildNamedSummary(string title, IReadOnlyList<string> items)
        {
            if (items.Count == 0)
                return "";

            return title + "：" + string.Join("；", items.Take(2)) + (items.Count > 2 ? "；..." : "") + "。";
        }
    }
}
