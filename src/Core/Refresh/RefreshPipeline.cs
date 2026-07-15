using System;
using System.Threading;
using System.Threading.Tasks;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.src.Core.Refresh
{
    public sealed class RefreshPipeline
    {
        private readonly SemaphoreSlim _runner = new(1, 1);
        private long _requestedVersion;
        private long _completedVersion;
        private int _isRunning;

        public string Name { get; }
        public bool IsRunning => Volatile.Read(ref _isRunning) != 0;

        public RefreshPipeline(string name)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "RefreshPipeline" : name;
        }

        public bool IsLatest(long version)
        {
            return Interlocked.Read(ref _requestedVersion) == version;
        }

        public async Task<RefreshPipelineResult> RunAsync(
            string reason,
            Func<long, CancellationToken, Task> refresh,
            CancellationToken cancellationToken = default,
            bool waitForTurn = false)
        {
            if (refresh == null) throw new ArgumentNullException(nameof(refresh));

            long requested;
            if (waitForTurn)
            {
                await _runner.WaitAsync(cancellationToken).ConfigureAwait(false);
                requested = Interlocked.Increment(ref _requestedVersion);
            }
            else
            {
                requested = Interlocked.Increment(ref _requestedVersion);
                if (!await _runner.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                    return RefreshPipelineResult.AlreadyRunning(Name, requested, reason);
            }

            try
            {
                Interlocked.Exchange(ref _isRunning, 1);
                RefreshPipelineResult result = RefreshPipelineResult.Completed(Name, requested, reason);

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var version = Interlocked.Read(ref _requestedVersion);
                    try
                    {
                        await refresh(version, cancellationToken).ConfigureAwait(false);
                        Interlocked.Exchange(ref _completedVersion, version);
                        result = RefreshPipelineResult.Completed(Name, version, reason);
                    }
                    catch (OperationCanceledException)
                    {
                        result = RefreshPipelineResult.Cancelled(Name, version, reason);
                        break;
                    }
                    catch (Exception ex)
                    {
                        DiagnosticsLogger.Error(
                            "RefreshPipeline",
                            $"{Name} failed. Reason={DiagnosticsLogger.Redact(reason)}; Version={version}",
                            ex);
                        result = RefreshPipelineResult.Failed(Name, version, reason, ToFriendlyError(ex));
                    }

                    if (Interlocked.Read(ref _requestedVersion) == version)
                    {
                        break;
                    }
                }

                return result;
            }
            finally
            {
                Interlocked.Exchange(ref _isRunning, 0);
                _runner.Release();
            }
        }

        private static string ToFriendlyError(Exception ex)
        {
            var message = ex.GetBaseException().Message;
            if (string.IsNullOrWhiteSpace(message))
            {
                return "刷新失败，请稍后重试。";
            }

            return "刷新失败：" + message;
        }
    }

    public sealed class RefreshPipelineResult
    {
        private RefreshPipelineResult(
            string pipeline,
            long version,
            string reason,
            bool success,
            bool coalesced,
            bool canceled,
            string message)
        {
            Pipeline = pipeline;
            Version = version;
            Reason = reason;
            Success = success;
            Coalesced = coalesced;
            Canceled = canceled;
            Message = message;
        }

        public string Pipeline { get; }
        public long Version { get; }
        public string Reason { get; }
        public bool Success { get; }
        public bool Coalesced { get; }
        public bool Canceled { get; }
        public string Message { get; }

        public static RefreshPipelineResult Completed(string pipeline, long version, string reason)
        {
            return new RefreshPipelineResult(pipeline, version, reason, true, false, false, "刷新完成");
        }

        public static RefreshPipelineResult AlreadyRunning(string pipeline, long version, string reason)
        {
            return new RefreshPipelineResult(pipeline, version, reason, false, true, false, "已有刷新正在执行，本次请求已合并。");
        }

        public static RefreshPipelineResult Cancelled(string pipeline, long version, string reason)
        {
            return new RefreshPipelineResult(pipeline, version, reason, false, false, true, "刷新已取消。");
        }

        public static RefreshPipelineResult Failed(string pipeline, long version, string reason, string message)
        {
            return new RefreshPipelineResult(pipeline, version, reason, false, false, false, message);
        }
    }
}
