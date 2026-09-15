using System.Text.Json;

namespace CS2TradeMonitor.Shared.Trading;

/// <summary>
/// Operating-system and diagnostics seam used by the shared Steam offer
/// orchestration. Trading rules never live in this host.
/// </summary>
public interface ISteamOfferPlatformHost : IAutoConfirmationRuntime, IAutoConfirmationAuditLog
{
    string TriggerUserManual { get; }

    void InfoThrottled(string key, string message, TimeSpan interval);

    void LogRefreshResult(bool success, int count, string message);

    void LogImportToken(string steamId, string sourceKind);

    void LogAcceptOffer(
        string tradeOfferId,
        bool safe,
        bool verifiedByYouPin,
        string platformOrderNo,
        string trigger = "");

    void LogDenyOffer(string tradeOfferId);

    void LogMobileConfirmation(
        string tradeOfferId,
        string platformOrderNo,
        string trigger,
        string message);

    void LogMobileConfirmationSubmissionStarted();

    void LogMobileConfirmationSubmissionCompleted(bool success, Exception? exception = null);

    void LogMobileConfirmationMatchEvaluation(int confirmationCount, int sameOfferIdCount, bool matched);

    string BuildNetworkFailureMessage(string platform, string operation, Exception exception);

    void ReportConnectionSuccess();

    void ReportConnectionFailure(string message);

    string ResolveLocalItemName(string marketHashName);

    void NotifySteamLoginExpired(string title, string message);
}

public static class SteamOfferPlatform
{
    private static readonly object Gate = new();
    private static ISteamOfferPlatformHost _host = UnconfiguredSteamOfferPlatformHost.Instance;

    public static ISteamOfferPlatformHost Host
    {
        get
        {
            lock (Gate)
                return _host;
        }
    }

    public static void Configure(ISteamOfferPlatformHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        lock (Gate)
            _host = host;
    }

    private sealed class UnconfiguredSteamOfferPlatformHost : ISteamOfferPlatformHost
    {
        public static UnconfiguredSteamOfferPlatformHost Instance { get; } = new();
        public JsonSerializerOptions JsonSerializerOptions { get; } = new(JsonSerializerDefaults.Web);
        public string BackgroundTrigger => "后台自动";
        public string TriggerUserManual => "用户手动";
        public string GetDataFilePath(string fileName) => "";
        public void WriteTextAtomic(string path, string content) { }
        public string RedactSecrets(string? text) => string.IsNullOrWhiteSpace(text) ? "" : "已隐去敏感信息";
        public void LogAutoTradeStarted(bool enabled, int intervalSeconds) { }
        public void LogAutoTradeFailure(string reason) { }
        public void Error(string message, Exception? exception = null) { }
        public void InfoThrottled(string key, string message, TimeSpan interval) { }
        public void DiagnosticError(string message, Exception? exception = null) { }
        public void LogRefreshResult(bool success, int count, string message) { }
        public void LogImportToken(string steamId, string sourceKind) { }
        public void LogAcceptOffer(string tradeOfferId, bool safe, bool verifiedByYouPin, string platformOrderNo, string trigger = "") { }
        public void LogDenyOffer(string tradeOfferId) { }
        public void LogMobileConfirmation(string tradeOfferId, string platformOrderNo, string trigger, string message) { }
        public void LogMobileConfirmationSubmissionStarted() { }
        public void LogMobileConfirmationSubmissionCompleted(bool success, Exception? exception = null) { }
        public void LogMobileConfirmationMatchEvaluation(int confirmationCount, int sameOfferIdCount, bool matched) { }
        public string BuildNetworkFailureMessage(string platform, string operation, Exception exception)
            => $"{platform} {operation} 网络请求失败。";
        public void ReportConnectionSuccess() { }
        public void ReportConnectionFailure(string message) { }
        public string ResolveLocalItemName(string marketHashName) => "";
        public void NotifySteamLoginExpired(string title, string message) { }
    }
}
