using System;

namespace EscapeFromBorderlands.Client;

internal static class SessionCleanup
{
    internal static void Run(string name, Action reset)
    {
        try { reset(); }
        catch (Exception error)
        {
            // One optional cleanup must not keep later subsystems in the old raid.
            Plugin.LogSource?.LogWarning($"EFB session cleanup failed in {name}; continuing remaining cleanup. {error}");
        }
    }
}
