using System;
using System.Collections.Generic;
using CS2TradeMonitor.Infrastructure.Diagnostics;

namespace CS2TradeMonitor.src.UI.Framework
{
    /// <summary>
    /// Keeps one draft settings surface and delegates persistence to the owner.
    /// </summary>
    public sealed class SettingsStore
    {
        private readonly Dictionary<string, object?> _draft = new Dictionary<string, object?>();
        private readonly Action<IReadOnlyDictionary<string, object?>> _saveSnapshot;
        private int _bulkLoadDepth;
        private long _version;
        private long _savedVersion;

        public SettingsStore(Action<IReadOnlyDictionary<string, object?>> saveSnapshot)
        {
            _saveSnapshot = saveSnapshot ?? throw new ArgumentNullException(nameof(saveSnapshot));
        }

        public event EventHandler<SettingChangedEventArgs>? DraftChanged;

        public event EventHandler? Saved;

        public IReadOnlyDictionary<string, object?> Snapshot => new Dictionary<string, object?>(_draft);

        public bool HasUnsavedChanges => _version != _savedVersion;

        public void BeginBulkLoad()
        {
            _bulkLoadDepth++;
        }

        public void EndBulkLoad(bool markClean)
        {
            if (_bulkLoadDepth > 0)
                _bulkLoadDepth--;

            if (markClean && _bulkLoadDepth == 0)
                _savedVersion = _version;
        }

        public T Get<T>(string key, T fallback = default!)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Setting key is required.", nameof(key));
            }

            return _draft.TryGetValue(key, out object? value) && value is T typedValue
                ? typedValue
                : fallback;
        }

        public void Set<T>(string key, T value, bool forceChanged = false)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Setting key is required.", nameof(key));
            }

            if (!forceChanged && _draft.TryGetValue(key, out object? current) && Equals(current, value))
                return;

            _draft[key] = value;
            _version++;
            if (_bulkLoadDepth == 0)
            {
                RecordSafeSettingChange(key, value);
                DraftChanged?.Invoke(this, new SettingChangedEventArgs(key, value));
            }
        }

        public void Save()
        {
            if (!HasUnsavedChanges)
                return;

            _saveSnapshot(Snapshot);
            _savedVersion = _version;
            Saved?.Invoke(this, EventArgs.Empty);
        }

        private static void RecordSafeSettingChange<T>(string key, T value)
        {
            if (!DetailedDiagnosticsRuntime.IsEnabled)
                return;

            object? safeValue = value is bool
                || value is byte or sbyte or short or ushort or int or uint or long or ulong
                || value is float or double or decimal
                || value is Enum
                ? value
                : null;
            DetailedDiagnosticsRuntime.Record(
                "Information",
                "UI",
                "SettingChanged",
                new Dictionary<string, object?>
                {
                    ["settingName"] = key,
                    ["valueType"] = value is null ? "null" : value.GetType().Name,
                    ["value"] = safeValue
                });
        }
    }

    public sealed class SettingChangedEventArgs : EventArgs
    {
        public SettingChangedEventArgs(string key, object? value)
        {
            Key = key;
            Value = value;
        }

        public string Key { get; }

        public object? Value { get; }
    }
}
