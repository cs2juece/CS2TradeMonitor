using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Infrastructure.Paths;
using CS2TradeMonitor.Infrastructure.Security;
using CS2TradeMonitor.Infrastructure.System;

namespace CS2TradeMonitor.Infrastructure.Diagnostics
{
    public static class DetailedDiagnosticsRuntime
    {
        private static readonly object Sync = new();
        private static DetailedDiagnosticsService? _service;

        public static bool IsEnabled => Volatile.Read(ref _service)?.IsEnabledFast == true;

        public static IDetailedDiagnosticsService Service
            => Volatile.Read(ref _service)
                ?? throw new InvalidOperationException("详细诊断服务尚未初始化。");

        internal static DetailedDiagnosticsService? Current => Volatile.Read(ref _service);

        public static void Initialize()
        {
            if (Volatile.Read(ref _service) is not null)
                return;
            lock (Sync)
            {
                if (_service is not null)
                    return;
                var service = new DetailedDiagnosticsService(
                    InstanceRuntimeContext.Current,
                    new SystemClock(),
                    new DpapiSecureDataProtector());
                service.Initialize();
                _service = service;
            }
        }

        internal static void Record(
            string level,
            string module,
            string eventName,
            IReadOnlyDictionary<string, object?>? data = null,
            string? correlation = null,
            DetailedDiagnosticPriority priority = DetailedDiagnosticPriority.Normal)
        {
            DetailedDiagnosticsService? service = Volatile.Read(ref _service);
            if (service is null)
                return;
            correlation ??= DetailedDiagnosticOperationContext.CurrentOperationId;
            service.Record(level, module, eventName, data, correlation, priority);
        }

        public static void Shutdown()
        {
            DetailedDiagnosticsService? service = Volatile.Read(ref _service);
            service?.Shutdown();
        }

        internal static void ResetForTests()
        {
            lock (Sync)
            {
                _service?.Dispose();
                _service = null;
            }
        }
    }
}
