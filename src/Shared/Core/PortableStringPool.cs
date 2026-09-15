namespace CS2TradeMonitor.Shared.Core
{
    /// <summary>
    /// Provides one process-wide, platform-neutral pool for frequently repeated strings.
    /// </summary>
    public static class PortableStringPool
    {
        private static readonly Dictionary<string, string> Pool = new(StringComparer.Ordinal);
        private static readonly object Gate = new();

        public static string Intern(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            lock (Gate)
            {
                if (Pool.TryGetValue(value, out string? pooled))
                    return pooled;

                Pool[value] = value;
                return value;
            }
        }

        public static void Clear()
        {
            lock (Gate)
            {
                Pool.Clear();
            }
        }
    }
}
