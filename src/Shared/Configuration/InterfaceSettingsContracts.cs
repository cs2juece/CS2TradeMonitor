namespace CS2TradeMonitor.Shared.Configuration;

public sealed record InterfaceSettingAssignment(string Key, object Value);

public sealed record InterfaceSafeVisibilityResult(
    bool RequiresCorrection,
    bool HideMainForm,
    bool ShowTaskbar);

public sealed record InterfaceSettingsProjection(
    bool OverlayEnabled,
    bool TopMost,
    bool ClickThrough,
    bool LockPosition,
    bool ClampToScreen,
    bool AutoHide,
    bool HorizontalMode,
    int PanelWidth,
    double PanelBackgroundOpacity,
    double TextOpacity,
    string PanelBackgroundColor,
    int HorizontalItemSpacing,
    int HorizontalInnerSpacing,
    bool ShowTaskbar,
    int TaskbarPresetStyle,
    bool TaskbarSingleLine,
    bool TaskbarClickThrough,
    bool TaskbarHoverShowAll,
    bool TaskbarAlignLeft,
    int TaskbarManualOffset,
    string TaskbarFontFamily,
    float TaskbarFontSize,
    bool TaskbarFontBold,
    int TaskbarItemSpacing,
    int TaskbarInnerSpacing,
    int TaskbarVerticalPadding,
    string TaskbarColorBg,
    string TaskbarColorLabel,
    string TaskbarColorCrit,
    string TaskbarColorSafe,
    string TaskbarColorWarn,
    int DefaultItemRefreshIntervalSec,
    bool ItemMonitorDefaultVisibleInPanel,
    bool ItemMonitorDefaultVisibleInTaskbar,
    double DefaultItemPriceAlertRisePercent,
    double DefaultItemPriceAlertFallPercent,
    int DefaultItemPriceAlertWindowMinutes,
    int DefaultItemPriceAlertCooldownMinutes,
    string SteamDtPositiveColor,
    string SteamDtNegativeColor,
    string SteamDtWarningColor,
    string SteamDtNeutralColor,
    bool YouPinTrendIndicatorVisibleInPanel,
    bool YouPinTrendIndicatorVisibleInTaskbar,
    int YouPinTrendIndicatorDisplayMode,
    int YouPinTrendIndicatorSignMode,
    float YouPinTrendIndicatorFontSize,
    bool YouPinTrendIndicatorFontBold,
    string YouPinTrendIndicatorProfitColor,
    string YouPinTrendIndicatorLossColor,
    string YouPinTrendIndicatorZeroColor,
    string YouPinTrendIndicatorSubTextColor)
{
    public static InterfaceSettingsProjection FromSettings(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new InterfaceSettingsProjection(
            !settings.HideMainForm,
            settings.TopMost,
            settings.ClickThrough,
            settings.LockPosition,
            settings.ClampToScreen,
            settings.AutoHide,
            settings.HorizontalMode,
            settings.PanelWidth,
            settings.PanelBackgroundOpacity,
            settings.TextOpacity,
            settings.PanelBackgroundColor,
            settings.HorizontalItemSpacing,
            settings.HorizontalInnerSpacing,
            settings.ShowTaskbar,
            settings.TaskbarPresetStyle,
            settings.TaskbarSingleLine,
            settings.TaskbarClickThrough,
            settings.TaskbarHoverShowAll,
            settings.TaskbarAlignLeft,
            settings.TaskbarManualOffset,
            settings.TaskbarFontFamily,
            settings.TaskbarFontSize,
            settings.TaskbarFontBold,
            settings.TaskbarItemSpacing,
            settings.TaskbarInnerSpacing,
            settings.TaskbarVerticalPadding,
            settings.TaskbarColorBg,
            settings.TaskbarColorLabel,
            settings.TaskbarColorCrit,
            settings.TaskbarColorSafe,
            settings.TaskbarColorWarn,
            settings.DefaultItemRefreshIntervalSec,
            settings.ItemMonitorDefaultVisibleInPanel,
            settings.ItemMonitorDefaultVisibleInTaskbar,
            settings.DefaultItemPriceAlertRisePercent,
            settings.DefaultItemPriceAlertFallPercent,
            settings.DefaultItemPriceAlertWindowMinutes,
            settings.DefaultItemPriceAlertCooldownMinutes,
            settings.SteamDtPositiveColor,
            settings.SteamDtNegativeColor,
            settings.SteamDtWarningColor,
            settings.SteamDtNeutralColor,
            settings.YouPinTrendIndicatorVisibleInPanel,
            settings.YouPinTrendIndicatorVisibleInTaskbar,
            settings.YouPinTrendIndicatorDisplayMode,
            settings.YouPinTrendIndicatorSignMode,
            settings.YouPinTrendIndicatorFontSize,
            settings.YouPinTrendIndicatorFontBold,
            settings.YouPinTrendIndicatorProfitColor,
            settings.YouPinTrendIndicatorLossColor,
            settings.YouPinTrendIndicatorZeroColor,
            settings.YouPinTrendIndicatorSubTextColor);
    }
}

