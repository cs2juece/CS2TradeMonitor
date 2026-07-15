using System;
using System.Collections.Generic;

namespace CS2TradeMonitor.src.Core.Modules
{
    public sealed class MonitorModuleDescriptor
    {
        public MonitorModuleDescriptor(
            string id,
            string displayName,
            string description,
            IReadOnlyList<string> pageKeys,
            IReadOnlyList<string> serviceAreas,
            IReadOnlyList<string> reminderEvents,
            bool isHighRisk = false,
            bool processIsolationCandidate = false)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            PageKeys = pageKeys;
            ServiceAreas = serviceAreas;
            ReminderEvents = reminderEvents;
            IsHighRisk = isHighRisk;
            ProcessIsolationCandidate = processIsolationCandidate;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public IReadOnlyList<string> PageKeys { get; }
        public IReadOnlyList<string> ServiceAreas { get; }
        public IReadOnlyList<string> ReminderEvents { get; }
        public bool IsHighRisk { get; }
        public bool ProcessIsolationCandidate { get; }

        public string ScopeSummary
        {
            get
            {
                var parts = new List<string>();
                if (PageKeys.Count > 0)
                    parts.Add("页面 " + string.Join("/", PageKeys));
                if (ReminderEvents.Count > 0)
                    parts.Add("提醒 " + string.Join("/", ReminderEvents));
                return parts.Count == 0 ? Description : string.Join("；", parts);
            }
        }
    }
}
