using System;
using System.Collections.Generic;

namespace EscapeFromBorderlands.Client;

// Caller owns its state lock. Preserve OrderBy's stable tie order and Take's
// original removal budget without allocating a filtered/sorted array each time.
internal sealed class StableHistoryPruner<T>
{
    private readonly List<(int Key, int Sequence, int Ordinal)> Entries = new();
    private static readonly Comparison<(int Key, int Sequence, int Ordinal)> Compare = (a, b) =>
    { var order = a.Sequence.CompareTo(b.Sequence); return order != 0 ? order : a.Ordinal.CompareTo(b.Ordinal); };
    internal int Prune(Dictionary<int, T> history, int keep, Func<T, bool> eligible, Func<T, int> sequence)
    {
        var remove = history.Count - keep;
        if (remove <= 0) return 0;
        try
        {
            var sort = false; var previous = int.MinValue;
            foreach (var pair in history)
            {
                if (!eligible(pair.Value)) continue;
                var order = sequence(pair.Value);
                sort |= order < previous; previous = order;
                Entries.Add((pair.Key, order, Entries.Count));
            }
            if (sort) Entries.Sort(Compare);
            remove = Math.Min(remove, Entries.Count);
            for (var i = 0; i < remove; i++) history.Remove(Entries[i].Key);
            return remove;
        }
        finally { Entries.Clear(); }
    }
}
