using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Infrastructure.Diagnostics
{
    public sealed class DiagnosticsLoggerAdapter : IAppDiagnostics
    {
        public string LogFilePath => DiagnosticsLogger.LogFilePath;

        public string GetDiagnosticFilePath(string fileName)
            => DiagnosticsLogger.GetDiagnosticFilePath(fileName);

        public void Info(string source, string message)
            => DiagnosticsLogger.Info(source, message);

        public void InfoThrottled(string source, string key, string message, TimeSpan window)
            => DiagnosticsLogger.InfoThrottled(source, key, message, window);

        public void Error(string source, string message, Exception? exception = null)
            => DiagnosticsLogger.Error(source, message, exception);

        public void Ignored(string source, string operation, Exception exception, bool retryable = false, string category = "BestEffort")
            => DiagnosticsLogger.Ignored(source, operation, exception, retryable, category);

        public string Redact(string? text)
            => DiagnosticsLogger.Redact(text);
    }
}