public interface ISettingsPlatformBridge
{
    Task<SettingsPlatformApplyResult> ApplyAsync(
        Settings previous,
        Settings current,
        IReadOnlyCollection<string> changedProperties,
        CancellationToken cancellationToken = default);
}

public sealed record SettingsPlatformApplyResult(
    bool Applied,
    bool RequiresUserAction,
    string ReasonCode,
    string Message)
{
    public static SettingsPlatformApplyResult Success(string message = "已保存并应用。")
        => new(true, false, "ok", message);

    public static SettingsPlatformApplyResult NeedsUserAction(string reasonCode, string message)
        => new(false, true, reasonCode, message);

    public static SettingsPlatformApplyResult Failure(string reasonCode, string message)
        => new(false, false, reasonCode, message);
}

public static class InterfaceSettingsRules
{
    public static IReadOnlyList<InterfaceSettingAssignment> BuildTaskbarStylePreset(bool bold)
        =>
        [
            Assign(nameof(Settings.TaskbarPresetStyle), bold ? 1 : 0),
            Assign(nameof(Settings.TaskbarCustomLayout), true),
            Assign(nameof(Settings.TaskbarFontFamily), Settings.DEFAULT_TB_FONT),
            Assign(nameof(Settings.TaskbarFontSize), bold ? Settings.DEFAULT_TB_SIZE_BOLD : Settings.DEFAULT_TB_SIZE_REGULAR),
            Assign(nameof(Settings.TaskbarFontBold), bold),
            Assign(nameof(Settings.TaskbarInnerSpacing), bold ? Settings.DEFAULT_TB_INNER_BOLD : Settings.DEFAULT_TB_INNER_REGULAR),
            Assign(nameof(Settings.TaskbarVerticalPadding), Settings.DEFAULT_TB_VOFF)
        ];

    public static IReadOnlyList<InterfaceSettingAssignment> BuildTaskbarPreset(int type)
        => type switch
        {
            0 =>
            [
                Assign(nameof(Settings.TaskbarPresetStyle), 1),
                Assign(nameof(Settings.TaskbarCustomLayout), true),
                Assign(nameof(Settings.TaskbarCustomStyle), true),
                Assign(nameof(Settings.TaskbarFontFamily), Settings.DEFAULT_TB_FONT),
                Assign(nameof(Settings.TaskbarFontSize), Settings.DEFAULT_TB_SIZE_BOLD),
                Assign(nameof(Settings.TaskbarFontBold), true),
                Assign(nameof(Settings.TaskbarItemSpacing), Settings.DEFAULT_TB_GAP),
                Assign(nameof(Settings.TaskbarInnerSpacing), Settings.DEFAULT_TB_INNER_BOLD),
                Assign(nameof(Settings.TaskbarVerticalPadding), Settings.DEFAULT_TB_VOFF),
                Assign(nameof(Settings.Skin), "DarkFlat_Classic")
            ],
            1 =>
            [
                Assign(nameof(Settings.TaskbarPresetStyle), 0),
                Assign(nameof(Settings.TaskbarCustomLayout), true),
                Assign(nameof(Settings.TaskbarFontSize), 9f),
                Assign(nameof(Settings.TaskbarItemSpacing), 4),
                Assign(nameof(Settings.TaskbarInnerSpacing), 4),
                Assign(nameof(Settings.TaskbarVerticalPadding), 2),
                Assign(nameof(Settings.TaskbarSingleLine), true),
                Assign(nameof(Settings.TaskbarFontBold), false)
            ],
            2 =>
            [
                Assign(nameof(Settings.TaskbarCustomLayout), true),
                Assign(nameof(Settings.TaskbarFontSize), 12f),
                Assign(nameof(Settings.TaskbarFontBold), true),
                Assign(nameof(Settings.TaskbarItemSpacing), 6),
                Assign(nameof(Settings.TaskbarInnerSpacing), 8),
                Assign(nameof(Settings.TaskbarVerticalPadding), 2),
                Assign(nameof(Settings.TaskbarCustomStyle), true),
                Assign(nameof(Settings.TaskbarColorBg), "#001E3D"),
                Assign(nameof(Settings.TaskbarColorLabel), "#FFFFFF"),
                Assign(nameof(Settings.TaskbarColorCrit), "#FF4444"),
                Assign(nameof(Settings.TaskbarColorSafe), "#00CC66"),
                Assign(nameof(Settings.TaskbarColorWarn), "#FFFF00")
            ],
            3 =>
            [
                Assign(nameof(Settings.TaskbarCustomLayout), true),
                Assign(nameof(Settings.TaskbarFontSize), 11f),
                Assign(nameof(Settings.TaskbarFontBold), true),
                Assign(nameof(Settings.TaskbarItemSpacing), 6),
                Assign(nameof(Settings.TaskbarInnerSpacing), 8),
                Assign(nameof(Settings.TaskbarVerticalPadding), 2),
                Assign(nameof(Settings.TaskbarCustomStyle), true),
                Assign(nameof(Settings.TaskbarColorBg), "#001E3D"),
                Assign(nameof(Settings.TaskbarColorLabel), "#FFD700"),
                Assign(nameof(Settings.TaskbarColorCrit), "#FF4444"),
                Assign(nameof(Settings.TaskbarColorSafe), "#00FFCC"),
                Assign(nameof(Settings.TaskbarColorWarn), "#FFFF00")
            ],
            _ => []
        };

