namespace CS2TradeMonitor.src.Core.Modules
{
    public interface IMonitorModuleHost
    {
        IReadOnlyList<IMonitorModule> Modules { get; }

        bool IsStarted { get; }

        Task StartAsync(CancellationToken cancellationToken);

        Task StopAsync(CancellationToken cancellationToken);

        Task<MonitorModuleHealth> RestartModuleAsync(string moduleId, CancellationToken cancellationToken);

        MonitorModuleHealth? GetHealth(string moduleId);

        IReadOnlyList<MonitorModuleHealth> GetHealthSnapshot();
    }
}
