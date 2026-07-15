using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.Infrastructure.Paths;
using CS2TradeMonitor.Infrastructure.Diagnostics;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.Core.Modules;
using CS2TradeMonitor.src.Core.State;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Framework;

namespace CS2TradeMonitor
{
    internal static class Program
    {
        // 保持 Mutex 引用，防止被 GC 回收
        private static Mutex? _mutex = null;
        private static SingleInstanceCommandChannel? _commandChannel = null;
        private static int _forcedExitFallbackStarted;
        private static bool _diagnosticsReady;
        private const string DisplayName = "CS2交易监控";
        private const string TechnicalName = "CS2TradeMonitor";
        private const string CrashMarkerName = TechnicalName + "_LastCrash.flag";

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                InstanceStartupPreflight.EnsureWritable();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    InstanceStartupPreflight.BuildFailureMessage(ex),
                    DisplayName + " 启动失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                Environment.ExitCode = 1;
                return;
            }

            try
            {
                Run(args);
            }
            catch (Exception ex)
            {
                if (_diagnosticsReady)
                {
                    LogCrash(ex, "Startup");
                }
                else
                {
                    MessageBox.Show(
                        "当前目录实例启动失败。\n\n" + ex.Message,
                        DisplayName + " 启动失败",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }

                ReleaseOwnedMutexes();
                Environment.ExitCode = 1;
            }
        }

