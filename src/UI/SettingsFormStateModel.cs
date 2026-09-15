using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Drawing;
using System.Text.Json;

namespace CS2TradeMonitor.src.UI
{
    internal static class SettingsFormStateModel
    {
        public const int CurrentWindowPlacementVersion = Settings.CurrentSettingsPanelWindowPlacementVersion;

        public static Size BuildWindowSize(Rectangle workingArea)
        {
            return new Size(
                Math.Min(UIUtils.S(1120), Math.Max(UIUtils.S(920), workingArea.Width - UIUtils.S(40))),
                Math.Min(UIUtils.S(760), Math.Max(UIUtils.S(680), workingArea.Height - UIUtils.S(40))));
        }

        public static Size BuildMinimumWindowSize(Rectangle workingArea)
        {
            int widthLimit = Math.Max(1, workingArea.Width - UIUtils.S(40));
            int heightLimit = Math.Max(1, workingArea.Height - UIUtils.S(40));
            return new Size(
                Math.Min(UIUtils.S(1120), widthLimit),
                Math.Min(UIUtils.S(720), heightLimit));
        }

        public static Size BuildRestoredWindowSize(
            Rectangle workingArea,
            int savedWidth,
            int savedHeight,
            int placementVersion)
        {
            if (savedWidth <= 0 || savedHeight <= 0)
                return BuildWindowSize(workingArea);

            if (placementVersion != CurrentWindowPlacementVersion)
                return BuildWindowSize(workingArea);

            return ClampWindowSize(UIUtils.S(new Size(savedWidth, savedHeight)), workingArea);
        }

        public static Size BuildPersistedWindowSize(Size physicalSize)
        {
            float scale = Math.Max(0.01f, UIUtils.ScaleFactor);
            return new Size(
                Math.Max(1, (int)Math.Round(physicalSize.Width / scale)),
                Math.Max(1, (int)Math.Round(physicalSize.Height / scale)));
        }

        public static Size ClampWindowSize(Size requested, Rectangle workingArea)
        {
            Size minimum = BuildMinimumWindowSize(workingArea);
            int maxWidth = Math.Max(minimum.Width, workingArea.Width);
            int maxHeight = Math.Max(minimum.Height, workingArea.Height);

            return new Size(
                Math.Clamp(requested.Width, minimum.Width, maxWidth),
                Math.Clamp(requested.Height, minimum.Height, maxHeight));
        }

        public static Rectangle BuildCenteredWindowBounds(Rectangle workingArea, Size requestedSize)
        {
            Size size = ClampWindowSize(requestedSize, workingArea);
            return new Rectangle(
                workingArea.Left + Math.Max(0, (workingArea.Width - size.Width) / 2),
                workingArea.Top + Math.Max(0, (workingArea.Height - size.Height) / 2),
                size.Width,
                size.Height);
        }

        public static Rectangle ClampWindowBounds(Rectangle requested, Rectangle workingArea)
        {
            Size size = ClampWindowSize(requested.Size, workingArea);
            int maxLeft = Math.Max(workingArea.Left, workingArea.Right - size.Width);
            int maxTop = Math.Max(workingArea.Top, workingArea.Bottom - size.Height);
            return new Rectangle(
                Math.Clamp(requested.Left, workingArea.Left, maxLeft),
                Math.Clamp(requested.Top, workingArea.Top, maxTop),
                size.Width,
                size.Height);
        }

        public static Rectangle ScaleNormalWindowBoundsForDpi(
            Rectangle normalBounds,
            Rectangle targetWorkingArea,
            int oldDpi,
            int newDpi)
        {
            if (normalBounds.Width <= 0 || normalBounds.Height <= 0 || oldDpi <= 0 || newDpi <= 0)
                return BuildCenteredWindowBounds(targetWorkingArea, BuildWindowSize(targetWorkingArea));

            double ratio = newDpi / (double)oldDpi;
            var scaledSize = new Size(
                Math.Max(1, (int)Math.Round(normalBounds.Width * ratio)),
                Math.Max(1, (int)Math.Round(normalBounds.Height * ratio)));
            return BuildCenteredWindowBounds(targetWorkingArea, scaledSize);
        }

        public static bool NeedsMaximizedBoundsRefresh(
            bool isMaximized,
            Rectangle currentBounds,
            Rectangle workingArea)
        {
            return isMaximized && currentBounds != workingArea;
        }

        public static SettingsFormSidebarLayout BuildSidebarLayout(Size sidebarSize, int lineWidth)
        {
            int normalizedLineWidth = Math.Max(1, lineWidth);
            int width = Math.Max(1, sidebarSize.Width - normalizedLineWidth);
            int height = Math.Max(1, sidebarSize.Height);
            int systemHeight = UIUtils.S(50);
            int themeHeight = UIUtils.S(74);
            int navHeight = Math.Max(1, height - systemHeight - themeHeight);
            int switchWidth = Math.Min(width - UIUtils.S(36), UIUtils.S(204));
            int switchHeight = UIUtils.S(44);

            return new SettingsFormSidebarLayout(
                NavBounds: new Rectangle(0, 0, width, navHeight),
                SystemBounds: new Rectangle(0, navHeight, width, systemHeight),
                ThemeHostBounds: new Rectangle(0, navHeight + systemHeight, width, themeHeight),
                ThemeSwitchBounds: new Rectangle(
                    UIUtils.S(18),
                    Math.Max(0, (themeHeight - switchHeight) / 2),
                    switchWidth,
                    switchHeight),
                LineBounds: new Rectangle(width, 0, normalizedLineWidth, height));
        }

        public static string CaptureSnapshot(Settings settings)
        {
            return JsonSerializer.Serialize(settings);
        }

        public static bool IsDirty(Settings draftSettings, string savedSnapshot)
        {
            return CaptureSnapshot(draftSettings) != savedSnapshot;
        }

        public static SettingsFormStatusState BuildDirtyStatus(Settings draftSettings, string savedSnapshot)
        {
            return new SettingsFormStatusState(
                ApplyEnabled: IsDirty(draftSettings, savedSnapshot),
                Text: "",
                ForeColor: UIColors.TextSub);
        }

        public static SettingsFormStatusState BuildSavedStatus()
        {
            return new SettingsFormStatusState(
                ApplyEnabled: false,
                Text: "",
                ForeColor: UIColors.TextSub);
        }
    }

    internal readonly record struct SettingsFormSidebarLayout(
        Rectangle NavBounds,
        Rectangle SystemBounds,
        Rectangle ThemeHostBounds,
        Rectangle ThemeSwitchBounds,
        Rectangle LineBounds);

    internal readonly record struct SettingsFormStatusState(
        bool ApplyEnabled,
        string Text,
        Color ForeColor);
}
