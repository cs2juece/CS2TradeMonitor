namespace CS2TradeMonitor.Application.YouPin
{
    internal sealed class YouPinLandlordWriteCoordinator : IDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly TimeSpan _minimumInterval;
        private DateTime _lastGrantUtc = DateTime.MinValue;

        public YouPinLandlordWriteCoordinator(TimeSpan minimumInterval)
        {
            _minimumInterval = minimumInterval < TimeSpan.Zero ? TimeSpan.Zero : minimumInterval;
        }

        public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                TimeSpan wait = (_lastGrantUtc + _minimumInterval) - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                _lastGrantUtc = DateTime.UtcNow;
                return new Lease(_gate);
            }
            catch
            {
                _gate.Release();
                throw;
            }
        }

        public void Dispose() => _gate.Dispose();

        private sealed class Lease : IDisposable
        {
            private SemaphoreSlim? _gate;

            public Lease(SemaphoreSlim gate)
            {
                _gate = gate;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _gate, null)?.Release();
            }
        }
    }
}
