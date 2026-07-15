using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class YouPinSaleReminderPagePresenter
    {
        private readonly IYouPinSaleReminderService _saleReminderService;
        private readonly IManualYouPinOfferAutoConfirmation _manualOfferAutoConfirmation;

        public YouPinSaleReminderPagePresenter()
            : this(YouPinPageRuntimeServices.Resolve())
        {
        }

        internal YouPinSaleReminderPagePresenter(YouPinPageRuntimeServices runtimeServices)
            : this(
                runtimeServices.SaleReminders,
                runtimeServices.ManualOfferAutoConfirmation)
        {
        }

        internal YouPinSaleReminderPagePresenter(
            IYouPinSaleReminderService saleReminderService,
            IManualYouPinOfferAutoConfirmation manualOfferAutoConfirmation)
        {
            _saleReminderService = saleReminderService ?? throw new ArgumentNullException(nameof(saleReminderService));
            _manualOfferAutoConfirmation = manualOfferAutoConfirmation ?? throw new ArgumentNullException(nameof(manualOfferAutoConfirmation));
        }

        public event Action? DataUpdated
        {
            add => _saleReminderService.DataUpdated += value;
            remove => _saleReminderService.DataUpdated -= value;
        }

        public void Configure(Settings settings)
        {
            _saleReminderService.Configure(settings);
        }

        public YouPinSaleReminderState GetState() => _saleReminderService.GetState();

        public YouPinSaleReminderStatusBlockViewModel BuildTodoStatus(YouPinSaleReminderState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            return BuildStatusBlock(
                "待办提醒",
                state.Enabled,
                state.LastStatus,
                state.LastError,
                state.LastCheck,
                state.RecentOrders.Count);
        }

        public YouPinSaleReminderStatusBlockViewModel BuildWaitDeliverStatus(YouPinSaleReminderState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            return BuildStatusBlock(
                "待发货/报价处理",
                state.QuoteAutoRefreshEnabled,
                state.LastAutoDeliveryStatus,
                state.LastAutoDeliveryError,
                state.LastAutoDeliveryCheck,
                state.RecentWaitDeliverOrders.Count);
        }

        public YouPinSaleReminderStatusBlockViewModel BuildMsgCenterStatus(YouPinSaleReminderState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            return BuildStatusBlock(
                "消息中心提醒",
                state.MsgCenterEnabled,
                state.LastMsgCenterStatus,
                state.LastMsgCenterError,
                state.LastMsgCenterCheck,
                state.RecentMsgCenterOrders.Count);
        }

        public YouPinSaleReminderAutoDeliveryViewModel BuildAutoDeliveryStatus(YouPinSaleReminderState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            return BuildAutoDeliveryStatus(
                state.LastAutoDeliveryStatus,
                state.LastAutoDeliveryCheck,
                state.LastAutoDeliveryError);
        }

        public YouPinSaleReminderPageStateViewModel BuildPageState(YouPinSaleReminderState state)
        {
            return BuildPageStateView(state);
        }

        public Task<YouPinSaleReminderCheckResult> CheckTodoNowAsync(bool useMock, bool notify)
        {
            return _saleReminderService.CheckTodoNowAsync(useMock, notify);
        }

        public Task<YouPinSaleReminderCheckResult> CheckQuoteNowAsync(string trigger = "立即刷新")
        {
            return _saleReminderService.CheckQuoteNowAsync(trigger);
        }

        public Task<YouPinSaleReminderCheckResult> CheckMsgCenterNowAsync(bool useMock, bool notify)
        {
            return _saleReminderService.CheckMsgCenterNowAsync(useMock, notify);
        }

        public async Task<YouPinSaleActionResult> SendOfferAsync(string orderNo)
        {
            YouPinSaleOrder? order = FindWaitDeliverOrder(orderNo);
            YouPinSaleActionResult result = await _saleReminderService.SendOfferAsync(orderNo);
            if (result.Ok && order != null)
                await _manualOfferAutoConfirmation.HandleManuallySentYouPinOfferAsync(order, result);
            return result;
        }

        private YouPinSaleOrder? FindWaitDeliverOrder(string orderNo)
        {
            string normalized = (orderNo ?? "").Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return null;

            return _saleReminderService.GetState().RecentWaitDeliverOrders.FirstOrDefault(order =>
                (string.Equals(order.OrderNo, normalized, StringComparison.OrdinalIgnoreCase)
                    || order.OrderNos.Any(member => string.Equals(member, normalized, StringComparison.OrdinalIgnoreCase)))
                && YouPinSaleOrderActionResolver.Resolve(order).Kind == YouPinSaleOrderActionKind.SendOffer);
        }

        public Task<YouPinSaleActionResult> ConfirmOfferAsync(string orderNo, string tradeOfferId = "")
        {
            return _saleReminderService.ConfirmOfferAsync(orderNo, tradeOfferId);
        }

        public Task<YouPinSaleActionResult> QueryOfferStatusAsync(string orderNo)
        {
            return _saleReminderService.QueryOfferStatusAsync(orderNo);
        }

        public Task<YouPinSaleOrder> EnrichOrderDetailAsync(YouPinSaleOrder order)
        {
            return _saleReminderService.EnrichOrderDetailAsync(order);
        }

        public string EnsureQuoteLogFile()
        {
            return _saleReminderService.EnsureQuoteLogFile();
        }

        internal static YouPinSaleReminderStatusBlockViewModel BuildStatusBlock(
            string title,
            bool enabled,
            string status,
            string error,
            DateTime lastCheck,
            int count)
        {
            return new YouPinSaleReminderStatusBlockViewModel(
                BuildRunText(enabled, status, error, lastCheck, count),
                BuildSummary(title, lastCheck, count, error),
                string.IsNullOrWhiteSpace(error));
        }

        internal static string BuildCheckResultText(YouPinSaleReminderCheckResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            string prefix = result.Skipped ? "已跳过" : (result.Ok ? "检查完成" : "检查失败");
            string count = result.NewOrderCount > 0 ? $"，新增 {result.NewOrderCount} 条" : string.Empty;
            return $"{prefix}：{result.Message}{count}";
        }

        internal static YouPinSaleReminderAutoDeliveryViewModel BuildAutoDeliveryStatus(
            string status,
            DateTime lastCheck,
            string error)
        {
            bool ok = string.IsNullOrWhiteSpace(error);
            return new YouPinSaleReminderAutoDeliveryViewModel(
                string.IsNullOrWhiteSpace(status) ? "状态：未检查" : status,
                "上次诊断：" + FormatTime(lastCheck),
                ok ? "错误：无" : "错误：" + error,
                ok);
        }

        internal static YouPinSaleReminderPageStateViewModel BuildPageStateView(YouPinSaleReminderState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            return new YouPinSaleReminderPageStateViewModel(
                new YouPinSaleReminderOrderSectionViewModel(
                    BuildStatusBlock(
                        "待办提醒",
                        state.Enabled,
                        state.LastStatus,
                        state.LastError,
                        state.LastCheck,
                        state.RecentOrders.Count),
                    state.RecentOrders,
                    WaitDeliverActions: false),
                new YouPinSaleReminderOrderSectionViewModel(
                    BuildStatusBlock(
                        "待发货/报价处理",
                        state.Enabled,
                        state.LastAutoDeliveryStatus,
                        state.LastAutoDeliveryError,
                        state.LastAutoDeliveryCheck,
                        state.RecentWaitDeliverOrders.Count),
                    state.RecentWaitDeliverOrders,
                    WaitDeliverActions: true),
                new YouPinSaleReminderOrderSectionViewModel(
                    BuildStatusBlock(
                        "消息中心提醒",
                        state.MsgCenterEnabled,
                        state.LastMsgCenterStatus,
                        state.LastMsgCenterError,
                        state.LastMsgCenterCheck,
                        state.RecentMsgCenterOrders.Count),
                    state.RecentMsgCenterOrders,
                    WaitDeliverActions: false),
                BuildAutoDeliveryStatus(
                    state.LastAutoDeliveryStatus,
                    state.LastAutoDeliveryCheck,
                    state.LastAutoDeliveryError));
        }

        private static string BuildRunText(bool enabled, string status, string error, DateTime lastCheck, int count)
        {
            if (!enabled)
            {
                if (lastCheck != DateTime.MinValue)
                    return count > 0 ? "后台未启用；手动有数据" : "后台未启用；手动已刷新";
                return "后台未启用";
            }
            if (!string.IsNullOrWhiteSpace(error))
                return "运行：异常";
            if (string.IsNullOrWhiteSpace(status))
                return "运行：未检查";
            return status.Contains("完成", StringComparison.Ordinal)
                || status.Contains("成功", StringComparison.Ordinal)
                || status.Contains("正常", StringComparison.Ordinal)
                    ? "运行：正常"
                    : "运行：" + status;
        }

        private static string BuildSummary(string title, DateTime lastCheck, int count, string error)
        {
            string time = FormatTime(lastCheck);
            string countLabel = title.Contains("待办", StringComparison.Ordinal)
                ? "待办数量"
                : title;
            if (!string.IsNullOrWhiteSpace(error))
                return $"{countLabel}：{count} 条    上次刷新：{time}    错误：{error}";
            return $"{countLabel}：{count} 条    上次刷新：{time}";
        }

        private static string FormatTime(DateTime time)
        {
            return time == DateTime.MinValue ? "暂无" : time.ToString("MM-dd HH:mm:ss");
        }
    }

    internal sealed record YouPinSaleReminderStatusBlockViewModel(
        string RunText,
        string SummaryText,
        bool Ok);

    internal sealed record YouPinSaleReminderAutoDeliveryViewModel(
        string StatusText,
        string TimeText,
        string ErrorText,
        bool Ok);

    internal sealed record YouPinSaleReminderOrderSectionViewModel(
        YouPinSaleReminderStatusBlockViewModel Status,
        IReadOnlyList<YouPinSaleOrder> Orders,
        bool WaitDeliverActions);

    internal sealed record YouPinSaleReminderPageStateViewModel(
        YouPinSaleReminderOrderSectionViewModel Todo,
        YouPinSaleReminderOrderSectionViewModel WaitDeliver,
        YouPinSaleReminderOrderSectionViewModel MsgCenter,
        YouPinSaleReminderAutoDeliveryViewModel AutoDelivery);
}
