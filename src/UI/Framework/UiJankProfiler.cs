using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.Infrastructure.Diagnostics;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class UiJankProfiler
    {
        private static readonly bool EnabledValue = ResolveEnabled();
        private static readonly IDisposable NoopScope = new NoopDisposable();

        public static bool Enabled => EnabledValue || DetailedDiagnosticsRuntime.IsEnabled;

        public static bool VerboseLoggingEnabled => EnabledValue;

        public static IDisposable Measure(string scope, string detail = "", long thresholdMs = 16)
        {
            if (!Enabled)
                return NoopScope;

            return new MeasureScope(scope, detail, Math.Max(0, thresholdMs));
        }

        public static void Log(string scope, string detail)
        {
            if (!Enabled)
                return;

            DiagnosticsLogger.InfoEvent(
                "Perf",
                "PerformanceSample",
                $"{scope}; {detail}; Thread={Thread.CurrentThread.ManagedThreadId}; MessageLoop={System.Windows.Forms.Application.MessageLoop}",
                new Dictionary<string, object?>
                {
                    ["scope"] = scope,
                    ["detail"] = detail,
                    ["threadId"] = Thread.CurrentThread.ManagedThreadId,
                    ["messageLoop"] = System.Windows.Forms.Application.MessageLoop
                });
        }

        private static bool ResolveEnabled()
        {
            try
            {
                string? value = Environment.GetEnvironmentVariable("CS2_UI_JANK_PROFILE");
                return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private sealed class MeasureScope : IDisposable
        {
            private readonly string _scope;
            private readonly string _detail;
            private readonly long _thresholdMs;
            private readonly Stopwatch _stopwatch;
            private bool _disposed;

            public MeasureScope(string scope, string detail, long thresholdMs)
            {
                _scope = scope;
                _detail = detail;
                _thresholdMs = thresholdMs;
                _stopwatch = Stopwatch.StartNew();
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _stopwatch.Stop();
                if (_stopwatch.ElapsedMilliseconds < _thresholdMs)
                    return;

                string suffix = string.IsNullOrWhiteSpace(_detail) ? string.Empty : "; " + _detail;
                DiagnosticsLogger.InfoEvent(
                    "Perf",
                    "SlowOperation",
                    $"{_scope}; ElapsedMs={_stopwatch.Elapsed.TotalMilliseconds:F2}{suffix}; Thread={Thread.CurrentThread.ManagedThreadId}; MessageLoop={System.Windows.Forms.Application.MessageLoop}",
                    new Dictionary<string, object?>
                    {
                        ["scope"] = _scope,
                        ["detail"] = _detail,
                        ["elapsedMs"] = _stopwatch.Elapsed.TotalMilliseconds,
                        ["thresholdMs"] = _thresholdMs,
                        ["threadId"] = Thread.CurrentThread.ManagedThreadId,
                        ["messageLoop"] = System.Windows.Forms.Application.MessageLoop
                    });
            }
        }

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
