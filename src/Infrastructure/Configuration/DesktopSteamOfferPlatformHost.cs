using System.Text.Json;
using CS2TradeMonitor.Application.Notify;
using CS2TradeMonitor.Application.Market;
using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.Shared.Trading;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Infrastructure.Configuration;

public sealed class DesktopSteamOfferPlatformHost : ISteamOfferPlatformHost
{
    private DesktopSteamOfferPlatformHost()
    {
    }

    public static DesktopSteamOfferPlatformHost Instance { get; } = new();
    public JsonSerializerOptions JsonSerializerOptions => DesktopAutoConfirmationRuntime.Instance.JsonSerializerOptions;
    public string BackgroundTrigger => SteamOfferAuditLog.TriggerBackgroundAuto;
    public string TriggerUserManual => SteamOfferAuditLog.TriggerUserManual;
    public string GetDataFilePath(string fileName) => DesktopAutoConfirmationRuntime.Instance.GetDataFilePath(fileName);
    public void WriteTextAtomic(string path, string content) => DesktopAutoConfirmationRuntime.Instance.WriteTextAtomic(path, content);
    public string RedactSecrets(string? text) => SteamOfferAuditLog.RedactSecrets(text);
    public void LogAutoTradeStarted(bool enabled, int intervalSeconds) => SteamOfferAuditLog.LogAutoTradeStarted(enabled, intervalSeconds);
    public void LogAutoTradeFailure(string reason) => SteamOfferAuditLog.LogAutoTradeFailure(reason);
    public void Error(string message, Exception? exception = null) => SteamOfferAuditLog.Error(message, exception);
    public void InfoThrottled(string key, string message, TimeSpan interval) => SteamOfferAuditLog.InfoThrottled(key, message, interval);
    public void DiagnosticError(string message, Exception? exception = null) => SteamOfferAuditLog.DiagnosticError(message, exception);
    public void LogRefreshResult(bool success, int count, string message) => SteamOfferAuditLog.LogRefreshResult(success, count, message);
    public void LogImportToken(string steamId, string sourceKind) => SteamOfferAuditLog.LogImportToken(steamId, sourceKind);
    public void LogAcceptOffer(string tradeOfferId, bool safe, bool verifiedByYouPin, string platformOrderNo, string trigger = "")
        => SteamOfferAuditLog.LogAcceptOffer(
            tradeOfferId,
            safe,
            verifiedByYouPin,
            platformOrderNo,
            string.IsNullOrWhiteSpace(trigger) ? TriggerUserManual : trigger);
    public void LogDenyOffer(string tradeOfferId) => SteamOfferAuditLog.LogDenyOffer(tradeOfferId);
    public void LogMobileConfirmation(string tradeOfferId, string platformOrderNo, string trigger, string message)
        => SteamOfferAuditLog.LogMobileConfirmation(tradeOfferId, platformOrderNo, trigger, message);
    public void LogMobileConfirmationSubmissionStarted() => SteamOfferAuditLog.LogMobileConfirmationSubmissionStarted();
    public void LogMobileConfirmationSubmissionCompleted(bool success, Exception? exception = null)
        => SteamOfferAuditLog.LogMobileConfirmationSubmissionCompleted(success, exception);
    public void LogMobileConfirmationMatchEvaluation(int confirmationCount, int sameOfferIdCount, bool matched)
        => SteamOfferAuditLog.LogMobileConfirmationMatchEvaluation(confirmationCount, sameOfferIdCount, matched);
    public string BuildNetworkFailureMessage(string platform, string operation, Exception exception)
        => NetworkDiagnostics.BuildFailureMessage(platform, operation, exception);
    public void ReportConnectionSuccess() => SteamConnectionResolver.Instance.ReportSuccess();
    public void ReportConnectionFailure(string message) => SteamConnectionResolver.Instance.ReportFailure(message);
    public string ResolveLocalItemName(string marketHashName)
        => SteamDtLocalItemNameResolver.ResolveNameByMarketHashName(marketHashName);

    public void NotifySteamLoginExpired(string title, string message)
    {
        Settings settings = Settings.Load();
        if (!settings.DoNotDisturbEnabled)
        {
            AppNotificationHub.Instance.Request(
                title,
                message,
                AppNotificationSeverity.Warning,
                AppNotificationPlacement.Desktop);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (PhoneAlertDispatchService.IsConfigured(settings))
                    await PhoneAlertDispatchService.Instance.SendConfiguredAsync(settings, title, message).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                InfoThrottled(
                    "steam-login-expired-phone-alert-failed",
                    "Steam login expired phone alert failed: " + RedactSecrets(exception.Message),
                    TimeSpan.FromMinutes(10));
            }
        });
    }
}
