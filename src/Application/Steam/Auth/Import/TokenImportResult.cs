using System;

namespace CS2TradeMonitor.Application.Steam.Auth.Import
{
    public sealed class TokenImportResult
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = "";
        public SteamTokenEntry? Token { get; init; }

        public static TokenImportResult Success(SteamTokenEntry token, string message) => new()
        {
            Ok = true,
            Token = token,
            Message = message
        };

        public static TokenImportResult Failed(string message) => new()
        {
            Ok = false,
            Message = message
        };
    }

    public interface ITokenImporter
    {
        bool CanImport(string text, string sourcePath);
        TokenImportResult Import(string text, string sourcePath);
    }
}
