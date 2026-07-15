namespace CS2TradeMonitor.src.UI
{
    internal static class SettingsFormDeferredActionKeys
    {
        public const string PageLifecycle = "settings-page-lifecycle";

        public const string MainPanelTab = "settings-main-panel-tab";

        public const string SubRoute = "settings-sub-route";

        public const string ContentNativeTheme = "settings-content-native-theme";

        public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
        {
            PageLifecycle,
            MainPanelTab,
            SubRoute,
            ContentNativeTheme
        });
    }
}
