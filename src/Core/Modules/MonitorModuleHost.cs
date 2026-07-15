using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.src.Core.Modules
{
    public sealed class MonitorModuleHost : IMonitorModuleHost
    {
        private readonly List<IMonitorModule> _modules;
        private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
        private bool _started;

        private MonitorModuleHost(IEnumerable<IMonitorModule> modules)
        {
            _modules = modules.ToList();
        }

        public static MonitorModuleHost Instance { get; } =
            new MonitorModuleHost(MonitorModuleRegistry.CreateModules());

        public IReadOnlyList<IMonitorModule> Modules => _modules;

        public bool IsStarted
        {
            get
            {
                _lifecycleLock.Wait();
                try
                {
                    return _started;
                }
                finally
                {
                    _lifecycleLock.Release();
                }
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_started)
                    return;

                foreach (var module in _modules)
                {
                    try
                    {
                        await module.StartAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        DiagnosticsLogger.Info("ModuleHost", $"Module startup cancelled: {module.Id}");
                        break;
                    }
                    catch (Exception ex)
                    {
                        DiagnosticsLogger.Error("ModuleHost", $"Module startup failed and was isolated: {module.Id}", ex);
                    }
                }

                _started = true;
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_started)
                    return;

                foreach (var module in _modules.AsEnumerable().Reverse())
                {
                    try
                    {
                        await module.StopAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        DiagnosticsLogger.Info("ModuleHost", $"Module stop cancelled: {module.Id}");
                        break;
                    }
                    catch (Exception ex)
                    {
                        DiagnosticsLogger.Error("ModuleHost", $"Module stop failed and was isolated: {module.Id}", ex);
                    }
                }

                _started = false;
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task<MonitorModuleHealth> RestartModuleAsync(string moduleId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(moduleId))
                throw new ArgumentException("模块 ID 不能为空。", nameof(moduleId));

            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var module = _modules.FirstOrDefault(x => string.Equals(x.Id, moduleId, StringComparison.OrdinalIgnoreCase));
                if (module == null)
                    throw new InvalidOperationException("未知监控模块: " + moduleId);

                DiagnosticsLogger.Info("ModuleHost", $"Restarting monitor module: {module.Id}");
                try
                {
                    await module.StopAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    DiagnosticsLogger.Error("ModuleHost", $"Module stop during restart failed and was isolated: {module.Id}", ex);
                }

                try
                {
                    await module.StartAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    DiagnosticsLogger.Error("ModuleHost", $"Module restart failed and was isolated: {module.Id}", ex);
                }

                return SafeGetHealth(module);
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public MonitorModuleHealth? GetHealth(string moduleId)
        {
            if (string.IsNullOrWhiteSpace(moduleId))
                return null;

            var module = _modules.FirstOrDefault(x => string.Equals(x.Id, moduleId, StringComparison.OrdinalIgnoreCase));
            return module == null ? null : SafeGetHealth(module);
        }

        public IReadOnlyList<MonitorModuleHealth> GetHealthSnapshot()
        {
            return _modules.Select(SafeGetHealth).ToArray();
        }

        private static MonitorModuleHealth SafeGetHealth(IMonitorModule module)
        {
            try
            {
                return module.GetHealth();
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("ModuleHost", $"Reading module health failed: {module.Id}", ex);
                return new MonitorModuleHealth
                {
                    Id = module.Id,
                    DisplayName = module.DisplayName,
                    State = MonitorModuleState.Faulted,
                    Message = "健康状态读取失败：" + DiagnosticsLogger.Redact(ex.Message),
                    LastChanged = DateTimeOffset.Now
                };
            }
        }
    }
}
