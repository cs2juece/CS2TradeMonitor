using System;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI
{
    internal sealed class SettingsFormUiTickCoordinator : IDisposable
    {
        private const int DefaultTimerIntervalMs = 100;
        private const int DefaultAutoApplyIntervalMs = 450;

        private readonly Func<bool> _isFormDisposedOrDisposing;
        private readonly Action _applyPendingSettings;
        private readonly Func<long> _getTickCount;
        private readonly int _timerIntervalMs;
        private readonly int _autoApplyIntervalMs;
        private System.Windows.Forms.Timer? _timer;
        private long _lastAutoApplyTick;
        private bool _tracking;
        private bool _paused;
        private bool _changePending;

        public SettingsFormUiTickCoordinator(
            Func<bool> isFormDisposedOrDisposing,
            Action applyPendingSettings,
            int timerIntervalMs = DefaultTimerIntervalMs,
            int autoApplyIntervalMs = DefaultAutoApplyIntervalMs,
            Func<long>? getTickCount = null)
        {
            _isFormDisposedOrDisposing = isFormDisposedOrDisposing ?? throw new ArgumentNullException(nameof(isFormDisposedOrDisposing));
            _applyPendingSettings = applyPendingSettings ?? throw new ArgumentNullException(nameof(applyPendingSettings));
            _getTickCount = getTickCount ?? (() => Environment.TickCount64);
            _timerIntervalMs = Math.Max(1, timerIntervalMs);
            _autoApplyIntervalMs = Math.Max(1, autoApplyIntervalMs);
        }

        public void StartDirtyTracking()
        {
            _tracking = true;
            _paused = false;
            StartTimerForPendingChange(resetDelay: true);
        }

        public void NotifySettingsChanged()
        {
            if (_isFormDisposedOrDisposing())
                return;

            _changePending = true;
            _lastAutoApplyTick = _getTickCount();
            StartTimerForPendingChange(resetDelay: false);
        }

        public void Pause()
        {
            _paused = true;
            _timer?.Stop();
        }

        public void Resume()
        {
            _paused = false;
            if (_isFormDisposedOrDisposing())
                return;

            StartTimerForPendingChange(resetDelay: true);
        }

        public void Dispose()
        {
            _tracking = false;
            _paused = true;
            _changePending = false;
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
        }

        private void EnsureTimer()
        {
            if (_timer != null)
                return;

            _timer = new System.Windows.Forms.Timer { Interval = _timerIntervalMs };
            _timer.Tick += OnTick;
        }

        private void OnTick(object? sender, EventArgs e)
        {
            ProcessTick(_getTickCount());
        }

        internal void ProcessTick(long now)
        {
            if (!_tracking || _paused || !_changePending || _isFormDisposedOrDisposing())
                return;

            if (now - _lastAutoApplyTick >= _autoApplyIntervalMs)
            {
                _changePending = false;
                _applyPendingSettings();
                if (!_changePending)
                    _timer?.Stop();
            }
        }

        private void StartTimerForPendingChange(bool resetDelay)
        {
            if (!_tracking || _paused || !_changePending || _isFormDisposedOrDisposing())
                return;

            if (resetDelay)
                _lastAutoApplyTick = _getTickCount();

            EnsureTimer();
            _timer?.Start();
        }
    }
}
