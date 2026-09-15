using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.Shared.Trading;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface IYouPinAuthService : IYouPinCredentialSource
    {
        YouPinAuthState GetState(Settings? settings = null);

        Task<YouPinSmsSendResult> SendSmsCodeAsync(string phone, Settings? settings = null);

        Task<YouPinLoginResult> CompleteSmsLoginAsync(string phone, string code, string sessionId, Settings? settings = null);

        Task<YouPinLoginResult> ValidateCurrentAsync(Settings? settings = null);

        void ClearCredential(Settings? settings = null, bool clearLegacy = true);

        string EnsureDeviceToken(Settings? settings = null);
    }
}
