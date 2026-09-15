using System.Globalization;
using System.Text.RegularExpressions;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Links
{
    /// <summary>
    /// Parses a YouPin H5 shop link without retaining share credentials.
    /// </summary>
    public static class YouPinShopLinkParser
    {
        public const string OfficialHost = "hybrid.youpin898.com";
        public const string NormalBuyTargetFragment = "/web/h5/fragment/normalBuy";

        private static readonly Regex ShopPathPattern = new(
            "^/shop/(?<userId>[1-9][0-9]*)/?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static YouPinShopLinkInfo Parse(string rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl))
                throw new ArgumentException("悠悠店铺链接不能为空。", nameof(rawUrl));

            string normalizedUrl = rawUrl.Trim().Replace("\\&", "&", StringComparison.Ordinal);
            if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out Uri? uri))
                throw InvalidLink();

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(uri.Host, OfficialHost, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("仅支持悠悠官方 HTTPS 店铺链接。", nameof(rawUrl));
            }

            string fragment = uri.Fragment.TrimStart('#');
            int queryStart = fragment.IndexOf('?');
            string shopPath = queryStart >= 0 ? fragment[..queryStart] : fragment;
            string query = queryStart >= 0 ? fragment[(queryStart + 1)..] : string.Empty;

            Match match = ShopPathPattern.Match(shopPath);
            if (!match.Success
                || !long.TryParse(
                    match.Groups["userId"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out long userId))
            {
                throw InvalidLink();
            }

            FragmentQueryInfo queryInfo = ReadSafeQueryInfo(query);
            string sanitizedUrl = BuildSanitizedUrl(userId, queryInfo);

            return new YouPinShopLinkInfo(
                userId,
                OfficialHost,
                queryInfo.HasAuthSign,
                queryInfo.IsSharePage,
                queryInfo.TargetFragment,
                sanitizedUrl);
        }

        private static FragmentQueryInfo ReadSafeQueryInfo(string query)
        {
            bool hasAuthSign = false;
            bool isSharePage = false;
            string? targetFragment = null;

            foreach (string component in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int equalsIndex = component.IndexOf('=');
                string encodedKey = equalsIndex >= 0 ? component[..equalsIndex] : component;
                string encodedValue = equalsIndex >= 0 ? component[(equalsIndex + 1)..] : string.Empty;
                string key = Uri.UnescapeDataString(encodedKey);

                if (string.Equals(key, "authSign", StringComparison.OrdinalIgnoreCase))
                {
                    hasAuthSign = true;
                    continue;
                }

                if (string.Equals(key, "isSharePage", StringComparison.OrdinalIgnoreCase))
                {
                    isSharePage = string.Equals(encodedValue, "1", StringComparison.Ordinal);
                    continue;
                }

                if (string.Equals(key, "targetFragment", StringComparison.OrdinalIgnoreCase))
                {
                    string candidate = Uri.UnescapeDataString(encodedValue);
                    if (string.Equals(candidate, NormalBuyTargetFragment, StringComparison.Ordinal))
                        targetFragment = candidate;
                }
            }

            return new FragmentQueryInfo(hasAuthSign, isSharePage, targetFragment);
        }

        private static string BuildSanitizedUrl(long userId, FragmentQueryInfo queryInfo)
        {
            string safeUrl = $"https://{OfficialHost}/index.html#/shop/{userId}";
            var parameters = new List<string>(capacity: 2);

            if (queryInfo.IsSharePage)
                parameters.Add("isSharePage=1");

            if (queryInfo.TargetFragment is not null)
            {
                parameters.Add(
                    "targetFragment=" + Uri.EscapeDataString(queryInfo.TargetFragment));
            }

            return parameters.Count == 0
                ? safeUrl
                : safeUrl + "?" + string.Join("&", parameters);
        }

        private static ArgumentException InvalidLink()
            => new("无法识别悠悠店铺链接。", "rawUrl");

        private sealed record FragmentQueryInfo(
            bool HasAuthSign,
            bool IsSharePage,
            string? TargetFragment);
    }
}
