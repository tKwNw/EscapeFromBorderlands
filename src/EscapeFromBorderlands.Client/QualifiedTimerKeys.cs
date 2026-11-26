using System;
using System.Collections.Generic;

namespace EscapeFromBorderlands.Client;

// Only names are cached. Timer values and expiration are read live every time.
internal sealed class QualifiedTimerKeys
{
    private const int Capacity = 32;
    private string? Prefix;
    private readonly Dictionary<string, string> Keys = new(StringComparer.Ordinal);
    internal string Read(string prefix, string key)
    {
        key ??= string.Empty;
        if (!string.Equals(Prefix, prefix, StringComparison.Ordinal))
        {
            Keys.Clear();
            Prefix = prefix;
        }
        if (Keys.TryGetValue(key, out var full))
            return full;
        full = prefix + ":" + key;
        if (Keys.Count < Capacity)
            Keys.Add(key, full);
        return full;
    }
}
