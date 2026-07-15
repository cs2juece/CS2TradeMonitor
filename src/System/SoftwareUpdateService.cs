using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Infrastructure.Paths;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.UpdateSecurity;

namespace CS2TradeMonitor.src.SystemServices
{
    public sealed class SoftwareUpdateService : ISoftwareUpdateService
    {
        public static SoftwareUpdateService Instance { get; } = new();

        private const string Source = "SoftwareUpdate";
        private const string AppName = "CS2TradeMonitor";
        private const string UpdaterExeName = "CS2TradeMonitor.Updater.exe";
        private static readonly string UpdaterRelativePath = Path.Combine("app", UpdaterExeName);
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
        private readonly SemaphoreSlim _checkLock = new(1, 1);
        private readonly HttpClient _http;
        private readonly TimeSpan _downloadInactivityTimeout;

        private SoftwareUpdateService()
            : this(SoftwareUpdateRuntimeServices.ResolveDomesticHttpFactory())
        {
        }

        internal SoftwareUpdateService(
            IDomesticHttpClientFactory httpFactory,
            TimeSpan? downloadInactivityTimeout = null)
        {
            if (httpFactory == null) throw new ArgumentNullException(nameof(httpFactory));

            _http = httpFactory.Create(10);
            _downloadInactivityTimeout = downloadInactivityTimeout ?? TimeSpan.FromSeconds(15);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CS2TradeMonitor-Updater/1.0");
        }

        public async Task<SoftwareUpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            await _checkLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string currentVersion = GetCurrentVersion();
                var manifestSources = SupportInfo.GetUpdateManifestSources();
                if (manifestSources.Length == 0)
                {
                    return new SoftwareUpdateCheckResult
                    {
                        State = SoftwareUpdateState.Disabled,
                        CurrentVersion = currentVersion,
                        Message = "自动更新未启用：当前没有配置可用更新源，请从发布页手动获取新版。"
                    };
                }

                var manifestResult = await FetchManifestAsync(manifestSources, cancellationToken).ConfigureAwait(false);
                if (manifestResult.Manifest == null)
                {
                    return new SoftwareUpdateCheckResult
                    {
                        State = SoftwareUpdateState.Failed,
                        CurrentVersion = currentVersion,
                        Message = manifestResult.Error,
                        ManifestUrl = manifestResult.ManifestUrl,
                        ManifestSourceName = manifestResult.SourceName
                    };
                }

                var manifest = manifestResult.Manifest;
                var asset = manifest.Assets.WinX64;
                if (string.IsNullOrWhiteSpace(manifest.Version))
                    throw new InvalidDataException("latest.json / version.json 缺少 version 字段。");

                if (!string.IsNullOrWhiteSpace(manifest.MinSupportedVersion)
                    && CompareVersions(currentVersion, manifest.MinSupportedVersion) < 0)
                {
                    return new SoftwareUpdateCheckResult
                    {
                        State = SoftwareUpdateState.Failed,
                        CurrentVersion = currentVersion,
                        Manifest = manifest,
                        ManifestUrl = manifestResult.ManifestUrl,
                        ManifestSourceName = manifestResult.SourceName,
                        Message = $"当前版本 v{currentVersion} 低于最低支持版本 v{manifest.MinSupportedVersion}，请手动下载安装。"
                    };
                }

                if (CompareVersions(manifest.Version, currentVersion) <= 0)
                {
                    return new SoftwareUpdateCheckResult
                    {
                        State = SoftwareUpdateState.Latest,
                        CurrentVersion = currentVersion,
                        Manifest = manifest,
                        Asset = asset,
                        ManifestUrl = manifestResult.ManifestUrl,
                        ManifestSourceName = manifestResult.SourceName,
                        Message = $"当前已是最新版本 v{currentVersion}。更新源：{manifestResult.SourceName}。"
                    };
                }

                var downloadSources = asset.GetDownloadSources();
                if (downloadSources.Count == 0)
                {
                    return new SoftwareUpdateCheckResult
                    {
                        State = SoftwareUpdateState.Failed,
                        CurrentVersion = currentVersion,
                        Manifest = manifest,
                        Asset = asset,
                        ManifestUrl = manifestResult.ManifestUrl,
                        ManifestSourceName = manifestResult.SourceName,
                        Message = "发现新版本，但没有可用下载源。"
                    };
                }

                var firstDownload = downloadSources[0];

