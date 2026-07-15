using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.Helpers;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class MainPanelPreviewController : IDisposable
    {
        private readonly Control _container;
        private readonly Func<Settings?> _getConfig;
        private readonly Func<string, bool, bool> _getBool;
        private readonly Func<string, string, string> _getString;
        private readonly Func<string, float, float> _getFloat;
        private readonly Func<bool> _canQueueRefresh;
        private readonly Func<bool> _canApplyRefresh;
        private readonly UiDeferredActionScheduler _deferredActions;
        private Panel? _floatingPreviewPanel;
        private Panel? _taskbarPreviewPanel;
        private Label? _floatingPreviewTitle;
        private Label? _taskbarPreviewTitle;
        private Label? _floatingQaqText;
        private Label? _floatingDtText;
        private bool _previewRefreshPending;
        private string? _previewSignature;

        public MainPanelPreviewController(
            Control container,
            Func<Settings?> getConfig,
            Func<string, bool, bool> getBool,
            Func<string, string, string> getString,
            Func<string, float, float> getFloat,
            Func<bool> canQueueRefresh,
            Func<bool> canApplyRefresh)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
            _getConfig = getConfig ?? throw new ArgumentNullException(nameof(getConfig));
            _getBool = getBool ?? throw new ArgumentNullException(nameof(getBool));
            _getString = getString ?? throw new ArgumentNullException(nameof(getString));
            _getFloat = getFloat ?? throw new ArgumentNullException(nameof(getFloat));
            _canQueueRefresh = canQueueRefresh ?? throw new ArgumentNullException(nameof(canQueueRefresh));
            _canApplyRefresh = canApplyRefresh ?? throw new ArgumentNullException(nameof(canApplyRefresh));
            _deferredActions = new UiDeferredActionScheduler(() => true);
        }

        public Panel? Wrapper { get; private set; }

        public void Reset()
        {
            Wrapper = null;
            _floatingPreviewPanel = null;
            _taskbarPreviewPanel = null;
            _floatingPreviewTitle = null;
            _taskbarPreviewTitle = null;
            _floatingQaqText = null;
            _floatingDtText = null;
            _previewSignature = null;
            _previewRefreshPending = false;
        }

        public void Create()
        {
            Wrapper = new BufferedPanel
            {
                Height = UIUtils.S(138),
                Padding = new Padding(0, 0, 0, UIUtils.S(10)),
                BackColor = Color.Transparent
            };

            var frame = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UIColors.CardBg,
                Padding = UIUtils.S(new Padding(12))
            };
            frame.Paint += PaintBorder;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
                BackColor = UIColors.CardBg
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            layout.Controls.Add(CreateFloatingPreview(), 0, 0);
            layout.Controls.Add(CreateTaskbarPreview(), 1, 0);
            frame.Controls.Add(layout);
            Wrapper.Controls.Add(frame);
            _container.Controls.Add(Wrapper);
            _container.Controls.SetChildIndex(Wrapper, 0);
        }

        public void Show()
        {
            if (Wrapper != null)
                Wrapper.Visible = true;
        }

        public int Layout(int x, int y, int width)
        {
            if (Wrapper == null)
                return y;

            Wrapper.SetBounds(x, y, width, UIUtils.S(138));
            return Wrapper.Bottom;
        }

        public void Refresh()
        {
            if (!_canQueueRefresh())
                return;

            if (_previewRefreshPending)
                return;

            _previewRefreshPending = true;
            _deferredActions.Schedule("preview-refresh", 90, ApplyDeferredRefresh);
        }

        public void Dispose()
        {
            _deferredActions.Dispose();
        }

        private Control CreateFloatingPreview()
        {
            var panel = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                Margin = UIUtils.S(new Padding(0, 0, 8, 0)),
                BackColor = UIColors.CardBg
            };
            panel.Paint += PaintBorder;
            panel.Resize += (_, __) => LayoutFloatingPreview();
            _floatingPreviewPanel = panel;

            _floatingPreviewTitle = CreatePreviewTitle("悬浮窗预览");
            _floatingQaqText = CreatePreviewLabel("QAQ指数 1888.00  +1.00%", Color.FromArgb(210, 70, 70));
            _floatingDtText = CreatePreviewLabel("DT指数 888.00  -1.00%", Color.FromArgb(0, 160, 100));

            panel.Controls.Add(_floatingPreviewTitle);
            panel.Controls.Add(_floatingQaqText);
            panel.Controls.Add(_floatingDtText);
            LayoutFloatingPreview();
            return panel;
        }

        private Control CreateTaskbarPreview()
        {
            var panel = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                Margin = UIUtils.S(new Padding(8, 0, 0, 0)),
                BackColor = UIColors.CardBg
            };
            panel.Paint += PaintTaskbarPreview;
            panel.Resize += (_, __) => LayoutTaskbarPreview();
            _taskbarPreviewPanel = panel;

            _taskbarPreviewTitle = CreatePreviewTitle("任务栏预览");

            panel.Controls.Add(_taskbarPreviewTitle);
            LayoutTaskbarPreview();
            return panel;
        }

        private static Label CreatePreviewTitle(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Font = UIFonts.Bold(10f),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
        }

        private static Label CreatePreviewLabel(string text, Color color)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Font = UIFonts.Bold(9f),
                ForeColor = color,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
        }

        private void LayoutFloatingPreview()
        {
            if (_floatingPreviewPanel is null) return;
            int pad = UIUtils.S(14);
            int line = UIUtils.S(24);
            _floatingPreviewTitle?.SetBounds(pad, UIUtils.S(10), Math.Max(1, _floatingPreviewPanel.Width - pad * 2), line);
            _floatingQaqText?.SetBounds(pad, UIUtils.S(42), Math.Max(1, _floatingPreviewPanel.Width - pad * 2), line);
            _floatingDtText?.SetBounds(pad, UIUtils.S(68), Math.Max(1, _floatingPreviewPanel.Width - pad * 2), line);
        }

        private void LayoutTaskbarPreview()
        {
            if (_taskbarPreviewPanel is null) return;
            int pad = UIUtils.S(14);
            int line = UIUtils.S(24);
            _taskbarPreviewTitle?.SetBounds(pad, UIUtils.S(10), Math.Max(1, _taskbarPreviewPanel.Width - pad * 2), line);
            _taskbarPreviewPanel.Invalidate();
        }

        private void ApplyDeferredRefresh()
        {
            _previewRefreshPending = false;
            if (_canApplyRefresh())
                RefreshNow();
        }

        private void RefreshNow()
        {
            Color previewBg = UIColors.MainBg;
            Color taskbarBg = ParseColor(_getString(nameof(Settings.TaskbarColorBg), "#000000"), UIColors.MainBg);
            Color safe = ParseColor(_getString(nameof(Settings.TaskbarColorSafe), "#00CC66"), Color.FromArgb(0, 160, 100));
            Color crit = ParseColor(_getString(nameof(Settings.TaskbarColorCrit), "#FF4444"), Color.FromArgb(210, 70, 70));
            Color label = ParseColor(_getString(nameof(Settings.TaskbarColorLabel), "#FFFFFF"), UIColors.TextMain);
            bool taskbarSingleLine = _getBool(nameof(Settings.TaskbarSingleLine), false);
            string fontFamily = _getString(nameof(Settings.TaskbarFontFamily), Settings.DEFAULT_TB_FONT);
            float fontSize = Math.Max(8f, _getFloat(nameof(Settings.TaskbarFontSize), Settings.DEFAULT_TB_SIZE_BOLD));
            bool bold = _getBool(nameof(Settings.TaskbarFontBold), true);
            float taskbarDpiScale = TaskbarWinHelper.GetTaskbarDpi() / 96f;
            string signature = string.Join("|",
                previewBg.ToArgb(),
                taskbarBg.ToArgb(),
                safe.ToArgb(),
                crit.ToArgb(),
                label.ToArgb(),
                taskbarSingleLine,
                fontFamily,
                fontSize.ToString("0.###"),
                bold,
                taskbarDpiScale.ToString("0.###"));

            if (string.Equals(_previewSignature, signature, StringComparison.Ordinal))
                return;

            _previewSignature = signature;

            if (_floatingPreviewPanel != null) _floatingPreviewPanel.BackColor = previewBg;
            if (_taskbarPreviewPanel != null) _taskbarPreviewPanel.BackColor = previewBg;
            if (_floatingPreviewTitle != null) _floatingPreviewTitle.ForeColor = UIColors.TextSub;
            if (_taskbarPreviewTitle != null) _taskbarPreviewTitle.ForeColor = label;
            if (_floatingQaqText != null) _floatingQaqText.ForeColor = crit;
            if (_floatingDtText != null) _floatingDtText.ForeColor = safe;
            Font previewFont = UIUtils.GetFont(fontFamily, fontSize * taskbarDpiScale, bold);
            if (_floatingQaqText != null) _floatingQaqText.Font = previewFont;
            if (_floatingDtText != null) _floatingDtText.Font = previewFont;

            _floatingPreviewPanel?.Invalidate();
            _taskbarPreviewPanel?.Invalidate();
        }

        private void PaintTaskbarPreview(object? sender, PaintEventArgs e)
        {
            if (sender is not Control control) return;

            PaintBorder(sender, e);

            int pad = UIUtils.S(14);
            int top = UIUtils.S(42);
            int width = Math.Max(1, control.ClientSize.Width - pad * 2);
            int availableHeight = Math.Max(1, control.ClientSize.Height - top - pad);
            bool singleLine = _getBool(nameof(Settings.TaskbarSingleLine), false);
            int previewHeight = Math.Min(availableHeight, UIUtils.S(singleLine ? 34 : 48));
            var strip = new Rectangle(
                pad,
                top,
                width,
                previewHeight);

            TaskbarRenderer.RenderStaticPreview(
                e.Graphics,
                // 设置页预览优先使用草稿配置；没有绑定设置存储时才回退到持久化配置。
                _getConfig() ?? Settings.Load(),
                strip,
                light: !UIColors.IsDark,
                singleLine: singleLine);
        }

        private static void PaintBorder(object? sender, PaintEventArgs e)
        {
            if (sender is not Control control) return;
            using var pen = new Pen(UIColors.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, control.Width - 1, control.Height - 1);
        }

        private static Color ParseColor(string hex, Color fallback)
        {
            try
            {
                return string.IsNullOrWhiteSpace(hex) ? fallback : ColorTranslator.FromHtml(hex);
            }
            catch
            {
                return fallback;
            }
        }
    }
}
