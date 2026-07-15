using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.src.UI.Helpers
{
    public static class SystemActions
    {
        // ==================================================================================
        // P/Invoke Definitions
        // ==================================================================================

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint esFlags);

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const int SHCNF_IDLIST = 0x0000;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint Msg,
            UIntPtr wParam,
            string lParam,
            uint fuFlags,
            uint uTimeout,
            out IntPtr lpdwResult);

        private const int HWND_BROADCAST = 0xffff;
        private const uint WM_SETTINGCHANGE = 0x001A;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_MONITORPOWER = 0xF170;
        private const int MONITOR_OFF = 2;

        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;
        private const uint ES_DISPLAY_REQUIRED = 0x00000002;

        // ==================================================================================
        // Public Methods
        // ==================================================================================

        private static void NotifyShellUpdate()
        {
            // 广播设置改变消息 (刷新注册表设置)
            SendMessageTimeout(
                (IntPtr)HWND_BROADCAST,
                WM_SETTINGCHANGE,
                UIntPtr.Zero,
                "ShellState",
                SMTO_ABORTIFHUNG,
                2000,
                out _);

            // 再次广播关联改变，确保 Shell 刷新
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>
        /// 刷新桌面图标缓存
        /// </summary>
        public static void RefreshIconCache()
        {
            try
            {
                // Win10+ 有效的刷新命令，不需要重启 Explorer
                Process.Start(new ProcessStartInfo("ie4uinit.exe", "-show") { CreateNoWindow = true, UseShellExecute = false });

                // 通知系统关联已改变，强制刷新图标
                NotifyShellUpdate();
            }
            catch (Exception ex)
            {
                GlobalPromptService.Show("刷新图标缓存失败：" + ex.Message, "CS2交易监控", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// 清理 C 盘临时文件 (异步)
        /// </summary>
        public static async Task CleanTempFilesAsync()
        {
            await Task.Run(() =>
            {
                long freedBytes = 0;
                int count = 0;
                var tempPath = Path.GetTempPath(); // Resolves the current user's %TEMP% directory.

                try
                {
                    var dir = new DirectoryInfo(tempPath);
                    foreach (var file in dir.GetFiles())
                    {
                        try
                        {
                            long size = file.Length;
                            file.Delete();
                            freedBytes += size;
                            count++;
                        }
                        catch (Exception ex)
                        {
                            DiagnosticsLogger.Info("SystemActions", $"Ignored temp file cleanup failure: {ex.Message}");
                        }
                    }

                    foreach (var subDir in dir.GetDirectories())
                    {
                        try
                        {
                            // 递归删除子文件夹，如果包含占用文件会失败，catch住即可
                            subDir.Delete(true);
                        }
                        catch (Exception ex)
                        {
                            DiagnosticsLogger.Info("SystemActions", $"Ignored temp directory cleanup failure: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    GlobalPromptService.Show("清理临时文件失败：" + ex.Message, "系统清理", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 格式化大小
                string sizeStr;
                if (freedBytes > 1024 * 1024 * 1024) sizeStr = $"{freedBytes / 1024.0 / 1024 / 1024:F2} GB";
                else if (freedBytes > 1024 * 1024) sizeStr = $"{freedBytes / 1024.0 / 1024:F2} MB";
                else sizeStr = $"{freedBytes / 1024.0:F2} KB";

                bool isZh = LanguageManager.CurrentLang == "zh";
                string title = isZh ? "系统清理" : "System Cleanup";
                string message = isZh
                    ? $"清理完成！\n共删除 {count} 个文件/文件夹\n释放空间: {sizeStr}"
                    : $"Cleanup complete!\nDeleted {count} files/folders\nFreed space: {sizeStr}";
                GlobalPromptService.Show(message, title);
            });
        }

        /// <summary>
        /// 打开任务管理器
        /// </summary>
        public static void OpenTaskManager()
        {
            try
            {
                Process.Start(new ProcessStartInfo("taskmgr") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                GlobalPromptService.Show("打开任务管理器失败：" + ex.Message, "CS2交易监控", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// 重启资源管理器
        /// </summary>
        public static void RestartExplorer()
        {
            try
            {
                // 使用 cmd 组合命令避免中间出现太长时间的黑屏
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c taskkill /f /im explorer.exe & start explorer.exe",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch (Exception ex)
            {
                GlobalPromptService.Show("重启资源管理器失败：" + ex.Message, "CS2交易监控", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// 关闭显示器
        /// </summary>
        public static void TurnOffMonitor(IntPtr ownerHandle)
        {
            // 延时一小会儿，防止鼠标点击动作立即唤醒屏幕
            System.Threading.Tasks.Task.Delay(500).ContinueWith(_ =>
            {
                SendMessage(ownerHandle, WM_SYSCOMMAND, SC_MONITORPOWER, MONITOR_OFF);
            });
        }

        // 状态存储
        public static bool IsPreventSleep { get; private set; } = false;

        /// <summary>
        /// 切换“禁止自动休眠”状态
        /// </summary>
        public static void TogglePreventSleep()
        {
            IsPreventSleep = !IsPreventSleep;
            if (IsPreventSleep)
            {
                // 阻止系统休眠 + 阻止屏幕关闭
                SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
            }
            else
            {
                // 恢复默认
                SetThreadExecutionState(ES_CONTINUOUS);
            }
        }

        /// <summary>
        /// 重启软件
        /// </summary>
        public static void RestartApplication()
        {
            try
            {
                string exePath = LauncherExecutablePath.GetCurrent();

                string? workingDirectory = Path.GetDirectoryName(exePath);
                if (string.IsNullOrWhiteSpace(workingDirectory))
                {
                    workingDirectory = InstallationPaths.InstallDirectory;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"--restart-after-pid={Environment.ProcessId}",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false
                };

                Process.Start(startInfo);
                DiagnosticsLogger.Info("Restart", $"Restart requested for {exePath}");
                System.Windows.Forms.Application.Exit();
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("Restart", "Restart application failed.", ex);
                GlobalPromptService.Show("重启软件失败：" + ex.Message, "CS2交易监控", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 打开 URL
        /// </summary>
        public static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                GlobalPromptService.Show("无法打开链接：" + ex.Message, "CS2交易监控", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// 执行定时关机
        /// </summary>
        /// <param name="seconds">秒数，0或负数表示取消</param>
        public static void ScheduleShutdown(int seconds)
        {
            try
            {
                if (seconds <= 0)
                {
                    Process.Start(new ProcessStartInfo("shutdown", "-a") { CreateNoWindow = true, UseShellExecute = false });
                    // GlobalPromptService.Show("已取消定时关机"); // 可选提示
                }
                else
                {
                    Process.Start(new ProcessStartInfo("shutdown", $"-s -t {seconds}") { CreateNoWindow = true, UseShellExecute = false });
                }
            }
            catch (Exception ex)
            {
                GlobalPromptService.Show("执行关机命令失败：" + ex.Message, "CS2交易监控", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
