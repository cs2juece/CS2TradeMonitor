using System;

namespace CS2TradeMonitor.src.UI.Framework
{
    /// <summary>
    /// Debounces draft changes and forces a final flush before close.
    /// </summary>
    public sealed class AutoSaveCoordinator : IDisposable
    {
        private readonly SettingsStore _settingsStore;
        private readonly UiDeferredActionScheduler _deferredActions;
        private readonly int _delayMs;
        private bool _hasPendingChanges;

        public AutoSaveCoordinator(SettingsStore settingsStore, int delayMs = 500)
        {
            if (delayMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(delayMs), "Auto-save delay must be positive.");
            }

            _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
            _settingsStore.DraftChanged += OnDraftChanged;
            _delayMs = delayMs;
            _deferredActions = new UiDeferredActionScheduler(() => true);
        }

        public void NotifyChanged()
        {
            _hasPendingChanges = true;
            _deferredActions.Schedule("settings-auto-save", _delayMs, Flush);
        }

        public void Flush()
        {
            _deferredActions.Cancel("settings-auto-save");

            if (!_hasPendingChanges)
            {
                return;
            }

            _settingsStore.Save();
            _hasPendingChanges = false;
        }

        public void Dispose()
        {
            Flush();
            _settingsStore.DraftChanged -= OnDraftChanged;
            _deferredActions.Dispose();
        }

        private void OnDraftChanged(object? sender, SettingChangedEventArgs e)
        {
            NotifyChanged();
        }
    }
}
