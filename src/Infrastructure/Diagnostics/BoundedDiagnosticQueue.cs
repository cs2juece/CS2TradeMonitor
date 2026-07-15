using System.Threading.Channels;

namespace CS2TradeMonitor.Infrastructure.Diagnostics
{
    internal enum DetailedDiagnosticPriority
    {
        Normal,
        Critical
    }

    internal sealed record DetailedDiagnosticEnvelope(
        string SessionId,
        DateTime TimestampUtc,
        string Level,
        string Module,
        string EventName,
        global::System.Text.Json.Nodes.JsonNode? Data,
        string? Correlation,
        DetailedDiagnosticPriority Priority,
        bool EndsSession = false);

    internal sealed class BoundedDiagnosticQueue : IDisposable
    {
        private readonly Channel<DetailedDiagnosticEnvelope> _critical;
        private readonly Channel<DetailedDiagnosticEnvelope> _normal;
        private readonly SemaphoreSlim _signal = new(0);
        private readonly CancellationTokenSource _disposeCancellation = new();
        private readonly object _stateSync = new();
        private long _droppedCritical;
        private long _droppedNormal;
        private int _pendingCount;
        private int _inFlightCount;

        public BoundedDiagnosticQueue(int capacity)
        {
            if (capacity < 2)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            int criticalCapacity = Math.Max(1, capacity / 4);
            int normalCapacity = Math.Max(1, capacity - criticalCapacity);
            _critical = CreateChannel(criticalCapacity);
            _normal = CreateChannel(normalCapacity);
        }

        public int PendingCount
        {
            get
            {
                lock (_stateSync)
                    return Math.Max(0, _pendingCount);
            }
        }

        public bool IsIdle
        {
            get
            {
                lock (_stateSync)
                    return _pendingCount == 0 && _inFlightCount == 0;
            }
        }

        public long DroppedCount => Interlocked.Read(ref _droppedCritical) + Interlocked.Read(ref _droppedNormal);

        public bool TryEnqueue(DetailedDiagnosticEnvelope envelope)
        {
            ChannelWriter<DetailedDiagnosticEnvelope> writer = envelope.Priority == DetailedDiagnosticPriority.Critical
                ? _critical.Writer
                : _normal.Writer;
            lock (_stateSync)
            {
                if (!writer.TryWrite(envelope))
                {
                    if (envelope.Priority == DetailedDiagnosticPriority.Critical)
                        Interlocked.Increment(ref _droppedCritical);
                    else
                        Interlocked.Increment(ref _droppedNormal);
                    return false;
                }

                _pendingCount++;
            }

            _signal.Release();
            return true;
        }

        public async ValueTask<DetailedDiagnosticEnvelope> DequeueAsync(CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _disposeCancellation.Token);
            await _signal.WaitAsync(linked.Token).ConfigureAwait(false);
            if (!TryReadCore(out DetailedDiagnosticEnvelope? envelope))
                throw new InvalidOperationException("诊断队列信号与内容不一致。");
            MarkInFlight();
            return envelope!;
        }

        public bool TryDequeue(out DetailedDiagnosticEnvelope? envelope)
        {
            if (!_signal.Wait(0))
            {
                envelope = null;
                return false;
            }

            if (!TryReadCore(out envelope))
                return false;
            MarkInFlight();
            return true;
        }

        public void CompleteProcessing(int count)
        {
            if (count <= 0)
                return;

            lock (_stateSync)
                _inFlightCount = Math.Max(0, _inFlightCount - count);
        }

        public void Dispose()
        {
            _disposeCancellation.Cancel();
            _critical.Writer.TryComplete();
            _normal.Writer.TryComplete();
            _signal.Dispose();
            _disposeCancellation.Dispose();
        }

        private bool TryReadCore(out DetailedDiagnosticEnvelope? envelope)
        {
            if (_critical.Reader.TryRead(out envelope))
                return true;
            return _normal.Reader.TryRead(out envelope);
        }

        private void MarkInFlight()
        {
            lock (_stateSync)
            {
                _pendingCount = Math.Max(0, _pendingCount - 1);
                _inFlightCount++;
            }
        }

        private static Channel<DetailedDiagnosticEnvelope> CreateChannel(int capacity)
        {
            return Channel.CreateBounded<DetailedDiagnosticEnvelope>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
        }
    }
}
