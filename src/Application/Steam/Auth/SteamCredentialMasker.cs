namespace CS2TradeMonitor.Application.Steam.Auth;

public static class SteamCredentialMasker
{
    public static string MaskTail(string? value, int tail)
    {
        string text = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
            return "";
        if (text.Length <= tail)
            return new string('*', text.Length);
        return "***" + text[^Math.Min(tail, text.Length)..];
    }

    public static string MaskSteamId(string? value)
    {
        string text = (value ?? "").Trim();
        if (text.Length <= 6)
            return MaskTail(text, 2);
        return text[..3] + "****" + text[^4..];
    }
}
