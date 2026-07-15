using CS2TradeMonitor.Application.Steam.Auth;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface ISteamTokenVault
    {
        string CredentialPath { get; }

        string LegacyCredentialPath { get; }

        string LastError { get; }

        List<SteamTokenEntry> GetAllTokens();

        SteamTokenEntry? GetDefaultSteamToken();

        string GetDefaultSteamTokenId();

        void SetDefaultSteamToken(string id);

        string SaveToken(SteamTokenEntry entry);

        void DeleteToken(string id);

        void ClearDefaultSteamToken();

        void IncrementHotpCounter(string id);
    }
}