        private static void Run(string[] args)
        {
            if (TryGetIntArg(args, "--restart-after-pid", out int previousPid))
                WaitForPreviousInstanceExit(previousPid);

            InstanceRuntimeContext instance = InstanceRuntimeContext.Current;
            string mutexName = instance.BuildOsResourceName(InstanceResourceKind.Application);
            string pipeName = instance.BuildOsResourceName(InstanceResourceKind.ArgumentsPipe);
            bool createNew;
            try
            {
                _mutex = new Mutex(true, mutexName, out createNew);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("无法创建当前目录实例的业务互斥锁，软件已拒绝启动。", ex);
            }

            if (!createNew)
            {
                bool forwarded = SingleInstanceCommandChannel.TryForward(pipeName, args);
                _mutex.Dispose();
                _mutex = null;
                if (!forwarded)
                {
                    MessageBox.Show(
                        "当前目录实例正在启动，暂时无法转发启动参数。请稍后重试。",
                        DisplayName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    Environment.ExitCode = 2;
                }

                return;
            }

            var commandBuffer = new StartupCommandBuffer();
            _commandChannel = new SingleInstanceCommandChannel(pipeName, commandBuffer.Dispatch);
            _commandChannel.Start();

            DiagnosticsLogger.EnsureLogFile();
            try
            {
                DetailedDiagnosticsRuntime.Initialize();
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("Diagnostics", "Detailed diagnostics initialization failed; business startup will continue.", ex);
            }
            _diagnosticsReady = true;
            RegisterExceptionHandlers();
            RuntimeHeartbeatService.Start();
            DiagnosticsLogger.Info("Startup", BuildStartupDiagnostics(args));

            ProgramRuntimeServices? runtimeServices = null;
            try
            {
                if (args.Any(arg => string.Equals(arg, "--repair-autostart", StringComparison.OrdinalIgnoreCase)))
                {
                    DiagnosticsLogger.Info("Startup", "Repair auto-start command requested.");
                    AutoStart.RepairIfNeeded(enabled: true, showErrorMessage: true);
                    return;
                }

                if (args.Any(arg => string.Equals(arg, "--disable-autostart", StringComparison.OrdinalIgnoreCase)))
                {
                    DiagnosticsLogger.Info("Startup", "Disable auto-start command requested.");
                    AutoStart.RepairIfNeeded(enabled: false, showErrorMessage: true);
                    return;
                }

                var startupStopwatch = Stopwatch.StartNew();
                ApplicationConfiguration.Initialize();
                // 初始化 DI 入口；阶段 0 只注册到现有单例，不替换旧调用链，避免状态分裂。
                AppServices.Initialize();
                runtimeServices = ProgramRuntimeServices.Resolve();
                NotifyPreviousCrashIfAny();
                RuntimeHealthLogger.StartIfEnabled(args);
                RuntimeStateCoordinator.Initialize();
                // 启动时发布首个配置快照，后续热路径读取 AppConfigState。
                Settings startupSettings = Settings.Load();
                if (startupSettings.AutoStart)
                    AutoStart.RepairIfNeeded(enabled: true, showErrorMessage: false);
                runtimeServices.AppConfigState.PublishFrom(startupSettings, "StartupSettingsLoaded");

                var moduleStopwatch = Stopwatch.StartNew();
                runtimeServices.ModuleHost.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
                moduleStopwatch.Stop();
                if (UiJankProfiler.Enabled)
                {
                    DiagnosticsLogger.Info("Perf", $"Startup.ModuleHostStart; ElapsedMs={moduleStopwatch.Elapsed.TotalMilliseconds:F2}");
                }

                StartSteamAutoConfirmIfEnabled(runtimeServices.SteamOffers);
                runtimeServices.SteamSessionKeepAlive.Start();

                string? startupSettingsPage = args
                    .FirstOrDefault(arg => arg.StartsWith("--open-settings=", StringComparison.OrdinalIgnoreCase))
                    ?.Split('=', 2)[1];
                bool openSettingsOnStartup = !string.IsNullOrWhiteSpace(startupSettingsPage) || args.Any(arg =>
                    string.Equals(arg, "--open-settings", StringComparison.OrdinalIgnoreCase));
                var formStopwatch = Stopwatch.StartNew();
                var mainForm = new MainForm(openSettingsOnStartup, startupSettingsPage);
                runtimeServices.SteamConnectivity.Start();
                runtimeServices.NetworkRouteRecovery.Start();
                commandBuffer.Attach(forwardedArgs =>
                {
                    DiagnosticsLogger.Info("Startup", $"Received forwarded startup args. Count={forwardedArgs.Length}");
                    PostForwardedStartupArgs(mainForm, forwardedArgs);
                });
                formStopwatch.Stop();
                startupStopwatch.Stop();
                if (UiJankProfiler.Enabled)
                {
                    DiagnosticsLogger.Info(
                        "Perf",
                        $"Startup.BeforeRun; FormCtorMs={formStopwatch.Elapsed.TotalMilliseconds:F2}; TotalBeforeRunMs={startupStopwatch.Elapsed.TotalMilliseconds:F2}");
                }

                System.Windows.Forms.Application.Run(mainForm);
            }
            catch (Exception ex)
            {
                LogCrash(ex, "Startup");
                Environment.ExitCode = 1;
            }
            finally
            {
                try
                {
                    RuntimeHealthLogger.Stop();
                    if (runtimeServices != null)
                    {
                        runtimeServices.NetworkRouteRecovery.StopAsync().GetAwaiter().GetResult();
                        runtimeServices.SteamConnectivity.StopAsync().GetAwaiter().GetResult();
                        runtimeServices.SteamSessionKeepAlive.Stop();
                        runtimeServices.SteamOffers.StopAutoConfirm();
                        runtimeServices.ModuleHost.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Error("ModuleHost", "Stopping monitor modules during shutdown failed.", ex);
                }

                DiagnosticsLogger.Info("Shutdown", "Application exiting; flushing diagnostics and releasing mutexes.");
                BeginForcedExitFallback();
                RuntimeHeartbeatService.Stop();
                _commandChannel?.Dispose();
                _commandChannel = null;
                // =================================================================
                // ★★★ [新增] 退出时的终极清理 ★★★
                // 无论程序是正常关闭、崩溃还是被强制结束(部分情况)，这里都会尝试执行
                // 确保 FPS 进程被杀掉，且 ETW 会话被停止，防止系统卡顿
                // =================================================================
                // 显式释放锁
                DetailedDiagnosticsRuntime.Shutdown();
                ReleaseOwnedMutexes();
            }
        }

        private static void RegisterExceptionHandlers()
        {
            // 捕获 UI 线程的异常
            System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            System.Windows.Forms.Application.ThreadException += Application_ThreadException;

            // 捕获非 UI 线程（后台线程）的异常
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private static void StartSteamAutoConfirmIfEnabled(ISteamOfferService steamOffers)
        {
            ArgumentNullException.ThrowIfNull(steamOffers);

            try
            {
                // 启动后台报价读取/自动处理任务需要读取用户保存的开关和间隔，不能依赖尚未打开的设置页。
                Settings settings = Settings.Load();
                var autoTradeSettings = new SteamAutoTradeSettings
                {
                    Enabled = settings.SteamAutoTradeEnabled || (settings.SteamOfferAutoCheck && settings.SteamOfferAutoAccept),
                    AcceptPureIncomingEnabled = settings.SteamAutoTradeAcceptPureIncomingEnabled || (settings.SteamOfferAutoCheck && settings.SteamOfferAutoAccept),
                    AcceptYouPinPurchaseEnabled = settings.SteamAutoTradeAcceptYouPinPurchaseEnabled || (settings.SteamOfferAutoCheck && settings.SteamOfferAutoAccept && settings.SteamOfferAllowYouPinVerifiedAccept),
                    SendYouPinSaleEnabled = settings.SteamAutoTradeSendYouPinSaleEnabled,
                    SendYouPinRentalEnabled = settings.SteamAutoTradeSendYouPinRentalEnabled,
                    IntervalSeconds = settings.SteamAutoTradeIntervalSeconds > 0
                        ? settings.SteamAutoTradeIntervalSeconds
                        : Math.Max(30, settings.SteamOfferRedesignRefreshMinutes * 60)
                };

                steamOffers.StartAutoTrade(autoTradeSettings);
                DiagnosticsLogger.Info("SteamOffer", $"Auto trade loop started from config. Enabled={autoTradeSettings.Enabled}; Interval={autoTradeSettings.IntervalSeconds}s");
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("SteamOffer", "Starting Steam offer auto trade loop from config failed.", ex);
            }
        }

        private static bool TryGetIntArg(string[] args, string name, out int value)
        {
            value = 0;
            string prefix = name + "=";
            string? raw = args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (raw == null) return false;
            return int.TryParse(raw.Substring(prefix.Length), out value);
        }

        private static void WaitForPreviousInstanceExit(int processId)
        {
            if (processId <= 0 || processId == Environment.ProcessId) return;

            try
            {
                using var process = Process.GetProcessById(processId);
                if (!ProcessBelongsToCurrentInstance(process))
                    return;

                if (!process.WaitForExit(10000))
                {
                    try
                    {
                        if (ProcessBelongsToCurrentInstance(process))
                        {
                            process.Kill(entireProcessTree: false);
                            process.WaitForExit(3000);
                        }
                    }
                    catch { /* 更新兼容参数只能影响已验证属于当前目录的已知 PID。 */ }
                }
            }
            catch
            {
                // The previous instance may already be gone.
            }

            Thread.Sleep(150);
        }

        internal static bool IsPathInsideCurrentInstance(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
                return false;

            try
            {
                string root = Path.GetFullPath(InstanceRuntimeContext.Current.InstallRoot);
                string candidate = Path.GetFullPath(executablePath);
                string relative = Path.GetRelativePath(root, candidate);
                return !Path.IsPathRooted(relative)
                    && !string.Equals(relative, "..", StringComparison.Ordinal)
                    && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static bool ProcessBelongsToCurrentInstance(Process process)
        {
            try
            {
                return IsPathInsideCurrentInstance(process.MainModule?.FileName ?? string.Empty);
            }
            catch
            {
                return false;
            }
        }

        private static void PostForwardedStartupArgs(MainForm mainForm, string[] args)
        {
            try
            {
                if (mainForm.IsDisposed || mainForm.Disposing) return;

                void Dispatch() => mainForm.HandleForwardedStartupArgs(args);

                if (mainForm.IsHandleCreated)
                {
                    mainForm.BeginInvoke(new Action(Dispatch));
                    return;
                }

                EventHandler? handler = null;
                handler = (_, __) =>
                {
                    mainForm.HandleCreated -= handler;
                    if (!mainForm.IsDisposed && !mainForm.Disposing)
                        mainForm.BeginInvoke(new Action(Dispatch));
                };
                mainForm.HandleCreated += handler;
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("Startup", "Dispatching forwarded startup args failed.", ex);
            }
        }

        private static void BeginForcedExitFallback()
        {
            if (Interlocked.Exchange(ref _forcedExitFallbackStarted, 1) == 1)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(3000).ConfigureAwait(false);
                    DiagnosticsLogger.Info("Shutdown", "Forced shutdown fallback reached after 3 seconds.");
                }
                catch
                {
                    // Fall through to Environment.Exit.
                }

                Environment.Exit(0);
            });
        }

        // --- 异常处理委托 ---
        static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            LogCrash(e.Exception, "UI_Thread");
            ExitAfterFatalUiException();
        }

        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            LogCrash(e.ExceptionObject as Exception, "Background_Thread");
        }

