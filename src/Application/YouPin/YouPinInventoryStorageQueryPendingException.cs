namespace CS2TradeMonitor.Application.YouPin
{
    internal sealed class YouPinInventoryStorageQueryPendingException : InvalidOperationException
    {
        public static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(3);

        public YouPinInventoryStorageQueryPendingException(
            string message,
            TimeSpan? retryAfter = null)
            : base(message)
        {
            RetryAfter = retryAfter ?? DefaultRetryAfter;
        }

        public TimeSpan RetryAfter { get; }

        public static bool IsMatch(string? message)
        {
            string value = message ?? string.Empty;
            bool isQuerying = value.Contains("正在查询", StringComparison.Ordinal)
                || value.Contains("查询中", StringComparison.Ordinal);
            bool asksToWait = value.Contains("请稍等", StringComparison.Ordinal)
                || value.Contains("稍后", StringComparison.Ordinal);
            return isQuerying && asksToWait;
        }
    }
}
