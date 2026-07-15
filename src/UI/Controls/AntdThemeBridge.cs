using System;
using System.Drawing;
using System.Reflection;
using AntdUI;

namespace CS2TradeMonitor.src.UI.Controls
{
    internal static class AntdThemeBridge
    {
        private static bool _initialized;

        public static bool IsInitialized => _initialized;

        public static void Apply(bool dark)
        {
            try
            {
                ApplyMode(dark);
                SetConfigProperty("Animation", false);
                SetConfigProperty("FocusBorderEnabled", true);
                SetConfigProperty("ShowInWindow", true);
                SetConfigProperty("TextRenderingHighQuality", true);
                SetConfigProperty("Font", new Font("Microsoft YaHei UI", 9F));
                _initialized = true;
            }
            catch
            {
                // AntdUI is a visual enhancement layer. The existing WinForms UI remains usable if theme bridging fails.
            }
        }

        private static void ApplyMode(bool dark)
        {
            var modeProperty = typeof(Config).GetProperty("Mode", BindingFlags.Public | BindingFlags.Static);
            if (modeProperty == null || !modeProperty.CanWrite) return;

            var modeType = modeProperty.PropertyType;
            if (!modeType.IsEnum) return;

            var valueName = dark ? "Dark" : "Light";
            var modeValue = Enum.Parse(modeType, valueName, ignoreCase: true);
            modeProperty.SetValue(null, modeValue);
        }

        private static void SetConfigProperty(string name, object value)
        {
            var property = typeof(Config).GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            if (property == null || !property.CanWrite) return;

            if (!property.PropertyType.IsInstanceOfType(value))
            {
                value = Convert.ChangeType(value, property.PropertyType);
            }

            property.SetValue(null, value);
        }
    }
}
