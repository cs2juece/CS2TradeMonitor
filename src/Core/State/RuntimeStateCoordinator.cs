using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.Steam;
using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Linq;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.src.Core.State
{
    public static class RuntimeStateCoordinator
    {
        private static readonly object Gate = new();
        private static bool _initialized;
        private static RuntimeStateCoordinatorRuntimeServices? _services;

        private static RuntimeStateCoordinatorRuntimeServices Services => _services ??= RuntimeStateCoordinatorRuntimeServices.Resolve();

        public static void Initialize()
        {
            Initialize(RuntimeStateCoordinatorRuntimeServices.Resolve());
        }

        internal static void Initialize(RuntimeStateCoordinatorRuntimeServices services)
        {
            lock (Gate)
            {
                if (_initialized)
                    return;

                _services = services ?? throw new ArgumentNullException(nameof(services));
                _initialized = true;
            }

            services.YouPinInventory.DataUpdated += PublishYouPinInventory;
            services.YouPinSaleReminders.DataUpdated += PublishYouPinTodo;
            services.SteamOffers.DataUpdated += PublishSteamOffers;

            PublishYouPinInventory();
            PublishYouPinTodo();
            PublishSteamOffers();
        }

        private static void PublishYouPinInventory()
        {
            try
            {
                var services = Services;
                var inventory = services.YouPinInventory.GetState();
                var trend = services.YouPinInventory.GetTrendState();

                var itemCount = trend.TotalCount > 0 ? trend.TotalCount : inventory.TotalCount;
                var marketValue = trend.TotalValue > 0 ? trend.TotalValue : inventory.TotalValue;
                var buyValue = trend.PurchaseValue;
                var profitLoss = trend.TotalDelta != 0 ? trend.TotalDelta : inventory.TotalDelta;
                var profitLossPercent = trend.TotalDeltaPercent != 0 ? trend.TotalDeltaPercent : inventory.TotalDeltaPercent;
                var retrievedAt = trend.LastFetch > DateTime.MinValue ? trend.LastFetch : inventory.LastFetch;
                var status = BuildStatus(trend.LastStatus, trend.LastError, inventory.LastStatus, inventory.LastError);

                services.RuntimeState.UpdateYouPinInventory(
                    new YouPinInventorySnapshot(
                        itemCount,
                        marketValue,
                        buyValue,
                        profitLoss,
                        profitLossPercent,
                        retrievedAt,
                        status),
                    "YouPinInventoryServiceUpdated");
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Info("RuntimeState", "Publishing YouPin inventory snapshot failed: " + ex.Message);
            }
        }

        private static void PublishYouPinTodo()
        {
            try
            {
                var services = Services;
                var state = services.YouPinSaleReminders.GetState();
                var retrievedAt = MaxDate(state.LastCheck, state.LastMsgCenterCheck, state.LastAutoDeliveryCheck);
                var status = BuildStatus(state.LastStatus, state.LastError, state.LastMsgCenterStatus, state.LastMsgCenterError);

                services.RuntimeState.UpdateYouPinTodo(
                    new YouPinTodoSnapshot(
                        state.RecentOrders?.Count ?? 0,
                        state.RecentWaitDeliverOrders?.Count ?? 0,
                        retrievedAt,
                        status),
                    "YouPinTodoServiceUpdated");
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Info("RuntimeState", "Publishing YouPin todo snapshot failed: " + ex.Message);
            }
        }

        private static void PublishSteamOffers()
        {
            try
            {
                var services = Services;
                var state = services.SteamOffers.GetState();
                var offers = state.Offers ?? new();
                var safePending = offers.Count(x => x.CanAcceptSafely && x.Status == SteamOfferStatus.Pending);
                var status = BuildStatus(state.LastStatus, state.LastError);

                services.RuntimeState.UpdateSteamOffers(
                    new SteamOfferSnapshot(
                        offers.Count,
                        safePending,
                        state.LastRefresh,
                        status),
                    "SteamOfferServiceUpdated");
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Info("RuntimeState", "Publishing Steam offer snapshot failed: " + ex.Message);
            }
        }

        private static DateTime MaxDate(params DateTime[] values)
        {
            var result = DateTime.MinValue;
            foreach (var value in values)
            {
                if (value > result)
                    result = value;
            }

            return result;
        }

        private static string BuildStatus(params string[] parts)
        {
            var error = parts
                .Where((_, index) => index % 2 == 1)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (!string.IsNullOrWhiteSpace(error))
                return "异常：" + error.Trim();

            var status = parts
                .Where((_, index) => index % 2 == 0)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

            return string.IsNullOrWhiteSpace(status) ? "未获取" : status.Trim();
        }
    }
}
