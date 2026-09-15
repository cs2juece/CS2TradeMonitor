using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Reports;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring
{
    /// <summary>
    /// Explicit authorization to rotate one immutable Coverage Plan for one Audit Subject.
    /// </summary>
    public sealed class YouPinAuthorizedCoverageWatch
    {
        private YouPinAuthorizedCoverageWatch(
            Guid id,
            YouPinAuditSubject subject,
            string planFingerprint,
            YouPinWatchPurpose purpose,
            TimeSpan interval)
        {
            if (id == Guid.Empty)
                throw new ArgumentException("监控 ID 不能为空。", nameof(id));
            ArgumentNullException.ThrowIfNull(subject);
            ValidateFingerprint(planFingerprint);
            if (!Enum.IsDefined(purpose))
                throw new ArgumentOutOfRangeException(nameof(purpose));
            if (interval < YouPinAuthorizedWatch.MinimumInterval
                || interval > YouPinAuthorizedWatch.MaximumInterval)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(interval),
                    $"扫描间隔必须在 {YouPinAuthorizedWatch.MinimumInterval.TotalMinutes:0} 分钟到 "
                    + $"{YouPinAuthorizedWatch.MaximumInterval.TotalHours:0} 小时之间。");
            }

            Id = id;
            Subject = subject;
            PlanFingerprint = planFingerprint;
            Purpose = purpose;
            Interval = interval;
        }

        public Guid Id { get; }
        public YouPinAuditSubject Subject { get; }
        public string PlanFingerprint { get; }
        public YouPinWatchPurpose Purpose { get; }
        public TimeSpan Interval { get; }

        public static YouPinAuthorizedCoverageWatch Create(
            YouPinAuditSubject subject,
            YouPinHotCoveragePlan plan,
            YouPinWatchPurpose purpose,
            TimeSpan? interval = null)
        {
            ArgumentNullException.ThrowIfNull(plan);
            return new YouPinAuthorizedCoverageWatch(
                Guid.NewGuid(),
                subject,
                plan.Fingerprint,
                purpose,
                interval ?? YouPinAuthorizedWatch.DefaultInterval);
        }

        public static YouPinAuthorizedCoverageWatch Restore(
            Guid id,
            YouPinAuditSubject subject,
            string planFingerprint,
            YouPinWatchPurpose purpose,
            TimeSpan interval)
            => new(id, subject, planFingerprint, purpose, interval);

        private static void ValidateFingerprint(string fingerprint)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
            if (fingerprint.Length != 64
                || fingerprint.Any(character =>
                    !char.IsAsciiHexDigit(character) || char.IsUpper(character)))
            {
                throw new ArgumentException("覆盖计划指纹必须为小写 SHA-256。", nameof(fingerprint));
            }
        }
    }
}
