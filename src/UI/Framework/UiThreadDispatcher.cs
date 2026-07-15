#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace CS2TradeMonitor.src.UI.Framework
{
    public sealed class UiThreadDispatcher : IDisposable
    {
        private bool _disposed;

        public UiThreadDispatcher(
            int refreshMs,
            Func<UiSnapshot> buildSnapshot,
            Action<UiSnapshot> applySnapshot,
            SynchronizationContext? uiContext = null)
        {
            Queue = new ConcurrentQueue<UiSnapshot>();
            Producer = new BackgroundDataProducer(Queue, refreshMs, buildSnapshot);
            Consumer = new UiThreadConsumer(Queue, refreshMs, applySnapshot, uiContext);
        }

        public ConcurrentQueue<UiSnapshot> Queue { get; }

        public BackgroundDataProducer Producer { get; }

        public UiThreadConsumer Consumer { get; }

        public void Start()
        {
            ThrowIfDisposed();
            Producer.Start();
            Consumer.Start();
        }

        public void Stop()
        {
            Consumer.Stop();
            Producer.Stop();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            Consumer.Dispose();
            Producer.Dispose();
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(UiThreadDispatcher));
            }
        }
    }

    public sealed class BackgroundDataProducer : IDisposable
    {
        private const int MaxQueueSize = 5;
        private readonly ConcurrentQueue<UiSnapshot> _queue;
        private readonly Func<UiSnapshot> _buildSnapshot;
        private readonly ManualResetEventSlim _stopRequested;
        private readonly object _syncRoot;
        private Thread? _thread;
        private bool _disposed;

        public BackgroundDataProducer(
            ConcurrentQueue<UiSnapshot> queue,
            int refreshMs,
            Func<UiSnapshot> buildSnapshot)
        {
            if (queue == null)
            {
                throw new ArgumentNullException(nameof(queue));
            }

            if (buildSnapshot == null)
            {
                throw new ArgumentNullException(nameof(buildSnapshot));
            }

            if (refreshMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(refreshMs), refreshMs, "RefreshMs must be greater than zero.");
            }

            _queue = queue;
            _buildSnapshot = buildSnapshot;
            _stopRequested = new ManualResetEventSlim(false);
            _syncRoot = new object();
            RefreshMs = refreshMs;
        }

        public int RefreshMs { get; }

        public bool IsRunning
        {
            get
            {
                Thread? thread = _thread;
                return thread != null && thread.IsAlive;
            }
        }

        public event EventHandler<Exception>? Faulted;

        public void Start()
        {
            ThrowIfDisposed();

            lock (_syncRoot)
            {
                if (IsRunning)
                {
                    return;
                }

                _stopRequested.Reset();
                _thread = new Thread(Run)
                {
                    IsBackground = true,
                    Name = nameof(BackgroundDataProducer)
                };
                _thread.Start();
            }
        }

        public void Stop()
        {
            Thread? thread;

            lock (_syncRoot)
            {
                thread = _thread;
                if (thread == null)
                {
                    return;
                }

                _stopRequested.Set();
            }

            if (!ReferenceEquals(Thread.CurrentThread, thread))
            {
                thread.Join();
            }

            lock (_syncRoot)
            {
                if (ReferenceEquals(_thread, thread))
                {
                    _thread = null;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            _stopRequested.Dispose();
            _disposed = true;
        }

        private void Run()
        {
            while (!_stopRequested.IsSet)
            {
                try
                {
                    _queue.Enqueue(_buildSnapshot());

                    // 队列超上限时丢弃最旧的快照
                    while (_queue.Count > MaxQueueSize)
                    {
                        _queue.TryDequeue(out _);
                    }
                }
                catch (Exception ex)
                {
                    Faulted?.Invoke(this, ex);
                }

                _stopRequested.Wait(RefreshMs);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(BackgroundDataProducer));
            }
        }
    }

    public sealed class UiThreadConsumer : IDisposable
    {
        private readonly ConcurrentQueue<UiSnapshot> _queue;
        private readonly Action<UiSnapshot> _applySnapshot;
        private readonly SynchronizationContext? _uiContext;
        private readonly System.Threading.Timer _timer;
        private bool _disposed;

        public UiThreadConsumer(
            ConcurrentQueue<UiSnapshot> queue,
            int refreshMs,
            Action<UiSnapshot> applySnapshot,
            SynchronizationContext? uiContext = null)
        {
            if (queue == null)
            {
                throw new ArgumentNullException(nameof(queue));
            }

            if (applySnapshot == null)
            {
                throw new ArgumentNullException(nameof(applySnapshot));
            }

            if (refreshMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(refreshMs), refreshMs, "RefreshMs must be greater than zero.");
            }

            _queue = queue;
            _applySnapshot = applySnapshot;
            _uiContext = uiContext ?? SynchronizationContext.Current;
            if (_uiContext == null)
            {
                throw new InvalidOperationException(
                    "UiThreadConsumer requires a SynchronizationContext. " +
                    "Construct on the UI thread or pass a valid SynchronizationContext.");
            }
            _timer = new System.Threading.Timer(OnTimerTick, null, Timeout.Infinite, Timeout.Infinite);
            RefreshMs = refreshMs;
        }

        public int RefreshMs { get; }

        public void Start()
        {
            ThrowIfDisposed();
            _timer.Change(0, RefreshMs);
        }

        public void Stop()
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            _timer.Dispose();
            _disposed = true;
        }

        private void OnTimerTick(object? state)
        {
            UiSnapshot? latest = null;
            bool forceLayoutRebuild = false;
            bool forceRender = false;
            UiSnapshot? current;
            while (_queue.TryDequeue(out current))
            {
                if (current != null)
                {
                    latest = current;
                    forceLayoutRebuild = forceLayoutRebuild || current.ForceLayoutRebuild;
                    forceRender = forceRender || current.ForceRender;
                }
            }

            if (latest == null)
            {
                return;
            }

            if (forceLayoutRebuild != latest.ForceLayoutRebuild || forceRender != latest.ForceRender)
            {
                latest = new UiSnapshot(
                    latest.Groups,
                    latest.Columns,
                    latest.Alerts,
                    latest.TextValues,
                    forceLayoutRebuild,
                    forceRender);
            }

            _uiContext!.Post(ApplySnapshotOnUiThread, latest);
        }

        private void ApplySnapshotOnUiThread(object? state)
        {
            UiSnapshot? snapshot = state as UiSnapshot;
            if (snapshot != null)
            {
                _applySnapshot(snapshot);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(UiThreadConsumer));
            }
        }
    }
}
