using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class YouPinSaleReminderPageRenderer
    {
        private readonly List<LiteButton> _actionButtons;

        public YouPinSaleReminderPageRenderer(List<LiteButton> actionButtons)
        {
            _actionButtons = actionButtons ?? throw new ArgumentNullException(nameof(actionButtons));
        }

        public void ApplyPageState(
            YouPinSaleReminderPageStateViewModel view,
            YouPinSaleReminderTabRefreshPlan refreshPlan,
            YouPinSaleReminderRenderTargets targets)
        {
            ArgumentNullException.ThrowIfNull(view);
            ArgumentNullException.ThrowIfNull(refreshPlan);
            ArgumentNullException.ThrowIfNull(targets);

            UpdateOrderSection(targets.WaitDeliverStatusLabel, targets.WaitDeliverSummaryLabel, targets.WaitDeliverList, view.WaitDeliver, refreshPlan.UpdateWaitDeliverList);
            UpdateOrderSection(targets.MsgStatusLabel, targets.MsgSummaryLabel, targets.MsgList, view.MsgCenter, refreshPlan.UpdateMsgCenterList);
            if (refreshPlan.UpdateAutoDelivery)
                UpdateAutoDeliveryState(view.AutoDelivery, targets);
        }

        private void UpdateOrderSection(
            Label? statusLabel,
            Label? summaryLabel,
            YouPinSaleReminderOrderListPanel? list,
            YouPinSaleReminderOrderSectionViewModel section,
            bool updateList)
        {
            SetStatus(statusLabel, section.Status.RunText, section.Status.Ok);
            SetText(summaryLabel, section.Status.SummaryText);
            if (updateList)
                UpdateOrderList(list, section.Orders, section.WaitDeliverActions);
        }

        private void UpdateAutoDeliveryState(
            YouPinSaleReminderAutoDeliveryViewModel status,
            YouPinSaleReminderRenderTargets targets)
        {
            SetText(targets.AutoDeliveryStatusLabel, status.StatusText);
            SetText(targets.AutoDeliveryTimeLabel, status.TimeText);
            SetText(targets.AutoDeliveryErrorLabel, status.ErrorText);
            if (targets.AutoDeliveryErrorLabel != null)
                targets.AutoDeliveryErrorLabel.ForeColor = status.Ok ? UIColors.TextSub : UIColors.TextWarn;
        }

        private void UpdateOrderList(YouPinSaleReminderOrderListPanel? list, IReadOnlyList<YouPinSaleOrder> orders, bool waitDeliverActions)
        {
            if (list == null)
                return;

            using (UiJankProfiler.Measure("YouPinSaleReminder.UpdateOrderList", $"Count={orders.Count}; WaitDeliver={waitDeliverActions}", thresholdMs: 1))
            {
                _actionButtons.RemoveAll(button => button.IsDisposed);
                string emptyText = waitDeliverActions ? "暂无待发货或报价处理订单。" : "暂无最近提醒。";
                list.SetOrders(orders, emptyText);
            }
        }

        private static void SetText(Label? label, string text)
        {
            if (label == null)
                return;
            label.Text = text;
        }

        private static void SetStatus(Label? label, string text, bool ok)
        {
            if (label == null)
                return;
            label.Text = text;
            label.ForeColor = ok ? UIColors.Positive : UIColors.TextWarn;
        }
    }

    internal sealed record YouPinSaleReminderRenderTargets(
        Label? WaitDeliverStatusLabel,
        Label? WaitDeliverSummaryLabel,
        Label? MsgStatusLabel,
        Label? MsgSummaryLabel,
        Label? AutoDeliveryStatusLabel,
        Label? AutoDeliveryTimeLabel,
        Label? AutoDeliveryErrorLabel,
        YouPinSaleReminderOrderListPanel? WaitDeliverList,
        YouPinSaleReminderOrderListPanel? MsgList);
}
