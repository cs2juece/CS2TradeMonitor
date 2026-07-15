using System;

namespace CS2TradeMonitor.Domain.Steam
{
    public sealed class SteamAuthCredential
    {
        public string SteamId { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string PersonaName { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public string SharedSecret { get; set; } = "";
        public string IdentitySecret { get; set; } = "";
        public string SessionId { get; set; } = "";
        public string SteamLoginSecure { get; set; } = "";
        public string SteamLogin { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public string AccessToken { get; set; } = "";
        public DateTime AccessTokenExpiresAt { get; set; } = DateTime.MinValue;
        public DateTime RefreshTokenExpiresAt { get; set; } = DateTime.MinValue;
        public string ApiKey { get; set; } = "";
        public string LoginAccountName { get; set; } = "";
        public string LoginPassword { get; set; } = "";
        public DateTime SavedAt { get; set; } = DateTime.Now;
        public DateTime SessionSavedAt { get; set; } = DateTime.MinValue;
        public DateTime LastAutoReloginAt { get; set; } = DateTime.MinValue;
        public string LastAutoReloginResult { get; set; } = "";
        public DateTime AutoReloginCooldownUntil { get; set; } = DateTime.MinValue;
    }
}
