using System;
using System.Collections.Generic;
using System.Linq;
using CS2TradeMonitor.src.UI.Framework;

namespace CS2TradeMonitor.src.UI
{
    internal static class SettingsFormNavigationModel
    {
        public static SettingsFormNavigationEntries BuildEntries(IEnumerable<SettingsPageRoute> routes)
        {
            if (routes == null)
                throw new ArgumentNullException(nameof(routes));

            var mainEntries = new List<SettingsFormNavigationEntry>();
            var systemEntries = new List<SettingsFormNavigationEntry>();

            foreach (var route in routes)
            {
                var entry = new SettingsFormNavigationEntry(route.Key, route.Title);
                if (route.PlaceInSystemContainer)
                    systemEntries.Add(entry);
                else
                    mainEntries.Add(entry);
            }

            return new SettingsFormNavigationEntries(mainEntries, systemEntries);
        }
    }

    internal sealed class SettingsFormNavigationEntries
    {
        public SettingsFormNavigationEntries(
            IReadOnlyList<SettingsFormNavigationEntry> mainEntries,
            IReadOnlyList<SettingsFormNavigationEntry> systemEntries)
        {
            MainEntries = mainEntries ?? throw new ArgumentNullException(nameof(mainEntries));
            SystemEntries = systemEntries ?? throw new ArgumentNullException(nameof(systemEntries));
        }

        public IReadOnlyList<SettingsFormNavigationEntry> MainEntries { get; }

        public IReadOnlyList<SettingsFormNavigationEntry> SystemEntries { get; }
    }

    internal readonly record struct SettingsFormNavigationEntry(string Key, string Title);
}
