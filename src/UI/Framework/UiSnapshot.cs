#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace CS2TradeMonitor.src.UI.Framework
{
    public sealed record UiSnapshot
    {
        public static UiSnapshot Empty { get; } = new UiSnapshot();

        public UiSnapshot(
            IEnumerable<UiGroupSnapshot>? groups = null,
            IEnumerable<UiColumnSnapshot>? columns = null,
            IEnumerable<UiAlertSnapshot>? alerts = null,
            IReadOnlyDictionary<string, string?>? textValues = null,
            bool forceLayoutRebuild = false,
            bool forceRender = false)
        {
            Groups = ToReadOnlyList(groups);
            Columns = ToReadOnlyList(columns);
            Alerts = ToReadOnlyList(alerts);
            TextValues = ToReadOnlyDictionary(textValues);
            ForceLayoutRebuild = forceLayoutRebuild;
            ForceRender = forceRender;
        }

        public IReadOnlyList<UiGroupSnapshot> Groups { get; }

        public IReadOnlyList<UiColumnSnapshot> Columns { get; }

        public IReadOnlyList<UiAlertSnapshot> Alerts { get; }

        public IReadOnlyDictionary<string, string?> TextValues { get; }

        public bool ForceLayoutRebuild { get; }

        public bool ForceRender { get; }

        public bool TryGetTextValue(string key, out string? value)
        {
            return TextValues.TryGetValue(key, out value);
        }

        private static IReadOnlyList<TItem> ToReadOnlyList<TItem>(IEnumerable<TItem>? items)
        {
            return Array.AsReadOnly((items ?? Enumerable.Empty<TItem>()).ToArray());
        }

        private static IReadOnlyDictionary<string, string?> ToReadOnlyDictionary(
            IReadOnlyDictionary<string, string?>? items)
        {
            var copy = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (items != null)
            {
                foreach (var item in items)
                {
                    copy[item.Key] = item.Value;
                }
            }

            return new ReadOnlyDictionary<string, string?>(copy);
        }
    }

    public sealed record UiGroupSnapshot(string Id, string Title, int ItemCount);

    public sealed record UiColumnSnapshot(string Key, string Title, string? Format = null);

    public sealed record UiAlertSnapshot(
        string Id,
        string Title,
        string Message,
        string Severity,
        DateTimeOffset CreatedAt);
}

#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
#endif
