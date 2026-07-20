namespace CS2TradeMonitor.src.Core
{
    public static class SupportUrls
    {
        public const string QqGroup = "https://qm.qq.com/q/K2AuJdMxG";
        public const string GitHubRepository = "https://github.com/cs2juece/CS2TradeMonitor";
        public const string GitHubReleases = "https://github.com/cs2juece/CS2TradeMonitor/releases";

        // 公开发布由 GitHub 与 Gitee 同批生成并独立校验；Gitee 为国内优先源。
        public const bool GitHubUpdateManifestEnabled = true;
        public const string GiteeHome = "https://gitee.com/cs2juece/CS2TradeMonitor";
        public const string GiteeReleases = "https://gitee.com/cs2juece/CS2TradeMonitor/releases";
        public const string GiteeLatestJson = "https://gitee.com/cs2juece/CS2TradeMonitor/raw/update/latest.json";
        public const string GitHubLatestJson = "https://raw.githubusercontent.com/cs2juece/CS2TradeMonitor/update/latest.json";
        public const string GitHubVersionJson = "https://raw.githubusercontent.com/cs2juece/CS2TradeMonitor/update/version.json";
        // Gitee Release 附件 URL 含服务端分配的附件 ID，由发布工作流写入清单。
        public const string GiteePackageBase = "";
        public const string CloudflareReleases = "";
        public const string CloudflareLatestJson = "";
        public const string CloudflarePackageBase = "";

        public const string GitHubHome = "https://github.com/";

        public static string GitHubLatestDownload(string owner, string repo, string fileName)
            => $"https://github.com/{owner}/{repo}/releases/latest/download/{fileName}";

        public static string GitHubRawResource(string owner, string repo, string branch, string fileName)
            => $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/resources/{fileName}";

        public static string GitHubReleaseAsset(string owner, string repo, string version, string packageFileName)
            => $"https://github.com/{owner}/{repo}/releases/download/v{version}/{packageFileName}";
    }
}
