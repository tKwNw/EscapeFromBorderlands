namespace EscapeFromBorderlands.Client;

// The adapters supply native conditions; these are the shared, testable rules
// for what each signature changes. Multiplication preserves independent rolls.
internal static class EarpieceEffectRules
{
    internal static float Recoil(string key, bool active, bool sideways, bool crouched, bool moving) => key switch
    { "EP04-2" when sideways => .75f, "EP08-1" when crouched && !moving => .6f, "EP06-5" when active => .5f, _ => 1f };
    internal static bool HighFidelity(string key, bool aiming, float seconds) => key == "EP10-1" && aiming && seconds >= 2f;
    internal static float Sway(string key, bool prone, float seconds, bool night) => key switch
    { "EP02-5" when prone && seconds >= 3f => .75f, "EP08-3" when night => .65f, _ => 1f };
    internal static float Movement(string key, bool active, bool sprint, bool melee) => key == "EP06-1" ? .8f : sprint
        ? key == "EP04-5" && active ? 1.3f : 1f
        : key == "EP05-4" && melee ? 1.2f : active && key == "EP07-1" ? 1.3f : active && key == "EP07-5" ? 1.2f : 1f;
    internal static float Contusion(string key, float random) => random * (key == "EP09-2" ? 0f : key == "EP03-1" ? .25f : 1f);
    internal static float Ambient(string key, float random, bool moving, float stillSeconds) => random * (key == "EP01-1" && !moving && stillSeconds >= 1f ? .25f : 1f);
    internal static float Hearing(string key, float random, float temporary, bool own, string kind, string? map)
    {
        if (own) return 1f;
        var factor = random * temporary;
        if (key == "EP06-1") factor *= 2f;
        if (key == "EP02-1" && map is "woods" or "shoreline" or "rezervbase") factor *= 1.5f;
        if (key == "EP11-1" && kind == "step") factor *= 1.25f;
        return factor;
    }
    internal static float Volume(string key, float selfRoll, bool own, string kind, bool prone, float emitted)
    {
        var factor = kind == "step" ? emitted : 1f;
        if (own && kind is "step" or "item") factor *= selfRoll * (key == "EP01-2" ? .5f : 1f);
        if (key == "EP11-2" && prone)
        { if (kind == "gun") factor *= .5f; if (kind == "step" && !own) factor *= 1.75f; }
        return factor;
    }
    internal static float AimingWalkLimit(float native) => native + (1f - native) * .5f;
}
