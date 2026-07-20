using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor;
using System.Diagnostics; // TaskbarForm
using Debug = System.Diagnostics.Debug;


namespace CS2TradeMonitor.src.UI.Helpers
{
    /// <summary>
    /// 负责管理任务栏窗口的悬浮提示逻辑
    /// </summary>
    public class TaskbarTooltipHelper : IDisposable
    {
        private readonly Form _targetForm;
        private readonly Settings _cfg;
        private readonly UIController _ui;

        // 使用自定义的高性能无闪烁窗体替代 System.Windows.Forms.ToolTip
        private LiteTooltipForm? _tooltipForm;
        private bool _isHovering = false;

        // 悬停延迟机制
        private System.Windows.Forms.Timer? _hoverTimer;
        private System.Windows.Forms.Timer? _pollingTimer;

        private bool _canShow = false;
        private int _cachedTargetWidth = 0; // 缓存计算后的宽度
        private const int HOVER_DELAY_MS = 400; // 400ms 延迟
        private const int POLLING_INTERVAL_MS = 500;
        public TaskbarTooltipHelper(Form targetForm, Settings cfg, UIController ui)
        {
            _targetForm = targetForm;
            _cfg = cfg;
            _ui = ui;

            Initialize();
        }

        private void Initialize()
        {
            SetupMode();
        }

        public void ReloadMode()
        {
            // 清理旧的事件和计时器
            if (!_targetForm.IsDisposed)
            {
                _targetForm.MouseEnter -= OnMouseEnter;
                _targetForm.MouseLeave -= OnMouseLeave;
                _targetForm.MouseMove -= OnMouseMove;
            }

            _hoverTimer?.Stop();
            _hoverTimer?.Dispose();
            _hoverTimer = null;
            _pollingTimer?.Stop();
            _pollingTimer?.Dispose();
            _pollingTimer = null;
            _tooltipForm?.Hide();
            _isHovering = false;
            _canShow = false;
            _cachedTargetWidth = 0;

            // 重新初始化
            SetupMode();
        }

        private void SetupMode()
        {
            // 只要开启了悬浮窗功能，就进行初始化 (不再屏蔽穿透模式)
            if (_cfg.TaskbarHoverShowAll)
            {
                if (_tooltipForm == null || _tooltipForm.IsDisposed)
                {
                    _tooltipForm = new LiteTooltipForm();
                }

                // 初始化延迟计时器
                _hoverTimer = new System.Windows.Forms.Timer();
                _hoverTimer.Interval = HOVER_DELAY_MS;
                _hoverTimer.Tick += OnHoverTimerTick;

                if (_cfg.TaskbarClickThrough)
                {
                    _pollingTimer = new System.Windows.Forms.Timer
                    {
                        Interval = POLLING_INTERVAL_MS
                    };
                    _pollingTimer.Tick += OnPollingTimerTick;
                    _pollingTimer.Start();
                }
                else
                {
                    _targetForm.MouseEnter += OnMouseEnter;
                    _targetForm.MouseLeave += OnMouseLeave;
                    _targetForm.MouseMove += OnMouseMove;
                }
            }
            else
            {
                // 如果功能关闭，销毁窗体
                _tooltipForm?.Dispose();
                _tooltipForm = null;
            }
        }

        private void OnPollingTimerTick(object? sender, EventArgs e)
        {
            if (_targetForm.IsDisposed || !_targetForm.IsHandleCreated)
                return;

            Rectangle screenBounds;
            try
            {
                screenBounds = _targetForm.RectangleToScreen(_targetForm.ClientRectangle);
            }
            catch
            {
                return;
            }

            bool containsMouse = screenBounds.Contains(Cursor.Position);
            if (containsMouse && !_isHovering)
            {
                OnMouseEnter(sender, e);
            }
            else if (!containsMouse && _isHovering)
            {
                OnMouseLeave(sender, e);
            }
        }

        private void OnMouseEnter(object? sender, EventArgs e)
        {
            _isHovering = true;
            _canShow = false; // 进入时不立即显示
            _cachedTargetWidth = 0; // 重置宽度缓存，确保每次新悬停时重新测量

            _hoverTimer?.Stop();
            _hoverTimer?.Start(); // 开始计时
        }

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            // 如果 ToolTip 还没显示，且鼠标在移动，则重置计时器 (防抖)
            // 这样只有鼠标停下 (或移动非常慢) HOVER_DELAY_MS 后才会显示
            if (_tooltipForm != null && !_tooltipForm.Visible)
            {
                _hoverTimer?.Stop();
                _hoverTimer?.Start();
            }
        }

