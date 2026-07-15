namespace CS2TradeMonitor.Infrastructure.Diagnostics
{
    internal sealed class DetailedDiagnosticsOptions
    {
        public static DetailedDiagnosticsOptions Production { get; } = new();

        public TimeSpan SessionDuration { get; init; } = TimeSpan.FromHours(48);
        public TimeSpan EndedSessionRetention { get; init; } = TimeSpan.FromDays(7);
        public long MaximumTotalBytes { get; init; } = 200L * 1024 * 1024;
        public int MaximumBodyBytes { get; init; } = 5 * 1024 * 1024;
        public int QueueCapacity { get; init; } = 4096;
    }
}
