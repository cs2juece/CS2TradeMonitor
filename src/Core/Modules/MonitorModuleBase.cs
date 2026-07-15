using System;
using System.Threading;
using System.Threading.Tasks;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.src.Core.Modules
{
    public abstract class MonitorModuleBase : IMonitorModule
    {
        private readonly object _sync = new();
        private MonitorModuleHealth _health;

        protected MonitorModuleBase(MonitorModuleDescriptor descriptor)
        {
            Descriptor = descriptor;
            _health = CreateHealth(MonitorModuleState.NotStarted, "未启动");
        }

        public string Id => Descriptor.Id;
        public string DisplayName => Descriptor.DisplayName;
        protected MonitorModuleDescriptor Descriptor { get; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            SetHealth(MonitorModuleState.Starting, "正在启动");
            try
            {
                await StartModuleAsync(cancellationToken).ConfigureAwait(false);
                SetHealth(MonitorModuleState.Running, "运行中");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                SetHealth(MonitorModuleState.Paused, "启动已取消");
                throw;
            }
            catch (Exception ex)
            {
                SetHealth(MonitorModuleState.Faulted, "启动失败：" + DiagnosticsLogger.Redact(ex.Message));
                throw;
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            SetHealth(MonitorModuleState.Stopping, "正在停止");
            try
            {
                await StopModuleAsync(cancellationToken).ConfigureAwait(false);
                SetHealth(MonitorModuleState.Stopped, "已停止");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                SetHealth(MonitorModuleState.Paused, "停止已取消");
                throw;
            }
            catch (Exception ex)
            {
                SetHealth(MonitorModuleState.Faulted, "停止失败：" + DiagnosticsLogger.Redact(ex.Message));
                throw;
            }
        }

        public MonitorModuleHealth GetHealth()
        {
            lock (_sync)
            {
                return _health;
            }
        }

        protected virtual Task StartModuleAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        protected virtual Task StopModuleAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        protected void PauseModule(string reason)
        {
            SetHealth(MonitorModuleState.Paused, DiagnosticsLogger.Redact(reason));
        }

        private void SetHealth(MonitorModuleState state, string message)
        {
            lock (_sync)
            {
                _health = CreateHealth(state, message);
            }
        }

        private MonitorModuleHealth CreateHealth(MonitorModuleState state, string message)
        {
            return new MonitorModuleHealth
            {
                Id = Descriptor.Id,
                DisplayName = Descriptor.DisplayName,
                State = state,
                Message = message,
                LastChanged = DateTimeOffset.Now,
                Scope = Descriptor.ScopeSummary,
                IsHighRisk = Descriptor.IsHighRisk,
                ProcessIsolationCandidate = Descriptor.ProcessIsolationCandidate
            };
        }
    }
}
