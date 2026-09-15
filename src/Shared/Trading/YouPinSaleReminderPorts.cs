using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;

namespace CS2TradeMonitor.Shared.Trading
{
    public interface IYouPinCredentialSource
    {
        YouPinCredential? GetCredential(Settings? settings = null);
    }

    public interface IYouPinHttpClientFactory
    {
        HttpClient Create(int timeoutSeconds = 20);

        HttpClient Create(int timeoutSeconds, Uri baseAddress)
        {
            ArgumentNullException.ThrowIfNull(baseAddress);
            HttpClient client = Create(timeoutSeconds);
            client.BaseAddress = baseAddress;
            return client;
        }
    }

    public interface IDeviceIdentityProvider
    {
        string GetMachineFingerprint();
    }

    public interface IYouPinAuthHost
    {
        string GetSecureCredentialLocation(string fileName);
        bool CredentialExists(string location);
        DateTime GetCredentialLastWriteTimeUtc(string location);
        string ReadCredentialText(string location);
        void DeleteCredential(string location);
        void WriteCredentialTextAtomic(string location, string content);
        Settings LoadSettings(bool forceReload = false);
        void SaveSettings(Settings settings);
        byte[] ProtectCredential(byte[] plainBytes, byte[] entropy);
        byte[] UnprotectCredential(byte[] protectedBytes, byte[] entropy);
        void Ignored(
            string source,
            string operation,
            Exception exception,
            bool retryable,
            string category);
    }

    public interface IYouPinSaleReminderHost
    {
        string UserManualTrigger { get; }
        string UserCheckNowTrigger { get; }
        string BackgroundAutoTrigger { get; }
        string InstallDirectory { get; }

        string GetDataFilePath(string fileName);
        void WriteTextAtomic(string path, string content);
        Task WaitForTradeWriteAsync(string key, CancellationToken cancellationToken = default);

        string EnsureQuoteLogFile();
        void LogTradeAction(
            string trigger,
            string action,
            string result,
            string orderNo,
            string tradeOfferId,
            string message);

        void Info(string source, string message);
        void InfoThrottled(string source, string key, string message, TimeSpan window);
        void Error(string source, string message, Exception? exception = null);
        void Ignored(
            string source,
            string operation,
            Exception exception,
            bool retryable,
            string category);

        void ShowNotification(
            string title,
            string message,
            bool warning,
            bool playSound = false,
            bool showToast = true);

        bool IsPhoneAlertConfigured(Settings settings);
        Task SendPhoneAlertAsync(Settings settings, string title, string message);
    }

    public interface IYouPinMobileApiHost : IDeviceIdentityProvider
    {
        string GetSecureFilePath(string fileName);
        void WriteTextAtomic(string path, string content);
        string Redact(string? text);
        void InfoThrottled(string source, string key, string message, TimeSpan window);
        void Error(string source, string message, Exception? exception = null);
    }

    internal static class YouPinSaleReminderPlatform
    {
        private static IYouPinSaleReminderHost? _host;
        private static Func<YouPinSaleReminderService>? _factory;

        internal static IYouPinSaleReminderHost Host => Volatile.Read(ref _host)
            ?? throw new InvalidOperationException("当前平台尚未配置悠悠报价宿主适配器。");

        internal static void Configure(
            IYouPinSaleReminderHost host,
            Func<YouPinSaleReminderService> factory)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(factory);
            Volatile.Write(ref _host, host);
            Volatile.Write(ref _factory, factory);
        }

        internal static YouPinSaleReminderService CreateService()
        {
            Func<YouPinSaleReminderService>? factory = Volatile.Read(ref _factory);
            return factory?.Invoke()
                ?? throw new InvalidOperationException("当前平台尚未配置悠悠报价服务工厂。");
        }
    }

    internal static class YouPinMobileApiPlatform
    {
        private static IYouPinMobileApiHost? _host;

        internal static IYouPinMobileApiHost Host => Volatile.Read(ref _host)
            ?? throw new InvalidOperationException("当前平台尚未配置悠悠设备宿主适配器。");

        internal static void Configure(IYouPinMobileApiHost host)
        {
            ArgumentNullException.ThrowIfNull(host);
            Volatile.Write(ref _host, host);
        }
    }

    internal static class YouPinAuthPlatform
    {
        private static IYouPinAuthHost? _host;
        private static Func<YouPinAuthService>? _factory;

        internal static IYouPinAuthHost Host => Volatile.Read(ref _host)
            ?? throw new InvalidOperationException("当前平台尚未配置悠悠登录宿主适配器。");

        internal static void Configure(
            IYouPinAuthHost host,
            Func<YouPinAuthService> factory)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(factory);
            Volatile.Write(ref _host, host);
            Volatile.Write(ref _factory, factory);
        }

        internal static YouPinAuthService CreateService()
        {
            Func<YouPinAuthService>? factory = Volatile.Read(ref _factory);
            return factory?.Invoke()
                ?? throw new InvalidOperationException("当前平台尚未配置悠悠登录服务工厂。");
        }
    }
}
