namespace CS2TradeMonitor.Shared.Core
{
    /// <summary>
    /// Keeps legacy desktop-only settings operations outside the portable settings contract.
    /// Platform shells configure this bridge during module initialization.
    /// </summary>
    internal static class SettingsPlatformBridge
    {
        private static readonly object Gate = new();
        private static Adapter? _adapter;

        internal static void Configure(
            Func<bool, Settings> load,
            Func<bool> getGlobalBlockSave,
            Action<bool> setGlobalBlockSave,
            Func<string, string, long, IDisposable> measure)
        {
            ArgumentNullException.ThrowIfNull(load);
            ArgumentNullException.ThrowIfNull(getGlobalBlockSave);
            ArgumentNullException.ThrowIfNull(setGlobalBlockSave);
            ArgumentNullException.ThrowIfNull(measure);

            lock (Gate)
            {
                Volatile.Write(
                    ref _adapter,
                    new Adapter(load, getGlobalBlockSave, setGlobalBlockSave, measure));
            }
        }

        internal static Settings Load(bool forceReload)
        {
            Adapter? adapter = Volatile.Read(ref _adapter);
            return adapter?.Load(forceReload)
                ?? throw new InvalidOperationException(
                    "当前平台尚未配置 Settings 持久化适配器。请通过平台服务加载配置。");
        }

        internal static bool GetGlobalBlockSave()
        {
            Adapter? adapter = Volatile.Read(ref _adapter);
            return adapter?.GetGlobalBlockSave() ?? false;
        }

        internal static void SetGlobalBlockSave(bool value)
        {
            Adapter? adapter = Volatile.Read(ref _adapter);
            if (adapter is null)
            {
                throw new InvalidOperationException(
                    "当前平台尚未配置 Settings 持久化适配器。请通过平台服务保存配置。");
            }

            adapter.SetGlobalBlockSave(value);
        }

        internal static IDisposable Measure(string scope, string detail, long thresholdMs)
        {
            Adapter? adapter = Volatile.Read(ref _adapter);
            return adapter?.Measure(scope, detail, thresholdMs) ?? NoopDisposable.Instance;
        }

        private sealed record Adapter(
            Func<bool, Settings> Load,
            Func<bool> GetGlobalBlockSave,
            Action<bool> SetGlobalBlockSave,
            Func<string, string, long, IDisposable> Measure);

        private sealed class NoopDisposable : IDisposable
        {
            internal static NoopDisposable Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
