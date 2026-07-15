using System;
using System.Net;

namespace CS2TradeMonitor.Application.Steam.Auth
{
    public enum SteamLoginFailureCategory
    {
        Unknown,
        InvalidPassword,
        InvalidTwoFactor,
        EmailCodeRequired,
        CaptchaRequired,
        RateLimited,
        AuthExpired,
        NetworkError,
        ProtocolChanged
    }

    public sealed class SteamLoginException : Exception
    {
        public SteamLoginException(SteamLoginFailureCategory category, string message) : base(message)
        {
            Category = category;
        }

        public SteamLoginFailureCategory Category { get; }
    }

    internal enum SteamSessionValidationState
    {
        Valid,
        Expired,
        NetworkUnavailable,
        ProtocolUnexpected
    }

    internal sealed class SteamSessionValidationResult
    {
        private SteamSessionValidationResult(SteamSessionValidationState state, string message, HttpStatusCode? httpStatusCode = null)
        {
            State = state;
            Message = message;
            HttpStatusCode = httpStatusCode;
        }

        public SteamSessionValidationState State { get; }
        public string Message { get; }
        public HttpStatusCode? HttpStatusCode { get; }

        public static SteamSessionValidationResult Valid() => new(SteamSessionValidationState.Valid, "");

        public static SteamSessionValidationResult Expired(string message, HttpStatusCode? statusCode = null)
            => new(SteamSessionValidationState.Expired, message, statusCode);

        public static SteamSessionValidationResult NetworkUnavailable(string message, HttpStatusCode? statusCode = null)
            => new(SteamSessionValidationState.NetworkUnavailable, message, statusCode);

        public static SteamSessionValidationResult ProtocolUnexpected(string message, HttpStatusCode? statusCode = null)
            => new(SteamSessionValidationState.ProtocolUnexpected, message, statusCode);
    }

    internal sealed class RsaKeyResponse
    {
        public string PublicKeyMod { get; set; } = "";
        public string PublicKeyExp { get; set; } = "";
        public string Timestamp { get; set; } = "";
    }

    internal sealed class ApiKeyPageResult
    {
        public bool Success { get; private init; }
        public int StatusCode { get; private init; }
        public string PageKind { get; private init; } = "";
        public string Html { get; private init; } = "";

        public static ApiKeyPageResult Succeeded(string html, string pageKind) => new()
        {
            Success = true,
            StatusCode = 200,
            PageKind = pageKind,
            Html = html
        };

        public static ApiKeyPageResult Failed(int statusCode, string pageKind) => new()
        {
            Success = false,
            StatusCode = statusCode,
            PageKind = pageKind,
            Html = ""
        };
    }
}
