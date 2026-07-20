using System;
using System.Threading;
using System.Threading.Tasks;

namespace CS2TradeMonitor.src.Core.Lifecycle
{
    internal sealed class PeriodicAsyncSingleFlight
    {
        private int _running;

        public async Task<bool> TryRunAsync(Func<Task> work)
        {
            ArgumentNullException.ThrowIfNull(work);

            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
                return false;

            try
            {
                await work().ConfigureAwait(false);
                return true;
            }
            finally
            {
                Volatile.Write(ref _running, 0);
            }
        }
    }
}
