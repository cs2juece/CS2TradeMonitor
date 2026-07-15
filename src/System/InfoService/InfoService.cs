using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Runtime.InteropServices; // [Added] For P/Invoke
using System.Diagnostics; // [Added] For Process
using System.Net.NetworkInformation; // [Added] For NetworkChange
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.Core;

namespace CS2TradeMonitor.src.SystemServices.InfoService
{
    /// <summary>
    /// 系统信息服务 (单例)
    /// 负责管理 HOST, IP, Time 等看板数据的获取与缓存
    /// 原 DashboardService
    /// </summary>
    public class InfoService : IInfoService
    {
        #region Singleton
        private static InfoService? _instance;
        public static InfoService Instance => _instance ??= new InfoService();
        private InfoService() { Initialize(); }
        #endregion

        // === Constants ===
        private const string KEY_HOST = "HOST";
        private const string KEY_IP = "IP";
        private const string KEY_TIME = "Time";
        private const string KEY_UPTIME = "Uptime";

        // Default Values (User Friendly)
        private const string DEFAULT_IP = "0.0.0.0";


        // Update Intervals (For IP/Host only, NOT for Uptime/Time)
        private const int INTERVAL_SLOW = 60000; // 1 min (Stable state: IP found)
        private const int INTERVAL_FAST = 2000;  // 2 sec (Retry state: IP missing)

        // === State ===
        private readonly Dictionary<string, string> _data = new();
        private readonly object _lock = new();

        private long _lastUpdateTick = 0;
        private int _currentInterval = INTERVAL_FAST;

        // [Optimization] Cache time strings to avoid allocs every tick
        private string _lastTimeStr = "";
        private int _lastSecond = -1;
        private string _lastUptimeStr = "";
        private int _lastUptimeMinute = -1;

        // [Fix] Offset for Fast Startup handling
        private long _uptimeOffsetTicks = 0;

        // [Fix] 使用 QueryUnbiasedInterruptTime 排除休眠时间，解决"开机一天"的问题
        [DllImport("kernel32.dll")]
        private static extern bool QueryUnbiasedInterruptTime(out ulong UnbiasedTime);

        /// <summary>
        /// 初始化默认值并启动首次更新
        /// </summary>
        private void Initialize()
        {
            lock (_lock)
            {
                _data[KEY_HOST] = Environment.MachineName; // Hostname always available
                _data[KEY_IP] = DEFAULT_IP;
                _data[KEY_TIME] = DateTime.Now.ToString("ddd HH:mm:ss"); // ★★★ 立即赋值当前时间，不再使用 00:00:00 默认值 ★★★
            }

            // [Fix #287] 监听网络变更，立即触发IP刷新
            NetworkChange.NetworkAddressChanged += (s, e) =>
            {
                _currentInterval = INTERVAL_FAST;
                _lastUpdateTick = 0; // 强制下次 Update 立即执行
            };

            // [Optimization] 异步执行耗时的进程检查，避免阻塞程序启动或触发杀软扫描导致UI卡顿
            Task.Run(() =>
            {
                CalculateUptimeOffset();
                // [Fix] 强制让 UpdateTimeInfo 刷新数据，忽略分钟缓存，确保校准结果立即生效
                _lastUptimeMinute = -1;
                // 校准完成后立即刷新一次数据，确保界面显示正确
                UpdateTimeInfo();
            });

            // [Fix] Calculate Uptime immediately so it's ready for first render
            // (Initial value might be uncorrected for a few ms, which is acceptable)
            UpdateTimeInfo();

            // Trigger first async update
            UpdateData();
        }

        /// <summary>
        /// 计算开机时间偏差 (解决快速启动不重置问题，同时保留重启后登录等待的时间)
        /// </summary>
        private void CalculateUptimeOffset()
        {
            try
            {
                // 1. 获取当前用户会话时长 (Session Time)
                // 使用多个锚点进程防止单一进程重启导致误判
                long minStart = long.MaxValue;
                foreach (var name in new[] { "explorer", "sihost", "taskhostw" })
                {
                    foreach (var p in Process.GetProcessesByName(name))
                    {
                        try { if (p.StartTime.Ticks < minStart) minStart = p.StartTime.Ticks; }
                        catch (System.Exception ex) { CS2TradeMonitor.src.SystemServices.DiagnosticsLogger.Ignored(ex); }
                        p.Dispose();
                    }
                }

                if (minStart == long.MaxValue) return; // 无法获取会话时间，不进行修正

                long sessionTicks = DateTime.Now.Ticks - minStart;

                // [Logic] 如果会话时间已经很长 (>10分钟)，说明不是刚开机(可能是睡眠唤醒或一直开着)，
                // 此时直接信任内核时间，不需要重置。
                if (sessionTicks > 600L * 1000 * 10000) return;

                // 2. 获取内核非休眠时间 (Unbiased Time)
                if (!QueryUnbiasedInterruptTime(out ulong unbiasedVal))
                    unbiasedVal = (ulong)Environment.TickCount64 * 10000;
                long unbiasedTicks = (long)unbiasedVal;

                // 3. 获取系统物理运行时间 (Wall Clock Uptime)
                // [Optimization] 直接使用 Environment.TickCount64 (包含休眠时间) 替代查找 System 进程
                // 这比 Process.GetProcessesByName 更快且无权限问题
                long wallClockTicks = Environment.TickCount64 * 10000;

                // [Core Logic] 区分 "重启等待" vs "快速启动残留"
                // Case A (重启等待): 重启 -> 登录界面挂机7h -> 登录。
                //    WallClock(7h) ≈ Unbiased(7h). 差异很小。 -> 应保留 7h (不修正)。
                // Case B (快启残留): 昨用5h -> 关机(快启) -> 今开 -> 登录。
                //    WallClock(24h+) >> Unbiased(5h+). 差异巨大(中间在休眠)。 -> 应显示 0h (修正)。

                // 判定阈值：如果 物理时间 比 内核时间 多出 30分钟以上，说明中间经历过长时间休眠/关机
                if (wallClockTicks > unbiasedTicks + 1800L * 1000 * 10000)
                {
                    // 确认为快速启动残留，扣除旧时间，从本次会话开始计算
                    _uptimeOffsetTicks = unbiasedTicks - sessionTicks;
                }
            }
            catch (System.Exception ex) { CS2TradeMonitor.src.SystemServices.DiagnosticsLogger.Ignored(ex); }
        }

