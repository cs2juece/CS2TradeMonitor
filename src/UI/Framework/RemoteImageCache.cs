using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.SystemServices;
using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class RemoteImageCache
    {
        private readonly HttpClient _http;
        private readonly int _maxBytes;
        private readonly TimeSpan _failureTtl;
        private readonly INetworkRecoverySignal? _recoverySignal;
        private readonly ConcurrentDictionary<string, Image> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Task<Image?>> _tasks = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, DateTime> _failures = new(StringComparer.OrdinalIgnoreCase);
        private long _routeGeneration;

        internal RemoteImageCache(
            HttpClient http,
            int maxBytes,
            TimeSpan failureTtl,
            INetworkRecoverySignal? recoverySignal)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _maxBytes = Math.Max(1, maxBytes);
            _failureTtl = failureTtl <= TimeSpan.Zero ? TimeSpan.FromMinutes(5) : failureTtl;
            _recoverySignal = recoverySignal;
            if (_recoverySignal != null)
                _recoverySignal.Recovered += OnNetworkRecovered;
        }

        public static RemoteImageCache CreateDomestic(int timeoutSeconds, int maxBytes, TimeSpan failureTtl)
        {
            var http = AppServices
                .GetRequiredService<IDomesticHttpClientFactory>()
                .Create(timeoutSeconds, useCookies: false);
            var recoverySignal = AppServices.GetRequiredService<INetworkRecoverySignal>();
            return new RemoteImageCache(http, maxBytes, failureTtl, recoverySignal);
        }

        public bool TryGet(string imageUrl, out Image? image)
        {
            image = null;
            if (string.IsNullOrWhiteSpace(imageUrl))
                return false;

            return _cache.TryGetValue(imageUrl.Trim(), out image);
        }

        public Task<Image?> GetAsync(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                return Task.FromResult<Image?>(null);

            imageUrl = imageUrl.Trim();
            if (_cache.TryGetValue(imageUrl, out var cached))
                return Task.FromResult<Image?>(cached);

            if (IsFailureFresh(imageUrl))
                return Task.FromResult<Image?>(null);

            return GetOrLoadAsync(imageUrl);
        }

        private async Task<Image?> GetOrLoadAsync(string imageUrl)
        {
            Task<Image?> task = _tasks.GetOrAdd(imageUrl, LoadCoreAsync);
            try
            {
                return await task.ConfigureAwait(false);
            }
            finally
            {
                ((ICollection<KeyValuePair<string, Task<Image?>>>)_tasks)
                    .Remove(new KeyValuePair<string, Task<Image?>>(imageUrl, task));
            }
        }

        private async Task<Image?> LoadCoreAsync(string imageUrl)
        {
            long generation = Interlocked.Read(ref _routeGeneration);
            try
            {
                byte[] bytes = await _http.GetByteArrayAsync(imageUrl).ConfigureAwait(false);
                if (bytes.Length == 0 || bytes.Length > _maxBytes)
                {
                    MarkFailed(imageUrl, generation);
                    return null;
                }

                using var stream = new MemoryStream(bytes);
                using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
                var bitmap = new Bitmap(image);
                _cache[imageUrl] = bitmap;
                _failures.TryRemove(imageUrl, out _);
                return bitmap;
            }
            catch
            {
                MarkFailed(imageUrl, generation);
                return null;
            }
        }

        private bool IsFailureFresh(string imageUrl)
        {
            if (!_failures.TryGetValue(imageUrl, out var failedAt))
                return false;

            if (DateTime.UtcNow - failedAt <= _failureTtl)
                return true;

            _failures.TryRemove(imageUrl, out _);
            return false;
        }

        private void MarkFailed(string imageUrl, long generation)
        {
            if (generation == Interlocked.Read(ref _routeGeneration) && !string.IsNullOrWhiteSpace(imageUrl))
                _failures[imageUrl] = DateTime.UtcNow;
        }

        private void OnNetworkRecovered()
        {
            Interlocked.Increment(ref _routeGeneration);
            _failures.Clear();
        }
    }
}
