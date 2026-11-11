namespace EscapeFromBorderlands.Server;

// Role names come from SPT 4.0.13 WildSpawnType, not the broad Savage side:
// Raiders, Rogues, Cultists and bosses also use Savage. Role/faction metadata
// chooses a category group; ordinary Scavs have their own explicit rates.
internal static class BotSpawnGroups
{
    private static IReadOnlyDictionary<string, bool> CustomRoles = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    private static IReadOnlySet<string> InfectedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    internal static void SetCustomRoles(IReadOnlyDictionary<string, bool> roles, IReadOnlySet<string>? infectedRoles = null)
    { CustomRoles = roles; InfectedRoles = infectedRoles ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
    internal static bool IsClassifiedNonScav(string? role) => role is not null && CustomRoles.TryGetValue(role, out var scav) && !scav;
    internal static readonly string[] Expanded = ["raiders", "rogues", "followers", "cultists", "arenaFighters", "otherNonScavs"];

    internal static string? Group(string? role)
    {
        var name = role?.ToLowerInvariant() ?? "";
        if (name.StartsWith("infected", StringComparison.Ordinal) || InfectedRoles.Contains(name)) return null;
        if (CustomRoles.TryGetValue(name, out var customScav) && customScav) return "scavs";
        if (name is "assault" or "marksman" or "cursedassault" or "assaultgroup" or "crazyassaultevent") return "scavs";
        if (name is "" or "gifter" or "shooterbtr" or "test" or "bosstest" or "followertest") return null;
        if (name is "usec" or "pmcusec") return "pmcUsec";
        if (name is "bear" or "pmcbear") return "pmcBear";
        if (name.StartsWith("boss", StringComparison.Ordinal) || name is "peacefullzryachiyevent" or "ravangezryachiyevent") return "bosses";
        if (name is "pmcbot" or "raider" or "raiders") return "raiders";
        if (name is "exusec" or "rogue" or "rogues") return "rogues";
        if (name.StartsWith("follower", StringComparison.Ordinal) || name == "tagillahelperagro") return "followers";
        if (name.StartsWith("sectant", StringComparison.Ordinal) || name == "sectactpriestevent") return "cultists";
        if (name is "arenafighter" or "arenafighterevent") return "arenaFighters";
        if (name is "peacemaker" or "skier" or "spiritwinter" or "spiritspring") return "otherNonScavs";
        return IsClassifiedNonScav(name) ? "otherNonScavs" : null;
    }
}
