namespace CS2TradeMonitor.Application.Steam;

public sealed class SteamConfirmationRequest
{
    public string TradeOfferId { get; set; } = "";
    public string ConfirmationId { get; set; } = "";
    public string ConfirmationKey { get; set; } = "";
}

public sealed class SteamConfirmationBatchResult
{
    public bool Ok { get; set; }
    public int AcceptedCount { get; set; }
    public string Message { get; set; } = "";

    public static SteamConfirmationBatchResult Success(int acceptedCount) => new()
    {
        Ok = true,
        AcceptedCount = acceptedCount,
        Message = $"Steam 已批量确认 {acceptedCount} 条。"
    };

    public static SteamConfirmationBatchResult Failed(string message) => new()
    {
        Ok = false,
        AcceptedCount = 0,
        Message = message
    };
}
