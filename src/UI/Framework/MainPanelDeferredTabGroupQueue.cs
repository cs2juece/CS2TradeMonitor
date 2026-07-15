using System;
using System.Collections.Generic;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class MainPanelDeferredTabGroupQueue : IDisposable
    {
        private readonly Func<bool> _canQueue;
        private readonly Action _onTick;
        private readonly Queue<DeferredTabGroupBuild> _queue = new();
        private readonly HashSet<string> _queued = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _completed = new(StringComparer.OrdinalIgnoreCase);
        private System.Windows.Forms.Timer? _timer;

        public MainPanelDeferredTabGroupQueue(Func<bool> canQueue, Action onTick)
        {
            _canQueue = canQueue ?? throw new ArgumentNullException(nameof(canQueue));
            _onTick = onTick ?? throw new ArgumentNullException(nameof(onTick));
        }

        public bool HasPending => _queue.Count > 0;

        public void Queue(DeferredTabGroupBuild item, int delayMs)
        {
            if (!_canQueue())
                return;

            if (_completed.Contains(item.Key) || _queued.Contains(item.Key))
                return;

            _queued.Add(item.Key);
            _queue.Enqueue(item);
            _timer ??= CreateTimer();
            if (!_timer.Enabled)
            {
                _timer.Interval = Math.Max(1, delayMs);
                _timer.Start();
            }
        }

        public bool TryDequeueForTab(string activeTab, out DeferredTabGroupBuild item)
        {
            while (_queue.Count > 0)
            {
                item = _queue.Dequeue();
                _queued.Remove(item.Key);
                if (_completed.Contains(item.Key))
                    continue;

                if (!string.Equals(activeTab, item.Tab, StringComparison.OrdinalIgnoreCase))
                    continue;

                return true;
            }

            item = null!;
            return false;
        }

        public void MarkCompleted(string key)
        {
            _completed.Add(key);
        }

        public void Stop()
        {
            _timer?.Stop();
        }

        public void Start()
        {
            _timer?.Start();
        }

        public void Clear()
        {
            _timer?.Stop();
            _queue.Clear();
            _queued.Clear();
            _completed.Clear();
        }

        public void Dispose()
        {
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
            _queue.Clear();
            _queued.Clear();
            _completed.Clear();
        }

        private System.Windows.Forms.Timer CreateTimer()
        {
            var timer = new System.Windows.Forms.Timer { Interval = 90 };
            timer.Tick += (_, __) => _onTick();
            return timer;
        }
    }
}
