using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace CS2TradeMonitor.src.SystemServices;

public static class RuntimeHealthLogger
{
    private const int GdiObjects = 0;
    private const int UserObjects = 1;
    private static readonly object Sync = new();
    private static System.Threading.Timer? _timer;
    private static long _pageSwitchCount;
    private static string _lastPage = "";

    [DllImport("user32.dll")]
    private static extern int GetGuiResources(IntPtr hProcess, int uiFlags);

    public static void StartIfEnabled(string[] args)
    {
        bool enabled =
#if DEBUG
            true;
#else
            args.Any(arg =>
                string.Equals(arg, "--diagnostics", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--health-log", StringComparison.OrdinalIgnoreCase));
#endif

        if (!enabled)
            return;

        lock (Sync)
        {
            if (_timer is not null)
                return;

            _timer = new System.Threading.Timer(_ => WriteSnapshot(), null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5));
        }
    }

    public static void Stop()
    {
        System.Threading.Timer? timer;
        lock (Sync)
        {
            timer = _timer;
            _timer = null;
        }

        timer?.Dispose();
    }

    public static void RecordPageSwitch(string key)
    {
        Interlocked.Increment(ref _pageSwitchCount);
        if (!string.IsNullOrWhiteSpace(key))
            Volatile.Write(ref _lastPage, key);
    }

    private static void WriteSnapshot()
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            long workingSetMb = process.WorkingSet64 / 1024 / 1024;
            long privateMb = process.PrivateMemorySize64 / 1024 / 1024;
            int gdi = GetGuiResources(process.Handle, GdiObjects);
            int user = GetGuiResources(process.Handle, UserObjects);
            long pageSwitches = Interlocked.Read(ref _pageSwitchCount);
            string lastPage = Volatile.Read(ref _lastPage);
            DiagnosticsLogger.Info(
                "Health",
                $"WorkingSet={workingSetMb}MB; PrivateMemory={privateMb}MB; GDI={gdi}; USER={user}; PageSwitches={pageSwitches}; LastPage={lastPage}");
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Info("Health", $"Runtime health snapshot skipped: {ex.Message}");
        }
    }
}
