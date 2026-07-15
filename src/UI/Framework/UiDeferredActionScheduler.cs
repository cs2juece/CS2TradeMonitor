using System;
using System.Collections.Generic;

namespace CS2TradeMonitor.src.UI.Framework
{
    /// <summary>
    /// Owns keyed, one-shot WinForms delays and their lifecycle.
    /// </summary>
    internal sealed class UiDeferredActionScheduler : IDisposable
    {
        private readonly Func<bool> _canRun;
        private readonly Dictionary<string, ScheduledAction> _scheduled = new(StringComparer.Ordinal);
        private bool _disposed;

        public UiDeferredActionScheduler(Func<bool> canRun)
        {
            _canRun = canRun ?? throw new ArgumentNullException(nameof(canRun));
        }

        public int PendingCount => _scheduled.Count;

        public bool Schedule(string key, int delayMs, Action action)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("A deferred action key is required.", nameof(key));
            ArgumentNullException.ThrowIfNull(action);

            if (_disposed || !_canRun())
                return false;

            if (_scheduled.TryGetValue(key, out ScheduledAction? scheduled))
            {
                scheduled.Timer.Stop();
                scheduled.Action = action;
                scheduled.Timer.Interval = NormalizeDelay(delayMs);
                scheduled.Timer.Start();
                return true;
            }

            var timer = new System.Windows.Forms.Timer { Interval = NormalizeDelay(delayMs) };
            scheduled = new ScheduledAction(timer, action);
            timer.Tick += (_, __) => RunNow(key);
            _scheduled.Add(key, scheduled);
            timer.Start();
            return true;
        }

        public bool Cancel(string key)
        {
            if (!_scheduled.Remove(key, out ScheduledAction? scheduled))
                return false;

            scheduled.Dispose();
            return true;
        }

        public void CancelAll()
        {
            foreach (ScheduledAction scheduled in _scheduled.Values)
                scheduled.Dispose();
            _scheduled.Clear();
        }

        internal bool RunNow(string key)
        {
            if (!_scheduled.Remove(key, out ScheduledAction? scheduled))
                return false;

            Action action = scheduled.Action;
            scheduled.Dispose();
            if (_disposed || !_canRun())
                return false;

            action();
            return true;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            CancelAll();
        }

        internal static int NormalizeDelay(int delayMs)
        {
            return Math.Max(1, delayMs);
        }

        private sealed class ScheduledAction : IDisposable
        {
            public ScheduledAction(System.Windows.Forms.Timer timer, Action action)
            {
                Timer = timer;
                Action = action;
            }

            public System.Windows.Forms.Timer Timer { get; }

            public Action Action { get; set; }

            public void Dispose()
            {
                Timer.Stop();
                Timer.Dispose();
            }
        }
    }
}
