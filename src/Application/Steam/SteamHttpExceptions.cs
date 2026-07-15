using System;

namespace CS2TradeMonitor.Application.Steam
{
    public sealed class SteamAuthExpiredException : Exception
    {
        public SteamAuthExpiredException(string message, int? statusCode = null) : base(message)
        {
            StatusCode = statusCode;
        }

        public int? StatusCode { get; }
        public string Code => "auth-expired";
    }

    public sealed class SteamTransientSteamException : Exception
    {
        public SteamTransientSteamException(string message, int? statusCode = null, string code = "steam-transient") : base(message)
        {
            StatusCode = statusCode;
            Code = string.IsNullOrWhiteSpace(code) ? "steam-transient" : code;
        }

        public int? StatusCode { get; }
        public string Code { get; }
    }
}
