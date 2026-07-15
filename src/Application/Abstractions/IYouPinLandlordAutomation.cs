using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface IYouPinLandlordAutomation : IDisposable
    {
        event Action? SnapshotChanged;

        void Configure(Settings settings);

        YouPinLandlordPolicy ApplyPolicy(YouPinLandlordPolicy policy);

        YouPinLandlordPolicy GetPolicy();

        YouPinLandlordSnapshot GetSnapshot();

        Task<YouPinLandlordRunResult> RunNowAsync(
            YouPinLandlordWorkflow workflow,
            string trigger = "用户立即检查",
            CancellationToken cancellationToken = default);

        Task<YouPinLandlordRunResult> RunRentalTypeNowAsync(
            YouPinRentalShelfType rentalType,
            string trigger = "用户立即检查",
            CancellationToken cancellationToken = default);

        Task<YouPinLandlordRunResult> RunInventoryNowAsync(
            string trigger = "用户立即扫描库存",
            CancellationToken cancellationToken = default);

        Task<YouPinLandlordRunResult> ScanRentalTypeNowAsync(
            YouPinRentalShelfType rentalType,
            string trigger = "用户立即扫描货架",
            CancellationToken cancellationToken = default);

        Task<YouPinLandlordRunResult> ScanRentalNowAsync(
            YouPinRentalScanScope scope,
            string trigger = "用户立即扫描货架",
            CancellationToken cancellationToken = default);

        Task<YouPinLandlordRunResult> ExecuteRentalTypeNowAsync(
            YouPinRentalShelfType rentalType,
            string trigger = "用户立即执行改价",
            CancellationToken cancellationToken = default);

        Task<YouPinLandlordRunResult> ExecuteRentalNowAsync(
            YouPinRentalScanScope scope,
            string trigger = "用户立即执行改价",
            CancellationToken cancellationToken = default);

        Task<YouPinLandlordRunResult> ScanInventoryNowAsync(
            string trigger = "用户立即扫描库存",
            CancellationToken cancellationToken = default);

        Task<YouPinLandlordRunResult> ExecuteInventoryNowAsync(
            string trigger = "用户立即执行库存自动出租",
            CancellationToken cancellationToken = default);

        Task<YouPinLandlordPricingPreference> RefreshPricingPreferenceAsync(
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<YouPinLandlordOperationRecord>> QueryHistoryAsync(
            YouPinLandlordAuditQuery query,
            CancellationToken cancellationToken = default);

        YouPinLandlordAuditHealth GetAuditHealth();
    }
}
