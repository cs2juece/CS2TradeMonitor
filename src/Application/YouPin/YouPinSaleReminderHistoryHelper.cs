using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.SystemServices;
using System;
using System.Collections.Generic;
using System.Linq;
using static CS2TradeMonitor.Application.YouPin.YouPinSaleOrderGroupHelper;

namespace CS2TradeMonitor.Application.YouPin
{
    internal static class YouPinSaleReminderHistoryHelper
    {
        private const int MaxHistoryItems = 1000;

        public static void PruneHistory(YouPinSaleReminderHistory history)
        {
            try
            {
                EnsureLists(history);

                history.KnownOrderIds = history.KnownOrderIds.TakeLast(MaxHistoryItems).ToList();
                history.KnownMsgCenterIds = history.KnownMsgCenterIds.TakeLast(MaxHistoryItems).ToList();
                history.KnownWaitDeliverIds = history.KnownWaitDeliverIds.TakeLast(MaxHistoryItems).ToList();
                history.RecentOrders = history.RecentOrders.OrderBy(x => x.DetectedAt).TakeLast(MaxHistoryItems).ToList();
                history.RecentMsgCenterOrders = history.RecentMsgCenterOrders.OrderBy(x => x.DetectedAt).TakeLast(MaxHistoryItems).ToList();
                history.RecentWaitDeliverOrders = history.RecentWaitDeliverOrders.OrderBy(x => x.DetectedAt).TakeLast(MaxHistoryItems).ToList();
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Info("YouPinTodo", $"历史裁剪跳过: {ex.Message}");
            }
        }

        public static void RemoveMockHistory(YouPinSaleReminderHistory history)
        {
            EnsureLists(history);

            history.KnownOrderIds = history.KnownOrderIds.Where(x => !IsMockId(x)).ToList();
            history.KnownMsgCenterIds = history.KnownMsgCenterIds.Where(x => !IsMockId(x)).ToList();
            history.KnownWaitDeliverIds = history.KnownWaitDeliverIds.Where(x => !IsMockId(x)).ToList();
            history.RecentOrders = history.RecentOrders.Where(x => !IsMockOrder(x)).ToList();
            history.RecentMsgCenterOrders = history.RecentMsgCenterOrders.Where(x => !IsMockOrder(x)).ToList();
            history.RecentWaitDeliverOrders = history.RecentWaitDeliverOrders.Where(x => !IsMockOrder(x)).ToList();
        }

        public static List<YouPinSaleOrder> RecordTodoOrders(
            YouPinSaleReminderHistory history,
            List<YouPinSaleOrder> orders,
            string source,
            string accountKey)
        {
            EnsureLists(history);
            var newOrders = new List<YouPinSaleOrder>();
            var known = new HashSet<string>(history.KnownOrderIds, StringComparer.OrdinalIgnoreCase);
            RemoveCurrentAccountOrders(history.RecentOrders, accountKey);

            foreach (var order in orders)
            {
                if (string.IsNullOrWhiteSpace(order.OrderNo)) continue;
                PrepareOrder(order, source, accountKey);

                string scopedOrderId = BuildScopedOrderId(accountKey, order.OrderNo);
                if (known.Add(scopedOrderId))
                {
                    history.KnownOrderIds.Add(scopedOrderId);
                    newOrders.Add(order);
                }
                history.RecentOrders.Add(order);
            }

            history.KnownOrderIds = history.KnownOrderIds.TakeLast(MaxHistoryItems).ToList();
            history.RecentOrders = history.RecentOrders.OrderBy(x => x.DetectedAt).TakeLast(MaxHistoryItems).ToList();
            return newOrders;
        }

        public static List<YouPinSaleOrder> RecordMsgCenterOrders(
            YouPinSaleReminderHistory history,
            List<YouPinSaleOrder> orders,
            string source,
            string accountKey)
        {
            EnsureLists(history);
            var newOrders = new List<YouPinSaleOrder>();
            var known = new HashSet<string>(history.KnownMsgCenterIds, StringComparer.OrdinalIgnoreCase);

            foreach (var order in orders)
            {
                if (string.IsNullOrWhiteSpace(order.OrderNo)) continue;
                PrepareOrder(order, source, accountKey);

                string scopedOrderId = BuildScopedOrderId(accountKey, order.OrderNo);
                if (!known.Add(scopedOrderId))
                {
                    BackfillKnownOrderImage(history.RecentMsgCenterOrders, order, accountKey);
                    continue;
                }
                history.KnownMsgCenterIds.Add(scopedOrderId);
                history.RecentMsgCenterOrders.Add(order);
                newOrders.Add(order);
            }

            history.KnownMsgCenterIds = history.KnownMsgCenterIds.TakeLast(MaxHistoryItems).ToList();
            history.RecentMsgCenterOrders = history.RecentMsgCenterOrders.OrderBy(x => x.DetectedAt).TakeLast(MaxHistoryItems).ToList();
            return newOrders;
        }

