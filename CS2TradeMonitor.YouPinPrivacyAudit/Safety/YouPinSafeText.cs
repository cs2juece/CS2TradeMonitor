namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Safety
{
    internal static class YouPinSafeText
    {
        internal static string? Normalize(string? value, int maximumLength)
        {
            if (maximumLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumLength));
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string normalized = string.Join(
                ' ',
                value.Split(
                    new[] { ' ', '\t', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries));
            return normalized.Length <= maximumLength
                ? normalized
                : normalized[..maximumLength];
        }
    }
}
