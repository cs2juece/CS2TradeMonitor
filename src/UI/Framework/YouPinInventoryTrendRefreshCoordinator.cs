using System;
using System.Threading;
using System.Threading.Tasks;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class YouPinInventoryTrendRefreshCoordinator : IDisposable
    {
        private readonly Func<bool> _isUnavailable;
        private readonly Func<bool> _isVisible;
        private readonly Func<bool> _isHandleCreated;
        private readonly Action<Action> _beginInvoke;
        private readonly Action<bool> _refreshData;
        private readonly Action<bool> _populateGrid;
        private readonly Action _configureService;
        private readonly Func<CancellationToken, Task> _fetchNowAsync;
        private readonly Action<string> _logManualRefreshFailure;

        private readonly UiDeferredActionScheduler _deferredActions;
        private bool _pendingDeferredRefreshForce;
        private bool _pendingGridRefreshForce;
        private bool _refreshing;
        private bool _disposed;

        public YouPinInventoryTrendRefreshCoordinator(
            Func<bool> isUnavailable,
            Func<bool> isVisible,
            Func<bool> isHandleCreated,
            Action<Action> beginInvoke,
            Action<bool> refreshData,
            Action<bool> populateGrid,
            Action configureService,
            Func<CancellationToken, Task> fetchNowAsync,
            Action<string> logManualRefreshFailure)
        {
            _isUnavailable = isUnavailable ?? throw new ArgumentNullException(nameof(isUnavailable));
            _isVisible = isVisible ?? throw new ArgumentNullException(nameof(isVisible));
            _isHandleCreated = isHandleCreated ?? throw new ArgumentNullException(nameof(isHandleCreated));
            _beginInvoke = beginInvoke ?? throw new ArgumentNullException(nameof(beginInvoke));
            _refreshData = refreshData ?? throw new ArgumentNullException(nameof(refreshData));
            _populateGrid = populateGrid ?? throw new ArgumentNullException(nameof(populateGrid));
            _configureService = configureService ?? throw new ArgumentNullException(nameof(configureService));
            _fetchNowAsync = fetchNowAsync ?? throw new ArgumentNullException(nameof(fetchNowAsync));
            _logManualRefreshFailure = logManualRefreshFailure ?? throw new ArgumentNullException(nameof(logManualRefreshFailure));
            _deferredActions = new UiDeferredActionScheduler(() => !_isUnavailable());
        }

        public bool IsRefreshing => _refreshing;

        public void HandleDataUpdated()
        {
            if (_isUnavailable() || !_isHandleCreated() || !_isVisible())
                return;

            try
            {
                _beginInvoke(() =>
                {
                    if (!_isUnavailable() && _isVisible())
                        QueueRefreshData(force: true);
                });
            }
            catch
            {
                // The page may be closing while the service publishes an update.
            }
        }

        public void QueueRefreshData(bool force = false, int delayMs = 90)
        {
            if (_isUnavailable())
                return;

            _pendingDeferredRefreshForce |= force;
            _deferredActions.Schedule("inventory-refresh", delayMs, RunDeferredRefresh);
        }

        public void StopDeferredRefresh()
        {
            _deferredActions.Cancel("inventory-refresh");
        }

        public void QueueGridRefresh(bool force = false, int delayMs = 120)
        {
            if (_isUnavailable())
                return;

            _pendingGridRefreshForce |= force;
            _deferredActions.Schedule("inventory-grid-refresh", delayMs, RunDeferredGridRefresh);
        }

        public async Task RefreshNowAsync(CancellationToken cancellationToken = default)
        {
            if (_refreshing || cancellationToken.IsCancellationRequested || _isUnavailable())
                return;

            _refreshing = true;
            try
            {
                _configureService();
                cancellationToken.ThrowIfCancellationRequested();
                await _fetchNowAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                // 页面切换或重复刷新会取消旧任务，取消不视为失败。
            }
            catch (Exception ex)
            {
                _logManualRefreshFailure("Manual refresh failed: " + ex.Message);
            }
            finally
            {
                _refreshing = false;
                if (!cancellationToken.IsCancellationRequested && !_isUnavailable())
                    _refreshData(true);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _deferredActions.Dispose();
        }

        internal static int NormalizeDelay(int delayMs)
        {
            return UiDeferredActionScheduler.NormalizeDelay(delayMs);
        }

        private void RunDeferredRefresh()
        {
            if (_isUnavailable() || !_isVisible())
                return;

            bool force = _pendingDeferredRefreshForce;
            _pendingDeferredRefreshForce = false;
            _refreshData(force);
        }

        private void RunDeferredGridRefresh()
        {
            if (_isUnavailable() || !_isVisible())
                return;

            bool force = _pendingGridRefreshForce;
            _pendingGridRefreshForce = false;
            _populateGrid(force);
        }
    }
}
