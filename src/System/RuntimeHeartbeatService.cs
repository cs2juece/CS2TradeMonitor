using System.Diagnostics;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor;

/// <summary>
/// Runtime heartbeat used for diagnostics only.
/// This service does not restart the application; recovery is handled by startup crash markers.
/// </summary>
public static class RuntimeHeartbeatService
{
    private static string? _pidFile;
    private static string? _crashMarkerFile;
    private static System.Threading.Timer? _heartbeatTimer;
    private static bool _started;

    /// <summary>
    /// Starts a lightweight heartbeat file for diagnostics.
    /// </summary>
    public static void Start(string? pidFile = null, string? crashMarkerFile = null)
    {
        if (_started) return;

        _pidFile = pidFile ?? GetDefaultPidFile();
        _crashMarkerFile = crashMarkerFile
            ?? (pidFile == null
                ? RuntimeDataPaths.GetLogFilePath("runtime_heartbeat.crash")
                : _pidFile + ".crash");

        try
        {
            string? dir = Path.GetDirectoryName(_pidFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            WriteHeartbeat();

            if (File.Exists(_crashMarkerFile))
                File.Delete(_crashMarkerFile);
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Ignored("RuntimeHeartbeat", "Start", ex, retryable: true, category: "Diagnostics");
        }

        _heartbeatTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                WriteHeartbeat();
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored("RuntimeHeartbeat", "WriteHeartbeat", ex, retryable: true, category: "Diagnostics");
            }
        }, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => MarkCrash();

        _started = true;
    }

    /// <summary>
    /// Marks a fatal crash for next-start diagnostics.
    /// </summary>
    public static void MarkCrash()
    {
        try
        {
            if (_crashMarkerFile != null)
                File.WriteAllText(_crashMarkerFile, DateTime.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Ignored("RuntimeHeartbeat", "MarkCrash", ex, retryable: true, category: "Diagnostics");
        }
    }

    /// <summary>
    /// Stops heartbeat updates and removes the pid file.
    /// </summary>
    public static void Stop()
    {
        try
        {
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;

            if (_pidFile != null && File.Exists(_pidFile))
                File.Delete(_pidFile);
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Ignored("RuntimeHeartbeat", "Stop", ex, retryable: true, category: "Diagnostics");
        }
        finally
        {
            _started = false;
            _pidFile = null;
            _crashMarkerFile = null;
        }
    }

    private static void WriteHeartbeat()
    {
        if (_pidFile != null)
            File.WriteAllText(_pidFile, Environment.ProcessId.ToString());
    }

    private static string GetDefaultPidFile()
    {
        return RuntimeDataPaths.GetCacheFilePath("runtime_heartbeat.pid");
    }
}
