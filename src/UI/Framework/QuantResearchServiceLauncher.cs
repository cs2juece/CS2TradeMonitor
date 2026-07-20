using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
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

    internal interface IQuantResearchServiceHost
    {
        QuantResearchServiceHostInspection Inspect();

        void Start(Uri serviceUrl);

        void Stop();
    }

    internal sealed class QuantResearchServiceProcessHost : IQuantResearchServiceHost
    {
        private const string ServiceExecutableName = "CS2QuantWeb.exe";
        private readonly object _sync = new();
        private Process? _ownedProcess;

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
                    WindowStyle = ProcessWindowStyle.Hidden
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
                _ownedProcess = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("系统未能创建量化研究服务进程。");
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
                    _ownedProcess.Dispose();
                    _ownedProcess = null;
                }
            }
        }

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

            try
            {
                _host.Start(_serviceUrl);
                for (int attempt = 0; attempt < _startupPollAttempts; attempt++)
                {
                    await _delayAsync(_startupPollInterval, cancellationToken).ConfigureAwait(false);
                    QuantResearchServiceStatus status = await _checkAvailabilityAsync(cancellationToken).ConfigureAwait(false);
                    if (status.State == QuantResearchServiceState.Online)
                    {
                        return new QuantResearchServiceLaunchResult(
                            QuantResearchServiceLaunchState.Started,
                            "量化研究服务已启动。");
                    }
                }
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

            StopHostSafely();
            return new QuantResearchServiceLaunchResult(
                QuantResearchServiceLaunchState.Failed,
                "量化研究服务进程已启动，但健康检查未在规定时间内通过。");
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
