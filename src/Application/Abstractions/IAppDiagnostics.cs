namespace CS2TradeMonitor.Application.Abstractions
{
    public interface IAppDiagnostics
    {
        string LogFilePath { get; }

        string GetDiagnosticFilePath(string fileName);

        void Info(string source, string message);

        void InfoThrottled(string source, string key, string message, TimeSpan window);

        void Error(string source, string message, Exception? exception = null);

        void Ignored(string source, string operation, Exception exception, bool retryable = false, string category = "BestEffort");

        string Redact(string? text);
    }
}
