using System;
using CS2TradeMonitor.src.Core;

namespace CS2TradeMonitor.src.UI.Framework
{
    /// <summary>
    /// Owns the draft-to-Settings lifecycle used by framework settings hosts.
    /// </summary>
    internal sealed class SettingsTransaction
    {
        private readonly Func<Settings?> _settingsAccessor;
        private readonly SettingsStoreAdapter _adapter;

        public SettingsTransaction(Func<Settings?> settingsAccessor)
        {
            _settingsAccessor = settingsAccessor ?? throw new ArgumentNullException(nameof(settingsAccessor));
            Draft = new SettingsStore(_ => ApplyDraft());
            _adapter = new SettingsStoreAdapter(Draft);
        }

        public SettingsStore Draft { get; }

        public bool HasUnsavedChanges => Draft.HasUnsavedChanges;

        public void Rebase()
        {
            Settings? settings = _settingsAccessor();
            if (settings is not null)
                _adapter.Load(settings);
        }

        public void Commit()
        {
            Draft.Save();
        }

        private void ApplyDraft()
        {
            Settings? settings = _settingsAccessor();
            if (settings is not null)
                _adapter.Apply(settings);
        }
    }
}
