using CS2TradeMonitor.Domain.YouPin;

namespace CS2TradeMonitor.Application.YouPin
{
    internal enum YouPinLandlordExecutionStartStatus
    {
        Started,
        Disabled,
        NotDue,
        CoolingDown
    }

    internal readonly record struct YouPinLandlordExecutionStartResult(
        YouPinLandlordExecutionStartStatus Status,
        YouPinLandlordExecutionState State,
        TimeSpan CooldownRemaining)
    {
        public bool Started => Status == YouPinLandlordExecutionStartStatus.Started;
    }

    internal sealed class YouPinLandlordExecutionCadence
    {
        public const int MinimumIntervalMinutes = 20;
        public static readonly TimeSpan MinimumCooldown = TimeSpan.FromMinutes(3);

        private readonly Dictionary<YouPinLandlordExecutionLane, LaneState> _states = new();

        public void Configure(
            YouPinLandlordExecutionLane lane,
            bool enabled,
            int intervalMinutes,
            DateTime nowUtc,
            DateTime persistedLastStartedAtUtc)
        {
            int normalizedInterval = Math.Clamp(intervalMinutes, MinimumIntervalMinutes, 1440);
            DateTime normalizedNow = EnsureUtc(nowUtc);
            DateTime persistedLast = NormalizePersistedLast(persistedLastStartedAtUtc, normalizedNow);

            if (!_states.TryGetValue(lane, out LaneState? state))
            {
                DateTime nextAutomatic = DateTime.MaxValue;
                if (enabled)
                {
                    nextAutomatic = persistedLast == DateTime.MinValue
                        ? normalizedNow.AddMinutes(normalizedInterval)
                        : persistedLast.AddMinutes(normalizedInterval);
                    if (nextAutomatic < normalizedNow)
                        nextAutomatic = normalizedNow;
                }
                _states[lane] = new LaneState(
                    enabled,
                    normalizedInterval,
                    persistedLast,
                    nextAutomatic);
                return;
            }

            bool newlyEnabled = enabled && !state.Enabled;
            bool intervalChanged = state.IntervalMinutes != normalizedInterval;
            state.Enabled = enabled;
            state.IntervalMinutes = normalizedInterval;
            if (persistedLast > state.LastStartedAtUtc)
                state.LastStartedAtUtc = persistedLast;

            if (!enabled)
            {
                state.NextAutomaticAtUtc = DateTime.MaxValue;
            }
            else if (newlyEnabled || intervalChanged || state.NextAutomaticAtUtc == DateTime.MaxValue)
            {
                state.NextAutomaticAtUtc = normalizedNow.AddMinutes(normalizedInterval);
            }
        }

        public YouPinLandlordExecutionStartResult TryStart(
            YouPinLandlordExecutionLane lane,
            DateTime nowUtc,
            bool ignoreAutomaticInterval)
        {
            if (!_states.TryGetValue(lane, out LaneState? state) || !state.Enabled)
            {
                return new YouPinLandlordExecutionStartResult(
                    YouPinLandlordExecutionStartStatus.Disabled,
                    GetState(lane),
                    TimeSpan.Zero);
            }

            DateTime normalizedNow = EnsureUtc(nowUtc);
            DateTime cooldownUntil = state.LastStartedAtUtc == DateTime.MinValue
                ? DateTime.MinValue
                : state.LastStartedAtUtc.Add(MinimumCooldown);
            if (cooldownUntil > normalizedNow)
            {
                return new YouPinLandlordExecutionStartResult(
                    YouPinLandlordExecutionStartStatus.CoolingDown,
                    ToSnapshot(lane, state),
                    cooldownUntil - normalizedNow);
            }

            if (!ignoreAutomaticInterval && state.NextAutomaticAtUtc > normalizedNow)
            {
                return new YouPinLandlordExecutionStartResult(
                    YouPinLandlordExecutionStartStatus.NotDue,
                    ToSnapshot(lane, state),
                    TimeSpan.Zero);
            }

            state.LastStartedAtUtc = normalizedNow;
            state.NextAutomaticAtUtc = normalizedNow.AddMinutes(state.IntervalMinutes);
            return new YouPinLandlordExecutionStartResult(
                YouPinLandlordExecutionStartStatus.Started,
                ToSnapshot(lane, state),
                TimeSpan.Zero);
        }

        public YouPinLandlordExecutionState GetState(YouPinLandlordExecutionLane lane)
        {
            return _states.TryGetValue(lane, out LaneState? state)
                ? ToSnapshot(lane, state)
                : YouPinLandlordExecutionState.Never(lane);
        }

        public bool IsAutomaticDue(YouPinLandlordExecutionLane lane, DateTime nowUtc)
        {
            return _states.TryGetValue(lane, out LaneState? state)
                && state.Enabled
                && state.NextAutomaticAtUtc <= EnsureUtc(nowUtc);
        }

        private static YouPinLandlordExecutionState ToSnapshot(
            YouPinLandlordExecutionLane lane,
            LaneState state)
        {
            return new YouPinLandlordExecutionState(
                lane,
                state.LastStartedAtUtc,
                state.NextAutomaticAtUtc);
        }

        private static DateTime NormalizePersistedLast(DateTime value, DateTime nowUtc)
        {
            if (value == DateTime.MinValue)
                return DateTime.MinValue;

            DateTime utc = EnsureUtc(value);
            return utc > nowUtc ? nowUtc : utc;
        }

        private static DateTime EnsureUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }

        private sealed class LaneState
        {
            public LaneState(
                bool enabled,
                int intervalMinutes,
                DateTime lastStartedAtUtc,
                DateTime nextAutomaticAtUtc)
            {
                Enabled = enabled;
                IntervalMinutes = intervalMinutes;
                LastStartedAtUtc = lastStartedAtUtc;
                NextAutomaticAtUtc = nextAutomaticAtUtc;
            }

            public bool Enabled { get; set; }

            public int IntervalMinutes { get; set; }

            public DateTime LastStartedAtUtc { get; set; }

            public DateTime NextAutomaticAtUtc { get; set; }
        }
    }
}
