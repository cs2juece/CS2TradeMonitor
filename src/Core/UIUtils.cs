using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.Core
{
    /// <summary>
    /// CS2TradeMonitor UI Utilities (Refactored)
    /// Logic delegated to MetricUtils.
    /// This class now focuses on GDI+ Rendering helpers and Resource Management.
    /// </summary>
    public static class UIUtils
    {
        // ============================================================
        // 1. String Interning (Memory Optimization)
        // ============================================================
        private static readonly Dictionary<string, string> _stringPool = new(StringComparer.Ordinal);
        private static readonly object _poolLock = new object();

        public static string Intern(string str)
        {
            if (string.IsNullOrEmpty(str)) return string.Empty;
            lock (_poolLock)
            {
                if (_stringPool.TryGetValue(str, out var pooled)) return pooled;
                _stringPool[str] = str;
                return str;
            }
        }

        public static void ClearStringPool()
        {
            lock (_poolLock) _stringPool.Clear();
        }

        // ============================================================
        // 2. DPI Scaling
        // ============================================================
        public static float DpiScale { get; set; } = 1.0f;
        public static float UserScale { get; set; } = 1.0f;
        public static float ScaleFactor
        {
            get => DpiScale * UserScale;
            set
            {
                DpiScale = value <= 0 ? 1.0f : value;
                UserScale = 1.0f;
            }
        }

        public static void UpdateScale(float dpiScale, float userScale)
        {
            DpiScale = dpiScale;
            UserScale = Math.Clamp(userScale, 0.5f, 2.0f);
        }

        public static int S(int px) => (int)(px * ScaleFactor);
        public static float S(float px) => px * ScaleFactor;
        public static Size S(Size size) => new Size(S(size.Width), S(size.Height));
        public static Padding S(Padding p) => new Padding(S(p.Left), S(p.Top), S(p.Right), S(p.Bottom));

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, BoxedFloat> _originalFontSizes = new();

        private class BoxedFloat
        {
            public float Value { get; set; }
            public BoxedFloat(float val) { Value = val; }
        }

        public static void ScaleControlFontIfNeeded(Control control)
        {
            ScaleControlFonts(control, recursive: true, skipContentPanel: false);
        }

        public static void ScaleControlFontAndSize(Control control, bool recursive = true, bool skipContentPanel = false)
        {
            ScaleControlFonts(control, recursive, skipContentPanel);
        }

        public static void ScaleControlFonts(Control control, bool recursive = true, bool skipContentPanel = false)
        {
            if (control == null) return;

            bool isInherited = control.Parent != null && control.Font == control.Parent.Font;
            if (control.Font != null && !isInherited)
            {
                if (!_originalFontSizes.TryGetValue(control, out var boxedFont))
                {
                    boxedFont = new BoxedFloat(control.Font.Size);
                    _originalFontSizes.Add(control, boxedFont);
                }
                float targetSize = boxedFont.Value * UserScale;
                if (Math.Abs(control.Font.Size - targetSize) > 0.01f)
                {
                    control.Font = GetFont(control.Font.FontFamily.Name, targetSize, control.Font.Bold);
                }
            }

            if (recursive)
            {
                foreach (Control child in control.Controls)
                {
                    if (skipContentPanel && child.Name == "_pnlContent")
                        continue;
                    ScaleControlFonts(child, true, skipContentPanel);
                }
            }
        }

        // ============================================================
        // 3. GDI+ Resource Cache (Brushes & Fonts)
        // ============================================================
        private static readonly Dictionary<string, SolidBrush> _brushCache = new(16);
        private static readonly Dictionary<string, Pen> _penCache = new(16);
        private static readonly Dictionary<string, Font> _fontCache = new(16);
        private static readonly Dictionary<GrayTextCacheKey, GrayTextCacheEntry> _grayTextCache = new(128);
        private static readonly object _brushLock = new object();
        private const int MAX_BRUSH_CACHE = 32;
        private const int MAX_GRAY_TEXT_CACHE_ITEMS = 256;
        private const long MAX_GRAY_TEXT_CACHE_BYTES = 8L * 1024 * 1024;
        private static long _grayTextCacheBytes = 0;
        private static long _grayTextCacheTick = 0;

        public static Pen GetPen(Color color, float width = 1.0f)
        {
            string key = $"{color.ToArgb()}_{width:F1}";
            lock (_brushLock)
            {
                if (!_penCache.TryGetValue(key, out var pen))
                {
                    if (_penCache.Count >= MAX_BRUSH_CACHE)
                    {
                        foreach (var p in _penCache.Values) p.Dispose();
                        _penCache.Clear();
                    }
                    pen = new Pen(color, width);
                    _penCache[key] = pen;


                }
                return pen;
            }
        }

        public static SolidBrush GetBrush(string color)
        {
            if (string.IsNullOrEmpty(color)) return (SolidBrush)Brushes.Transparent;

            lock (_brushLock)
            {
                if (!_brushCache.TryGetValue(color, out var br))
                {
                    // Cache eviction policy
                    if (_brushCache.Count >= MAX_BRUSH_CACHE)
                    {
                        var keysToRemove = _brushCache.Keys.Take(_brushCache.Count / 2).ToList();
                        foreach (var k in keysToRemove)
                        {
                            if (_brushCache.TryGetValue(k, out var oldBrush))
                            {
                                oldBrush.Dispose();
                                _brushCache.Remove(k);
                            }
                        }
                    }

                    br = new SolidBrush(ThemeManager.ParseColor(color));
                    _brushCache[color] = br;
                }
                return br;
            }
        }

        public static Font GetFont(string familyName, float size, bool bold)
        {
            string key = $"{familyName}_{size}_{bold}";
            lock (_brushLock)
            {
                if (!_fontCache.TryGetValue(key, out var font))
                {
                    try
                    {
                        var style = bold ? FontStyle.Bold : FontStyle.Regular;
                        font = new Font(familyName, size, style);
                    }
                    catch
                    {
                        font = new Font(SystemFonts.DefaultFont.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular);
                    }
                    _fontCache[key] = font;
                }
                return font;
            }
        }

        public static Font GetScaledFont(string familyName, float baseSize, bool bold)
        {
            return GetFont(familyName, baseSize * UserScale, bold);
        }

        public static void ClearBrushCache()
        {
            lock (_brushLock)
            {
                foreach (var b in _brushCache.Values) b.Dispose();
                _brushCache.Clear();

                foreach (var p in _penCache.Values) p.Dispose();
                _penCache.Clear();

                foreach (var f in _fontCache.Values) f.Dispose();
                _fontCache.Clear();

                ClearGrayTextCacheLocked();
            }
        }

        // ============================================================
        // 4. Theme Helpers
        // ============================================================

        public static Color GetStateColor(int state, Theme t, bool isValueText = true)
        {
            if (state == MetricUtils.STATE_CRIT) return ThemeManager.ParseColor(isValueText ? t.Color.ValueCrit : t.Color.BarHigh);
            if (state == MetricUtils.STATE_WARN) return ThemeManager.ParseColor(isValueText ? t.Color.ValueWarn : t.Color.BarMid);
            return ThemeManager.ParseColor(t.Color.ValueSafe);
        }

        // ============================================================
        // 5. Drawing Helpers
        // ============================================================

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0) return p;

            if (radius <= 0)
            {
                p.AddRectangle(r);
                return p;
            }

            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;

            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRoundRect(Graphics g, Rectangle r, int radius, Color c)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using var brush = new SolidBrush(c);
            using var path = RoundRect(r, radius);
            g.FillPath(brush, path);
        }

        public static Color WithOpacity(Color color, double opacity)
        {
            opacity = Math.Clamp(opacity, 0.0, 1.0);
            int alpha = (int)Math.Round(color.A * opacity);
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        public static void DrawText(Graphics g, string text, Font font, Rectangle bounds, Color color, TextFormatFlags flags, double opacity = 1.0)
        {
            if (string.IsNullOrEmpty(text) || bounds.Width <= 0 || bounds.Height <= 0) return;

            opacity = Math.Clamp(opacity, 0.0, 1.0);
            if (opacity >= 0.995)
            {
                TextRenderer.DrawText(g, text, font, bounds, color, flags);
                return;
            }

            using var layer = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
            using (var layerGraphics = Graphics.FromImage(layer))
            {
                layerGraphics.Clear(Color.Transparent);
                TextRenderer.DrawText(layerGraphics, text, font,
                    new Rectangle(0, 0, bounds.Width, bounds.Height),
                    Color.FromArgb(255, color.R, color.G, color.B),
                    flags);
            }

            using var attributes = new ImageAttributes();
            var matrix = new ColorMatrix { Matrix33 = (float)opacity };
            attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            g.DrawImage(layer, bounds, 0, 0, layer.Width, layer.Height, GraphicsUnit.Pixel, attributes);
        }

        public static void DrawTextGrayAA(Graphics g, string text, Font font, Rectangle bounds, Color color, TextFormatFlags flags, double opacity = 1.0)
        {
            if (string.IsNullOrEmpty(text) || bounds.Width <= 0 || bounds.Height <= 0) return;

            opacity = Math.Clamp(opacity, 0.0, 1.0);
            font = EnsureUsableFont(font);

            try
            {
                var key = GrayTextCacheKey.Create(text, font, bounds.Size, color, flags, opacity);
                lock (_brushLock)
                {
                    Bitmap layer;
                    if (_grayTextCache.TryGetValue(key, out var entry))
                    {
                        entry.LastUsed = ++_grayTextCacheTick;
                        layer = entry.Bitmap;
                    }
                    else
                    {
                        layer = BuildGrayTextBitmap(text, font, bounds.Size, color, flags, opacity);
                        var newEntry = new GrayTextCacheEntry(layer, EstimateBitmapBytes(layer), ++_grayTextCacheTick);
                        _grayTextCache[key] = newEntry;
                        _grayTextCacheBytes += newEntry.Bytes;
                    }

                    g.DrawImageUnscaled(layer, bounds.X, bounds.Y);
                    TrimGrayTextCacheLocked();
                }
            }
            catch
            {
                DrawTextFallback(g, text, font, bounds, color, flags, opacity);
            }
        }

        private static Bitmap BuildGrayTextBitmap(string text, Font font, Size size, Color color, TextFormatFlags flags, double opacity)
        {
            var layer = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            try
            {
                using var layerGraphics = Graphics.FromImage(layer);
                layerGraphics.Clear(Color.Transparent);
                layerGraphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                layerGraphics.SmoothingMode = SmoothingMode.None;
                layerGraphics.PixelOffsetMode = PixelOffsetMode.None;

                using var brush = new SolidBrush(Color.White);
                using var format = new StringFormat(StringFormat.GenericTypographic)
                {
                    FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
                    Trimming = StringTrimming.None,
                    Alignment = ToStringAlignment(flags),
                    LineAlignment = ToLineAlignment(flags)
                };

                layerGraphics.DrawString(text, font, brush, new RectangleF(0, 0, size.Width, size.Height), format);
                ColorizePremultipliedMask(layer, color, opacity);
                return layer;
            }
            catch
            {
                layer.Dispose();
                throw;
            }
        }

        private static Font EnsureUsableFont(Font font)
        {
            try
            {
                _ = font.FontFamily.Name;
                _ = font.Size;
                return font;
            }
            catch
            {
                return SystemFonts.DefaultFont;
            }
        }

        private static void DrawTextFallback(Graphics g, string text, Font font, Rectangle bounds, Color color, TextFormatFlags flags, double opacity)
        {
            try
            {
                int alpha = (int)Math.Round(color.A * Math.Clamp(opacity, 0.0, 1.0));
                var fallbackColor = Color.FromArgb(Math.Clamp(alpha, 0, 255), color.R, color.G, color.B);
                TextRenderer.DrawText(g, text, font, bounds, fallbackColor, flags);
            }
            catch
            {
                // Paint must never crash the taskbar host. Dropping one text segment is safer than a red error rectangle.
            }
        }

        private static void ColorizePremultipliedMask(Bitmap bitmap, Color color, double opacity)
        {
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            try
            {
                int bytes = Math.Abs(data.Stride) * bitmap.Height;
                var buffer = new byte[bytes];
                Marshal.Copy(data.Scan0, buffer, 0, bytes);

                double colorAlpha = color.A / 255.0;
                double alphaScale = opacity * colorAlpha;
                for (int y = 0; y < bitmap.Height; y++)
                {
                    int row = y * Math.Abs(data.Stride);
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        int i = row + x * 4;
                        int maskAlpha = buffer[i + 3];
                        if (maskAlpha <= 0)
                        {
                            buffer[i] = 0;
                            buffer[i + 1] = 0;
                            buffer[i + 2] = 0;
                            continue;
                        }

                        int finalAlpha = (int)Math.Round(maskAlpha * alphaScale);
                        buffer[i] = (byte)(color.B * finalAlpha / 255);
                        buffer[i + 1] = (byte)(color.G * finalAlpha / 255);
                        buffer[i + 2] = (byte)(color.R * finalAlpha / 255);
                        buffer[i + 3] = (byte)Math.Clamp(finalAlpha, 0, 255);
                    }
                }

                Marshal.Copy(buffer, 0, data.Scan0, bytes);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private static StringAlignment ToStringAlignment(TextFormatFlags flags)
        {
            if ((flags & TextFormatFlags.Right) == TextFormatFlags.Right) return StringAlignment.Far;
            if ((flags & TextFormatFlags.HorizontalCenter) == TextFormatFlags.HorizontalCenter) return StringAlignment.Center;
            return StringAlignment.Near;
        }

        private static StringAlignment ToLineAlignment(TextFormatFlags flags)
        {
            if ((flags & TextFormatFlags.Bottom) == TextFormatFlags.Bottom) return StringAlignment.Far;
            if ((flags & TextFormatFlags.VerticalCenter) == TextFormatFlags.VerticalCenter) return StringAlignment.Center;
            return StringAlignment.Near;
        }

        private static long EstimateBitmapBytes(Bitmap bitmap) => (long)bitmap.Width * bitmap.Height * 4;

        private static void TrimGrayTextCacheLocked()
        {
            if (_grayTextCache.Count <= MAX_GRAY_TEXT_CACHE_ITEMS && _grayTextCacheBytes <= MAX_GRAY_TEXT_CACHE_BYTES)
                return;

            var remove = _grayTextCache
                .OrderBy(kv => kv.Value.LastUsed)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in remove)
            {
                if (!_grayTextCache.TryGetValue(key, out var entry))
                    continue;

                _grayTextCacheBytes -= entry.Bytes;
                entry.Bitmap.Dispose();
                _grayTextCache.Remove(key);

                if (_grayTextCache.Count <= MAX_GRAY_TEXT_CACHE_ITEMS && _grayTextCacheBytes <= MAX_GRAY_TEXT_CACHE_BYTES)
                    break;
            }
        }

        private static void ClearGrayTextCacheLocked()
        {
            foreach (var entry in _grayTextCache.Values)
            {
                entry.Bitmap.Dispose();
            }

            _grayTextCache.Clear();
            _grayTextCacheBytes = 0;
        }

        private readonly record struct GrayTextCacheKey(
            string Text,
            string FontFamily,
            float FontSize,
            FontStyle FontStyle,
            GraphicsUnit FontUnit,
            byte GdiCharSet,
            bool GdiVerticalFont,
            int Width,
            int Height,
            int ColorArgb,
            TextFormatFlags Flags,
            int OpacityPermille)
        {
            public static GrayTextCacheKey Create(string text, Font font, Size size, Color color, TextFormatFlags flags, double opacity)
            {
                return new GrayTextCacheKey(
                    text,
                    font.FontFamily.Name,
                    font.Size,
                    font.Style,
                    font.Unit,
                    font.GdiCharSet,
                    font.GdiVerticalFont,
                    size.Width,
                    size.Height,
                    color.ToArgb(),
                    flags,
                    (int)Math.Round(Math.Clamp(opacity, 0.0, 1.0) * 1000));
            }
        }

        private sealed class GrayTextCacheEntry
        {
            public GrayTextCacheEntry(Bitmap bitmap, long bytes, long lastUsed)
            {
                Bitmap = bitmap;
                Bytes = bytes;
                LastUsed = lastUsed;
            }

            public Bitmap Bitmap { get; }
            public long Bytes { get; }
            public long LastUsed { get; set; }
        }

        public static void DrawBar(Graphics g, MetricItem item, Theme t)
        {
            if (item.BarRect.Width <= 0 || item.BarRect.Height <= 0) return;

            // Background
            using (var bgPath = RoundRect(item.BarRect, item.BarRect.Height / 2))
            {
                g.FillPath(GetBrush(t.Color.BarBackground), bgPath);
            }

            // 2. Value Bar
            // [Refactor] CachedPercent already includes visual correction (min 5%, max 100%) via MetricUtils.GetProgressValue
            double percent = item.CachedPercent;

            int w = (int)(item.BarRect.Width * percent);
            if (w < 1 && percent > 0) w = 1; // 绝对防御：只要有百分比，至少画1像素

            // Color
            Color barColor = GetStateColor(item.CachedColorState, t, false);

            if (w > 0)
            {
                Rectangle bar = new Rectangle(item.BarRect.X, item.BarRect.Y, w, item.BarRect.Height);
                if (bar.Width > 0 && bar.Height > 0)
                {
                    using (var fgPath = RoundRect(bar, bar.Height / 2))
                    using (var brush = new SolidBrush(barColor))
                    {
                        g.FillPath(brush, fgPath);
                    }
                }
            }
        }
    }
}
