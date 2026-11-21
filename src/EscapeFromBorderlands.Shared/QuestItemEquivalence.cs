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
        if (required is null) throw new ArgumentNullException("source");
        var source = Source(actual);
        if (required is string[] array)
        {
            for (var i = 0; i < array.Length; i++) if (actual == array[i] || source == array[i]) return true;
            return false;
        }
        foreach (var id in required) if (actual == id || source == id) return true;
        return false;
    }
    // Null means no identity needs appending. Never modify the event's lists:
    // multiple quest objectives consume the same hit in sequence.
    internal List<string>? ExpandedSources(IReadOnlyList<string> actual)
    {
        List<string>? expanded = null;
        for (var i = 0; i < actual.Count; i++)
        {
            var id = actual[i];
            var source = Source(id);
            if (source == id) continue;
            var values = (IReadOnlyList<string>?)expanded ?? actual;
            var exists = false;
            for (var j = 0; j < values.Count; j++)
                if (values[j] == source) { exists = true; break; }
            if (exists) continue;
            if (expanded is null)
            {
                expanded = new List<string>(actual.Count + 1);
                for (var j = 0; j < actual.Count; j++) expanded.Add(actual[j]);
            }
            expanded.Add(source);
        }
        return expanded;
    }
    internal List<string> WithSources(IEnumerable<string> actual)
    {
        var values = actual.ToList();
        HashSet<string>? known = null;
        var originalCount = values.Count;
        for (var i = 0; i < originalCount; i++)
        {
            var id = values[i];
            var source = Source(id);
            if (source == id) continue;
            known ??= new HashSet<string>(values, StringComparer.Ordinal);
            if (known.Add(source)) values.Add(source);
        }
        return values;
    }
}
