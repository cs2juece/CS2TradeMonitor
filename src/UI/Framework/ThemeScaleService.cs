using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using Message = System.Windows.Forms.Message;

namespace CS2TradeMonitor.src.UI.Framework
{
    public interface IThemeConsumer
    {
        void OnThemeChanged();

        void OnDpiChanged();
    }

    public sealed class ThemeScaleService
    {
        public const int WmDpiChanged = 0x02E0;

        private const int DefaultDpi = 96;
        private static readonly Lazy<ThemeScaleService> _instance = new(() => new ThemeScaleService());

        private readonly object _syncRoot = new();
        private readonly HashSet<IThemeConsumer> _consumers = new();

        private Dictionary<string, Color> _colors = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, FontToken> _fonts = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, int> _spacing = new(StringComparer.OrdinalIgnoreCase);

        private ThemeScaleService()
        {
            IsDarkMode = UIColors.IsDark;
            Dpi = DefaultDpi;
            DpiScale = 1.0f;
            UserScale = 1.0f;
            ScaleFactor = 1.0f;
            SyncThemeLibraries(IsDarkMode);
            RebuildTokens();
        }

        public static ThemeScaleService Instance => _instance.Value;

        public bool IsDarkMode { get; private set; }

        public int Dpi { get; private set; }

        public float DpiScale { get; private set; }

        public float UserScale { get; private set; }

        public float ScaleFactor { get; private set; }

        public IReadOnlyDictionary<string, Color> Colors => _colors;

        public IReadOnlyDictionary<string, FontToken> Fonts => _fonts;

        public IReadOnlyDictionary<string, int> Spacing => _spacing;

        public void Subscribe(IThemeConsumer consumer, bool notifyCurrent = false)
        {
            if (consumer == null) throw new ArgumentNullException(nameof(consumer));

            lock (_syncRoot)
            {
                _consumers.Add(consumer);
            }

            if (notifyCurrent)
            {
                consumer.OnThemeChanged();
                consumer.OnDpiChanged();
            }
        }

        public void Unsubscribe(IThemeConsumer consumer)
        {
            if (consumer == null) return;

            lock (_syncRoot)
            {
                _consumers.Remove(consumer);
            }
        }

        public void SetDarkMode(bool dark)
        {
            if (IsDarkMode == dark) return;

            IsDarkMode = dark;
            SyncThemeLibraries(dark);
            RebuildTokens();
            NotifyThemeChanged();
        }

        public void ToggleDarkMode()
        {
            SetDarkMode(!IsDarkMode);
        }

        public void SetUserScale(float userScale)
        {
            float normalized = NormalizeUserScale(userScale);
            if (Math.Abs(UserScale - normalized) < 0.001f) return;

            UserScale = normalized;
            RecalculateScale();
            RebuildTokens();
            NotifyDpiChanged();
        }

        public void InitializeDpiFrom(Control control)
        {
            if (control == null) throw new ArgumentNullException(nameof(control));

            int dpi = TryGetDpiForControl(control);
            SetDpi(dpi);
        }

        public bool TryHandleDpiChanged(Message message)
        {
            if (message.Msg != WmDpiChanged) return false;

            int dpi = GetDpiFromWParam(message.WParam);
            SetDpi(dpi);
            return true;
        }

