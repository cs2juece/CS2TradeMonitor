using CS2TradeMonitor.Shared.Ports;

namespace CS2TradeMonitor.Infrastructure.Configuration;

public sealed class DesktopSharedClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        => Task.Delay(delay, cancellationToken);
}
