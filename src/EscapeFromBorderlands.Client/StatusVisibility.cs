using System;

namespace EscapeFromBorderlands.Client;

// Presentation-only: callers filter completed status snapshots, never game state.
internal static class StatusVisibility
{
    internal static bool Allows(string key, bool guns, bool melee, bool backpacks, bool rigs, bool grenades = true, bool earpieces = true)
    {
        if (key.StartsWith("earpiece:", StringComparison.Ordinal)) return earpieces;
        if (key.StartsWith("grenade:", StringComparison.Ordinal) || key.StartsWith("grenade-buff:", StringComparison.Ordinal)) return grenades;
        if (key.StartsWith("melee:", StringComparison.Ordinal)) return melee;
        if (key.StartsWith("BP", StringComparison.Ordinal)) return backpacks;
        if (key.StartsWith("rig:", StringComparison.Ordinal)) return rigs;
        return guns;
    }
}
