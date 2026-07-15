using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Infrastructure.Windows
{
    public sealed class AutoStartManagerAdapter : IAutoStartManager
    {
        public bool Set(bool enabled, bool showErrorMessage = true)
            => AutoStart.Set(enabled, showErrorMessage);

        public bool IsEnabled()
            => AutoStart.IsEnabled();

        public bool IsEnabledForCurrentExe()
            => AutoStart.IsEnabledForCurrentExe();

        public string GetStatusSummary()
            => AutoStart.GetStatusSummary();

        public bool RepairIfNeeded(bool enabled, bool showErrorMessage = false)
            => AutoStart.RepairIfNeeded(enabled, showErrorMessage);
    }
}
