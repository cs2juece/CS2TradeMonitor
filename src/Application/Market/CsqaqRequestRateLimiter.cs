namespace CS2TradeMonitor.Application.Market
{
    public sealed class CsqaqRequestRateLimiter
    {
        private static readonly TimeSpan DefaultMinimumInterval = TimeSpan.FromMilliseconds(1100);

        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly TimeSpan _minimumInterval;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private DateTimeOffset _lastPermitAt = DateTimeOffset.MinValue;

        private CsqaqRequestRateLimiter()
            : this(DefaultMinimumInterval, Task.Delay)
        {
        }

        internal CsqaqRequestRateLimiter(
            TimeSpan minimumInterval,
            Func<TimeSpan, CancellationToken, Task> delay)
        {
            _minimumInterval = minimumInterval < TimeSpan.Zero ? TimeSpan.Zero : minimumInterval;
            _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        }

        public static CsqaqRequestRateLimiter Instance { get; } = new();

        public async Task WaitAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                TimeSpan remaining = _minimumInterval - (DateTimeOffset.UtcNow - _lastPermitAt);
                if (remaining > TimeSpan.Zero)
                    await _delay(remaining, cancellationToken).ConfigureAwait(false);

                _lastPermitAt = DateTimeOffset.UtcNow;
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
