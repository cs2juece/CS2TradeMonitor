using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.Core
{
    public static class SupportInfo
    {
        public const string QqGroupNumber = "1057043823";
        public const string QqGroupUrl = SupportUrls.QqGroup;
        public const string GitHubUrl = SupportUrls.GitHubRepository;
        public const string ReleasesUrl = SupportUrls.GitHubReleases;

        public const string GiteeUrl = SupportUrls.GiteeHome;
        public const string GiteeReleasesUrl = SupportUrls.GiteeReleases;
        public const string GiteeLatestJsonUrl = SupportUrls.GiteeLatestJson;
        public const string GiteePackageBaseUrl = SupportUrls.GiteePackageBase;
        public const string CloudflareReleasesUrl = SupportUrls.CloudflareReleases;
        public const string CloudflareLatestJsonUrl = SupportUrls.CloudflareLatestJson;
        public const string CloudflarePackageBaseUrl = SupportUrls.CloudflarePackageBase;
        public const bool GitHubUpdateManifestEnabled = SupportUrls.GitHubUpdateManifestEnabled;

        public sealed class UpdateManifestSource
        {
            public UpdateManifestSource(string name, string url)
            {
                Name = name;
                Url = url;
            }

            public string Name { get; }
            public string Url { get; }
        }

        public static bool HasOwnGitHubRepository =>
            TryGetGitHubRepo(out _, out _);

        public static bool HasGitHubUpdateManifestSource =>
            GitHubUpdateManifestEnabled
            && HasOwnGitHubRepository;

        public static UpdateManifestSource[] GetUpdateManifestSources()
        {
            var sources = new System.Collections.Generic.List<UpdateManifestSource>();
            AddSource(sources, "Gitee 国内", GiteeLatestJsonUrl);

            if (HasGitHubUpdateManifestSource
                && TryGetGitHubRepo(out string owner, out string repo))
            {
                AddSource(sources, "GitHub 官方", SupportUrls.GitHubLatestJson);
                AddSource(sources, "GitHub 官方兼容", SupportUrls.GitHubVersionJson);
                AddSource(sources, "GitHub 过渡兼容",
                    SupportUrls.GitHubLatestDownload(owner, repo, "latest.json"));
            }

            AddSource(sources, "Cloudflare 备用", CloudflareLatestJsonUrl);

            return sources.ToArray();
        }

        public static string[] GetUpdateManifestUrls()
        {
            return Array.ConvertAll(GetUpdateManifestSources(), x => x.Url);
        }

        public static string GetReleaseDownloadPage()
        {
            if (!string.IsNullOrWhiteSpace(GiteeReleasesUrl))
                return GiteeReleasesUrl;
            if (!string.IsNullOrWhiteSpace(GiteeUrl))
                return GiteeUrl;
            if (HasOwnGitHubRepository && !string.IsNullOrWhiteSpace(ReleasesUrl))
                return ReleasesUrl;
            if (!string.IsNullOrWhiteSpace(CloudflareReleasesUrl))
                return CloudflareReleasesUrl;

            return SupportUrls.GitHubHome;
        }

        private static void AddSource(System.Collections.Generic.List<UpdateManifestSource> sources, string name, string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            if (sources.Exists(x => string.Equals(x.Url, url, StringComparison.OrdinalIgnoreCase)))
                return;

            sources.Add(new UpdateManifestSource(name, url.Trim()));
        }

        public static (string Name, string Url, int Priority)[] GetReleaseAssetUrls(string packageFileName, string version)
        {
            var urls = new System.Collections.Generic.List<(string Name, string Url, int Priority)>();

            if (!string.IsNullOrWhiteSpace(GiteePackageBaseUrl))
                urls.Add(("Gitee 国内", CombineUrl(GiteePackageBaseUrl, packageFileName), 1));

            if (HasGitHubUpdateManifestSource
                && TryGetGitHubRepo(out string owner, out string repo))
            {
                urls.Add(("GitHub 官方", SupportUrls.GitHubReleaseAsset(owner, repo, version, packageFileName), 2));
            }

            if (!string.IsNullOrWhiteSpace(CloudflarePackageBaseUrl))
                urls.Add(("Cloudflare 备用", CombineUrl(CloudflarePackageBaseUrl, packageFileName), 3));

            return urls.ToArray();
        }

        private static string CombineUrl(string baseUrl, string fileName)
        {
            return baseUrl.TrimEnd('/') + "/" + fileName.TrimStart('/');
        }

        public static bool TryGetGitHubRepo(out string owner, out string repo)
        {
            owner = "";
            repo = "";

            try
            {
                var uri = new Uri(GitHubUrl);
                var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    return false;

                owner = parts[0];
                repo = parts[1];
                return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repo);
            }
            catch
            {
                return false;
            }
        }

        public static void OpenQqGroup()
        {
            Process.Start(new ProcessStartInfo(QqGroupUrl) { UseShellExecute = true });
        }

        public static void CopyQqGroupNumber()
        {
            Clipboard.SetText(QqGroupNumber);
        }

        public static void ShowCopySuccess(IWin32Window? owner = null)
        {
            GlobalPromptService.Show(owner, $"QQ群号已复制：{QqGroupNumber}", "CS2交易监控", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static void ShowOpenFailure(Exception ex, IWin32Window? owner = null)
        {
            GlobalPromptService.Show(owner, "打开 QQ 群链接失败：" + ex.Message, "CS2交易监控", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
