using System.Text.Json;
using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.Shared.Trading;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Infrastructure.Configuration
{
    public sealed class DesktopAutoConfirmationRuntime : IAutoConfirmationRuntime
    {
        private DesktopAutoConfirmationRuntime()
        {
        }

        public static DesktopAutoConfirmationRuntime Instance { get; } = new();

        public JsonSerializerOptions JsonSerializerOptions => ServiceInfra.DefaultJsonOptions;

        public string GetDataFilePath(string fileName)
            => RuntimeDataPaths.GetDataFilePath(fileName);

        public void WriteTextAtomic(string path, string content)
            => RuntimeDataPaths.WriteTextAtomic(path, content);
    }

    public sealed class DesktopAutoConfirmationAuditLog : IAutoConfirmationAuditLog
    {
        private DesktopAutoConfirmationAuditLog()
        {
        }

        public static DesktopAutoConfirmationAuditLog Instance { get; } = new();

        public string BackgroundTrigger => SteamOfferAuditLog.TriggerBackgroundAuto;

        public string RedactSecrets(string? text)
            => SteamOfferAuditLog.RedactSecrets(text);

        public void LogAutoTradeStarted(bool enabled, int intervalSeconds)
            => SteamOfferAuditLog.LogAutoTradeStarted(enabled, intervalSeconds);

        public void LogAutoTradeFailure(string reason)
            => SteamOfferAuditLog.LogAutoTradeFailure(reason);

        public void Error(string message, Exception? exception = null)
            => SteamOfferAuditLog.Error(message, exception);

        public void DiagnosticError(string message, Exception? exception = null)
            => SteamOfferAuditLog.DiagnosticError(message, exception);
    }
}
