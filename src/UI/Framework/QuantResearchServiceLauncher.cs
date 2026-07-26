using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal enum QuantResearchServiceLaunchState
    {
        AlreadyRunning,
        MissingMarketDataSourceCredential,
        MissingRuntime,
        MissingExecutable,
        Started,
        Failed
    }

    internal sealed record QuantResearchServiceLaunchResult(
        QuantResearchServiceLaunchState State,
        string Detail);

    internal sealed record QuantResearchServiceHostInspection(
        bool HasRequiredRuntime,
        bool HasExecutable,
        string ExecutablePath,
        bool HasMarketDataSourceCredential);

    internal sealed record QuantResearchServiceProcessStatus(
        bool IsRunning,
        int? ExitCode,
        string Detail);

    internal interface IQuantResearchServiceHost
    {
        QuantResearchServiceHostInspection Inspect();

        void Start(Uri serviceUrl);

        QuantResearchServiceProcessStatus CaptureProcessStatus();

        bool IsOwnedServiceRunning(Uri serviceUrl);

        void Stop();
    }

    internal sealed class QuantResearchServiceProcessHost : IQuantResearchServiceHost
    {
        private const string ServiceExecutableName = "CS2QuantWeb.exe";
        private const int MaximumCapturedOutputCharacters = 8192;
        private readonly object _sync = new();
        private readonly object _outputSync = new();
        private readonly StringBuilder _capturedOutput = new();
        private Process? _ownedProcess;
        private Uri? _ownedServiceUrl;

        private QuantResearchServiceProcessHost()
        {
        }

        public static QuantResearchServiceProcessHost Instance { get; } = new();

        public QuantResearchServiceHostInspection Inspect()
        {
            string executablePath = Path.Combine(
                InstallationPaths.InstallDirectory,
                "quant-web",
                ServiceExecutableName);
            Settings settings = Settings.Load();
            return new QuantResearchServiceHostInspection(
                HasRequiredRuntime: HasRequiredAspNetCoreRuntime(),
                HasExecutable: File.Exists(executablePath),
                ExecutablePath: executablePath,
                HasMarketDataSourceCredential: !string.IsNullOrWhiteSpace(settings.SteamDtApiKey));
        }

        public void Start(Uri serviceUrl)
        {
            ArgumentNullException.ThrowIfNull(serviceUrl);
            if (!QuantResearchEntryPageModel.CanStartLocalService(serviceUrl))
                throw new ArgumentException("量化研究服务仅允许监听本机回环 HTTP 地址。", nameof(serviceUrl));

            lock (_sync)
            {
                if (_ownedProcess is { HasExited: false })
                    return;

                _ownedProcess?.Dispose();
                _ownedProcess = null;

                QuantResearchServiceHostInspection inspection = Inspect();
                if (!inspection.HasExecutable)
                    throw new FileNotFoundException("量化研究服务程序不存在。", inspection.ExecutablePath);

                string workingDirectory = Path.GetDirectoryName(inspection.ExecutablePath)
                    ?? throw new InvalidOperationException("量化研究服务目录无效。");
                var startInfo = new ProcessStartInfo(inspection.ExecutablePath)
                {
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
                startInfo.Environment["CS2_QUANT_LISTEN_URL"] = serviceUrl.GetLeftPart(UriPartial.Authority);
                startInfo.Environment["CS2_QUANT_PARENT_PID"] = Environment.ProcessId.ToString(
                    CultureInfo.InvariantCulture);
                Settings settings = Settings.Load();
                ApplyCredentialEnvironment(
                    startInfo,
                    settings.SteamDtApiKey,
                    settings.CsqaqApiToken);
                using (Process currentProcess = Process.GetCurrentProcess())
                {
                    startInfo.Environment["CS2_QUANT_PARENT_START_UTC_TICKS"] = currentProcess.StartTime
                        .ToUniversalTime()
                        .Ticks
                        .ToString(CultureInfo.InvariantCulture);
                }
                lock (_outputSync)
                    _capturedOutput.Clear();
                var process = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };
                process.OutputDataReceived += CaptureProcessOutput;
                process.ErrorDataReceived += CaptureProcessOutput;
                try
                {
                    if (!process.Start())
                        throw new InvalidOperationException("系统未能创建量化研究服务进程。");
                }
                catch
                {
                    process.Dispose();
                    throw;
                }

                _ownedProcess = process;
                _ownedServiceUrl = serviceUrl;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                DiagnosticsLogger.Info("QuantResearch", $"Owned quant service started. PID={_ownedProcess.Id}");
            }
        }

        internal static void ApplyCredentialEnvironment(
            ProcessStartInfo startInfo,
            string? steamDtApiKey,
            string? csqaqApiToken)
        {
            ArgumentNullException.ThrowIfNull(startInfo);
            startInfo.Environment.Remove("STEAMDT_API_KEY");
            SetCredential(startInfo, "CS2_QUANT_STEAMDT_API_KEY", steamDtApiKey);
            SetCredential(startInfo, "QAQ_API_KEY", csqaqApiToken);
        }

        public QuantResearchServiceProcessStatus CaptureProcessStatus()
        {
            lock (_sync)
            {
                if (_ownedProcess is null)
                    return new QuantResearchServiceProcessStatus(false, null, string.Empty);
                if (!_ownedProcess.HasExited)
                    return new QuantResearchServiceProcessStatus(true, null, string.Empty);

                _ownedProcess.WaitForExit();
                string output;
                lock (_outputSync)
                    output = _capturedOutput.ToString();
                return new QuantResearchServiceProcessStatus(
                    IsRunning: false,
                    ExitCode: _ownedProcess.ExitCode,
                    Detail: BuildProcessFailureDetail(
                        _ownedProcess.ExitCode,
                        output,
                        _ownedServiceUrl));
            }
        }

        public bool IsOwnedServiceRunning(Uri serviceUrl)
        {
            ArgumentNullException.ThrowIfNull(serviceUrl);
            lock (_sync)
            {
                return _ownedProcess is { HasExited: false }
                    && _ownedServiceUrl is not null
                    && Uri.Compare(
                        _ownedServiceUrl,
                        serviceUrl,
                        UriComponents.SchemeAndServer,
                        UriFormat.SafeUnescaped,
                        StringComparison.OrdinalIgnoreCase) == 0;
            }
        }

        internal static string BuildProcessFailureDetail(int exitCode, string? output, Uri? serviceUrl)
        {
            string evidence = output ?? string.Empty;
            int port = serviceUrl?.Port ?? new Uri(QuantResearchEntryPageModel.DefaultUrl).Port;
            if (ContainsAny(
                    evidence,
                    "address already in use",
                    "failed to bind to address",
                    "only one usage of each socket address"))
            {
                return $"量化研究服务无法监听端口 {port}，该端口已被其他程序占用。";
            }

            if (ContainsAny(
                    evidence,
                    "you must install or update .net",
                    "microsoft.aspnetcore.app",
                    "failed to load the hostfxr"))
            {
                return "量化研究服务无法加载 Microsoft ASP.NET Core Runtime 10（x64），请修复或重新安装该运行时。";
            }

            if (ContainsAny(
                    evidence,
                    "could not load file or assembly",
                    "filenotfoundexception",
                    "the application to execute does not exist",
                    "the specified module could not be found"))
            {
                return "量化研究服务组件缺失或已损坏，请重新安装完整版本并检查安全软件隔离记录。";
            }

            if (ContainsAny(evidence, "access is denied", "unauthorizedaccessexception"))
                return "量化研究服务被系统或安全软件阻止启动，请检查安全软件拦截记录。";

            return $"量化研究服务进程已提前退出（退出代码 {exitCode}）。";
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (_ownedProcess is null)
                    return;

                try
                {
                    if (!_ownedProcess.HasExited)
                    {
                        _ownedProcess.Kill(entireProcessTree: true);
                        _ownedProcess.WaitForExit(3000);
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Ignored("QuantResearch", "StopOwnedService", ex);
                }
                finally
                {
                    try { _ownedProcess.CancelOutputRead(); }
                    catch { /* 输出流可能已随进程退出而关闭；这里只做尽力清理。 */ }
                    try { _ownedProcess.CancelErrorRead(); }
                    catch { /* 错误流可能已随进程退出而关闭；这里只做尽力清理。 */ }
                    _ownedProcess.Dispose();
                    _ownedProcess = null;
                    _ownedServiceUrl = null;
                    lock (_outputSync)
                        _capturedOutput.Clear();
                }
            }
        }

        private void CaptureProcessOutput(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;

            lock (_outputSync)
            {
                _capturedOutput.AppendLine(e.Data.Trim());
                int excess = _capturedOutput.Length - MaximumCapturedOutputCharacters;
                if (excess > 0)
                    _capturedOutput.Remove(0, excess);
            }
        }

        private static bool ContainsAny(string value, params string[] candidates) =>
            candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

        private static bool HasRequiredAspNetCoreRuntime()
        {
            try
            {
                string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT_X64");
                if (string.IsNullOrWhiteSpace(dotnetRoot))
                    dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
                if (string.IsNullOrWhiteSpace(dotnetRoot))
                {
                    dotnetRoot = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        "dotnet");
                }

                string sharedFrameworkDirectory = Path.Combine(
                    Path.GetFullPath(dotnetRoot),
                    "shared",
                    "Microsoft.AspNetCore.App");
                return Directory.Exists(sharedFrameworkDirectory)
                    && Directory.EnumerateDirectories(sharedFrameworkDirectory)
                        .Select(Path.GetFileName)
                        .Any(IsCompatibleAspNetCoreRuntimeVersion);
            }
            catch
            {
                return false;
            }
        }

        private static void SetCredential(ProcessStartInfo startInfo, string name, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                startInfo.Environment.Remove(name);
            else
                startInfo.Environment[name] = value.Trim();
        }

        internal static bool IsCompatibleAspNetCoreRuntimeVersion(string? version)
        {
            return Version.TryParse(version, out Version? parsed) && parsed.Major == 10;
        }
    }

    internal sealed class QuantResearchServiceLauncher
    {
        private readonly IQuantResearchServiceHost _host;
        private readonly Func<CancellationToken, Task<QuantResearchServiceStatus>> _checkAvailabilityAsync;
        private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
        private readonly int _startupPollAttempts;
        private readonly TimeSpan _startupPollInterval;
        private readonly Uri _serviceUrl;

        internal QuantResearchServiceLauncher(
            IQuantResearchServiceHost host,
            Func<CancellationToken, Task<QuantResearchServiceStatus>> checkAvailabilityAsync,
            Func<TimeSpan, CancellationToken, Task> delayAsync,
            int startupPollAttempts,
            TimeSpan startupPollInterval,
            Uri? serviceUrl = null)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(checkAvailabilityAsync);
            ArgumentNullException.ThrowIfNull(delayAsync);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(startupPollAttempts);
            ArgumentOutOfRangeException.ThrowIfLessThan(startupPollInterval, TimeSpan.Zero);

            _host = host;
            _checkAvailabilityAsync = checkAvailabilityAsync;
            _delayAsync = delayAsync;
            _startupPollAttempts = startupPollAttempts;
            _startupPollInterval = startupPollInterval;
            _serviceUrl = serviceUrl ?? new Uri(QuantResearchEntryPageModel.DefaultUrl);
        }

        public async Task<QuantResearchServiceLaunchResult> StartAsync(CancellationToken cancellationToken)
        {
            QuantResearchServiceStatus current = await _checkAvailabilityAsync(cancellationToken).ConfigureAwait(false);
            if (current.State == QuantResearchServiceState.Online)
            {
                return new QuantResearchServiceLaunchResult(
                    QuantResearchServiceLaunchState.AlreadyRunning,
                    "量化研究服务已经运行。");
            }
            if (current.State == QuantResearchServiceState.PortOccupied)
            {
                return new QuantResearchServiceLaunchResult(
                    QuantResearchServiceLaunchState.Failed,
                    current.Detail);
            }

            QuantResearchServiceHostInspection inspection = _host.Inspect();
            if (!inspection.HasMarketDataSourceCredential)
            {
                return new QuantResearchServiceLaunchResult(
                    QuantResearchServiceLaunchState.MissingMarketDataSourceCredential,
                    "请先在“大盘数据源”中填写 SteamDT API，再启动量化研究服务。");
            }

            if (!inspection.HasRequiredRuntime)
            {
                return new QuantResearchServiceLaunchResult(
                    QuantResearchServiceLaunchState.MissingRuntime,
                    "量化研究服务需要 Microsoft ASP.NET Core Runtime 10（x64）。");
            }

            if (!inspection.HasExecutable)
            {
                return new QuantResearchServiceLaunchResult(
                    QuantResearchServiceLaunchState.MissingExecutable,
                    "当前安装包缺少量化研究服务组件，请重新下载完整版本。");
            }

            if (current.State == QuantResearchServiceState.ConfigurationRequired)
            {
                if (!_host.IsOwnedServiceRunning(_serviceUrl))
                {
                    return new QuantResearchServiceLaunchResult(
                        QuantResearchServiceLaunchState.Failed,
                        current.Detail + " 当前服务不是由当前桌面端进程启动，已保留该进程；请先完全退出现有量化服务，再点击“启动服务”。");
                }

                StopHostSafely();
            }

            try
            {
                _host.Start(_serviceUrl);
                QuantResearchServiceStatus lastStatus = current;
                for (int attempt = 0; attempt < _startupPollAttempts; attempt++)
                {
                    await _delayAsync(_startupPollInterval, cancellationToken).ConfigureAwait(false);
                    QuantResearchServiceLaunchResult? processFailure = CaptureProcessFailure();
                    if (processFailure is not null)
                        return processFailure;

                    QuantResearchServiceStatus status = await _checkAvailabilityAsync(cancellationToken).ConfigureAwait(false);
                    lastStatus = status;
                    if (status.State == QuantResearchServiceState.Online)
                    {
                        return new QuantResearchServiceLaunchResult(
                            QuantResearchServiceLaunchState.Started,
                            "量化研究服务已启动。");
                    }
                }

                QuantResearchServiceLaunchResult? finalProcessFailure = CaptureProcessFailure();
                if (finalProcessFailure is not null)
                    return finalProcessFailure;

                StopHostSafely();
                return new QuantResearchServiceLaunchResult(
                    QuantResearchServiceLaunchState.Failed,
                    $"量化研究服务进程已启动，但健康检查未在规定时间内通过。最后状态：{lastStatus.Text}，{lastStatus.Detail}");
            }
            catch (OperationCanceledException)
            {
                StopHostSafely();
                throw;
            }
            catch (Exception ex)
            {
                StopHostSafely();
                return new QuantResearchServiceLaunchResult(
                    QuantResearchServiceLaunchState.Failed,
                    $"启动量化研究服务失败：{DiagnosticsLogger.Redact(ex.Message)}");
            }

        }

        private QuantResearchServiceLaunchResult? CaptureProcessFailure()
        {
            QuantResearchServiceProcessStatus processStatus = _host.CaptureProcessStatus();
            if (processStatus.IsRunning || processStatus.ExitCode is null)
                return null;

            string detail = string.IsNullOrWhiteSpace(processStatus.Detail)
                ? "量化研究服务进程已提前退出。"
                : processStatus.Detail;
            if (!detail.Contains("退出代码", StringComparison.Ordinal))
                detail += $"（退出代码 {processStatus.ExitCode.Value}）";
            DiagnosticsLogger.Info("QuantResearch", detail);
            StopHostSafely();
            return new QuantResearchServiceLaunchResult(
                QuantResearchServiceLaunchState.Failed,
                detail);
        }

        private void StopHostSafely()
        {
            try
            {
                _host.Stop();
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored("QuantResearch", "RollbackOwnedService", ex);
            }
        }
    }
}
