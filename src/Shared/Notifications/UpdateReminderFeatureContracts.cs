using CS2TradeMonitor.Application.Notify;

namespace CS2TradeMonitor.Shared.Notifications;

public sealed record Cs2UpdateItemProjection(
    string Key,
    string Source,
    string Title,
    string Summary,
    string PublishedAtText);

public sealed record PhoneAlertChannelProjection(
    PhoneAlertChannelType Type,
    string Title,
    string DisplayName,
    bool Enabled,
    int Priority,
    bool Configured,
    string SecretMask,
    string ServerUrl,
    string Extra,
    string SecretLabel,
    bool ShowServerField,
    string ServerLabel,
    bool ShowExtraField,
    string ExtraLabel,
    string HelpUrl,
    string LastTestResult);

public sealed record Cs2UpdateReminderProjection(
    bool Enabled,
    int RefreshSeconds,
    bool PhoneReminderEnabled,
    bool SoundEnabled,
    string CurrentStatus,
    string LastCheckText,
    string BaselineTitle,
    string BaselineTimeText,
    bool PrimaryConfigured,
    int EnabledBackupCount,
    string DeliveryMode,
    IReadOnlyList<Cs2UpdateItemProjection> RecentItems,
    IReadOnlyList<PhoneAlertChannelProjection> PhoneChannels);

public sealed record SavePrimaryPhoneAlertRequest(string Secret, bool Clear = false);

public sealed record SaveBackupPhoneAlertRequest(
    PhoneAlertChannelType Type,
    bool Enabled,
    string DisplayName,
    string Secret,
    string ServerUrl,
    string Extra,
    bool ClearSecret = false);
