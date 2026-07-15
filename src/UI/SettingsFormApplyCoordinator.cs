using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.Core.Actions;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Framework;
using CS2TradeMonitor.src.UI.SettingsPage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI
{
    internal sealed class SettingsFormApplyCoordinator
    {
        private readonly Settings _liveSettings;
        private readonly Settings _draftSettings;
        private readonly IDictionary<string, SettingsPageBase> _pages;
        private readonly Func<string> _getCurrentPageKey;
        private readonly Func<float> _getDpiScale;
        private readonly Action _applyFormScaling;
        private readonly Action<string> _setSavedSnapshot;
        private readonly Action<bool> _updateDirtyState;
        private readonly Action _showSavedStatus;
        private readonly MainForm _mainForm;
        private readonly UIController _ui;

        public SettingsFormApplyCoordinator(
            Settings liveSettings,
            Settings draftSettings,
            IDictionary<string, SettingsPageBase> pages,
            Func<string> getCurrentPageKey,
            Func<float> getDpiScale,
            Action applyFormScaling,
            Action<string> setSavedSnapshot,
            Action<bool> updateDirtyState,
            Action showSavedStatus,
            MainForm mainForm,
            UIController ui)
        {
            _liveSettings = liveSettings ?? throw new ArgumentNullException(nameof(liveSettings));
            _draftSettings = draftSettings ?? throw new ArgumentNullException(nameof(draftSettings));
            _pages = pages ?? throw new ArgumentNullException(nameof(pages));
            _getCurrentPageKey = getCurrentPageKey ?? throw new ArgumentNullException(nameof(getCurrentPageKey));
            _getDpiScale = getDpiScale ?? throw new ArgumentNullException(nameof(getDpiScale));
            _applyFormScaling = applyFormScaling ?? throw new ArgumentNullException(nameof(applyFormScaling));
            _setSavedSnapshot = setSavedSnapshot ?? throw new ArgumentNullException(nameof(setSavedSnapshot));
            _updateDirtyState = updateDirtyState ?? throw new ArgumentNullException(nameof(updateDirtyState));
            _showSavedStatus = showSavedStatus ?? throw new ArgumentNullException(nameof(showSavedStatus));
            _mainForm = mainForm ?? throw new ArgumentNullException(nameof(mainForm));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        }

        public void Apply(IWin32Window owner)
        {
            var stopwatch = Stopwatch.StartNew();
            bool oldAutoStart = _liveSettings.AutoStart;
            double oldScale = _liveSettings.UIScale;
            Settings? rollbackSettings = null;
            bool merged = false;
            bool persisted = false;

            try
            {
                SaveLoadedPages();

                if (SettingsFormApplyModel.NeedsSafeTrayCorrection(_draftSettings))
                {
                    _draftSettings.HideTrayIcon = false;
                    GlobalPromptService.Show(
                        owner,
                        "为了防止所有可交互入口都被死锁（隐藏或穿透+隐藏托盘），已强制显示托盘图标。",
                        "CS2交易监控",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                rollbackSettings = _liveSettings.DeepClone();
                SettingsChanger.Merge(_liveSettings, _draftSettings);
                merged = true;
                bool autoStartNeedsApply = SettingsFormApplyModel.ShouldApplyAutoStart(
                    oldAutoStart,
                    _liveSettings.AutoStart,
                    AutoStart.IsEnabledForCurrentExe(),
                    AutoStart.IsEnabled());

                SettingsSaveResult saveResult = _liveSettings.Save();
                if (!saveResult.Succeeded)
                    throw new SettingsPersistenceException(saveResult.FailureType);

                persisted = true;
                AppActions.ApplyAllSettings(_liveSettings, _mainForm, _ui, autoStartNeedsApply);

                if (SettingsFormApplyModel.ScaleChanged(oldScale, _liveSettings.UIScale))
                {
                    UIUtils.UpdateScale(_getDpiScale(), (float)_liveSettings.UIScale);
                    _applyFormScaling();
                }

                SettingsChanger.RebaseDraftMonitorItems(_liveSettings, _draftSettings);
                _setSavedSnapshot(SettingsFormStateModel.CaptureSnapshot(_liveSettings));
                _updateDirtyState(false);
                _showSavedStatus();
            }
            catch (Exception ex)
            {
                if (merged && !persisted && rollbackSettings != null)
                    SettingsChanger.Merge(_liveSettings, rollbackSettings);

                DiagnosticsLogger.Error(
                    "Settings",
                    $"Apply settings failed. CurrentPage={_getCurrentPageKey()}; LoadedPages={string.Join(",", _pages.Keys)}",
                    ex);
                throw;
            }
            finally
            {
                stopwatch.Stop();
                if (UiJankProfiler.Enabled || stopwatch.ElapsedMilliseconds >= 300)
                {
                    DiagnosticsLogger.Info(
                        "Settings",
                        $"Apply settings completed. CurrentPage={_getCurrentPageKey()}; LoadedPages={_pages.Count}; ElapsedMs={stopwatch.ElapsedMilliseconds}");
                }
            }
        }

        private void SaveLoadedPages()
        {
            foreach (var page in _pages.Where(pair => pair.Key != "Monitor"))
            {
                page.Value.Save();
            }

            if (_pages.TryGetValue("Monitor", out SettingsPageBase? monitorPage))
            {
                monitorPage.Save();
            }
        }
    }

    internal static class SettingsFormApplyModel
    {
        public static bool NeedsSafeTrayCorrection(Settings draftSettings)
        {
            ArgumentNullException.ThrowIfNull(draftSettings);

            return Settings.HasNoInteractiveEntry(draftSettings);
        }

        public static bool ShouldApplyAutoStart(
            bool oldAutoStart,
            bool newAutoStart,
            bool enabledForCurrentExe,
            bool anyAutoStartEnabled)
        {
            return oldAutoStart != newAutoStart
                || (newAutoStart
                    ? !enabledForCurrentExe
                    : anyAutoStartEnabled);
        }

        public static bool ScaleChanged(double oldScale, double newScale)
        {
            return Math.Abs(oldScale - newScale) > 0.001;
        }
    }
}
