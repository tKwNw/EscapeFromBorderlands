using System;
using System.Collections.Generic;

namespace EscapeFromBorderlands.Client;

// Cache identity work, never an event or the mutable copy handed to a checker.
// Compare every input on reuse: other objectives/mods can edit arrays in place.
internal sealed class QuestIdentityExpansion
{
    private string[] Inputs = Array.Empty<string>();
    private int Count = -1;
    private List<string>? Expanded;
    private QuestItemEquivalence? Identity;
    internal bool Read(IReadOnlyList<string> values, QuestItemEquivalence identity)
    {
        var same = ReferenceEquals(Identity, identity) && Count == values.Count;
        if (same)
            for (var i = 0; i < Count; i++)
                if (Inputs[i] != values[i]) { same = false; break; }
        if (same) return Expanded is not null;
        var expanded = identity.ExpandedSources(values);
        if (Inputs.Length < values.Count) Inputs = new string[values.Count];
        for (var i = 0; i < values.Count; i++) Inputs[i] = values[i];
        if (Count > values.Count) Array.Clear(Inputs, values.Count, Count - values.Count);
        Count = values.Count;
        Identity = identity;
        Expanded = expanded;
        return expanded is not null;
    }
    internal string[] CopyArray() => Expanded!.ToArray();
    internal List<string> CopyList() => new(Expanded!);
}
