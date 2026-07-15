using System;
using System.Diagnostics;
using System.IO;
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
        private static string TaskName => InstanceRuntimeContext.Current.BuildOsResourceName(InstanceResourceKind.AutoStart);
        internal static string CurrentRegistrationName => TaskName;

        public static bool Set(bool enabled, bool showErrorMessage = true)
        {
            string exePath = LauncherExecutablePath.GetCurrent();

            if (enabled)
            {
                if (IsRunValueForExe(TaskName, exePath))
                {
                    DeleteTaskIfExists(TaskName, logFailureAsError: false);
                    return true;
                }

                if (SetRunValue(exePath))
                {
                    DeleteTaskIfExists(TaskName, logFailureAsError: false);
                    DiagnosticsLogger.Info("AutoStart", "HKCU Run auto start was created. Scheduled task is no longer required.");
                    return true;
                }

                if (IsNetworkPath(exePath))
                {
                    ShowError(
                        "当前网络路径无法创建 HKCU Run 启动项，Windows 计划任务也不支持网络路径。此目录实例的开机启动未启用。",
                        showErrorMessage);
                    return false;
                }

                DiagnosticsLogger.Info("AutoStart", "HKCU Run auto start failed; trying scheduled task fallback.");

                string tempXmlPath = RuntimeDataPaths.GetCacheFilePath($"autostart-task-{Guid.NewGuid():N}.xml");

                try
                {
                    // 生成 XML 内容 (修改为获取 XDocument 对象)
                    var doc = GetTaskXml(exePath);

                    // 写入临时文件 (修改为 doc.Save，它会自动处理 UTF-16 编码)
                    doc.Save(tempXmlPath);

                    // 调用 schtasks 导入 XML
                    // /F: 强制覆盖
                    // /TN: 任务名
                    // /XML: 指定配置文件
                    var result = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{tempXmlPath}\" /F");
                    if (!result.Success)
                    {
                        string message = $"设置开机启动失败，schtasks 返回 {result.ExitCode}。{result.Output}".Trim();
                        if (SetRunValue(exePath))
                        {
                            DiagnosticsLogger.Info("AutoStart", $"{message} HKCU Run fallback was created.");
                            return true;
                        }

                        DiagnosticsLogger.Error("AutoStart", message);
                        ShowError(message, showErrorMessage);
                        return false;
                    }

                    if (!IsScheduledTaskForCurrentExe(exePath))
                    {
                        string message = "开机启动任务已创建，但校验当前程序路径失败。请重新应用一次开机启动设置。";
                        if (SetRunValue(exePath))
                        {
                            DiagnosticsLogger.Info("AutoStart", $"{message} HKCU Run fallback was created.");
                            return true;
                        }

                        DiagnosticsLogger.Error("AutoStart", message);
                        ShowError(message, showErrorMessage);
                        return false;
                    }

                    DeleteRunValue(TaskName);
                }
                catch (Exception ex)
                {
                    // 捕获所有 IO 或 进程异常
                    if (SetRunValue(exePath))
                    {
                        DiagnosticsLogger.Info("AutoStart", $"Scheduled task exception; HKCU Run fallback was created. {ex.Message}");
                        return true;
                    }

                    DiagnosticsLogger.Error("AutoStart", "Setting auto start failed.", ex);
                    ShowError($"设置失败: {ex.Message}", showErrorMessage);
                    return false;
                }
                finally
                {
                    // 确保无论是否出错，都尝试清理临时文件
                    try
                    {
                        if (File.Exists(tempXmlPath)) File.Delete(tempXmlPath);
                    }
                    catch (Exception ex)
                    {
                        DiagnosticsLogger.Info("AutoStart", $"Ignored temp task XML cleanup failure: {ex.Message}");
                    }
                }

                return true;
            }
            else
            {
                DeleteTaskIfExists(TaskName);
                DeleteRunValue(TaskName);
                return !IsEnabled();
            }
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
                if (runCurrent)
                    return "开机启动状态：HKCU Run 已指向当前程序。";
                if (taskCurrent)
                    return "开机启动状态：计划任务已指向当前程序（兼容模式）。";
                if (runAny || taskAny)
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
                    if (IsEnabledForCurrentExe()) return true;
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
            return IsRunValueForExe(TaskName, exePath) || IsScheduledTaskForCurrentExe(exePath);
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

        /// <summary>
        /// 生成 XML 配置：完美复刻原始逻辑 + 增加高级电池/延迟设置
        /// (已重构为 XDocument 方式)
        /// </summary>
        private static XDocument GetTaskXml(string exePath)
        {
            // 细节保留：获取工作目录，对应你原始代码的 /STRTIN
            string exeDir = Path.GetDirectoryName(exePath)!;

            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

            // 使用 XDocument 构建 XML
            // 自动处理特殊字符转义（如路径中的 & ' 等）
            // 自动处理编码声明 (UTF-16)
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
                            new XElement(ns + "Delay", "PT5S")
                        )
                    ),
                    new XElement(ns + "Principals",
                        new XElement(ns + "Principal",
                            new XAttribute("id", "Author"),
                            new XElement(ns + "LogonType", "InteractiveToken"),
                            new XElement(ns + "RunLevel", "LeastPrivilege")
                        )
                    ),
                    new XElement(ns + "Settings",
                        new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                        new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                        new XElement(ns + "StopIfGoingOnBatteries", "false"),
                        new XElement(ns + "AllowHardTerminate", "true"),
                        new XElement(ns + "StartWhenAvailable", "false"),
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

        private static void DeleteTaskIfExists(string taskName, bool logFailureAsError = true)
        {
            try
            {
                var result = RunSchtasks($"/Delete /TN \"{taskName}\" /F");
                if (!result.Success)
                {
                    string message = $"Deleting task {taskName} failed. schtasks returned {result.ExitCode}. {result.Output}".Trim();

                    if (!IsTaskRegistered(taskName))
                        return;

                    if (logFailureAsError)
                    {
                        DiagnosticsLogger.Error("AutoStart", message);
                    }
                    else
                    {
                        DiagnosticsLogger.Info("AutoStart", $"{message} Continuing with current startup configuration.");
                    }
                }
            }
            catch
            {
                // Startup cleanup must never block the application.
            }
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

        private static bool SetRunValue(string exePath)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
                if (key == null) return false;
                key.SetValue(TaskName, QuoteCommand(exePath), RegistryValueKind.String);
                return IsRunValueForExe(TaskName, exePath);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("AutoStart", "Setting HKCU Run fallback failed.", ex);
                return false;
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

        private static void DeleteRunValue(string valueName)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                key?.DeleteValue(valueName, throwOnMissingValue: false);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("AutoStart", $"Deleting HKCU Run value {valueName} failed.", ex);
            }
        }

        private static string QuoteCommand(string exePath)
        {
            return $"\"{exePath.Replace("\"", "")}\"";
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

        private static void ShowError(string message, bool showErrorMessage)
        {
            if (!showErrorMessage) return;
            GlobalPromptService.Show(message, "CS2交易监控", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
