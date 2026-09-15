using System.Security.Cryptography;
using System.Text;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.Shared.Trading;

namespace CS2TradeMonitor.Application.YouPin.PurchaseMonitoring;

public enum PurchaseAccountSource { Unselected, CurrentYouPin, Independent }

public sealed record PurchaseAccountBinding(PurchaseAccountSource Source, string IdentityHash)
{
    public static PurchaseAccountBinding Unselected { get; } = new(PurchaseAccountSource.Unselected, "");
    public void Validate()
    {
        if (!Enum.IsDefined(Source) || IdentityHash is null || (Source == PurchaseAccountSource.Unselected
            ? IdentityHash.Length != 0 : IdentityHash.Length != 64 || IdentityHash.Any(c => !char.IsAsciiHexDigit(c))))
            throw new InvalidDataException("读取账号选择无效。");
    }
}

public sealed class PurchaseAccounts
{
    private readonly IYouPinAuthService _current;
    private readonly IYouPinAuthService _independent;
    private string? _accessPause;
    public PurchaseAccounts(IYouPinAuthService current, IYouPinAuthService independent)
        => (_current, _independent) = (current, independent);

    public IYouPinAuthService Auth(PurchaseAccountSource source) => source switch
    {
        PurchaseAccountSource.CurrentYouPin => _current,
        PurchaseAccountSource.Independent => _independent,
        _ => throw new InvalidOperationException("请先选择读取账号：悠悠有品当前账号或独立监控账号。")
    };

    public PurchaseAccountBinding Bind(PurchaseAccountSource source)
        => Binding(source, RequireCredential(source));

    private static PurchaseAccountBinding Binding(PurchaseAccountSource source, YouPinCredential credential)
    {
        // A legacy credential without user ID is bound to its token digest until validated.
        string identity = string.IsNullOrWhiteSpace(credential.UserId)
            ? "credential:" + credential.Token : "user:" + credential.UserId;
        return new(source, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity))));
    }

    internal YouPinCredential Resolve(PurchaseAccountBinding binding)
    {
        if (Volatile.Read(ref _accessPause) is { } pause) throw new InvalidOperationException(pause);
        YouPinCredential credential = RequireCredential(binding.Source);
        if (binding != Binding(binding.Source, credential))
            throw new InvalidOperationException("读取账号已变更，已暂停扫描。请在店铺规则中确认账号并重新建立基线。");
        return credential;
    }

    internal void PauseAccess(string reason) => Volatile.Write(ref _accessPause, reason);
    internal void RetryManually() => Volatile.Write(ref _accessPause, null);

    public string Summary(PurchaseAccountBinding binding)
    {
        if (binding.Source == PurchaseAccountSource.Unselected) return "读取账号：待选择 · 在规则或恢复观察中选择";
        string source = binding.Source == PurchaseAccountSource.CurrentYouPin ? "悠悠有品当前账号" : "独立监控账号";
        try
        {
            if (binding != Bind(binding.Source)) return $"读取账号：{source} · 账号已变更，请在规则中确认";
            if (Volatile.Read(ref _accessPause) is not null) return $"读取账号：{source} · 访问已暂停，请验证登录后手动恢复";
            return $"读取账号：{source} · 凭据已保存";
        }
        catch (InvalidOperationException) { return $"读取账号：{source} · 未登录或凭据不可用"; }
    }

    private YouPinCredential RequireCredential(PurchaseAccountSource source)
        => Auth(source).GetCredential() is { Token.Length: > 0 } credential ? credential
            : throw new InvalidOperationException("所选读取账号尚未登录或凭据不可用，请登录后手动恢复观察。");

    public string Describe(PurchaseAccountSource source)
    {
        if (source == PurchaseAccountSource.Unselected) return "请选择读取账号";
        YouPinAuthState state = Auth(source).GetState();
        string label = source == PurchaseAccountSource.CurrentYouPin ? "悠悠有品当前账号" : "独立监控账号";
        if (!string.IsNullOrWhiteSpace(state.Error)) return $"{label} · 登录异常，请验证或重新登录";
        return state.HasCredential ? $"{label} · {state.NickName}\r\n凭据已保存，读取结果以扫描为准" : $"{label} · 未登录";
    }
}

/// <summary>Never accept the primary module's settings as a legacy credential source.</summary>
internal sealed class PurchaseIndependentAuth(IYouPinAuthService inner) : IYouPinAuthService
{
    public YouPinCredential? GetCredential(Settings? settings = null) => inner.GetCredential();
    public YouPinAuthState GetState(Settings? settings = null) => inner.GetState();
    public Task<YouPinSmsSendResult> SendSmsCodeAsync(string phone, Settings? settings = null) => inner.SendSmsCodeAsync(phone);
    public Task<YouPinLoginResult> CompleteSmsLoginAsync(string phone, string code, string sessionId, Settings? settings = null)
        => inner.CompleteSmsLoginAsync(phone, code, sessionId);
    public Task<YouPinLoginResult> ValidateCurrentAsync(Settings? settings = null) => inner.ValidateCurrentAsync();
    public void ClearCredential(Settings? settings = null, bool clearLegacy = true) => inner.ClearCredential(clearLegacy: clearLegacy);
    public string EnsureDeviceToken(Settings? settings = null) => inner.EnsureDeviceToken();
}

/// <summary>Independent login delegates encryption and device identity but never reads/writes main settings.</summary>
internal sealed class PurchaseAuthHost(IYouPinAuthHost inner) : IYouPinAuthHost
{
    private Settings _settings = new();
    public string GetSecureCredentialLocation(string fileName) => inner.GetSecureCredentialLocation(fileName);
    public bool CredentialExists(string location) => inner.CredentialExists(location);
    public DateTime GetCredentialLastWriteTimeUtc(string location) => inner.GetCredentialLastWriteTimeUtc(location);
    public string ReadCredentialText(string location) => inner.ReadCredentialText(location);
    public void DeleteCredential(string location) => inner.DeleteCredential(location);
    public void WriteCredentialTextAtomic(string location, string content) => inner.WriteCredentialTextAtomic(location, content);
    public Settings LoadSettings(bool forceReload = false) => _settings;
    public void SaveSettings(Settings settings) => _settings = settings;
    public byte[] ProtectCredential(byte[] plainBytes, byte[] entropy) => inner.ProtectCredential(plainBytes, entropy);
    public byte[] UnprotectCredential(byte[] protectedBytes, byte[] entropy) => inner.UnprotectCredential(protectedBytes, entropy);
    public void Ignored(string source, string operation, Exception exception, bool retryable, string category)
        => inner.Ignored(source, operation, exception, retryable, category);
}
