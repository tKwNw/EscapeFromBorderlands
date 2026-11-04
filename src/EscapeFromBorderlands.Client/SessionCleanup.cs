using System;
using System.Collections.Generic;

namespace EscapeFromBorderlands.Client;

internal static class SessionCleanup
{
    private static readonly HashSet<string> Reports = new(StringComparer.Ordinal);
    internal static void Begin() => Reports.Clear();
    internal static void Run(string name, Action reset)
    {
        try { reset(); }
        catch (Exception error)
        {
            // One optional cleanup must not keep later subsystems in the old raid.
            if (Reports.Add(name)) Plugin.LogSource?.LogWarning($"EFB session cleanup failed in {name}; continuing remaining cleanup. {error}");
        }
    }
}
