using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CS2TradeMonitor
{
    internal sealed class SingleInstanceCommandChannel : IDisposable
    {
        private const int ConnectTimeoutMs = 1500;
        private const int ForwardAttemptCount = 3;
        private const int MaxPayloadBytes = 32 * 1024;
        private readonly string _pipeName;
        private readonly Action<string[]> _onArgsReceived;
        private readonly CancellationTokenSource _cts = new();
        private Task? _listenTask;

        public SingleInstanceCommandChannel(string pipeName, Action<string[]> onArgsReceived)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
            _pipeName = pipeName;
            _onArgsReceived = onArgsReceived ?? throw new ArgumentNullException(nameof(onArgsReceived));
        }

        public void Start()
        {
            if (_listenTask != null) return;
            var firstServer = CreateServer();
            _listenTask = Task.Run(() => ListenLoopAsync(firstServer));
        }

        public static bool TryForward(string pipeName, string[] args)
            => TryForward(pipeName, args, ConnectTimeoutMs, ForwardAttemptCount);

        internal static bool TryForward(string pipeName, string[] args, int connectTimeoutMs, int attemptCount)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(connectTimeoutMs);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attemptCount);
            for (int attempt = 1; attempt <= attemptCount; attempt++)
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                    client.Connect(connectTimeoutMs);

                    string payload = JsonSerializer.Serialize(args ?? Array.Empty<string>());
                    byte[] bytes = Encoding.UTF8.GetBytes(payload);
                    byte[] lengthPrefix = BitConverter.GetBytes(bytes.Length);
                    client.Write(lengthPrefix, 0, lengthPrefix.Length);
                    client.Write(bytes, 0, bytes.Length);
                    client.Flush();
                    return true;
                }
                catch
                {
                    if (attempt < attemptCount)
                        Thread.Sleep(150);
                }
            }

            return false;
        }

        private async Task ListenLoopAsync(NamedPipeServerStream firstServer)
        {
            NamedPipeServerStream? pendingServer = firstServer;
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    using var server = pendingServer ?? CreateServer();
                    pendingServer = null;

                    await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                    string payload = await ReadPayloadAsync(server, _cts.Token).ConfigureAwait(false);
                    string[] args = ParseArgs(payload);
                    _onArgsReceived(args);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    try
                    {
                        await Task.Delay(500, _cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }

            pendingServer?.Dispose();
        }

        private NamedPipeServerStream CreateServer()
        {
            return new NamedPipeServerStream(
                _pipeName,
                PipeDirection.In,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        }

        private static async Task<string> ReadPayloadAsync(Stream stream, CancellationToken cancellationToken)
        {
            byte[] lengthPrefix = new byte[sizeof(int)];
            if (!await ReadExactAsync(stream, lengthPrefix, cancellationToken).ConfigureAwait(false))
                return string.Empty;

            int length = BitConverter.ToInt32(lengthPrefix, 0);
            if (length <= 0 || length > MaxPayloadBytes)
                return string.Empty;

            byte[] bytes = new byte[length];
            if (!await ReadExactAsync(stream, bytes, cancellationToken).ConfigureAwait(false))
                return string.Empty;

            return Encoding.UTF8.GetString(bytes);
        }

        private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await stream
                    .ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                    return false;

                offset += read;
            }

            return true;
        }

        private static string[] ParseArgs(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return Array.Empty<string>();

            try
            {
                return JsonSerializer.Deserialize<string[]>(payload) ?? Array.Empty<string>();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listenTask?.Wait(500); } catch { /* 退出时监听任务可能因取消或管道关闭抛出，最多等待 500ms 后继续释放资源。 */ }
            _cts.Dispose();
        }
    }
}
