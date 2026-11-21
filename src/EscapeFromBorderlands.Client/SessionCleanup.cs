using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace EscapeFromBorderlands.Client;

internal static class SessionCleanup
{
    private static readonly HashSet<string> Reports = new(StringComparer.Ordinal);
    private static int Attempts, Failures;
    internal static void Begin() { Reports.Clear(); Attempts = Failures = 0; }
    internal static void Run(string name, Action reset)
    {
        Attempts++;
        try { reset(); }
        catch (Exception error) { Report(name, error); }
    }
    private static void Report(string name, Exception error)
    {
        Failures++;
        // One optional cleanup must not keep later subsystems in the old raid.
        if (Reports.Add(name)) Plugin.LogSource?.LogWarning($"EFB session cleanup failed in {name}; continuing remaining cleanup. {error}");
    }
    internal static void Run<T>(string name, T value, Action<T> reset)
    {
        Attempts++;
        try { reset(value); }
        catch (Exception error) { Report(name, error); }
    }
    [Conditional("EFB_PERFORMANCE_DIAGNOSTICS")]
    internal static void Complete()
    {
    }
}
