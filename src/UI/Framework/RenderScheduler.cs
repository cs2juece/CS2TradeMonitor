using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{

    public interface ILayeredRenderTarget
    {
        void CommitLayeredFrame();
    }

    public interface IRenderScheduler : IDisposable
    {
        bool IsSuspended { get; }

        void RequestRender(Control control);

        void Suspend();

        void Resume();
    }

    public sealed class RenderScheduler : IRenderScheduler
    {
        public const int MinIntervalMs = 16;
        public const int MaxIntervalMs = 1000;
        private const int DetailedFrameThresholdMs = 16;
        private const int StatsIntervalMs = 60_000;

        private static readonly Lazy<RenderScheduler> LazyInstance =
            new Lazy<RenderScheduler>(() => new RenderScheduler());

        private readonly object _syncRoot = new object();
        private readonly HashSet<Control> _pendingControls = new HashSet<Control>();
        private readonly System.Windows.Forms.Timer _timer;
        private DateTime _lastRenderUtc = DateTime.MinValue;
        private DateTime? _firstPendingUtc;
        private DateTime _lastStatsUtc = DateTime.MinValue;
        private int _suspendCount;
        private int _requestCount;
        private int _flushCount;
        private int _invalidateCount;
        private int _commitCount;
        private bool _disposed;

        private RenderScheduler()
        {
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = MinIntervalMs;
            _timer.Tick += OnTimerTick;
        }

        public static RenderScheduler Instance
        {
            get { return LazyInstance.Value; }
        }

        public bool IsSuspended
        {
            get
            {
                lock (_syncRoot)
                {
                    return _suspendCount > 0;
                }
            }
        }

        public void RequestRender(Control control)
        {
            if (control == null || _disposed)
            {
                return;
            }

            if (control.InvokeRequired)
            {
                TryBeginInvoke(control, delegate { RequestRender(control); });
                return;
            }

            if (!CanRender(control))
            {
                return;
            }

            lock (_syncRoot)
            {
                _pendingControls.Add(control);
                if (UiJankProfiler.Enabled)
                {
                    _requestCount++;
                }

                if (!_firstPendingUtc.HasValue)
                {
                    _firstPendingUtc = DateTime.UtcNow;
                }

                if (_suspendCount == 0)
                {
                    ScheduleTimer();
                }
            }
        }

        public void Suspend()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                _suspendCount++;
                _timer.Stop();
            }
        }

        public void Resume()
        {
            lock (_syncRoot)
            {
                if (_disposed || _suspendCount == 0)
                {
                    return;
                }

                _suspendCount--;

                if (_suspendCount == 0 && _pendingControls.Count > 0)
                {
                    ScheduleTimer();
                }
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _timer.Stop();
                _timer.Tick -= OnTimerTick;
                _timer.Dispose();
                _pendingControls.Clear();
                _firstPendingUtc = null;
            }
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            List<Control> controls;

            lock (_syncRoot)
            {
                _timer.Stop();

                if (_disposed || _suspendCount > 0 || _pendingControls.Count == 0)
                {
                    return;
                }

                controls = new List<Control>(_pendingControls);
                _pendingControls.Clear();
                _firstPendingUtc = null;
                _lastRenderUtc = DateTime.UtcNow;
            }

            foreach (Control control in controls)
            {
                if (!CanRender(control))
                {
                    continue;
                }

                try
                {
                    bool isLayeredRenderTarget = control is ILayeredRenderTarget;
                    using (UiJankProfiler.Measure(
                        isLayeredRenderTarget ? "RenderScheduler.CommitLayeredFrame" : "RenderScheduler.Invalidate",
                        $"{control.GetType().Name}; Bounds={control.ClientSize.Width}x{control.ClientSize.Height}; FullArea=True",
                        thresholdMs: UiJankProfiler.VerboseLoggingEnabled ? 1 : DetailedFrameThresholdMs))
                    {
                        if (control is ILayeredRenderTarget layeredRenderTarget)
                        {
                            layeredRenderTarget.CommitLayeredFrame();
                        }
                        else
                        {
                            control.Invalidate();
                        }
                    }

                    if (UiJankProfiler.Enabled)
                    {
                        if (isLayeredRenderTarget)
                            _commitCount++;
                        else
                            _invalidateCount++;
                    }
                }
                catch (ObjectDisposedException)
                {
                    // 控件释放期间的重绘请求已无目标，按丢弃处理。
                }
                catch (InvalidOperationException)
                {
                    // 句柄销毁或跨线程窗口状态变化时无法投递重绘，等待下一次调度。
                }
            }

            lock (_syncRoot)
            {
                if (UiJankProfiler.Enabled)
                {
                    _flushCount++;
                    LogStatsIfNeeded(controls.Count);
                }

                if (!_disposed && _suspendCount == 0 && _pendingControls.Count > 0)
                {
                    ScheduleTimer();
                }
            }
        }

        private void ScheduleTimer()
        {
            if (_timer.Enabled)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            int interval = MinIntervalMs;

            if (_lastRenderUtc != DateTime.MinValue)
            {
                int elapsedSinceLastRenderMs = (int)(now - _lastRenderUtc).TotalMilliseconds;
                interval = Math.Max(MinIntervalMs - elapsedSinceLastRenderMs, 1);
            }

            if (_firstPendingUtc.HasValue)
            {
                int elapsedSinceFirstRequestMs = (int)(now - _firstPendingUtc.Value).TotalMilliseconds;
                if (elapsedSinceFirstRequestMs >= MaxIntervalMs)
                {
                    interval = 1;
                }
            }

            _timer.Interval = Math.Min(Math.Max(interval, 1), MaxIntervalMs);
            _timer.Start();
        }

        private void LogStatsIfNeeded(int flushedControls)
        {
            DateTime now = DateTime.UtcNow;
            if (_lastStatsUtc != DateTime.MinValue && (now - _lastStatsUtc).TotalMilliseconds < StatsIntervalMs)
            {
                return;
            }

            _lastStatsUtc = now;
            UiJankProfiler.Log(
                "RenderScheduler.Stats",
                $"Requests={_requestCount}; Flushes={_flushCount}; Invalidates={_invalidateCount}; Commits={_commitCount}; LastFlushedControls={flushedControls}; IntervalMs={_timer.Interval}");
            _requestCount = 0;
            _flushCount = 0;
            _invalidateCount = 0;
            _commitCount = 0;
        }

        private static bool CanRender(Control control)
        {
            return control != null && !control.IsDisposed && !control.Disposing && control.IsHandleCreated;
        }

        private static void TryBeginInvoke(Control control, MethodInvoker callback)
        {
            try
            {
                if (CanRender(control))
                {
                    control.BeginInvoke(callback);
                }
            }
            catch (ObjectDisposedException)
            {
                // 控件已释放，排队渲染没有目标。
            }
            catch (InvalidOperationException)
            {
                // 控件句柄不可用时丢弃本次 UI 投递。
            }
        }
    }

}