        public bool TryHandleDpiChanged(Form form, Message message, bool applySuggestedBounds = true)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));
            if (message.Msg != WmDpiChanged) return false;

            int dpi = GetDpiFromWParam(message.WParam);
            if (applySuggestedBounds)
            {
                TryApplySuggestedBounds(form, message.LParam);
            }

            SetDpi(dpi);
            return true;
        }

        public int S(int px) => (int)Math.Round(px * ScaleFactor);

        public float S(float px) => px * ScaleFactor;

        public Size S(Size size) => new(S(size.Width), S(size.Height));

        public Padding S(Padding padding) => new(S(padding.Left), S(padding.Top), S(padding.Right), S(padding.Bottom));

        public Font CreateFont(string tokenName, FontStyle? styleOverride = null)
        {
            if (!_fonts.TryGetValue(tokenName, out FontToken token))
            {
                throw new KeyNotFoundException($"Unknown font token: {tokenName}");
            }

            return UIUtils.GetFont(token.Family, token.Size * UserScale, (styleOverride ?? token.Style).HasFlag(FontStyle.Bold));
        }

        public int GetSpacing(string tokenName)
        {
            if (!_spacing.TryGetValue(tokenName, out int value))
            {
                throw new KeyNotFoundException($"Unknown spacing token: {tokenName}");
            }

            return S(value);
        }

        private void SetDpi(int dpi)
        {
            int normalized = NormalizeDpi(dpi);
            if (Dpi == normalized) return;

            Dpi = normalized;
            RecalculateScale();
            RebuildTokens();
            NotifyDpiChanged();
        }

        private void RecalculateScale()
        {
            DpiScale = Dpi / (float)DefaultDpi;
            ScaleFactor = DpiScale * UserScale;
            UIUtils.UpdateScale(DpiScale, UserScale);
        }

        private void SyncThemeLibraries(bool dark)
        {
            TryInvokeAntdSetDarkMode(dark);
            UIColors.ApplySettingsTheme(dark);
        }

        private static void TryInvokeAntdSetDarkMode(bool dark)
        {
            try
            {
                MethodInfo? method = typeof(AntdUI.Config).GetMethod(
                    "SetDarkMode",
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: new[] { typeof(bool) },
                    modifiers: null);

                if (method != null)
                {
                    method.Invoke(null, new object[] { dark });
                }
            }
            catch
            {
                // AntdUI versions differ; AntdThemeBridge.Apply remains the compatibility fallback.
            }
        }

        private void RebuildTokens()
        {
            _colors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
            {
                ["MainBg"] = UIColors.MainBg,
                ["SidebarBg"] = UIColors.SidebarBg,
                ["CardBg"] = UIColors.CardBg,
                ["Border"] = UIColors.Border,
                ["Primary"] = UIColors.Primary,
                ["TextMain"] = UIColors.TextMain,
                ["TextSub"] = UIColors.TextSub,
                ["TextWarn"] = UIColors.TextWarn,
                ["TextCrit"] = UIColors.TextCrit,
                ["ControlBg"] = UIColors.ControlBg,
                ["InputBg"] = UIColors.InputBg,
                ["ControlHover"] = UIColors.ControlHover,
                ["ControlPressed"] = UIColors.ControlPressed,
                ["ControlDisabledBg"] = UIColors.ControlDisabledBg,
                ["TextDisabled"] = UIColors.TextDisabled,
                ["Link"] = UIColors.Link,
                ["LinkHover"] = UIColors.LinkHover,
                ["Positive"] = UIColors.Positive,
                ["Negative"] = UIColors.Negative,
                ["NavSelected"] = UIColors.NavSelected,
                ["NavHover"] = UIColors.NavHover,
                ["GroupHeader"] = UIColors.GroupHeader
            };

            _fonts = new Dictionary<string, FontToken>(StringComparer.OrdinalIgnoreCase)
            {
                ["Caption"] = new("Microsoft YaHei UI", 8.0f, FontStyle.Regular),
                ["Body"] = new("Microsoft YaHei UI", 9.0f, FontStyle.Regular),
                ["BodyStrong"] = new("Microsoft YaHei UI", 9.0f, FontStyle.Bold),
                ["Title"] = new("Microsoft YaHei UI", 11.0f, FontStyle.Bold),
                ["Mono"] = new("Consolas", 9.0f, FontStyle.Regular)
            };

            _spacing = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["None"] = 0,
                ["Xs"] = 4,
                ["Sm"] = 8,
                ["Md"] = 12,
                ["Lg"] = 16,
                ["Xl"] = 24,
                ["Xxl"] = 32
            };
        }

        private void NotifyThemeChanged()
        {
            foreach (IThemeConsumer consumer in SnapshotConsumers())
            {
                try
                {
                    consumer.OnThemeChanged();
                }
                catch
                {
                    // Theme consumers should not block global theme changes.
                }
            }
        }

        private void NotifyDpiChanged()
        {
            foreach (IThemeConsumer consumer in SnapshotConsumers())
            {
                try
                {
                    consumer.OnDpiChanged();
                }
                catch
                {
                    // DPI consumers should not block cross-monitor scale updates.
                }
            }
        }

        private IThemeConsumer[] SnapshotConsumers()
        {
            lock (_syncRoot)
            {
                IThemeConsumer[] snapshot = new IThemeConsumer[_consumers.Count];
                _consumers.CopyTo(snapshot);
                return snapshot;
            }
        }

        private static int NormalizeDpi(int dpi) => Math.Clamp(dpi, 48, 768);

        private static float NormalizeUserScale(float userScale)
        {
            if (float.IsNaN(userScale) || float.IsInfinity(userScale)) return 1.0f;
            return Math.Clamp(userScale, 0.5f, 2.0f);
        }

        private static int GetDpiFromWParam(IntPtr wParam)
        {
            long value = wParam.ToInt64();
            int dpiX = (int)(value & 0xFFFF);
            return dpiX > 0 ? dpiX : DefaultDpi;
        }

        private static int TryGetDpiForControl(Control control)
        {
            if (control.IsHandleCreated)
            {
                try
                {
                    int dpi = GetDpiForWindow(control.Handle);
                    if (dpi > 0) return dpi;
                }
                catch
                {
                    // Fall back to Graphics.DpiX below.
                }
            }

            using Graphics graphics = control.CreateGraphics();
            return (int)Math.Round(graphics.DpiX);
        }

        private static void TryApplySuggestedBounds(Form form, IntPtr suggestedRect)
        {
            if (suggestedRect == IntPtr.Zero) return;

            try
            {
                Rect rect = Marshal.PtrToStructure<Rect>(suggestedRect);
                form.Bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            }
            catch
            {
                // Suggested bounds are advisory; DPI token refresh still proceeds.
            }
        }

        [DllImport("user32.dll")]
        private static extern int GetDpiForWindow(IntPtr hwnd);

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct Rect
        {
            public readonly int Left;
            public readonly int Top;
            public readonly int Right;
            public readonly int Bottom;
        }
    }

    public readonly record struct FontToken(string Family, float Size, FontStyle Style);
}
