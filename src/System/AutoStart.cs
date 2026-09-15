using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Infrastructure.Paths;
using Microsoft.Win32;
using System.Windows.Forms;
using System.Xml.Linq;

namespace CS2TradeMonitor.src.SystemServices
{
    public static class AutoStart
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const int ErrorCancelled = 1223;
        private static string TaskName => InstanceRuntimeContext.Current.BuildOsResourceName(InstanceResourceKind.AutoStart);
        internal static string CurrentRegistrationName => TaskName;

        public static bool Set(bool enabled, bool showErrorMessage = true)
        {
            string exePath = LauncherExecutablePath.GetCurrent();
            bool runValueRemoved = DeleteRunValue(TaskName);

            if (enabled)
            {
                if (IsScheduledTaskForCurrentExe(exePath))
                    return runValueRemoved;

                if (IsNetworkPath(exePath))
                {
                    ShowError(
                        "Windows 计划任务不支持从网络路径启动。请将软件完整目录移动到本地硬盘后重试。",
                        showErrorMessage);
                    return false;
                }

                string tempXmlPath = RuntimeDataPaths.GetCacheFilePath($"autostart-task-{Guid.NewGuid():N}.xml");

                try
                {
                    string? userSid = WindowsIdentity.GetCurrent().User?.Value;
                    if (string.IsNullOrWhiteSpace(userSid))
                    {
                        ShowError("无法读取当前 Windows 用户身份，开机启动未启用。", showErrorMessage);
                        return false;
                    }

                    var doc = GetTaskXml(exePath, userSid);
                    doc.Save(tempXmlPath);

                    var result = RunSchtasksElevated($"/Create /TN \"{TaskName}\" /XML \"{tempXmlPath}\" /F");
                    if (!result.Success)
                    {
                        string message = result.ExitCode == ErrorCancelled
                            ? "未获得 Windows 权限，开机启动保持关闭。"
                            : $"设置开机启动失败，schtasks 返回 {result.ExitCode}。{result.Output}".Trim();
                        DiagnosticsLogger.Error("AutoStart", message);
                        ShowError(message, showErrorMessage);
                        return false;
                    }

                    if (!IsScheduledTaskForCurrentExe(exePath))
                    {
                        string message = "开机启动任务已创建，但校验失败。请重新开启一次开机启动。";
                        DiagnosticsLogger.Error("AutoStart", message);
                        ShowError(message, showErrorMessage);
                        return false;
                    }

                    if (!runValueRemoved)
                    {
                        string message = "计划任务已创建，但旧注册表启动项清理失败。请重新开启一次开机启动。";
                        DiagnosticsLogger.Error("AutoStart", message);
                        ShowError(message, showErrorMessage);
                        return false;
                    }

                    DiagnosticsLogger.Info("AutoStart", "Elevated logon task was created for the current portable instance.");
                    return true;
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Error("AutoStart", "Setting auto start failed.", ex);
                    ShowError($"设置失败: {ex.Message}", showErrorMessage);
                    return false;
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempXmlPath)) File.Delete(tempXmlPath);
                    }
                    catch (Exception ex)
                    {
                        DiagnosticsLogger.Info("AutoStart", $"Ignored temp task XML cleanup failure: {ex.Message}");
                    }
                }
            }

            if (!IsTaskRegistered(TaskName))
                return runValueRemoved;

            var deleteResult = RunSchtasksElevated($"/Delete /TN \"{TaskName}\" /F");
            if (!deleteResult.Success)
            {
                string message = deleteResult.ExitCode == ErrorCancelled
                    ? "未获得 Windows 权限，开机启动仍保持开启。"
                    : $"关闭开机启动失败，schtasks 返回 {deleteResult.ExitCode}。{deleteResult.Output}".Trim();
                DiagnosticsLogger.Error("AutoStart", message);
                ShowError(message, showErrorMessage);
                return false;
            }

            bool disabled = !IsTaskRegistered(TaskName) && runValueRemoved;
            if (disabled)
                DiagnosticsLogger.Info("AutoStart", "Elevated logon task was removed for the current portable instance.");
            return disabled;
        }

        public static bool IsEnabled()
        {
            return IsTaskRegistered(TaskName) || IsRunValueRegistered(TaskName);
        }

        public static bool IsEnabledForCurrentExe()
        {
            try
            {
                string exePath = LauncherExecutablePath.GetCurrent();
                return IsEnabledForCurrentExe(exePath);
            }
            catch
            {
                return false;
            }
        }

        public static string GetStatusSummary()
        {
            try
            {
                string exePath = LauncherExecutablePath.GetCurrent();
                bool runCurrent = IsRunValueForExe(TaskName, exePath);
                bool taskCurrent = IsScheduledTaskForCurrentExe(exePath);
                bool runAny = IsRunValueRegistered(TaskName);
                bool taskAny = IsTaskRegistered(TaskName);
                if (taskCurrent)
                    return "开机启动状态：计划任务已指向当前程序。";
                if (runCurrent)
                    return "开机启动状态：检测到旧启动项，请重新开启开机启动。";
                if (taskAny || runAny)
                    return "开机启动状态：存在启动项但路径不是当前程序，请重新启用。";
                return "开机启动状态：未启用。";
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Info("AutoStart", $"Reading auto start status failed: {ex.Message}");
                return "开机启动状态：读取失败，请重新打开设置页查看。";
            }
        }

        public static bool RepairIfNeeded(bool enabled, bool showErrorMessage = false)
        {
            try
            {
                if (enabled)
                {
                    if (IsEnabledForCurrentExe() && !IsRunValueRegistered(TaskName)) return true;
                    return Set(true, showErrorMessage);
                }

                if (!IsEnabled()) return true;
                return Set(false, showErrorMessage);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("AutoStart", "Auto start repair failed.", ex);
                ShowError($"修复开机启动失败: {ex.Message}", showErrorMessage);
                return false;
            }
        }

        private static bool IsEnabledForCurrentExe(string exePath)
        {
            return IsScheduledTaskForCurrentExe(exePath);
        }

        private static bool IsScheduledTaskForCurrentExe(string exePath)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks", $"/Query /TN \"{TaskName}\" /XML")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };

                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;

                    string xml = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode != 0 || string.IsNullOrWhiteSpace(xml)) return false;

                    var doc = XDocument.Parse(xml);
                    var command = doc.Descendants()
                        .FirstOrDefault(x => x.Name.LocalName == "Command")
                        ?.Value;
                    var enabled = doc.Descendants()
                        .FirstOrDefault(x => x.Name.LocalName == "Enabled" &&
                                             x.Parent?.Name.LocalName == "Settings")
                        ?.Value;

                    // 如果任务被用户或系统禁用，即使命令路径一致，也不能跳过重建/启用。
                    bool taskEnabled = !string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase);
                    return taskEnabled && string.Equals(command, exePath, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>生成当前用户的普通权限登录任务。</summary>
        private static XDocument GetTaskXml(string exePath, string userSid)
        {
            string exeDir = Path.GetDirectoryName(exePath)!;

            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

            var doc = new XDocument(
                new XDeclaration("1.0", "UTF-16", null),
                new XElement(ns + "Task",
                    new XAttribute("version", "1.2"),
                    new XElement(ns + "RegistrationInfo",
                        new XElement(ns + "Description", "CS2TradeMonitor Auto Start")
                    ),
                    new XElement(ns + "Triggers",
                        new XElement(ns + "LogonTrigger",
                            new XElement(ns + "Enabled", "true"),
                            new XElement(ns + "UserId", userSid),
                            new XElement(ns + "Delay", "PT10S")
                        )
                    ),
                    new XElement(ns + "Principals",
                        new XElement(ns + "Principal",
                            new XAttribute("id", "Author"),
                            new XElement(ns + "UserId", userSid),
                            new XElement(ns + "LogonType", "InteractiveToken"),
                            new XElement(ns + "RunLevel", "LeastPrivilege")
                        )
                    ),
                    new XElement(ns + "Settings",
                        new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                        new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                        new XElement(ns + "StopIfGoingOnBatteries", "false"),
                        new XElement(ns + "AllowHardTerminate", "true"),
                        new XElement(ns + "StartWhenAvailable", "true"),
                        new XElement(ns + "RunOnlyIfNetworkAvailable", "false"),
                        new XElement(ns + "IdleSettings",
                            new XElement(ns + "StopOnIdleEnd", "true"),
                            new XElement(ns + "RestartOnIdle", "false")
                        ),
                        new XElement(ns + "AllowStartOnDemand", "true"),
                        new XElement(ns + "Enabled", "true"),
                        new XElement(ns + "Hidden", "false"),
                        new XElement(ns + "RunOnlyIfIdle", "false"),
                        new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                        new XElement(ns + "Priority", "7")
                    ),
                    new XElement(ns + "Actions",
                        new XAttribute("Context", "Author"),
                        new XElement(ns + "Exec",
                            new XElement(ns + "Command", exePath),
                            new XElement(ns + "WorkingDirectory", exeDir)
                        )
                    )
                )
            );

            return doc;
        }

        private static bool IsTaskRegistered(string taskName)
        {
            try
            {
                return RunSchtasks($"/Query /TN \"{taskName}\"").Success;
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsNetworkPath(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
                return false;

            try
            {
                if (new Uri(executablePath).IsUnc)
                    return true;

                string? root = Path.GetPathRoot(executablePath);
                return !string.IsNullOrWhiteSpace(root)
                    && new DriveInfo(root).DriveType == DriveType.Network;
            }
            catch
            {
                return executablePath.StartsWith(@"\\", StringComparison.Ordinal);
            }
        }

        private static bool IsRunValueRegistered(string valueName)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(valueName) is string value && !string.IsNullOrWhiteSpace(value);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsRunValueForExe(string valueName, string exePath)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                string? value = key?.GetValue(valueName) as string;
                string? registeredExe = ExtractExePath(value);
                return string.Equals(registeredExe, exePath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool DeleteRunValue(string valueName)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                key?.DeleteValue(valueName, throwOnMissingValue: false);
                return !IsRunValueRegistered(valueName);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("AutoStart", $"Deleting HKCU Run value {valueName} failed.", ex);
                return false;
            }
        }

        private static string? ExtractExePath(string? command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;

            string trimmed = command.Trim();
            if (trimmed.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = trimmed.IndexOf('"', 1);
                return end > 1 ? trimmed.Substring(1, end - 1) : null;
            }

            int exeEnd = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeEnd >= 0)
            {
                return trimmed.Substring(0, exeEnd + 4).Trim();
            }

            int firstSpace = trimmed.IndexOf(' ');
            return firstSpace > 0 ? trimmed.Substring(0, firstSpace) : trimmed;
        }

        private static (bool Success, int ExitCode, string Output) RunSchtasks(string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var p = Process.Start(startInfo);
            if (p == null) return (false, -1, "无法启动 schtasks.exe");

            string output = p.StandardOutput.ReadToEnd();
            string error = p.StandardError.ReadToEnd();
            p.WaitForExit();
            string combined = string.Join(" ", new[] { output.Trim(), error.Trim() }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return (p.ExitCode == 0, p.ExitCode, combined);
        }

        internal static ProcessStartInfo CreateElevatedSchtasksStartInfo(string arguments)
        {
            return new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
        }

        private static (bool Success, int ExitCode, string Output) RunSchtasksElevated(string arguments)
        {
            try
            {
                using Process? process = Process.Start(CreateElevatedSchtasksStartInfo(arguments));
                if (process == null)
                    return (false, -1, "无法启动 schtasks.exe");

                process.WaitForExit();
                return process.ExitCode == 0
                    ? (true, 0, string.Empty)
                    : (false, process.ExitCode, "计划任务命令执行失败。");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                return (false, ErrorCancelled, "用户取消了 Windows 权限请求。");
            }
            catch (Exception ex)
            {
                return (false, -1, ex.Message);
            }
        }

        private static void ShowError(string message, bool showErrorMessage)
        {
            if (!showErrorMessage) return;
            GlobalPromptService.Show(message, "CS2交易监控", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