        private void OnMouseLeave(object? sender, EventArgs e)
        {
            _isHovering = false;
            _canShow = false;
            _hoverTimer?.Stop();
            _tooltipForm?.Hide();
        }

        private void OnHoverTimerTick(object? sender, EventArgs e)
        {
            _hoverTimer?.Stop();
            if (_isHovering)
            {
                _canShow = true;
                UpdateContent(); // 此时 _canShow 为 true，会触发显示
            }
        }

        /// <summary>
        /// 更新 ToolTip 内容。建议在数据刷新周期（Tick）或鼠标悬浮时调用。
        /// </summary>
        public void UpdateContent()
        {
            try
            {
                UpdateContentCore();
            }
            catch (ObjectDisposedException)
            {
                _tooltipForm = null;
                _isHovering = false;
                _canShow = false;
            }
            catch (InvalidOperationException)
            {
                _tooltipForm?.Hide();
            }
            catch
            {
                _tooltipForm?.Hide();
                _isHovering = false;
                _canShow = false;
            }
        }

        private void UpdateContentCore()
        {
            // 基础检查
            if (_tooltipForm == null || !_cfg.TaskbarHoverShowAll) return;

            // 如果没有悬浮，直接隐藏（防御性编程，防止 MouseLeave 漏掉）
            if (!_isHovering)
            {
                if (_tooltipForm.Visible) _tooltipForm.Hide();
                return;
            }

            // 检查菜单是否打开 (互斥逻辑：菜单打开时不显示悬浮窗)
            if (_targetForm is TaskbarForm tf && tf.IsMenuOpen)
            {
                if (_tooltipForm.Visible) _tooltipForm.Hide();
                return;
            }

            // 再次确认鼠标是否真的在 Form 内（防止 Alt-Tab 等情况导致的事件丢失）
            // [Fix] 在任务栏自动隐藏模式下，Bounds 判定可能不稳定，导致悬浮窗不显示。
            // 既然有 MouseEnter/Leave 事件维护状态，这里放宽检查。

            // 核心逻辑：只有计时器触发后才允许显示
            if (!_canShow && !_tooltipForm.Visible)
            {
                return;
            }

            // 优化：只有当需要显示时才获取数据
            var groups = _ui.GetMainGroups();
            if (groups == null) return;

            // 创建副本；通用模式下只显示用户选择的监控项，数据源诊断保留在右键菜单/数据源页。
            var displayGroups = new List<GroupLayoutInfo>(groups);
            if (_cfg.AdvancedMode)
            {
                var qaqSummary = CS2TradeMonitor.src.Core.Actions.AppActions.GetDataSourceSummary(_cfg);
                var statusItems = new List<MetricItem>();

                var qaqItem = new MetricItem
                {
                    Key = "CSQAQ_Status",
                    Label = "QAQ 状态",
                    TextValue = qaqSummary.QaqStatus == "正常" ? $"正常 ({qaqSummary.QaqLastRefresh})" : qaqSummary.QaqStatus == "异常" ? $"异常({qaqSummary.QaqError})" : "未获取"
                };

                string dtValText = "";
                if (qaqSummary.SteamDtStatus == "正常")
                    dtValText = $"正常 ({qaqSummary.SteamDtType}，{qaqSummary.SteamDtLastRefresh})";
                else if (qaqSummary.SteamDtStatus == "异常")
                    dtValText = $"异常({qaqSummary.SteamDtError})";
                else if (qaqSummary.SteamDtConfigState == "未配置 API Key")
                    dtValText = "等待公开页面接口刷新";
                else
                    dtValText = "未获取";

                var dtItem = new MetricItem
                {
                    Key = "STEAMDT_Status",
                    Label = "SteamDT 状态",
                    TextValue = dtValText
                };

                statusItems.Add(qaqItem);
                statusItems.Add(dtItem);

                var statusGroup = new GroupLayoutInfo("STATUS", statusItems)
                {
                    Label = "数据源状态"
                };
                displayGroups.Add(statusGroup);
            }

            // 传递结构化数据、当前主题、透明度和缩放比例
            var theme = ThemeManager.Current;

            // 获取当前缩放比例 (确保不为 0)
            float scale = theme.Layout.LayoutScale;
            if (scale <= 0.1f) scale = 1.0f;

            // 动态计算宽度：基于最长文本长度测量。
            // ★★★ 性能优化：只在显示时测量一次，并缓存结果 ★★★
            int targetWidth = GetTargetWidth(displayGroups, theme, scale);

            // 获取任务栏字体设置 (大字模式/自定义模式)
            bool isBold = _cfg.GetStyle().Bold;

            _tooltipForm.SetData(displayGroups, theme, _cfg.TextOpacity, targetWidth, scale, isBold);

            // 如果未显示，则显示并定位
            if (!_tooltipForm.Visible)
            {
                // 获取目标窗体在屏幕上的实际位置
                var rect = _targetForm.RectangleToScreen(_targetForm.ClientRectangle);
                _tooltipForm.UpdatePosition(rect, Cursor.Position);
                _tooltipForm.Show(_targetForm); // 指定 Owner 确保层级正确
            }
            // 注意：我们不在每一帧都 UpdatePosition，否则 ToolTip 会跟着鼠标微颤，
            // 除非我们想实现跟随鼠标的效果。这里保持位置固定直到下一次 MouseEnter
            // 或者：如果用户移动了鼠标，我们也不动，直到鼠标移出。
        }

