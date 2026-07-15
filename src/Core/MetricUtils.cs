using System;
using System.Linq;
using CS2TradeMonitor.src.Core.State;
using CS2TradeMonitor.src.SystemServices.InfoService;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.src.Core
{
    public enum MetricType
    {
        Percent,    // 负载、电池百分比
        Temperature,// 温度
        Memory,     // 内存、显存
        DataSize,   // 数据总量
        DataSpeed,  // 网络、磁盘速度
        Frequency,  // 频率
        Power,      // 功耗
        Voltage,    // 电压
        Current,    // 电流
        RPM,        // 风扇、水泵转速
        FPS,        // 帧率
        Unknown
    }

    /// <summary>
    /// CS2TradeMonitor 核心指标处理工具
    /// 包含：类型解析、数据格式化、阈值评估、状态管理
    /// </summary>
    public static class MetricUtils
    {
        // =========================================================
        // 1. 全局状态 (自动缓存，3秒刷新)
        // =========================================================
        private static long _lastPowerCheck = 0;// 上次刷新电源功耗时间
        private static bool _lastAc = false;// 是否接通电源
        private static bool _lastCharging = false;// 是否正在充电 (排除充满/保护)

        /// <summary>
        /// 获取统一电源状态。
        /// AcOnline：是否接通电源。
        /// IsCharging：是否正在充电（排除充满 / 保护）。
        /// </summary>
        public static (bool AcOnline, bool IsCharging) GetPowerStatus()
        {
            long now = Environment.TickCount64;
            if (now - _lastPowerCheck > 3000) // 3000ms 自动节流
            {
                _lastPowerCheck = now;
                try
                {
                    var s = System.Windows.Forms.SystemInformation.PowerStatus;
                    _lastAc = s.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
                    _lastCharging = (s.BatteryChargeStatus & System.Windows.Forms.BatteryChargeStatus.Charging) == System.Windows.Forms.BatteryChargeStatus.Charging;
                }
                catch (System.Exception ex) { CS2TradeMonitor.src.SystemServices.DiagnosticsLogger.Ignored(ex); }
            }
            return (_lastAc, _lastCharging);
        }

        // 优化：缓存数据大小单位，避免格式化时重复分配。
        private static readonly string[] _dataSizes = { "KB", "MB", "GB", "TB", "PB" };

        public const int STATE_NEUTRAL = -1;
        public const int STATE_SAFE = 0;
        public const int STATE_WARN = 1;
        public const int STATE_CRIT = 2;

        // =========================================================
        // 2. 类型解析（原 MetricHelper）
        // =========================================================
        public static MetricType GetType(string key)
        {
            if (string.IsNullOrEmpty(key)) return MetricType.Unknown;

            if (key.Equals("FPS", StringComparison.OrdinalIgnoreCase)) return MetricType.FPS;
            if (key.StartsWith("BAT", StringComparison.OrdinalIgnoreCase))
            {
                if (key.IndexOf("Percent", StringComparison.OrdinalIgnoreCase) >= 0) return MetricType.Percent;
                if (key.IndexOf("Voltage", StringComparison.OrdinalIgnoreCase) >= 0) return MetricType.Voltage;
                if (key.IndexOf("Current", StringComparison.OrdinalIgnoreCase) >= 0) return MetricType.Current;
                if (key.IndexOf("Power", StringComparison.OrdinalIgnoreCase) >= 0) return MetricType.Power;
                return MetricType.Percent;
            }
            if (key.IndexOf("MEM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                key.IndexOf("VRAM", StringComparison.OrdinalIgnoreCase) >= 0) return MetricType.Memory;

            if (key.IndexOf("LOAD", StringComparison.OrdinalIgnoreCase) >= 0) return MetricType.Percent;
            if (key.IndexOf("TEMP", StringComparison.OrdinalIgnoreCase) >= 0) return MetricType.Temperature;
            if (key.IndexOf("CLOCK", StringComparison.OrdinalIgnoreCase) >= 0) return MetricType.Frequency;
            if (key.IndexOf("POWER", StringComparison.OrdinalIgnoreCase) >= 0) return MetricType.Power;
            if (key.IndexOf("VOLTAGE", StringComparison.OrdinalIgnoreCase) >= 0) return MetricType.Voltage;
            if (key.IndexOf("FAN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                key.IndexOf("PUMP", StringComparison.OrdinalIgnoreCase) >= 0) return MetricType.RPM;
            if (key.StartsWith("NET", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("DISK", StringComparison.OrdinalIgnoreCase)) return MetricType.DataSpeed;
            if (key.IndexOf("DATA", StringComparison.OrdinalIgnoreCase) >= 0) return MetricType.DataSize;

            return MetricType.Unknown;
        }

        // =========================================================
        // 3. 格式化逻辑（原 Formatter）
        // =========================================================

        /// <summary>
        /// 获取纯数值字符串 (已处理缩放、舍入、紧凑模式)
        /// </summary>
        public static string GetValueStr(string key, float? value, bool compact = false)
        {
            float v = value ?? 0.0f;
            var type = GetType(key);

            // 1. 内存特殊处理（容量 vs 百分比）
            if (type == MetricType.Memory)
            {
                var cfg = GetMetricConfig();
                if (cfg.MemoryDisplayMode == 1) // 容量模式
                {
                    double totalGB = (key.IndexOf("MEM", StringComparison.OrdinalIgnoreCase) >= 0)
                       ? Settings.DetectedRamTotalGB
                       : Settings.DetectedGpuVramTotalGB;

                    if (totalGB > 0)
                    {
                        // 转换为字节 -> 格式化 -> 取数值部分
                        // 重构：使用自适应格式化（-1），确保 100GB 时不显示 .0。
                        return FormatDataSizeParts((v / 100.0) * totalGB * 1073741824.0, -1).val;
                    }
                }
            }

            // 2. 数据量/速度 (自动单位缩放)
            if (type == MetricType.DataSpeed || type == MetricType.DataSize)
            {
                return FormatDataSizeParts(v, -1).val;
            }

            // 4. 标准模式（Panel）
            if (type == MetricType.FPS || type == MetricType.RPM) return $"{v:0}";
            if (type == MetricType.Frequency) return $"{v / 1000f:F1}"; // 频率保持 F1，通常 GHz 需要一位小数

            // 内存百分比、负载、温度、功耗等

            return FormatValueAdaptive(v, -1);
        }

        /// <summary>
        /// 内部通用的数值格式化逻辑 (自适应小数点)
        /// </summary>
        private static string FormatValueAdaptive(double val, int decimals)
        {
            if (decimals >= 0)
            {
                // 固定小数位模式
                string format = "0." + new string('0', decimals);
                if (decimals == 0) format = "0";

                // 修复：即使是固定模式，如果数值 >= 99.95，也遵循用户习惯显示为整数。
                // 除非 decimals > 1（通常是电压 / 电流等需要高精度的场合）。
                if (val >= 99.95 && decimals <= 1) return val.ToString("0");

                return val.ToString(format);
            }

            // 自适应模式（decimals < 0）
            if (val < 9.995) return val.ToString("0.00");   // 0.00 - 9.99
            if (val < 99.95) return val.ToString("0.0");   // 10.0 - 99.9
            return val.ToString("0");                      // 100+
        }

        public enum UnitContext
        {
            Panel,          // 主界面 (完整, 带空格)
            Taskbar,        // 任务栏 (紧凑, 无空格)
            SettingsPanel,  // 设置界面 - 主界面预览 (带占位符)
            SettingsTaskbar // 设置界面 - 任务栏预览 (带占位符)
        }

        /// <summary>
        /// 获取单位字符串 (处理上下文、动态单位)
        /// </summary>
        public static string GetUnitStr(string key, float? value, UnitContext context)
        {
            return GetUnitStr(key, value, context, MetricRuntimeServices.ResolveInfoService());
        }

        internal static string GetUnitStr(string key, float? value, UnitContext context, IInfoService infoService)
        {
            ArgumentNullException.ThrowIfNull(infoService);

            var type = GetType(key);

            // 1. 内存（GB vs %）
            if (type == MetricType.Memory)
            {
                if (GetMetricConfig().MemoryDisplayMode != 1) return "%";
                if (context == UnitContext.SettingsPanel || context == UnitContext.SettingsTaskbar) return "{u}";

                double totalGB = (key.IndexOf("MEM", StringComparison.OrdinalIgnoreCase) >= 0) ? Settings.DetectedRamTotalGB : Settings.DetectedGpuVramTotalGB;

                return (totalGB > 0 && value.HasValue)
                    ? FormatDataSizeParts((value.Value / 100.0) * totalGB * 1073741824.0, -1).unit
                    : "GB";
            }


            // 2. 数据 (动态单位)
            if (type == MetricType.DataSpeed || type == MetricType.DataSize)
            {
                // 设置页模式下，返回 {u} 占位符，提示用户这是动态单位。
                // 仅设置页悬浮窗和设置页任务栏返回占位符。
                if (context == UnitContext.SettingsPanel || context == UnitContext.SettingsTaskbar)
                {
                    if (type == MetricType.DataSpeed)
                        return context == UnitContext.SettingsTaskbar ? "{u}" : "{u}/s";
                    return "{u}";
                }

                // 需要根据数值重新计算单位；FormatDataSizeParts 开销很小。
                // 任务栏对应紧凑模式 -> 整数（0）。
                // 主界面对应自适应模式 -> 自适应（-1）。
                int decimals = (context == UnitContext.Taskbar) ? 0 : -1;
                string u = FormatDataSizeParts(value ?? 0, decimals).unit;

                if (type == MetricType.DataSpeed)
                    return context == UnitContext.Taskbar ? u : u + "/s";

                return u;
            }
            // 插件项从 InfoService 查询默认单位。
            if (key.StartsWith("DASH.", StringComparison.OrdinalIgnoreCase))
            {
                // 尝试获取刚才 SyncService 注入的单位
                string u = infoService.GetValue(key + ".Unit");
                if (!string.IsNullOrEmpty(u)) return u;
            }

            // 3. 电池充电后缀
            // 修复：仅在真正充电时显示闪电图标（AcOnline + IsCharging）。
            // 充满电插着电源时（AcOnline=true, IsCharging=false）不应显示闪电。
            var pStatus = GetPowerStatus();
            string suffix = (
                key.StartsWith("BAT", StringComparison.OrdinalIgnoreCase)
                && pStatus.AcOnline
                && pStatus.IsCharging // <--- 新增条件
                && context != UnitContext.SettingsPanel
                && context != UnitContext.SettingsTaskbar) ? "⚡" : "";

            // 4. 标准单位表
            return type switch
            {
                MetricType.Percent => "%" + suffix,
                MetricType.Temperature => "°C",
                MetricType.Frequency => "GHz",
                MetricType.Power => "W" + suffix,
                MetricType.Voltage => "V" + suffix,
                MetricType.Current => "A" + suffix,
                // 任务栏和设置页任务栏使用简写。
                MetricType.FPS => (context == UnitContext.Taskbar || context == UnitContext.SettingsTaskbar) ? "F" : " FPS",
                MetricType.RPM => (context == UnitContext.Taskbar || context == UnitContext.SettingsTaskbar) ? "R" : " RPM",
                _ => ""
            };
        }

        public static string GetSampleValueStr(string key)
        {
            var type = GetType(key);

            return type switch
            {
                MetricType.Frequency => "9.9", // GHz 示例宽度
                MetricType.RPM => "9999", // RPM 示例宽度
                MetricType.DataSpeed => "9.9.", // MB 示例宽度
                MetricType.DataSize => "9.9.", // MB 示例宽度
                _ => "99.9"
            };
        }

        public static string GetDisplayUnit(string key, string calculatedUnit, string? userFormat)
        {
            if (string.IsNullOrEmpty(userFormat))
            {
                if (string.IsNullOrEmpty(userFormat) && userFormat != null) return ""; // 空字符串表示隐藏单位。
                // 优化：GetUnitStr 已经按上下文返回正确单位，例如 "MB" 或 "MB/s"，这里不再追加 "/s"。
                return calculatedUnit;
            }
            return userFormat.Contains("{u}") ? userFormat.Replace("{u}", calculatedUnit) : userFormat;
        }
        // 重构：统一数据大小格式化逻辑。

        /// <summary>
        /// 将字节数转换为带单位的友好显示，返回数值字符串与单位字符串。
        /// </summary>
        public static (string val, string unit) FormatDataSizeParts(double bytes, int decimals = -1)
        {
            // 将字节转换为 KB 作为基准
            double len = bytes / 1024.0;

            // 逐级放大单位，直到数值小于 1000 或到达最大单位
            // 优化：使用 1000 而不是 1024 作为阈值，避免出现 "1023 KB" 这种较长文本，提前转换为 "0.99 MB"。
            int order = 0;
            while (len >= 1000 && order < _dataSizes.Length - 1)
            {
                order++;
                len /= 1024.0;
            }

            // 返回格式化后的数值与对应单位
            return (FormatValueAdaptive(len, decimals), _dataSizes[order]);
        }

        public static string FormatDataSize(double bytes, string suffix = "", int decimals = -1)
        {
            var (val, unit) = FormatDataSizeParts(bytes, decimals);
            return $"{val}{unit}{suffix}";
        }



        // =========================================================
        // 4. 评估逻辑（原 MetricEvaluator）
        // =========================================================
        public static int GetState(string key, double value)
        {
            if (double.IsNaN(value)) return STATE_SAFE;
            if (key.StartsWith("BAT", StringComparison.OrdinalIgnoreCase) && value < 0) return STATE_SAFE;

            var type = GetType(key);
            double checkValue = value;

            // 对于动态范围的指标（CPU 频率、功耗、转速、FPS），
            // 状态判断应该基于它们相对于"历史最大值"的百分比，而不是绝对数值。
            // 例如：如果 CPU 频率跑到了 历史最大值的 90%，就认为是高负载(红色)。
            // 否则 4.0GHz 可能永远触发不了阈值。
            if (type is MetricType.Frequency or MetricType.Power or MetricType.RPM or MetricType.FPS or MetricType.Voltage or MetricType.Current)
            {
                // 重构：当前已经在自适应类型分支内，直接使用 GetAdaptivePercentage。
                checkValue = GetAdaptivePercentage(key, value) * 100.0;

                if (checkValue >= 90) return STATE_CRIT;
                if (checkValue >= 80) return STATE_WARN;
                return STATE_SAFE;
            }
            else if (type is MetricType.DataSpeed or MetricType.DataSize)
            {
                checkValue = value / 1048576.0; // 转为 MB。
            }

            var (warn, crit) = GetThresholds(key);

            // 电池低电量反向判断
            if (key.Equals("BAT.Percent", StringComparison.OrdinalIgnoreCase))
            {
                if (checkValue <= crit) return STATE_CRIT;
                if (checkValue <= warn) return STATE_WARN;
                return STATE_SAFE;
            }

            if (checkValue >= crit) return STATE_CRIT;
            if (checkValue >= warn) return STATE_WARN;

            return STATE_SAFE;
        }

        public static (double warn, double crit) GetThresholds(string key)
        {
            var th = GetMetricConfig().Thresholds;
            var type = GetType(key);

            return type switch
            {
                MetricType.Temperature => ToTuple(th.Temp),
                MetricType.DataSpeed => key.StartsWith("NET")
                    ? (key.IndexOf("UP", StringComparison.OrdinalIgnoreCase) >= 0 ? ToTuple(th.NetUpMB) : ToTuple(th.NetDownMB))
                    : ToTuple(th.DiskIOMB),
                MetricType.DataSize => (key.IndexOf("UP", StringComparison.OrdinalIgnoreCase) >= 0 ? ToTuple(th.DataUpMB) : ToTuple(th.DataDownMB)),
                MetricType.Percent => key.Equals("BAT.Percent", StringComparison.OrdinalIgnoreCase) ? (60, 20) : ToTuple(th.Load),
                _ => ToTuple(th.Load)
            };
        }

        /// <summary>
        /// 计算进度条显示值 (0.0 - 1.0)
        /// 包含自适应最大值逻辑和最小视觉宽度修正 (5%)
        /// </summary>
        public static double GetProgressValue(string key, double value)
        {
            // 1. 获取基础百分比 (自适应或归一化)
            // 重构：内联 GetUnifiedPercent 逻辑。
            var type = GetType(key);
            double rawPct;

            if (type is MetricType.Frequency or MetricType.Power or MetricType.RPM or MetricType.FPS or MetricType.Voltage or MetricType.Current)
                rawPct = GetAdaptivePercentage(key, value);
            else
                rawPct = value / 100.0;

            // 2. 负值处理 (充电状态) - 统一返回 1.0 (满条)，颜色由状态决定
            if (rawPct < 0) rawPct = Math.Abs(rawPct);

            // 3. 视觉修正：限制在 [0.05, 1.0] 之间
            // 确保即使数值很小也能看到一条细线
            return Math.Max(0.05, Math.Min(1.0, rawPct));
        }

        public static double GetAdaptivePercentage(string key, double val)
        {
            var cfg = GetMetricConfig();
            float max = 1.0f;

            if (key == "CPU.Clock") max = cfg.RecordedMaxCpuClock;
            else if (key == "CPU.Power") max = cfg.RecordedMaxCpuPower;
            else if (key == "GPU.Clock") max = cfg.RecordedMaxGpuClock;
            else if (key == "GPU.Power") max = cfg.RecordedMaxGpuPower;
            else if (key == "CPU.Fan") max = cfg.RecordedMaxCpuFan;
            else if (key == "CPU.Pump") max = cfg.RecordedMaxCpuPump;
            else if (key == "CASE.Fan") max = cfg.RecordedMaxChassisFan;
            else if (key == "GPU.Fan") max = cfg.RecordedMaxGpuFan;
            else if (key == "FPS") max = cfg.RecordedMaxFps;
            else if (key == "CPU.Voltage") max = 1.6f;
            else if (key == "BAT.Power") max = 100f;
            else if (key == "BAT.Voltage") max = 20f;
            else if (key == "BAT.Current") max = 5f;

            if (max < 1) max = 1;
            double pct = val / max;
            return pct > 1.0 ? 1.0 : pct;
        }

        private static MetricConfigSnapshot GetMetricConfig()
        {
            return MetricRuntimeServices.Resolve().AppConfigState.Metrics;
        }

        private static (double warn, double crit) ToTuple(MetricValueRangeSnapshot range)
        {
            return (range.Warn, range.Crit);
        }

        // =========================================================
        // 5. 基础转换
        // =========================================================
        public static int ParseInt(string s) => int.TryParse(new string(s?.Where(c => char.IsDigit(c) || c == '-').ToArray()), out int v) ? v : 0;
        public static double ParseDouble(string s) => double.TryParse(new string(s?.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray()), out double v) ? v : 0;
        public static string ToStr(double v, string format = "F1") => v.ToString(format);
    }
}
