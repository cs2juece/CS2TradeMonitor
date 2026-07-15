using System;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Serialization;

namespace CS2TradeMonitor.src.UI.Framework
{
    /// <summary>
    /// Bridges the new dictionary-backed SettingsStore to the existing Settings POCO.
    /// </summary>
    internal sealed class SettingsStoreAdapter
    {
        private readonly SettingsStore _settingsStore;

        public SettingsStoreAdapter(SettingsStore settingsStore)
        {
            _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        }

        public void Load(Settings settings)
        {
            if (settings is null)
                throw new ArgumentNullException(nameof(settings));

            _settingsStore.BeginBulkLoad();
            try
            {
                foreach (PropertyInfo property in GetMappableProperties())
                {
                    _settingsStore.Set(property.Name, property.GetValue(settings));
                }
            }
            finally
            {
                _settingsStore.EndBulkLoad(markClean: true);
            }
        }

        public void Apply(Settings settings)
        {
            if (settings is null)
                throw new ArgumentNullException(nameof(settings));

            var snapshot = _settingsStore.Snapshot;
            foreach (PropertyInfo property in GetMappableProperties())
            {
                if (!snapshot.TryGetValue(property.Name, out object? value))
                    continue;

                if (TryConvertValue(value, property.PropertyType, out object? convertedValue))
                {
                    property.SetValue(settings, convertedValue);
                }
            }
        }

        private static IEnumerable<PropertyInfo> GetMappableProperties()
        {
            return typeof(Settings).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property =>
                    property.CanRead
                    && property.CanWrite
                    && property.GetIndexParameters().Length == 0
                    && property.GetCustomAttribute<JsonIgnoreAttribute>() is null);
        }

        private static bool TryConvertValue(object? value, Type targetType, out object? convertedValue)
        {
            Type effectiveTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (value is null)
            {
                convertedValue = null;
                return !effectiveTargetType.IsValueType || Nullable.GetUnderlyingType(targetType) is not null;
            }

            if (effectiveTargetType.IsInstanceOfType(value))
            {
                convertedValue = value;
                return true;
            }

            try
            {
                if (effectiveTargetType.IsEnum)
                {
                    convertedValue = value is string enumText
                        ? Enum.Parse(effectiveTargetType, enumText, ignoreCase: true)
                        : Enum.ToObject(effectiveTargetType, value);
                    return true;
                }

                if (effectiveTargetType == typeof(string))
                {
                    convertedValue = value.ToString();
                    return true;
                }

                Type valueType = value.GetType();
                TypeConverter targetConverter = TypeDescriptor.GetConverter(effectiveTargetType);
                if (targetConverter.CanConvertFrom(valueType))
                {
                    convertedValue = targetConverter.ConvertFrom(null, CultureInfo.InvariantCulture, value);
                    return true;
                }

                TypeConverter sourceConverter = TypeDescriptor.GetConverter(valueType);
                if (sourceConverter.CanConvertTo(effectiveTargetType))
                {
                    convertedValue = sourceConverter.ConvertTo(null, CultureInfo.InvariantCulture, value, effectiveTargetType);
                    return true;
                }

                convertedValue = Convert.ChangeType(value, effectiveTargetType, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                convertedValue = null;
                return false;
            }
        }
    }
}
