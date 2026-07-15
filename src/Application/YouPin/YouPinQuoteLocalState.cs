using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CS2TradeMonitor.Application.YouPin
{
    internal static class YouPinQuoteLocalState
    {
        public const string ConfirmSubmitted = "ConfirmSubmitted";

        public static bool IsConfirmSubmitted(YouPinSaleOrder? order)
        {
            return string.Equals(order?.LocalQuoteState, ConfirmSubmitted, StringComparison.OrdinalIgnoreCase);
        }

        public static void MarkConfirmSubmitted(YouPinSaleOrder order, string tradeOfferId = "", DateTime? submittedAt = null)
        {
            ArgumentNullException.ThrowIfNull(order);

            order.LocalQuoteState = ConfirmSubmitted;
            order.LocalQuoteStateAt = submittedAt ?? DateTime.Now;
            if (!string.IsNullOrWhiteSpace(tradeOfferId))
                order.TradeOfferId = tradeOfferId.Trim();

            order.Message = "确认报价成功，等待平台同步";
            order.OrderStatusDesc = "确认报价成功";
        }

        public static void Clear(YouPinSaleOrder order)
        {
            ArgumentNullException.ThrowIfNull(order);

            order.LocalQuoteState = "";
            order.LocalQuoteStateAt = DateTime.MinValue;
        }

        public static YouPinLocalQuoteStateSnapshot CreateSnapshot(YouPinSaleOrder order)
        {
            ArgumentNullException.ThrowIfNull(order);

            return new YouPinLocalQuoteStateSnapshot(
                order.LocalQuoteState ?? "",
                order.LocalQuoteStateAt,
                order.TradeOfferId ?? "",
                BuildOrderKeys(order).ToList());
        }

        public static bool Matches(YouPinSaleOrder order, YouPinLocalQuoteStateSnapshot snapshot)
        {
            if (!snapshot.HasValue)
                return false;

            var orderKeys = BuildOrderKeys(order);
            return snapshot.OrderNos.Any(key => orderKeys.Contains(key));
        }

        public static void ApplySnapshot(YouPinSaleOrder order, YouPinLocalQuoteStateSnapshot snapshot)
        {
            if (!snapshot.HasValue)
                return;

            if (string.Equals(snapshot.State, ConfirmSubmitted, StringComparison.OrdinalIgnoreCase))
            {
                MarkConfirmSubmitted(order, snapshot.TradeOfferId, snapshot.StateAt == DateTime.MinValue ? null : snapshot.StateAt);
                return;
            }

            order.LocalQuoteState = snapshot.State;
            order.LocalQuoteStateAt = snapshot.StateAt;
            if (string.IsNullOrWhiteSpace(order.TradeOfferId) && !string.IsNullOrWhiteSpace(snapshot.TradeOfferId))
                order.TradeOfferId = snapshot.TradeOfferId.Trim();
        }

        public static bool ShouldClearConfirmSubmitted(YouPinSaleActionResult result)
        {
            if (result == null)
                return false;

            string text = result.Message ?? "";
            if (result.Ok && result.Status == 4)
                return true;

            if (ContainsAny(text, "确认报价失败", "确认失败", "报价发送失败", "发送报价失败"))
                return true;

            if (result.Ok && ContainsAny(text, "待您确认报价", "等待确认报价", "待确认报价", "请确认报价"))
                return true;

            return false;
        }

        private static HashSet<string> BuildOrderKeys(YouPinSaleOrder order)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddKey(keys, order.OrderNo);
            if (order.OrderNos != null)
            {
                foreach (string value in order.OrderNos)
                    AddKey(keys, value);
            }

            return keys;
        }

        private static void AddKey(HashSet<string> keys, string? value)
        {
            string normalized = value?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(normalized))
                keys.Add(normalized);
        }

        private static bool ContainsAny(string text, params string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            foreach (string keyword in keywords)
            {
                if (text.Contains(keyword, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }

    internal sealed record YouPinLocalQuoteStateSnapshot(
        string State,
        DateTime StateAt,
        string TradeOfferId,
        IReadOnlyList<string> OrderNos)
    {
        public bool HasValue => !string.IsNullOrWhiteSpace(State) && OrderNos != null && OrderNos.Count > 0;
    }
}
