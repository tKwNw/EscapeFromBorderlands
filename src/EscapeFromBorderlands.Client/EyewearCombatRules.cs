using EFT.Ballistics;
namespace EscapeFromBorderlands.Client;

// Pure decisions shared by both native adapters and the offline boundary tests.
internal static class EyewearCombatRules
{
    internal static bool NeedsShotScope(string key) => key is "EW009" or "EW013" or "EW019" or "EW021" or "EW022" or "EW027" or "EW031";
    internal static bool SingleBullet(int naturalProjectiles, float explosionStrength, string weaponClass) =>
        naturalProjectiles == 1 && explosionStrength <= 0 && weaponClass != "shotgun";
    internal static float Outgoing(string? key) => key == "EW022" ? .15f : 1;
    internal static float Incoming(string? key, bool active) => key == "EW022" ? 1.2f : key == "EW025" && active ? .6f : 1;
    internal static bool ProtectedExplosion(string previousGrenade, string incomingGrenade, float elapsed, float originDistanceSquared) =>
        elapsed >= 0 && (incomingGrenade.Length > 0 ? previousGrenade == incomingGrenade && elapsed < 30 : previousGrenade.Length == 0 && elapsed < .25f && originDistanceSquared < .001f);
    internal static bool ConflictingGrenade(string? key) => key is "GR13" or "GR26";
}
