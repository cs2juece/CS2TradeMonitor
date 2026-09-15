using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.Win32;
using CS2TradeMonitor.Application;
using CS2TradeMonitor.Application.Notify;
using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Shared.Trading;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.Core;

namespace CS2TradeMonitor.Infrastructure.Configuration
{
    internal sealed class DesktopYouPinSaleReminderHost : IYouPinSaleReminderHost, IYouPinMobileApiHost, IYouPinAuthHost
    {
        internal static DesktopYouPinSaleReminderHost Instance { get; } = new();

        private DesktopYouPinSaleReminderHost()
        {
        }

        public string UserManualTrigger => SteamOfferAuditLog.TriggerUserManual;
        public string UserCheckNowTrigger => SteamOfferAuditLog.TriggerUserCheckNow;
        public string BackgroundAutoTrigger => SteamOfferAuditLog.TriggerBackgroundAuto;
        public string InstallDirectory => InstallationPaths.InstallDirectory;

        public string GetDataFilePath(string fileName) => RuntimeDataPaths.GetDataFilePath(fileName);
        public string GetSecureFilePath(string fileName) => RuntimeDataPaths.GetSecureFilePath(fileName);
        public string GetSecureCredentialLocation(string fileName) => RuntimeDataPaths.GetSecureFilePath(fileName);
        public void WriteTextAtomic(string path, string content) => RuntimeDataPaths.WriteTextAtomic(path, content);
        public bool CredentialExists(string location) => File.Exists(location);
        public DateTime GetCredentialLastWriteTimeUtc(string location) => File.GetLastWriteTimeUtc(location);
        public string ReadCredentialText(string location) => File.ReadAllText(location);
        public void DeleteCredential(string location) => File.Delete(location);
        public void WriteCredentialTextAtomic(string location, string content)
            => RuntimeDataPaths.WriteTextAtomic(location, content);
        public Settings LoadSettings(bool forceReload = false) => Settings.Load(forceReload);
        public void SaveSettings(Settings settings) => _ = settings.Save();
        public byte[] ProtectCredential(byte[] plainBytes, byte[] entropy)
            => ProtectedData.Protect(plainBytes, entropy, DataProtectionScope.CurrentUser);
        public byte[] UnprotectCredential(byte[] protectedBytes, byte[] entropy)
            => ProtectedData.Unprotect(protectedBytes, entropy, DataProtectionScope.CurrentUser);
        public Task WaitForTradeWriteAsync(string key, CancellationToken cancellationToken = default)
            => TradeWriteOperationGate.WaitAsync(key, cancellationToken);

        public string EnsureQuoteLogFile() => SteamOfferAuditLog.EnsureLogFile();

        public void LogTradeAction(
            string trigger,
            string action,
            string result,
            string orderNo,
            string tradeOfferId,
            string message)
            => SteamOfferAuditLog.LogTradeAction(
                SteamOfferAuditLog.SystemYouPin,
                trigger,
                action,
                result,
                orderNo,
                tradeOfferId,
                message);

        public void Info(string source, string message) => DiagnosticsLogger.Info(source, message);

        public void InfoThrottled(string source, string key, string message, TimeSpan window)
            => DiagnosticsLogger.InfoThrottled(source, key, message, window);

        public void Error(string source, string message, Exception? exception = null)
            => DiagnosticsLogger.Error(source, message, exception);

        public void Ignored(
            string source,
            string operation,
            Exception exception,
            bool retryable,
            string category)
            => DiagnosticsLogger.Ignored(source, operation, exception, retryable, category);

        public void ShowNotification(
            string title,
            string message,
            bool warning,
            bool playSound = false,
            bool showToast = true)
            => AppNotificationHub.Instance.Request(
                title,
                message,
                warning ? AppNotificationSeverity.Warning : AppNotificationSeverity.Info,
                AppNotificationPlacement.Desktop,
                playSound,
                showToast: showToast);

        public bool IsPhoneAlertConfigured(Settings settings)
            => PhoneAlertDispatchService.IsConfigured(settings);

        public Task SendPhoneAlertAsync(Settings settings, string title, string message)
            => PhoneAlertDispatchService.Instance.SendConfiguredAsync(settings, title, message);

        public string GetMachineFingerprint()
        {
            try
            {
                string? value = Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography",
                    "MachineGuid",
                    null)?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            catch (Exception exception)
            {
                DiagnosticsLogger.Ignored(
                    "YouPin",
                    "GetMachineGuid",
                    exception,
                    retryable: true,
                    category: "DeviceFingerprint");
            }

            return Environment.MachineName + "|" + Environment.UserName;
        }

        public string Redact(string? text) => DiagnosticsLogger.Redact(text);
    }

    internal static class DesktopYouPinSaleReminderRegistration
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            DesktopYouPinSaleReminderHost host = DesktopYouPinSaleReminderHost.Instance;
            YouPinAuthService.ConfigurePlatform(
                new DesktopYouPinAuthHttpClientFactory(),
                host,
                host);
            YouPinMobileApiPlatform.Configure(host);
            YouPinSaleReminderPlatform.Configure(
                host,
                () =>
                {
                    YouPinServiceRuntimeServices services = YouPinServiceRuntimeServices.Resolve();
                    return new YouPinSaleReminderService(
                        services.Auth,
                        services.DomesticHttpFactory,
                        host);
                });
        }
    }

    internal sealed class DesktopYouPinAuthHttpClientFactory : IYouPinHttpClientFactory
    {
        public HttpClient Create(int timeoutSeconds = 20)
            => YouPinServiceRuntimeServices.ResolveDomesticHttpFactory().Create(timeoutSeconds);
    }
}
