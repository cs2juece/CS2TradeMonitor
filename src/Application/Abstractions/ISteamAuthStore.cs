using CS2TradeMonitor.Application.Steam.Auth;
using CS2TradeMonitor.Domain.Steam;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface ISteamAuthStore
    {
        string CredentialPath { get; }

        string LegacyCredentialPath { get; }

        SteamAuthCredential? Load();

        string Save(SteamAuthCredential credential);

        void Clear();

        bool ClearTokenSecrets();

        bool ClearLoginState();

        List<SteamTokenEntry> GetAllTokens();

        void SetDefaultToken(string id);

        void DeleteToken(string id);

        SteamAuthStoreStatus GetStatus();
    }
}
