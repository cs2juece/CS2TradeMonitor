using CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Reports;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring
{
    public enum YouPinWatchPurpose
    {
        SelfAudit,
        ExplicitPermission,
        ResponsibleDisclosure
    }

    /// <summary>
    /// Explicitly authorized subject and immutable template scope for future scheduled scans.
    /// </summary>
    public sealed class YouPinAuthorizedWatch
    {
        public static TimeSpan DefaultInterval { get; } = TimeSpan.FromMinutes(30);
        public static TimeSpan MinimumInterval { get; } = TimeSpan.FromMinutes(10);
        public static TimeSpan MaximumInterval { get; } = TimeSpan.FromHours(24);

        private YouPinAuthorizedWatch(
            Guid id,
            YouPinAuditSubject subject,
            YouPinPurchaseAuditScope scope,
            YouPinWatchPurpose purpose,
            TimeSpan interval)
        {
            if (id == Guid.Empty)
                throw new ArgumentException("监控 ID 不能为空。", nameof(id));
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(scope);
            if (!Enum.IsDefined(purpose))
                throw new ArgumentOutOfRangeException(nameof(purpose));
            if (interval < MinimumInterval || interval > MaximumInterval)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(interval),
                    $"扫描间隔必须在 {MinimumInterval.TotalMinutes:0} 分钟到 "
                    + $"{MaximumInterval.TotalHours:0} 小时之间。");
            }

            Id = id;
            Subject = subject;
            Scope = scope;
            Purpose = purpose;
            Interval = interval;
        }

        public Guid Id { get; }
        public YouPinAuditSubject Subject { get; }
        public YouPinPurchaseAuditScope Scope { get; }
        public YouPinWatchPurpose Purpose { get; }
        public TimeSpan Interval { get; }

        public static YouPinAuthorizedWatch Create(
            YouPinAuditSubject subject,
            YouPinPurchaseAuditScope scope,
            YouPinWatchPurpose purpose,
            TimeSpan? interval = null)
            => new(Guid.NewGuid(), subject, scope, purpose, interval ?? DefaultInterval);

        public static YouPinAuthorizedWatch Restore(
            Guid id,
            YouPinAuditSubject subject,
            YouPinPurchaseAuditScope scope,
            YouPinWatchPurpose purpose,
            TimeSpan interval)
            => new(id, subject, scope, purpose, interval);
    }
}