                return new SoftwareUpdateCheckResult
                {
                    State = SoftwareUpdateState.Available,
                    CurrentVersion = currentVersion,
                    Manifest = manifest,
                    Asset = asset,
                    ManifestUrl = manifestResult.ManifestUrl,
                    ManifestSourceName = manifestResult.SourceName,
                    DownloadUrl = firstDownload.Url,
                    DownloadSourceName = firstDownload.DisplayName,
                    DownloadSources = downloadSources,
                    Message = $"发现新版本 v{manifest.Version}。更新源：{manifestResult.SourceName}。"
                };
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error(Source, "Checking software update failed.", ex);
                return new SoftwareUpdateCheckResult
                {
                    State = SoftwareUpdateState.Failed,
                    CurrentVersion = GetCurrentVersion(),
                    Message = "检查更新失败：" + GetFriendlyError(ex)
                };
            }
            finally
            {
                _checkLock.Release();
            }
        }

        public async Task<SoftwareUpdateDownloadResult> DownloadAsync(
            SoftwareUpdateCheckResult update,
            IProgress<SoftwareUpdateProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            if (!update.HasUpdate || update.Asset == null || update.Manifest == null)
                throw new InvalidOperationException("没有可下载的更新。");

            ValidateAssetMetadata(update.Asset);

            var downloadSources = (update.DownloadSources.Count > 0
                    ? update.DownloadSources
                    : update.Asset.GetDownloadSources())
                .Where(x => !string.IsNullOrWhiteSpace(x.Url))
                .ToArray();

            if (downloadSources.Length == 0)
                throw new InvalidOperationException("更新包下载地址为空。");

            string updatesDir = RuntimeDataPaths.UpdatesDirectory;
            Directory.CreateDirectory(updatesDir);

            Exception? lastError = null;
            foreach (var source in downloadSources)
            {
                string zipPath = GetUpdateZipPath(updatesDir, source.Url, update.Manifest.Version);
                string tempPath = zipPath + ".download";
                try
                {
                    return await DownloadFromSourceAsync(
                        source,
                        update.Asset,
                        zipPath,
                        tempPath,
                        progress,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    CleanupFailedDownload(zipPath, tempPath);
                    throw;
                }
                catch (Exception ex)
                {
                    CleanupFailedDownload(zipPath, tempPath);
                    lastError = ex;
                    DiagnosticsLogger.Info(Source, $"Download source failed: {source.DisplayName} -> {GetFriendlyError(ex)}");
                }
            }

            throw new InvalidOperationException("所有更新下载源都失败：" + (lastError == null ? "未知错误" : GetFriendlyError(lastError)));
        }

        private async Task<SoftwareUpdateDownloadResult> DownloadFromSourceAsync(
            SoftwareUpdateDownloadSource downloadSource,
            SoftwareUpdateAsset asset,
            string zipPath,
            string tempPath,
            IProgress<SoftwareUpdateProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            var sw = Stopwatch.StartNew();
            long readTotal = 0;
            long lastBytes = 0;
            long lastMs = 0;

            using var response = await _http.GetAsync(downloadSource.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var dest = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                while (true)
                {
                    int read;
                    using (var inactivityCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        inactivityCts.CancelAfter(_downloadInactivityTimeout);
                        try
                        {
                            read = await source.ReadAsync(
                                buffer.AsMemory(0, buffer.Length),
                                inactivityCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            int seconds = Math.Max(1, (int)Math.Ceiling(_downloadInactivityTimeout.TotalSeconds));
                            throw new TimeoutException($"下载长时间无响应（{seconds} 秒未收到数据）");
                        }
                    }

                    if (read <= 0)
                        break;

                    await dest.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    readTotal += read;

                    long now = sw.ElapsedMilliseconds;
                    if (now - lastMs >= 200)
                    {
                        double bps = (readTotal - lastBytes) * 1000.0 / Math.Max(1, now - lastMs);
                        progress?.Report(new SoftwareUpdateProgress
                        {
                            BytesReceived = readTotal,
                            TotalBytes = total,
                            BytesPerSecond = bps,
                            SourceUrl = downloadSource.Url,
                            SourceName = downloadSource.DisplayName
                        });
                        lastBytes = readTotal;
                        lastMs = now;
                    }
                }
            }

            if (File.Exists(zipPath))
                File.Delete(zipPath);
            File.Move(tempPath, zipPath);

            ValidateDownloadedPackage(zipPath, asset);
            var sha = ComputeSha256(zipPath);
            progress?.Report(new SoftwareUpdateProgress
            {
                BytesReceived = readTotal,
                TotalBytes = total ?? readTotal,
                BytesPerSecond = 0,
                SourceUrl = downloadSource.Url,
                SourceName = downloadSource.DisplayName
            });

            return new SoftwareUpdateDownloadResult
            {
                ZipPath = zipPath,
                SourceUrl = downloadSource.Url,
                SizeBytes = new FileInfo(zipPath).Length,
                Sha256 = sha
            };
        }

        private static string GetUpdateZipPath(string updatesDir, string downloadUrl, string version)
        {
            string fileName;
            try
            {
                fileName = Path.GetFileName(new Uri(downloadUrl).AbsolutePath);
            }
            catch
            {
                fileName = "";
            }

            if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                fileName = $"{AppName}_v{version}-win-x64.zip";

            return Path.Combine(updatesDir, fileName);
        }

        private static void CleanupFailedDownload(string zipPath, string tempPath)
        {
            TryDelete(tempPath);
            TryDelete(zipPath);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Cleanup failure should not hide the real download error.
            }
        }

        public void LaunchUpdater(SoftwareUpdateDownloadResult download)
        {
            if (string.IsNullOrWhiteSpace(download.ZipPath) || !File.Exists(download.ZipPath))
                throw new FileNotFoundException("更新包不存在。", download.ZipPath);

            InstanceRuntimeContext instance = InstanceRuntimeContext.Current;
            string installDir = instance.InstallRoot;
            string exePath = Path.Combine(installDir, AppName + ".exe");
            string zipPath = Path.GetFullPath(download.ZipPath);
            if (!IsPathInside(zipPath, instance.UpdatesDirectory))
                throw new InvalidDataException("更新包不属于当前目录实例。");
            if (!File.Exists(exePath))
                throw new FileNotFoundException("找不到当前目录实例启动器。", exePath);
            string updaterPath = Path.Combine(installDir, UpdaterRelativePath);
            if (!File.Exists(updaterPath))
                throw new FileNotFoundException("找不到独立更新器，请重新下载完整安装包。", updaterPath);

            string tempUpdaterDir = Path.Combine(
                Path.GetTempPath(),
                AppName,
                instance.InstanceHash,
                "updater",
                Environment.ProcessId + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempUpdaterDir);
            string tempUpdaterPath = Path.Combine(tempUpdaterDir, UpdaterExeName);
            File.Copy(updaterPath, tempUpdaterPath, true);

            var startInfo = new ProcessStartInfo
            {
                FileName = tempUpdaterPath,
                WorkingDirectory = tempUpdaterDir,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("--zip");
            startInfo.ArgumentList.Add(zipPath);
            startInfo.ArgumentList.Add("--install-dir");
            startInfo.ArgumentList.Add(installDir);
            startInfo.ArgumentList.Add("--launcher");
            startInfo.ArgumentList.Add(exePath);
            startInfo.ArgumentList.Add("--instance-hash");
            startInfo.ArgumentList.Add(instance.InstanceHash);
            startInfo.ArgumentList.Add("--pid");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("--restart");

            Process.Start(startInfo);
            DiagnosticsLogger.Info(Source, "Updater launched.");
            System.Windows.Forms.Application.Exit();
            Environment.Exit(0);
        }

        private async Task<(SoftwareUpdateManifest? Manifest, string ManifestUrl, string SourceName, string Error)> FetchManifestAsync(
            IReadOnlyList<SupportInfo.UpdateManifestSource> sources,
            CancellationToken cancellationToken)
        {
            string lastError = "";
            foreach (var source in sources)
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(6));
                    using var response = await _http.GetAsync(source.Url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        lastError = $"{source.Name} 返回 HTTP {(int)response.StatusCode}";
                        continue;
                    }

                    await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
                    var manifest = await JsonSerializer.DeserializeAsync<SoftwareUpdateManifest>(stream, JsonOptions, cts.Token).ConfigureAwait(false);
                    if (manifest == null)
                    {
                        lastError = $"{source.Name} 返回空 manifest";
                        continue;
                    }

                    return (manifest, source.Url, source.Name, "");
                }
                catch (Exception ex)
                {
                    lastError = $"{source.Name}：{GetFriendlyError(ex)}";
                    DiagnosticsLogger.Info(Source, $"Manifest source failed: {source.Name} -> {GetFriendlyError(ex)}");
                }
            }

            return (null, "", "", "无法连接更新源：" + lastError);
        }

        private static void ValidateDownloadedPackage(string zipPath, SoftwareUpdateAsset asset)
        {
            var info = new FileInfo(zipPath);
            if (!info.Exists || info.Length <= 0)
                throw new InvalidDataException("更新包下载为空。");

            if (info.Length != asset.SizeBytes)
                throw new InvalidDataException("更新包大小与 manifest 不一致。");

            string actualSha = ComputeSha256(zipPath);
            if (!string.Equals(actualSha, asset.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("更新包 SHA256 校验失败，已拒绝安装。");
            }

            using var archive = ZipFile.OpenRead(zipPath);
            var entries = archive.Entries.Where(e => !string.IsNullOrWhiteSpace(e.FullName)).ToArray();
            if (entries.Length == 0)
                throw new InvalidDataException("更新包为空。");

            foreach (var entry in entries)
            {
                string name = entry.FullName.Replace('\\', '/');
                string[] segments = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (name.StartsWith("/", StringComparison.Ordinal)
                    || Path.IsPathRooted(name)
                    || segments.Any(segment => segment is "." or ".."))
                    throw new InvalidDataException("更新包包含非法路径。");

                string fileName = Path.GetFileName(name);
                if (UpdatePackageFilePolicy.IsForbidden(name))
                    throw new InvalidDataException("更新包包含不应发布的文件：" + fileName);
            }

            var normalized = entries.Select(e => e.FullName.Replace('\\', '/')).ToArray();
            string? root = ResolveArchiveRoot(normalized);
            string prefix = string.IsNullOrWhiteSpace(root) ? "" : root.TrimEnd('/') + "/";
            bool hasLauncher = normalized.Any(x => string.Equals(x, prefix + AppName + ".exe", StringComparison.OrdinalIgnoreCase));
            bool hasAppHost = normalized.Any(x =>
                string.Equals(x, prefix + "app/" + AppName + ".exe", StringComparison.OrdinalIgnoreCase));
            bool hasAppDll = normalized.Any(x =>
                string.Equals(x, prefix + "app/" + AppName + ".dll", StringComparison.OrdinalIgnoreCase));
            bool hasUpdater = normalized.Any(x =>
                string.Equals(x, prefix + "app/" + UpdaterExeName, StringComparison.OrdinalIgnoreCase));
            if (!hasLauncher || !hasAppHost || !hasAppDll || !hasUpdater)
                throw new InvalidDataException("更新包缺少启动器、主程序 AppHost/DLL 或独立更新器。");

            string manifestName = prefix + ProgramFileManifestPolicy.RelativePath;
            ZipArchiveEntry? manifestEntry = entries.FirstOrDefault(entry =>
                string.Equals(entry.FullName.Replace('\\', '/'), manifestName, StringComparison.OrdinalIgnoreCase));
            if (manifestEntry == null)
                throw new InvalidDataException("更新包缺少程序文件清单。");

            string manifestJson;
            using (StreamReader reader = new(manifestEntry.Open()))
                manifestJson = reader.ReadToEnd();
            string[] actualFiles = entries
                .Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal))
                .Select(entry => entry.FullName.Replace('\\', '/'))
                .Select(name => prefix.Length == 0 ? name : name[prefix.Length..])
                .ToArray();
            _ = ProgramFileManifestPolicy.ParseAndValidate(manifestJson, actualFiles);
        }

        private static void ValidateAssetMetadata(SoftwareUpdateAsset asset)
        {
            if (asset.SizeBytes <= 0)
                throw new InvalidDataException("更新清单缺少有效的安装包大小，已拒绝下载。");

            string sha256 = (asset.Sha256 ?? "").Trim();
            if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("更新清单缺少有效的 SHA256，已拒绝下载。");
        }

        private static string? ResolveArchiveRoot(IReadOnlyList<string> names)
        {
            var firstSegments = names
                .Select(x => x.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return firstSegments.Length == 1 && names.All(x => x.Contains('/')) ? firstSegments[0] : "";
        }

        private static bool IsPathInside(string path, string directory)
        {
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetCurrentVersion()
        {
            string? informational = Assembly.GetEntryAssembly()
                ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
                return informational.Split('+')[0].Trim();

            return System.Windows.Forms.Application.ProductVersion.Split('+')[0].Trim();
        }

        private static int CompareVersions(string left, string right)
        {
            if (!Version.TryParse(NormalizeVersion(left), out var a))
                a = new Version(0, 0);
            if (!Version.TryParse(NormalizeVersion(right), out var b))
                b = new Version(0, 0);

            return a.CompareTo(b);
        }

        private static string NormalizeVersion(string version)
        {
            version = (version ?? "").Trim().TrimStart('v', 'V');
            int dash = version.IndexOf('-');
            if (dash >= 0)
                version = version[..dash];
            return string.IsNullOrWhiteSpace(version) ? "0.0" : version;
        }

        private static string ComputeSha256(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return $"{bytes / 1024d / 1024d / 1024d:F2} GB";
            if (bytes >= 1024L * 1024)
                return $"{bytes / 1024d / 1024d:F2} MB";
            if (bytes >= 1024L)
                return $"{bytes / 1024d:F1} KB";
            return $"{bytes} B";
        }

        public static string GetFriendlyError(Exception ex)
        {
            return ex switch
            {
                OperationCanceledException => "操作已取消或网络超时",
                HttpRequestException => "网络请求失败，请稍后再试",
                JsonException => "更新源格式异常",
                InvalidDataException => ex.Message,
                _ => ex.Message
            };
        }
    }
}
