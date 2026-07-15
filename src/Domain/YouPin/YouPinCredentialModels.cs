using System;

namespace CS2TradeMonitor.Domain.YouPin
{
    public sealed class YouPinCredential
    {
        public string Token { get; set; } = "";
        public string DeviceToken { get; set; } = "";
        public string Uk { get; set; } = "";
        public DateTime SavedAt { get; set; }
        public string NickName { get; set; } = "";
        public string UserId { get; set; } = "";
        public string Source { get; set; } = "";
    }
}
