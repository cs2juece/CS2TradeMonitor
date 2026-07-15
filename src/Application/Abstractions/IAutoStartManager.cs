namespace CS2TradeMonitor.Application.Abstractions
{
    public interface IAutoStartManager
    {
        bool Set(bool enabled, bool showErrorMessage = true);

        bool IsEnabled();

        bool IsEnabledForCurrentExe();

        string GetStatusSummary();

        bool RepairIfNeeded(bool enabled, bool showErrorMessage = false);
    }
}
