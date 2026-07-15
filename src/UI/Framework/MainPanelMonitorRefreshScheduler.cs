using System;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class MainPanelMonitorRefreshScheduler : IDisposable
    {
        private readonly Func<bool> _canRun;
        private readonly Func<bool> _isHandleCreated;
        private readonly Action _refresh;
        private readonly UiDeferredActionScheduler _deferredActions;

        public MainPanelMonitorRefreshScheduler(Func<bool> canRun, Func<bool> isHandleCreated, Action refresh)
        {
            _canRun = canRun ?? throw new ArgumentNullException(nameof(canRun));
            _isHandleCreated = isHandleCreated ?? throw new ArgumentNullException(nameof(isHandleCreated));
            _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
            _deferredActions = new UiDeferredActionScheduler(_canRun);
        }

        public void Queue()
        {
            if (!_canRun() || !_isHandleCreated())
                return;

            _deferredActions.Schedule("monitor-refresh", 120, _refresh);
        }

        public void Dispose()
        {
            _deferredActions.Dispose();
        }
    }
}
