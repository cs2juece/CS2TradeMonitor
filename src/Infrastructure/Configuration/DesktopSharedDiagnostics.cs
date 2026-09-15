using CS2TradeMonitor.Shared.Ports;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Infrastructure.Configuration;

public sealed class DesktopSharedDiagnostics : IAppDiagnostics
{
    public ValueTask ReportAsync(
        DiagnosticEvent diagnosticEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        cancellationToken.ThrowIfCancellationRequested();

        if (diagnosticEvent.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Critical)
        {
            DiagnosticsLogger.Error(diagnosticEvent.Code, diagnosticEvent.Message);
        }
        else
        {
            DiagnosticsLogger.Info(diagnosticEvent.Code, diagnosticEvent.Message);
        }

        return ValueTask.CompletedTask;
    }
}
