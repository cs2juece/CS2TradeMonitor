using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using AntdUI;
using CS2TradeMonitor.src.Core;
using AntdButton = AntdUI.Button;
using AntdCheckbox = AntdUI.Checkbox;
using AntdInput = AntdUI.Input;

namespace CS2TradeMonitor.src.UI.Controls
{
    internal static class AntdSettingsBuilder
    {
        public static AntdButton CreateButton(string text, bool primary = false, int width = 120, int height = 38)
        {
            var button = new AntdButton
            {
                Text = text,
                Size = UIUtils.S(new Size(width, height)),
                Font = new Font("Microsoft YaHei UI", 9F, primary ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = primary ? Color.White : UIColors.TextMain,
                BackColor = primary ? UIColors.Primary : UIColors.ControlBg
            };

            SetOptionalProperty(button, "BorderWidth", primary ? 0F : 1F);
            SetOptionalProperty(button, "Radius", UIUtils.S(3));
            SetOptionalProperty(button, "WaveSize", 0);
            return button;
        }

        public static AntdInput CreateInput(string placeholder = "", int width = 320, int height = 36)
        {
            var input = new AntdInput
            {
                Size = UIUtils.S(new Size(width, height)),
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextMain,
                BackColor = UIColors.InputBg
            };

            SetOptionalProperty(input, "PlaceholderText", placeholder);
            SetOptionalProperty(input, "PlaceholderColor", UIColors.TextDisabled);
            SetOptionalProperty(input, "Radius", UIUtils.S(3));
            return input;
        }

        public static AntdCheckbox CreateCheckbox(string text, bool isChecked = false)
        {
            var checkbox = new AntdCheckbox
            {
                Text = text,
                Checked = isChecked,
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent
            };

            return checkbox;
        }

        private static void SetOptionalProperty(object target, string propertyName, object value)
        {
            try
            {
                var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (property == null || !property.CanWrite) return;

                var converted = value;
                if (value != null && !property.PropertyType.IsInstanceOfType(value))
                {
                    converted = Convert.ChangeType(value, property.PropertyType);
                }

                property.SetValue(target, converted);
            }
            catch
            {
                // AntdUI property names can change between versions; keep the fallback control usable.
            }
        }
    }
}