    public static InterfaceSafeVisibilityResult ResolveSafeVisibility(
        bool hideMainForm,
        bool hideTrayIcon,
        bool showTaskbar,
        bool clickThrough,
        bool taskbarClickThrough)
    {
        bool noInteractiveEntry = (hideMainForm || clickThrough)
            && (!showTaskbar || taskbarClickThrough)
            && hideTrayIcon;
        return noInteractiveEntry
            ? new InterfaceSafeVisibilityResult(true, HideMainForm: false, ShowTaskbar: true)
            : new InterfaceSafeVisibilityResult(false, hideMainForm, showTaskbar);
    }

    public static object NormalizeValue(string propertyName, object value)
        => propertyName switch
        {
            nameof(Settings.PanelWidth) => Math.Clamp(Convert.ToInt32(value), 180, 1200),
            nameof(Settings.PanelBackgroundOpacity) or nameof(Settings.TextOpacity)
                => Math.Clamp(Convert.ToDouble(value), 0d, 1d),
            nameof(Settings.HorizontalItemSpacing) or nameof(Settings.HorizontalInnerSpacing)
                or nameof(Settings.TaskbarItemSpacing) or nameof(Settings.TaskbarInnerSpacing)
                => Math.Clamp(Convert.ToInt32(value), -20, 80),
            nameof(Settings.TaskbarVerticalPadding) => Math.Clamp(Convert.ToInt32(value), -10, 30),
            nameof(Settings.TaskbarManualOffset) => Math.Clamp(Convert.ToInt32(value), -1200, 1200),
            nameof(Settings.TaskbarFontSize) or nameof(Settings.YouPinTrendIndicatorFontSize)
                => Math.Clamp(Convert.ToSingle(value), 7f, 18f),
            nameof(Settings.DefaultItemRefreshIntervalSec)
                => Math.Max(60, Convert.ToInt32(value) <= 0 ? 600 : Convert.ToInt32(value)),
            nameof(Settings.Cs2UpdateReminderRefreshSec)
                => Math.Clamp(Convert.ToInt32(value) <= 0 ? 600 : Convert.ToInt32(value), 60, 86400),
            nameof(Settings.DefaultItemPriceAlertRisePercent) or nameof(Settings.DefaultItemPriceAlertFallPercent)
                => Math.Clamp(Convert.ToDouble(value), 0d, 1000d),
            nameof(Settings.DefaultItemPriceAlertWindowMinutes)
                => Math.Clamp(Convert.ToInt32(value), 1, 10080),
            nameof(Settings.DefaultItemPriceAlertCooldownMinutes)
                => Math.Clamp(Convert.ToInt32(value), 1, 1440),
            nameof(Settings.YouPinTrendIndicatorDisplayMode) => Math.Clamp(Convert.ToInt32(value), 0, 2),
            nameof(Settings.YouPinTrendIndicatorSignMode) => Math.Clamp(Convert.ToInt32(value), 0, 1),
            _ when propertyName.EndsWith("Color", StringComparison.Ordinal)
                || propertyName.StartsWith("TaskbarColor", StringComparison.Ordinal)
                => NormalizeColor(Convert.ToString(value) ?? string.Empty, propertyName == nameof(Settings.PanelBackgroundColor)),
            _ => value
        };

    public static string NormalizeColor(string value, bool allowEmpty = false)
    {
        string text = value.Trim().ToUpperInvariant();
        if (allowEmpty && text.Length == 0)
            return string.Empty;
        if (text.Length == 7 && text[0] == '#' && text.Skip(1).All(Uri.IsHexDigit))
            return text;
        throw new ArgumentException("颜色必须使用 #RRGGBB 格式。", nameof(value));
    }

    private static InterfaceSettingAssignment Assign(string key, object value)
        => new(key, value);
}