        public static List<YouPinSaleOrder> RecordWaitDeliverOrders(
            YouPinSaleReminderHistory history,
            List<YouPinSaleOrder> orders,
            string source,
            string accountKey)
        {
            EnsureLists(history);
            var newOrders = new List<YouPinSaleOrder>();
            var known = new HashSet<string>(history.KnownWaitDeliverIds, StringComparer.OrdinalIgnoreCase);
            var localStates = GetLocalQuoteStateSnapshots(history.RecentWaitDeliverOrders, accountKey);
            RemoveCurrentAccountOrders(history.RecentWaitDeliverOrders, accountKey);

            foreach (var order in orders)
            {
                if (string.IsNullOrWhiteSpace(order.OrderNo)) continue;
                PrepareOrder(order, source, accountKey);
                ApplyLocalQuoteState(order, localStates);

                string scopedOrderId = BuildScopedOrderId(accountKey, order.OrderNo);
                if (known.Add(scopedOrderId))
                {
                    history.KnownWaitDeliverIds.Add(scopedOrderId);
                    newOrders.Add(order);
                }
                history.RecentWaitDeliverOrders.Add(order);
            }

            history.KnownWaitDeliverIds = history.KnownWaitDeliverIds.TakeLast(MaxHistoryItems).ToList();
            history.RecentWaitDeliverOrders = history.RecentWaitDeliverOrders.OrderBy(x => x.DetectedAt).TakeLast(MaxHistoryItems).ToList();
            return newOrders;
        }

        public static List<YouPinLocalQuoteStateSnapshot> GetLocalQuoteStateSnapshots(
            List<YouPinSaleOrder>? orders,
            string accountKey)
        {
            if (orders == null || string.IsNullOrWhiteSpace(accountKey))
                return new List<YouPinLocalQuoteStateSnapshot>();

            return orders
                .Where(order => IsCurrentAccountOrder(order, accountKey))
                .Where(order => !string.IsNullOrWhiteSpace(order.LocalQuoteState))
                .Select(YouPinQuoteLocalState.CreateSnapshot)
                .Where(snapshot => snapshot.HasValue)
                .ToList();
        }

        public static void ClearMatchingLocalQuoteStates(
            YouPinSaleReminderHistory history,
            string accountKey,
            YouPinLocalQuoteStateSnapshot snapshot)
        {
            EnsureLists(history);
            if (!snapshot.HasValue || string.IsNullOrWhiteSpace(accountKey))
                return;

            foreach (var order in history.RecentWaitDeliverOrders)
            {
                if (!IsCurrentAccountOrder(order, accountKey))
                    continue;
                if (YouPinQuoteLocalState.Matches(order, snapshot))
                    YouPinQuoteLocalState.Clear(order);
            }
        }

        public static bool IsMockOrder(YouPinSaleOrder? order)
        {
            if (order == null) return false;
            return IsMockId(order.OrderNo)
                || IsMockId(order.TradeOfferId)
                || ContainsMockMarker(order.Source);
        }

        public static bool IsMockId(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string text = value.Trim();
            int separator = text.LastIndexOf('|');
            if (separator >= 0 && separator + 1 < text.Length)
                text = text[(separator + 1)..];

            return text.StartsWith("MOCK", StringComparison.OrdinalIgnoreCase);
        }

        public static bool ContainsMockMarker(string? value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Contains("模拟", StringComparison.OrdinalIgnoreCase);
        }

        private static void PrepareOrder(YouPinSaleOrder order, string source, string accountKey)
        {
            order.AccountKey = accountKey;
            order.Source = string.IsNullOrWhiteSpace(order.Source) ? source : $"{source}·{order.Source}";
            if (order.DetectedAt == DateTime.MinValue) order.DetectedAt = DateTime.Now;
        }

        private static void RemoveCurrentAccountOrders(List<YouPinSaleOrder>? orders, string accountKey)
        {
            if (orders == null || string.IsNullOrWhiteSpace(accountKey))
                return;

            orders.RemoveAll(order => IsCurrentAccountOrder(order, accountKey));
        }

        private static bool BackfillKnownOrderImage(List<YouPinSaleOrder>? existingOrders, YouPinSaleOrder freshOrder, string accountKey)
        {
            if (existingOrders == null || string.IsNullOrWhiteSpace(freshOrder.OrderNo) || string.IsNullOrWhiteSpace(freshOrder.ImageUrl))
                return false;

            var existing = existingOrders.FirstOrDefault(x =>
                string.Equals(x.OrderNo, freshOrder.OrderNo, StringComparison.OrdinalIgnoreCase)
                && IsCurrentAccountOrder(x, accountKey));
            if (existing == null || !string.IsNullOrWhiteSpace(existing.ImageUrl))
                return false;

            existing.ImageUrl = freshOrder.ImageUrl.Trim();
            return true;
        }

        private static void ApplyLocalQuoteState(
            YouPinSaleOrder order,
            IReadOnlyList<YouPinLocalQuoteStateSnapshot> snapshots)
        {
            if (snapshots.Count == 0)
                return;

            var snapshot = snapshots.FirstOrDefault(candidate => YouPinQuoteLocalState.Matches(order, candidate));
            if (snapshot != null && snapshot.HasValue)
                YouPinQuoteLocalState.ApplySnapshot(order, snapshot);
        }

        private static void EnsureLists(YouPinSaleReminderHistory history)
        {
            history.KnownOrderIds ??= new List<string>();
            history.KnownMsgCenterIds ??= new List<string>();
            history.KnownWaitDeliverIds ??= new List<string>();
            history.RecentOrders ??= new List<YouPinSaleOrder>();
            history.RecentMsgCenterOrders ??= new List<YouPinSaleOrder>();
            history.RecentWaitDeliverOrders ??= new List<YouPinSaleOrder>();
        }
    }
}
