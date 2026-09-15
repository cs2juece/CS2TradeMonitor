namespace CS2TradeMonitor.Application.YouPin
{
    // Only an explicit HTTP 429 (or its local endpoint cooldown) is retryable.
    // Ambiguous writes and platform risk-control responses keep their existing handling.
    internal sealed class YouPinRateLimitException : InvalidOperationException
    {
        public YouPinRateLimitException(string message, TimeSpan retryAfter) : base(message)
        {
            RetryAfter = retryAfter > TimeSpan.Zero ? retryAfter : TimeSpan.FromMinutes(5);
        }

        public TimeSpan RetryAfter { get; }
    }
}
