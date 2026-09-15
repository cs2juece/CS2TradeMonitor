namespace CS2TradeMonitor.Application.Steam.Auth;

/// <summary>
/// Credential-free Steam authentication status shared by desktop and Android
/// presentation hosts. Secret values remain inside the platform auth store.
/// </summary>
public sealed class SteamAuthStoreStatus
{
    public bool HasCredential { get; set; }
    public bool HasSecrets { get; set; }
    public bool HasSession { get; set; }
    public bool HasAutoLogin { get; set; }
    public bool HasAccessToken { get; set; }
    public bool HasRefreshToken { get; set; }
    public string AccountName { get; set; } = "";
    public string PersonaName { get; set; } = "";
    public string LoginAccountName { get; set; } = "";
    public string SteamId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public DateTime SavedAt { get; set; }
    public DateTime SessionSavedAt { get; set; }
    public DateTime LastAutoReloginAt { get; set; }
    public string LastAutoReloginResult { get; set; } = "";
    public DateTime AutoReloginCooldownUntil { get; set; }
    public string Message { get; set; } = "";
    public string Error { get; set; } = "";
}
