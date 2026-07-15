using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.Application.Steam.Auth;
using CS2TradeMonitor.Domain.Steam;
using CS2TradeMonitor.src.SystemServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CS2TradeMonitor.src.UI.Framework.SteamOffers
{
    internal sealed class SteamOfferPagePresenter
    {
        private readonly ISteamOfferService _steamOffers;
        private readonly ISteamAuthStore _steamAuthStore;
        private readonly ISteamTokenVault _steamTokenVault;
        private readonly ISteamLoginService _steamLogin;
        private readonly ISteamConnectionResolver _steamConnection;

        public SteamOfferPagePresenter()
            : this(SteamOfferPageRuntimeServices.Resolve())
        {
        }

        internal SteamOfferPagePresenter(SteamOfferPageRuntimeServices runtimeServices)
            : this(
                runtimeServices.SteamOffers,
                runtimeServices.SteamAuthStore,
                runtimeServices.SteamTokenVault,
                runtimeServices.SteamLogin,
                runtimeServices.SteamConnection)
        {
        }

        internal SteamOfferPagePresenter(
            ISteamOfferService steamOffers,
            ISteamAuthStore steamAuthStore,
            ISteamTokenVault steamTokenVault,
            ISteamLoginService steamLogin,
            ISteamConnectionResolver steamConnection)
        {
            _steamOffers = steamOffers ?? throw new ArgumentNullException(nameof(steamOffers));
            _steamAuthStore = steamAuthStore ?? throw new ArgumentNullException(nameof(steamAuthStore));
            _steamTokenVault = steamTokenVault ?? throw new ArgumentNullException(nameof(steamTokenVault));
            _steamLogin = steamLogin ?? throw new ArgumentNullException(nameof(steamLogin));
            _steamConnection = steamConnection ?? throw new ArgumentNullException(nameof(steamConnection));
        }

        public event Action? OfferDataUpdated
        {
            add => _steamOffers.DataUpdated += value;
            remove => _steamOffers.DataUpdated -= value;
        }

        public event Action? ConnectionStatusChanged
        {
            add => _steamConnection.StatusChanged += value;
            remove => _steamConnection.StatusChanged -= value;
        }

        public SteamOfferState GetOfferState() => _steamOffers.GetState();

        public SteamOfferSummaryViewModel GetOfferSummary()
        {
            return BuildOfferSummary(_steamOffers.GetState());
        }

        public SteamOfferListViewModel GetOfferListView()
        {
            return BuildOfferListView(_steamOffers.GetState());
        }

        public SteamConnectionProfile GetConnectionSnapshot() => _steamConnection.GetSnapshot();

        public SteamConnectionStatusViewModel GetConnectionStatus()
        {
            return BuildConnectionStatus(
                _steamConnection.GetSnapshot(),
                DateTime.Now,
                SteamEndpointConnectionManager.Instance.GetLatestSnapshot());
        }

        public string FormatConnectionRoute(SteamConnectionProfile profile)
        {
            return BuildSteamConnectionRouteText(
                profile,
                SteamEndpointConnectionManager.Instance.GetLatestSnapshot());
        }

        public Task<SteamConnectionProfile> ResolveConnectionAsync(bool force, CancellationToken cancellationToken)
        {
            return _steamConnection.ResolveAsync(force, cancellationToken);
        }

        public bool SaveManualProxy(string proxyUri, out string message)
        {
            return _steamConnection.SaveManualProxy(proxyUri, out message);
        }

        public string GetManualProxyDisplay() => _steamConnection.GetManualProxyDisplay();

        public void ApplyAutoConfirmSettings(int intervalSeconds, bool enabled, bool autoAccept, bool allowYouPinVerified)
        {
            if (enabled)
                _steamOffers.StartAutoConfirm(intervalSeconds, autoAccept, allowYouPinVerified);
            else
                _steamOffers.StopAutoConfirm();
        }

        public void ApplyAutoTradeSettings(SteamAutoTradeSettings settings)
        {
            _steamOffers.StartAutoTrade(settings);
        }

        public void RecordAutoTradeAction(SteamAutoTradeRecord record)
        {
            _steamOffers.RecordAutoTradeAction(record);
        }

        public SteamTokenBarSnapshot GetTokenBarSnapshot()
        {
            var steamTokens = _steamAuthStore.GetAllTokens()
                .Where(t => string.Equals(t.Platform, "Steam", StringComparison.OrdinalIgnoreCase))
                .ToList();
            string defaultTokenId = _steamTokenVault.GetDefaultSteamTokenId();
            var defaultToken = steamTokens.FirstOrDefault(t => string.Equals(t.Id, defaultTokenId, StringComparison.Ordinal));
            var visibleTokens = defaultToken != null
                ? new List<SteamTokenEntry> { defaultToken }
                : steamTokens.Take(1).ToList();

            return new SteamTokenBarSnapshot(visibleTokens, defaultTokenId);
        }

        public string FormatTokenComboText(SteamTokenEntry token)
        {
            return BuildTokenComboText(token);
        }

        public SteamTokenCodeDisplayViewModel GetTokenCodeDisplay(SteamTokenEntry? token, bool codeVisible)
        {
            long localNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long steamNow = token != null && string.Equals(token.Platform, "Steam", StringComparison.OrdinalIgnoreCase)
                ? _steamOffers.GetCorrectedSteamTimeSeconds()
                : localNow;
            return BuildTokenCodeDisplay(token, steamNow, localNow, codeVisible);
        }

        public SteamTokenSessionStatusViewModel GetTokenSessionStatus(SteamTokenEntry token)
        {
            return BuildTokenSessionStatus(token);
        }

        public string GetDefaultTokenId() => _steamTokenVault.GetDefaultSteamTokenId();

        public void SetDefaultToken(string tokenId) => _steamAuthStore.SetDefaultToken(tokenId);

        public Task<string> RefreshPersonaNameAsync(SteamTokenEntry token) => _steamLogin.RefreshPersonaNameAsync(token);

        public Task SyncSteamTimeOffsetAsync() => _steamOffers.SyncSteamTimeOffsetAsync();

        public long GetCorrectedSteamTimeSeconds() => _steamOffers.GetCorrectedSteamTimeSeconds();

        public Task<SteamOfferActionResult> LoadOffersAsync(bool useMock) => _steamOffers.LoadOffersAsync(useMock);

        public Task<SteamOfferActionResult> AcceptSafeOffersAsync(bool allowYouPinVerified)
        {
            return _steamOffers.AcceptSafeOffersAsync(allowYouPinVerified);
        }

        public Task<SteamOfferActionResult> AcceptOfferAsync(string tradeOfferId, bool requireSafe)
        {
            return _steamOffers.AcceptOfferAsync(tradeOfferId, requireSafe);
        }

        public Task<SteamOfferActionResult> DenyOfferAsync(string tradeOfferId)
        {
            return _steamOffers.DenyOfferAsync(tradeOfferId);
        }

        public void ClearCredentials() => _steamOffers.ClearCredentials();

        public SteamOfferActionResult ClearTokenSecrets() => _steamOffers.ClearTokenSecrets();

        public SteamOfferActionResult ClearLoginState() => _steamOffers.ClearLoginState();

        public SteamOfferActionResult UpdateSession(
            string sessionId,
            string steamLoginSecure,
            string steamLogin = "",
            string apiKey = "",
            string accessToken = "",
            string refreshToken = "",
            string steamId = "")
        {
            return _steamOffers.UpdateSession(
                sessionId,
                steamLoginSecure,
                steamLogin,
                apiKey,
                accessToken,
                refreshToken,
                steamId);
        }

        internal static SteamConnectionStatusViewModel BuildConnectionStatus(
            SteamConnectionProfile profile,
            DateTime now,
            SteamEndpointConnectionSnapshot? endpoint = null)
        {
            string technicalRoute = BuildSteamConnectionRouteText(profile, endpoint);
            string suffix = "";
            if (!string.IsNullOrWhiteSpace(profile.FailureReason))
                suffix = "；原因：" + profile.FailureReason;
            if (profile.CooldownUntil > now)
                suffix += "；冷却至 " + profile.CooldownUntil.ToString("HH:mm:ss");

            bool endpointRecovering = profile.Mode == SteamConnectionMode.Direct
                && endpoint is not null
                && !string.IsNullOrWhiteSpace(endpoint.FailureReason);
            var tone = profile.Mode switch
            {
                SteamConnectionMode.Failed => SteamConnectionStatusTone.Warning,
                SteamConnectionMode.Unknown => SteamConnectionStatusTone.Muted,
                SteamConnectionMode.Direct when endpointRecovering => SteamConnectionStatusTone.Warning,
                _ => SteamConnectionStatusTone.Success
            };

            var display = BuildSteamConnectionDisplayText(profile, endpoint);
            string label = profile.Mode == SteamConnectionMode.Failed
                ? display.LabelText + suffix
                : display.LabelText;
            string detail = "连接路径：" + technicalRoute + suffix + BuildEndpointDiagnosticText(profile, endpoint);

            return new SteamConnectionStatusViewModel(label, display.RouteText, tone, detail, detail);
        }

        internal static string BuildTokenComboText(SteamTokenEntry token)
        {
            string platform = FirstText(token.Platform, "Token");
            if (string.Equals(platform, "Steam", StringComparison.OrdinalIgnoreCase))
            {
                string name = FirstText(token.PersonaName, token.LoginAccountName, token.AccountName);
                string id = (token.SteamId ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name))
                    name = string.IsNullOrWhiteSpace(id) ? "未命名令牌" : SteamAuthSecureStore.MaskSteamId(id);

                if (!string.IsNullOrWhiteSpace(id)
                    && !string.Equals(name, id, StringComparison.Ordinal)
                    && !string.Equals(name, SteamAuthSecureStore.MaskSteamId(id), StringComparison.Ordinal))
                {
                    name += $"  {SteamAuthSecureStore.MaskSteamId(id)}";
                }

                return $"Steam  {name}";
            }
            return $"{platform}  {FirstText(token.AccountName, "未命名令牌")}";
        }

        internal static SteamTokenCodeDisplayViewModel BuildTokenCodeDisplay(
            SteamTokenEntry? token,
            long correctedSteamNow,
            long localNow,
            bool codeVisible)
        {
            if (token == null)
            {
                return new SteamTokenCodeDisplayViewModel(
                    "------",
                    false,
                    "显示",
                    0,
                    "未保存令牌",
                    SteamTokenSessionStatusTone.Warning);
            }

            try
            {
                string code;
                int secondsLeft;
                if (string.Equals(token.Platform, "Steam", StringComparison.OrdinalIgnoreCase))
                {
                    secondsLeft = 30 - (int)(correctedSteamNow % 30);
                    if (!SteamCryptoHelper.TryValidateSteamGuardSharedSecret(token.SharedSecret, out string validationMessage))
                    {
                        return new SteamTokenCodeDisplayViewModel(
                            "shared_secret 格式无效",
                            false,
                            "显示",
                            secondsLeft,
                            validationMessage,
                            SteamTokenSessionStatusTone.Warning);
                    }

                    code = SteamCryptoHelper.GenerateSteamGuardCode(token.SharedSecret, correctedSteamNow);
                }
                else if (string.Equals(token.Platform, "HOTP", StringComparison.OrdinalIgnoreCase))
                {
                    secondsLeft = 0;
                    code = SteamCryptoHelper.GenerateHotpCode(token.SharedSecret, token.HotpCounter);
                }
                else
                {
                    secondsLeft = 30 - (int)(localNow % 30);
                    code = SteamCryptoHelper.GenerateTotpCode(token.SharedSecret, localNow);
                }

                var sessionStatus = BuildTokenSessionStatus(token);
                return new SteamTokenCodeDisplayViewModel(
                    codeVisible ? code : "•••••",
                    true,
                    codeVisible ? "隐藏" : "显示",
                    secondsLeft,
                    sessionStatus.Text,
                    sessionStatus.Tone);
            }
            catch (FormatException ex)
            {
                return new SteamTokenCodeDisplayViewModel(
                    "shared_secret 格式无效",
                    false,
                    "显示",
                    0,
                    ex.Message,
                    SteamTokenSessionStatusTone.Warning);
            }
            catch
            {
                var sessionStatus = BuildTokenSessionStatus(token);
                return new SteamTokenCodeDisplayViewModel(
                    "ERROR",
                    false,
                    "显示",
                    0,
                    sessionStatus.Text,
                    sessionStatus.Tone);
            }
        }

        internal static SteamOfferSummaryViewModel BuildOfferSummary(SteamOfferState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            string text =
                $"状态：{SteamOfferDisplayFormatter.CompactStatusText(state.LastStatus)}  上次：{SteamOfferDisplayFormatter.FormatTime(state.LastRefresh)}  待处理：{state.Offers.Count} 条";
            var tone = string.IsNullOrWhiteSpace(state.LastError)
                ? SteamOfferSummaryTone.Success
                : SteamOfferSummaryTone.Warning;

            return new SteamOfferSummaryViewModel(text, tone, state.LastRefresh);
        }

        internal static SteamOfferListViewModel BuildOfferListView(SteamOfferState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            IReadOnlyList<SteamOfferItem> safeOffers = SortOffers(state.Offers.Where(o => o.RiskLevel == SteamOfferRisk.SafeIncoming));
            IReadOnlyList<SteamOfferItem> verifiedOffers = SortOffers(state.Offers.Where(o => o.RiskLevel == SteamOfferRisk.YouPinVerified));
            IReadOnlyList<SteamOfferItem> riskOffers = SortOffers(state.Offers.Where(o => o.RiskLevel == SteamOfferRisk.Unverified));
            bool hasOffers = safeOffers.Count > 0 || verifiedOffers.Count > 0 || riskOffers.Count > 0;

            return new SteamOfferListViewModel(
                BuildOfferListSignature(state),
                safeOffers,
                verifiedOffers,
                riskOffers,
                SteamOfferDisplayFormatter.BuildOfferPlaceholderText(state),
                hasOffers,
                state.HighlightTradeOfferId ?? "");
        }

        internal static string BuildOfferListSignature(SteamOfferState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            unchecked
            {
                int hash = 17;
                hash = hash * 31 + state.Offers.Count;
                hash = hash * 31 + (state.HighlightTradeOfferId ?? "").GetHashCode();
                foreach (SteamOfferItem offer in state.Offers)
                {
                    hash = hash * 31 + (offer.TradeOfferId ?? "").GetHashCode();
                    hash = hash * 31 + offer.RiskLevel.GetHashCode();
                    hash = hash * 31 + offer.Status.GetHashCode();
                    hash = hash * 31 + offer.CreatedAt.GetHashCode();
                    hash = hash * 31 + offer.ExpirationTime.GetHashCode();
                    hash = hash * 31 + offer.ItemsToGive.Count;
                    hash = hash * 31 + offer.ItemsToReceive.Count;
                }

                return hash.ToString();
            }
        }

        private static IReadOnlyList<SteamOfferItem> SortOffers(IEnumerable<SteamOfferItem> offers)
        {
            return offers.OrderByDescending(o => o.CreatedAt).ToList();
        }

        internal static SteamTokenSessionStatusViewModel BuildTokenSessionStatus(SteamTokenEntry token)
        {
            string platform = FirstText(token.Platform, "Token");
            if (string.Equals(platform, "Steam", StringComparison.OrdinalIgnoreCase))
            {
                bool hasSession = !string.IsNullOrWhiteSpace(token.SessionId) && !string.IsNullOrWhiteSpace(token.SteamLoginSecure);
                string text = hasSession
                    ? $"Steam 登录状态已保存，保存时间：{FormatTime(token.SessionSavedAt)}；刷新报价时会验证是否仍有效。"
                    : "Steam 未登录。下一步：登录后自动保存登录状态。";
                return new SteamTokenSessionStatusViewModel(
                    text,
                    hasSession ? SteamTokenSessionStatusTone.Success : SteamTokenSessionStatusTone.Warning);
            }

            if (string.Equals(platform, "HOTP", StringComparison.OrdinalIgnoreCase))
            {
                return new SteamTokenSessionStatusViewModel(
                    $"HOTP 计数器：{token.HotpCounter}。",
                    SteamTokenSessionStatusTone.Muted);
            }

            return new SteamTokenSessionStatusViewModel(
                platform + " 令牌，仅用于显示验证码。",
                SteamTokenSessionStatusTone.Muted);
        }

        private static string BuildSteamConnectionRouteText(
            SteamConnectionProfile profile,
            SteamEndpointConnectionSnapshot? endpoint = null)
        {
            return profile.Mode switch
            {
                SteamConnectionMode.ManualProxy => "手动代理 · " + FirstText(SteamConnectionResolver.RedactProxyUri(profile.ProxyUri), profile.RouteName, "已设置"),
                SteamConnectionMode.AutoProxy => "自动代理 · " + FirstText(profile.RouteName, SteamConnectionResolver.RedactProxyUri(profile.ProxyUri), "系统代理"),
                SteamConnectionMode.Direct when !string.IsNullOrWhiteSpace(endpoint?.FailureReason) => "直连 · 恢复中",
                SteamConnectionMode.Direct when endpoint?.IsConnected == true => "直连 · " + endpoint.AddressSource,
                SteamConnectionMode.Direct => "直连",
                SteamConnectionMode.Failed => "失败",
                _ => "正在检测网络"
            };
        }

        private static SteamConnectionDisplayText BuildSteamConnectionDisplayText(
            SteamConnectionProfile profile,
            SteamEndpointConnectionSnapshot? endpoint = null)
        {
            return profile.Mode switch
            {
                SteamConnectionMode.ManualProxy => new SteamConnectionDisplayText("Steam 网络：正常，使用手动代理", "正常：手动代理"),
                SteamConnectionMode.AutoProxy when IsLocalProxy(profile) => new SteamConnectionDisplayText("Steam 网络：正常，通过本机代理连接", "正常：本机代理"),
                SteamConnectionMode.AutoProxy when IsSystemProxy(profile) => new SteamConnectionDisplayText("Steam 网络：正常，通过系统代理连接", "正常：系统代理"),
                SteamConnectionMode.AutoProxy when IsEnvironmentProxy(profile) => new SteamConnectionDisplayText("Steam 网络：正常，通过环境代理连接", "正常：环境代理"),
                SteamConnectionMode.AutoProxy => new SteamConnectionDisplayText("Steam 网络：正常，通过自动代理连接", "正常：自动代理"),
                SteamConnectionMode.Direct when !string.IsNullOrWhiteSpace(endpoint?.FailureReason) => new SteamConnectionDisplayText("Steam 网络：连接异常，正在自动恢复", "恢复中"),
                SteamConnectionMode.Direct when endpoint?.UsedFallbackDns == true => new SteamConnectionDisplayText("Steam 网络：正常，直连 Steam", "正常：直连·备用"),
                SteamConnectionMode.Direct => new SteamConnectionDisplayText("Steam 网络：正常，直连 Steam", "正常：直连"),
                SteamConnectionMode.Failed => new SteamConnectionDisplayText("Steam 网络：无法连接 Steam", "无法连接"),
                _ => new SteamConnectionDisplayText("Steam 网络：正在检测连接", "检测中")
            };
        }

        private static string BuildEndpointDiagnosticText(
            SteamConnectionProfile profile,
            SteamEndpointConnectionSnapshot? endpoint)
        {
            if (endpoint == null
                || string.IsNullOrWhiteSpace(endpoint.Host)
                || profile.Mode is SteamConnectionMode.ManualProxy or SteamConnectionMode.AutoProxy)
            {
                return "";
            }

            string attempts = endpoint.AttemptCount > 0
                ? "，尝试 " + endpoint.AttemptCount + " 个地址"
                : "";
            if (endpoint.IsConnected)
            {
                return "；端点：" + endpoint.Host
                    + " → " + endpoint.EndpointAddress
                    + "（" + FirstText(endpoint.AddressSource, "系统 DNS")
                    + attempts + "）";
            }

            if (!string.IsNullOrWhiteSpace(endpoint.FailureReason))
            {
                return "；端点诊断：" + FirstText(endpoint.AddressSource, "系统 DNS")
                    + attempts + "，" + endpoint.FailureReason;
            }

            return "";
        }

        private static bool IsLocalProxy(SteamConnectionProfile profile)
        {
            return ContainsAnyProxyText(
                profile,
                "本地代理",
                "127.0.0.1",
                "localhost",
                "::1");
        }

        private static bool IsSystemProxy(SteamConnectionProfile profile)
        {
            return ContainsAnyProxyText(profile, "系统代理");
        }

        private static bool IsEnvironmentProxy(SteamConnectionProfile profile)
        {
            return ContainsAnyProxyText(profile, "环境代理", "HTTPS_PROXY", "HTTP_PROXY", "ALL_PROXY");
        }

        private static bool ContainsAnyProxyText(SteamConnectionProfile profile, params string[] needles)
        {
            string text = string.Join(" ", profile.RouteName ?? "", profile.ProxyUri ?? "");
            foreach (string needle in needles)
            {
                if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string FirstText(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        private static string FormatTime(DateTime? time)
        {
            if (!time.HasValue || time.Value == default) return "暂无";
            return time.Value.ToString("MM-dd HH:mm:ss");
        }
    }

    internal sealed record SteamTokenBarSnapshot(
        IReadOnlyList<SteamTokenEntry> VisibleTokens,
        string DefaultTokenId);

    internal sealed record SteamConnectionStatusViewModel(
        string LabelText,
        string RouteText,
        SteamConnectionStatusTone Tone,
        string DetailText = "",
        string TooltipText = "");

    internal sealed record SteamConnectionDisplayText(
        string LabelText,
        string RouteText);

    internal enum SteamConnectionStatusTone
    {
        Success,
        Warning,
        Muted
    }

    internal sealed record SteamOfferSummaryViewModel(
        string Text,
        SteamOfferSummaryTone Tone,
        DateTime LastRefresh);

    internal enum SteamOfferSummaryTone
    {
        Success,
        Warning
    }

    internal sealed record SteamOfferListViewModel(
        string Signature,
        IReadOnlyList<SteamOfferItem> SafeOffers,
        IReadOnlyList<SteamOfferItem> VerifiedOffers,
        IReadOnlyList<SteamOfferItem> RiskOffers,
        string PlaceholderText,
        bool HasOffers,
        string HighlightTradeOfferId);

    internal sealed record SteamTokenSessionStatusViewModel(
        string Text,
        SteamTokenSessionStatusTone Tone);

    internal sealed record SteamTokenCodeDisplayViewModel(
        string CodeText,
        bool CanToggleCodeVisibility,
        string VisibilityButtonText,
        int SecondsLeft,
        string SessionText,
        SteamTokenSessionStatusTone SessionTone);

    internal enum SteamTokenSessionStatusTone
    {
        Success,
        Warning,
        Muted
    }
}