        private int GetTargetWidth(List<GroupLayoutInfo> groups, Theme theme, float scale)
        {
            // 如果已有缓存宽度，直接使用 (避免每秒重复测量)
            if (_cachedTargetWidth > 0)
            {
                return _cachedTargetWidth;
            }

            _cachedTargetWidth = CalculateTargetWidth(groups, theme, scale);
            return _cachedTargetWidth;
        }

        internal static int CalculateTargetWidth(IReadOnlyCollection<GroupLayoutInfo> groups, Theme theme, float scale)
        {
            if (groups.Count == 0)
                return Scale(220, scale);

            scale = NormalizeScale(scale);

            int maxPixelW = 0;
            using (var font = new Font(theme.FontItem.FontFamily.Name, Math.Max(8f, theme.FontItem.Size - 0.5f)))
            {
                foreach (var item in groups.SelectMany(group => group.Items))
                {
                    maxPixelW = Math.Max(maxPixelW, MeasureTooltipRowWidth(item, font, scale));
                }
            }

            return Math.Max(Scale(220, scale), maxPixelW + Scale(32, scale));
        }

        private static int MeasureTooltipRowWidth(MetricItem item, Font font, float scale)
        {
            if (MarketDisplayFormatter.IsMarketDisplayKey(item.Key))
            {
                var segments = MarketDisplayFormatter.GetSegments(item.Key, item.RuntimeSettings);
                int width = MeasureTextWidth(segments.Label, font)
                    + Scale(MarketDisplayFormatter.LabelGap, scale)
                    + MeasureTextWidth(segments.PrimaryText, font);

                if (segments.HasData && !string.IsNullOrWhiteSpace(segments.SecondaryText))
                {
                    width += Scale(MarketDisplayFormatter.ValueGap, scale)
                        + MeasureTextWidth(segments.SecondaryText, font);
                }

                return width;
            }

            string label = item.Label;
            if (string.IsNullOrEmpty(label))
                label = item.Key;

            string value = item.GetFormattedText(false);
            if (string.IsNullOrWhiteSpace(value))
                value = item.TextValue ?? "";

            return MeasureTextWidth(label, font) + Scale(18, scale) + MeasureTextWidth(value, font);
        }

        private static int MeasureTextWidth(string text, Font font)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            return TextRenderer.MeasureText(
                text,
                font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        }

        private static int Scale(int value, float scale)
        {
            return (int)Math.Ceiling(value * NormalizeScale(scale));
        }

        private static float NormalizeScale(float scale)
        {
            return scale <= 0.1f ? 1.0f : scale;
        }

        public void Dispose()
        {
            if (!_targetForm.IsDisposed)
            {
                _targetForm.MouseEnter -= OnMouseEnter;
                _targetForm.MouseLeave -= OnMouseLeave;
                _targetForm.MouseMove -= OnMouseMove;
            }

            _hoverTimer?.Stop();
            _hoverTimer?.Dispose();
            _hoverTimer = null;
            _pollingTimer?.Stop();
            _pollingTimer?.Dispose();
            _pollingTimer = null;

            if (_tooltipForm != null)
            {
                _tooltipForm.Dispose();
                _tooltipForm = null;
            }
        }
    }
}