        /// <summary>
        /// 线程安全地获取数据
        /// </summary>
        public string GetValue(string key)
        {
            lock (_lock)
            {
                return _data.TryGetValue(key, out var val) ? val : "";
            }
        }

        /// <summary>
        /// 外部注入通用数据 (例如插件抓取的数据)
        /// </summary>
        public void InjectValue(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            // [Optimization] Intern keys and values from external injections
            SetData(UIUtils.Intern(key), value);
        }

        /// <summary>
        /// 外部注入 IP (例如从配置缓存恢复)
        /// </summary>
        public void InjectIP(string ip)
        {
            if (string.IsNullOrEmpty(ip) || ip == DEFAULT_IP) return;

            SetData(KEY_IP, ip);
            // If valid IP injected, switch to slow update immediately
            _currentInterval = INTERVAL_SLOW;
        }

        public void RemoveDataByPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return;
            lock (_lock)
            {
                var keysToRemove = _data.Keys.Where(k => k.StartsWith(prefix)).ToList();
                foreach (var k in keysToRemove)
                {
                    _data.Remove(k);
                }
            }
        }

        /// <summary>
        /// 主循环调用 (建议每帧或定时器调用)
        /// </summary>
        public void Update()
        {
            // 1. High Frequency: Time (Every tick)
            UpdateTimeInfo();

            // 2. Low Frequency: Network/Host (Based on interval)
            long now = Environment.TickCount64;
            if (now - _lastUpdateTick > _currentInterval)
            {
                _lastUpdateTick = now;
                UpdateData();
            }
        }

        private void UpdateTimeInfo()
        {
            var now = DateTime.Now;

            bool isNewSecond = now.Second != _lastSecond;

            // [Optimization] Only format time if second changed
            if (isNewSecond)
            {
                _lastSecond = now.Second;
                _lastTimeStr = now.ToString("ddd HH:mm:ss");
                SetData(KEY_TIME, _lastTimeStr);
            }

            // Uptime
            // [Optimization] Update every minute, hide seconds
            // [Fix] 改用 QueryUnbiasedInterruptTime (不含休眠) 替代 Environment.TickCount64 (含休眠)
            TimeSpan ts;
            try
            {
                if (QueryUnbiasedInterruptTime(out ulong ticks))
                {
                    // [Fix] 减去快速启动导致的偏差
                    long realTicks = (long)ticks - _uptimeOffsetTicks;
                    if (realTicks < 0) realTicks = 0;
                    ts = TimeSpan.FromTicks(realTicks);
                }
                else
                {
                    // Fallback (虽然理论上不会失败)
                    // TickCount64 无法排除休眠，也无法处理快启，但作为保底
                    ts = TimeSpan.FromMilliseconds(Environment.TickCount64);
                }
            }
            catch
            {
                ts = TimeSpan.FromMilliseconds(Environment.TickCount64);
            }

            if (now.Minute != _lastUptimeMinute || string.IsNullOrEmpty(_lastUptimeStr))
            {
                _lastUptimeMinute = now.Minute;

                _lastUptimeStr = ts.TotalDays < 1
                    ? $"{ts.Hours}时 {ts.Minutes}分"
                    : $"{(int)ts.TotalDays}天 {ts.Hours}时 {ts.Minutes}分";

                SetData(KEY_UPTIME, _lastUptimeStr);
            }
        }

        private void UpdateData()
        {
            // [Optimization] Host info is static, only set once or if changed
            string currentHost = Environment.MachineName;
            if (GetValue(KEY_HOST) != currentHost)
            {
                SetData(KEY_HOST, currentHost);
            }

            // IP info is potentially slow, run async
            Task.Run(UpdateIPInfo);
        }

        private void UpdateIPInfo()
        {
            try
            {
                string? ip = null;

                // Validate IP
                if (!string.IsNullOrEmpty(ip) && ip != DEFAULT_IP)
                {
                    // [Optimization] Only update if changed
                    if (GetValue(KEY_IP) != ip)
                    {
                        SetData(KEY_IP, ip);
                    }
                    _currentInterval = INTERVAL_SLOW; // Success -> Relax
                }
                else
                {
                    // Failed -> Retry fast
                    _currentInterval = INTERVAL_FAST;
                }
            }
            catch
            {
                _currentInterval = INTERVAL_FAST;
            }
        }

        private void SetData(string key, string value)
        {
            lock (_lock)
            {
                // [Optimization] Intern values to reduce duplicates (e.g. "KB/s", "luffy-pc")
                // [Fix] Do NOT intern dynamic time strings (Time, Uptime) as they change constantly and pollute the pool
                if (key == KEY_TIME || key == KEY_UPTIME)
                {
                    _data[key] = value;
                }
                else
                {
                    _data[key] = UIUtils.Intern(value);
                }
            }
        }
    }
}