        static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            DiagnosticsLogger.Error("Task", "Unobserved task exception captured.", e.Exception);
            e.SetObserved();
        }

        private static void ExitAfterFatalUiException()
        {
            try
            {
                ReleaseOwnedMutexes();
                System.Windows.Forms.Application.Exit();
            }
            catch
            {
                // Fatal exception handling must not throw another UI exception.
            }

            Environment.Exit(1);
        }

        // --- 写入 crash.log 的核心方法 ---
        static void LogCrash(Exception? ex, string source)
        {
            if (ex == null) return;

            try
            {
                RuntimeHeartbeatService.MarkCrash();
                string logPath = DiagnosticsLogger.EnsureLogFile();
                string markerPath = DiagnosticsLogger.GetDiagnosticFilePath(CrashMarkerName);

                string safeMessage = RedactSensitive(ex.Message);
                string safeStack = RedactSensitive(ex.StackTrace ?? string.Empty);

                string errorMsg = "==================================================\n" +
                                  $"[Time]: {DateTime.Now}\n" +
                                  $"[Source]: {source}\n" +
                                  $"[Version]: {typeof(Program).Assembly.GetName().Version}\n" +
                                  $"[PID]: {Environment.ProcessId}\n" +
                                  $"[OS]: {RuntimeInformation.OSDescription}\n" +
                                  $"[Runtime]: {RuntimeInformation.FrameworkDescription}\n" +
                                  $"[BaseDir]: {InstallationPaths.InstallDirectory}\n" +
                                  $"[Message]: {safeMessage}\n" +
                                  $"[Stack]:\n{safeStack}\n" +
                                  "==================================================\n\n";

                DiagnosticsLogger.PrependRaw(errorMsg);
                try
                {
                    File.WriteAllText(markerPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {source}: {safeMessage}");
                }
                catch (Exception markerEx)
                {
                    DiagnosticsLogger.Error("Crash", "Writing crash marker failed.", markerEx);
                }

                // 只有真的崩了才弹窗提示用户
                MessageBox.Show($"程序遇到致命错误！\n错误日志已保存至：{logPath}\n\n原因：{safeMessage}",
                                DisplayName + " 崩溃", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
                // 如果日志都写不进去，通常是磁盘满了或权限极度受限，只能忽略
            }
        }

        private static void NotifyPreviousCrashIfAny()
        {
            string logPath = DiagnosticsLogger.EnsureLogFile();
            string markerPath = DiagnosticsLogger.GetDiagnosticFilePath(CrashMarkerName);

            if (!File.Exists(markerPath) || !File.Exists(logPath)) return;

            try
            {
                string marker = File.ReadAllText(markerPath);
                try { File.Delete(markerPath); } catch (System.Exception ex) { CS2TradeMonitor.src.SystemServices.DiagnosticsLogger.Ignored(ex); }

                var result = MessageBox.Show(
                    "检测到 " + DisplayName + " 上次运行时发生异常。\n\n" +
                    marker + "\n\n是否打开日志文件？",
                    DisplayName + " 诊断",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
                }
            }
            catch
            {
                // Ignore diagnostics UI failures; startup should continue.
            }
        }

        private static string RedactSensitive(string? text)
        {
            return DiagnosticsLogger.Redact(text);
        }

        private static string BuildStartupDiagnostics(string[] args)
        {
            string version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
            string safeArgs = args.Length == 0
                ? "(none)"
                : string.Join(" ", args.Select(DiagnosticsLogger.Redact));

            return $"App started. Version={version}; PID={Environment.ProcessId}; " +
                   $"OS={RuntimeInformation.OSDescription}; Runtime={RuntimeInformation.FrameworkDescription}; " +
                   $"64BitProcess={Environment.Is64BitProcess}; UserInteractive={Environment.UserInteractive}; " +
                   $"BaseDir={InstallationPaths.InstallDirectory}; LogPath={DiagnosticsLogger.LogFilePath}; Args={safeArgs}";
        }

        private static void ReleaseOwnedMutexes()
        {
            try { _mutex?.ReleaseMutex(); }
            catch (Exception ex)
            {
                if (_diagnosticsReady)
                    DiagnosticsLogger.Ignored(ex);
            }
            _mutex?.Dispose();
            _mutex = null;
        }
    }
}
