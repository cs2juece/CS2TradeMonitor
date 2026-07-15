using System;
using System.Drawing;
using System.Linq; // 需要 Linq 查询配置。
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.SystemServices.InfoService; // 插件颜色覆盖需要 InfoService。

namespace CS2TradeMonitor
{
    public enum MetricRenderStyle
    {
        StandardBar,
        TwoColumn,
        TextOnly
    }

    public class MetricItem
    {
        // 绑定原始配置对象，实现动态标签。
        public MonitorItemConfig? BoundConfig { get; set; }
        public Settings? RuntimeSettings { get; set; } = null;
        private IInfoService? _infoService;
        public IInfoService InfoService
        {
            get => _infoService ??= MetricRuntimeServices.ResolveInfoService();
            set => _infoService = value ?? throw new ArgumentNullException(nameof(value));
        }

        private string _key = "";

        // 优化：缓存 InfoService 查找键
        private string? _dashValueKey;
        private string? _dashColorKey;
        private const string PluginPrefix = "DASH.";
        public string? DashValueKey => _dashValueKey;

        public string Key
        {
            get => _key;
            set
            {
                _key = UIUtils.Intern(value);
                // 预计算查找键
                // 修复：使用 OrdinalIgnoreCase 并正确处理可空性
                if (_key.StartsWith(PluginPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    // 移除 "DASH." 前缀（长度=5），并预计算颜色键。
                    // 使用 Span 避免中间子字符串分配 (CA1845)
                    _dashValueKey = UIUtils.Intern(_key[PluginPrefix.Length..]);
                    _dashColorKey = UIUtils.Intern(string.Concat(_key.AsSpan(PluginPrefix.Length), ".Color".AsSpan()));
                }
                else
                {
                    _dashValueKey = null;
                    _dashColorKey = null;
                }
            }
        }

        private string _label = "";
        public string Label
        {
            get
            {
                if (MarketDisplayFormatter.IsMarketDisplayKey(Key))
                {
                    if (Key.StartsWith("ITEM.", StringComparison.OrdinalIgnoreCase))
                    {
                        string labelResolved = MetricLabelResolver.ResolveLabel(BoundConfig, InfoService);
                        if (!string.IsNullOrEmpty(labelResolved) && labelResolved != " ") return labelResolved;
                    }
                    return MarketDisplayFormatter.GetLabel(Key);
                }

                // 重构：使用统一的 Label 解析器
                string labelResolvedGeneral = MetricLabelResolver.ResolveLabel(BoundConfig, InfoService);
                if (!string.IsNullOrEmpty(labelResolvedGeneral)) return labelResolvedGeneral;

                return _label;
            }
            set => _label = UIUtils.Intern(value);
        }

        private string _shortLabel = "";
        public string ShortLabel
        {
            get
            {
                if (MarketDisplayFormatter.IsMarketDisplayKey(Key))
                {
                    if (Key.StartsWith("ITEM.", StringComparison.OrdinalIgnoreCase))
                    {
                        string shortResolved = MetricLabelResolver.ResolveShortLabel(BoundConfig, InfoService);
                        if (!string.IsNullOrEmpty(shortResolved) && shortResolved != " ") return shortResolved;
                    }
                    return MarketDisplayFormatter.GetLabel(Key);
                }

                // 重构：使用统一 Label 解析器
                string shortResolvedGeneral = MetricLabelResolver.ResolveShortLabel(BoundConfig, InfoService);
                if (!string.IsNullOrEmpty(shortResolvedGeneral)) return shortResolvedGeneral;

                return _shortLabel;
            }
            set => _shortLabel = UIUtils.Intern(value);
        }

        public float? Value { get; set; } = null;
        public float DisplayValue { get; set; } = 0f;
        public string? TextValue { get; set; } = null;
        // =============================
        // 缓存字段
        // =============================
        private float _cachedDisplayValue = -99999f;
        private (bool Ac, bool Charging) _cachedPowerState = (false, false); // 缓存完整电源状态。
        private string _cachedNormalText = "";       // 完整文本 (值+单位)
        private string _cachedHorizontalText = "";   // 完整横屏文本

        // ★★★ [新增] 分离缓存 ★★★
        public string CachedValueText { get; private set; } = "";
        public string CachedUnitText { get; private set; } = "";
        public bool HasCustomUnit { get; private set; } = false; // 标记是否使用了自定义单位


        public int CachedColorState { get; private set; } = 0;
        public double CachedPercent { get; private set; } = 0.0;

        internal void SetStaticPreviewColorState(int state)
        {
            CachedColorState = state;
        }

        public Color GetTextColor(Theme t)
        {
            if (YouPinInventoryTrendDisplayMetric.IsKey(Key))
            {
                return YouPinInventoryTrendDisplayMetric.GetTextColor(RuntimeSettings, t);
            }

            if (MarketDisplayFormatter.IsMarketKey(Key))
            {
                return MarketDisplayFormatter.GetTextColor(Key, CachedColorState, RuntimeSettings, t);
            }
            return UIUtils.GetStateColor(CachedColorState, t, true);
        }

        public string GetFormattedText(bool isHorizontal)
        {
            // 调试修复：插件项必须先更新颜色状态，避免后续缓存沿用旧颜色。
            if (_dashColorKey != null) // 使用预先缓存的颜色键。
            {
                string colorVal = InfoService.GetValue(_dashColorKey);

                if (!string.IsNullOrEmpty(colorVal))
                {
                    if (int.TryParse(colorVal, out int state))
                    {
                        CachedColorState = state;
                    }
                }
                else
                {
                    CachedColorState = 0; // 未提供颜色覆盖时默认按安全状态显示。
                }
            }

            if (MarketDisplayFormatter.IsMarketDisplayKey(Key))
            {
                CachedColorState = MarketDisplayFormatter.GetColorState(Key);
                CachedValueText = MarketDisplayFormatter.GetValueText(Key, RuntimeSettings);
                CachedUnitText = "";
                HasCustomUnit = false;
                return CachedValueText;
            }

            if (YouPinInventoryTrendDisplayMetric.IsKey(Key))
            {
                CachedColorState = YouPinInventoryTrendDisplayMetric.GetColorState(RuntimeSettings);
                CachedValueText = YouPinInventoryTrendDisplayMetric.FormatValue(RuntimeSettings);
                CachedUnitText = "";
                HasCustomUnit = false;
                return CachedValueText;
            }

            // 2. 读取配置（已优化）
            var cfg = BoundConfig;

            string? userFormat = isHorizontal ? cfg?.UnitTaskbar : cfg?.UnitPanel;
            HasCustomUnit = !string.IsNullOrEmpty(userFormat);

            // 3. 返回 TextValue（插件 / 仪表盘项）
            if (TextValue != null)
            {
                // 优化：统一使用 MetricUtils 处理单位逻辑
                // 1. 获取默认单位（自动处理：插件查 InfoService，硬件查表）。
                var ctx = isHorizontal ? MetricUtils.UnitContext.Taskbar : MetricUtils.UnitContext.Panel;
                string defUnit = MetricUtils.GetUnitStr(Key, null, ctx, InfoService);

                // 2. 确定最终单位（自动处理：用户设为 null 时用默认，否则用自定义）。
                string finalUnit = MetricUtils.GetDisplayUnit(Key, defUnit, userFormat);

                // 3. 智能拼接：如果文本里还没包含这个单位，就拼上去
                if (!string.IsNullOrEmpty(finalUnit) && !TextValue.EndsWith(finalUnit))
                {
                    return TextValue + finalUnit;
                }

                return TextValue;
            }

            // 4. 数值处理（硬件项）
            // 修复：增加充电状态检查；如果数值变了，或电池项电源状态变了，就强制刷新。
            bool isBat = Key.StartsWith("BAT", StringComparison.OrdinalIgnoreCase);
            var currentPower = MetricUtils.GetPowerStatus();
            bool powerChanged = isBat && (_cachedPowerState != currentPower);

            // 修复：降低缓存失效阈值，从 0.05 降低到 0.005。
            // 之前的 0.05 太大，导致当数值从 0.06 变为 0.00 时，虽然 DisplayValue 变了，
            // 但因为 delta (0.06) 刚刚好大于 0.05 可能触发更新，
            // 但如果是 0.04 -> 0.00，delta=0.04 < 0.05，导致文本缓存不更新，UI 仍显示 0.04。
            // 考虑到显示精度通常为 0.00，阈值应至少小于 0.01。
            if (Math.Abs(DisplayValue - _cachedDisplayValue) > 0.005f || powerChanged)
            {
                _cachedDisplayValue = DisplayValue;
                if (isBat) _cachedPowerState = currentPower;

                // 重构：使用新的原子函数分别构建普通和紧凑文本。

                // === 1. 更新主界面缓存（Panel）===
                string valNormal = MetricUtils.GetValueStr(Key, DisplayValue, false);
                string unitNormal = MetricUtils.GetUnitStr(Key, DisplayValue, MetricUtils.UnitContext.Panel, InfoService);
                string? userFmtPanel = cfg?.UnitPanel;

                string finalUnitPanel = MetricUtils.GetDisplayUnit(Key, unitNormal, userFmtPanel);
                _cachedNormalText = valNormal + finalUnitPanel;

                // === 2. 更新任务栏缓存（Taskbar / Horizontal）===
                // 逻辑修正：任务栏模式下，必须使用 Taskbar 上下文，例如不带 /s。
                string? userFmtTaskbar = cfg?.UnitTaskbar;
                bool hasCustomTaskbar = !string.IsNullOrEmpty(userFmtTaskbar);

                // 自动模式：启用数值压缩（Compact=true）和紧凑单位（Taskbar 上下文）。
                // 自定义模式：不做数值压缩（false），但单位仍需基于 Taskbar 上下文计算基础值。
                bool compact = !hasCustomTaskbar;

                string valTaskbar = MetricUtils.GetValueStr(Key, DisplayValue, compact);
                string unitTaskbar = MetricUtils.GetUnitStr(Key, DisplayValue, MetricUtils.UnitContext.Taskbar, InfoService);

                string finalUnitTaskbar = MetricUtils.GetDisplayUnit(Key, unitTaskbar, userFmtTaskbar);
                _cachedHorizontalText = valTaskbar + finalUnitTaskbar;

                // 更新公共属性以便调试 (显示当前请求模式的值)
                if (isHorizontal)
                {
                    CachedValueText = valTaskbar;
                    CachedUnitText = finalUnitTaskbar;
                    HasCustomUnit = hasCustomTaskbar;
                }
                else
                {
                    CachedValueText = valNormal;
                    CachedUnitText = finalUnitPanel;
                    HasCustomUnit = !string.IsNullOrEmpty(userFmtPanel);
                }

                // 只有非插件项才在这里计算颜色；插件项上方已经按插件颜色键处理过。
                if (!Key.StartsWith("DASH."))
                {
                    CachedColorState = MetricUtils.GetState(Key, DisplayValue);
                }

                CachedPercent = MetricUtils.GetProgressValue(Key, DisplayValue);
            }
            return isHorizontal ? _cachedHorizontalText : _cachedNormalText;
        }

        public MetricRenderStyle Style { get; set; } = MetricRenderStyle.StandardBar;
        public Rectangle Bounds { get; set; } = Rectangle.Empty;

        public Rectangle LabelRect;
        public Rectangle ValueRect;
        public Rectangle BarRect;
        public Rectangle BackRect;

        public void TickSmooth(double speed)
        {
            if (!Value.HasValue) return;
            float target = Value.Value;
            float diff = Math.Abs(target - DisplayValue);

            // 核心修复：微小差异直接吸附，而不是跳过。
            if (diff < 0.05f)
            {
                DisplayValue = target;
                return;
            }

            // 大幅跳变或高速模式直接更新
            if (diff > 15f || speed >= 0.9) DisplayValue = target;
            else DisplayValue += (float)((target - DisplayValue) * speed);
        }
    }
}
