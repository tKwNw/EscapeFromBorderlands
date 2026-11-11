using System;
using System.Collections.Generic;
using System.Linq;

namespace EscapeFromBorderlands;

// Quest identity is one-way: an owned EFB clone may satisfy its vanilla source.
// A vanilla item or a different clone never satisfies a specific EFB requirement.
internal sealed class QuestItemEquivalence
{
    private readonly Dictionary<string, string> Sources;
    internal QuestItemEquivalence(IEnumerable<KeyValuePair<string, string>> sources) => Sources = sources
        .Where(p => !string.IsNullOrEmpty(p.Key) && !string.IsNullOrEmpty(p.Value) && p.Key != p.Value)
        .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
    internal string Source(string id) => id != null && Sources.TryGetValue(id, out var source) ? source : id!;
    internal bool IsVariant(string id) => id != null && Sources.ContainsKey(id);
    internal bool Matches(string actual, string required) => actual == required || Source(actual) == required;
    internal bool Contains(IEnumerable<string> required, string actual)
    {
        var source = Source(actual);
        foreach (var id in required)
            if (actual == id || source == id) return true;
        return false;
    }
    internal List<string> WithSources(IEnumerable<string> actual)
    {
        var values = actual.ToList();
        var known = new HashSet<string>(values, StringComparer.Ordinal);
        var originalCount = values.Count;
        for (var i = 0; i < originalCount; i++)
        {
            var id = values[i];
            var source = Source(id);
            if (source != id && known.Add(source)) values.Add(source);
        }
        return values;
    }
}
