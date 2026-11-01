using System;
using BepInEx.Configuration;

namespace EscapeFromBorderlands.Client;

internal static class LegendaryGrenadeSettings
{
    private static ConfigEntry<float>? Recharge, BigOneRecharge, NineBangerRecharge, DrawVolume;
    internal static void Bind(ConfigFile config)
    {
        Recharge = config.Bind("Legendary grenades", "Recharge seconds", 180f,
            "Seconds to restore 1 charge to each individual legendary grenade. Applies next raid. Minimum 1 second.");
        var legacy = config.Bind("Legendary grenades", "The Big One recharge seconds", 300f);
        BigOneRecharge = config.Bind("Legendary grenades", "Holy Hand Grenade recharge seconds", legacy.Value,
            "Seconds to restore Holy Hand Grenade's 1 charge. Applies next raid. Minimum 1 second.");
        config.Remove(legacy.Definition);
        NineBangerRecharge = config.Bind("Legendary grenades", "9-Banger recharge seconds", 300f,
            "Seconds to restore 9-Banger's 1 charge. Applies next raid. Minimum 1 second.");
        DrawVolume = config.Bind("Legendary grenades", "Holy Hand Grenade draw sound volume", .5f,
            "Local draw-cue volume: 0 mutes it; 1 is full volume. New volume applies on the next draw; muting also stops the current cue.");
    }
    // Freeze the settings per raid so editing them cannot move an existing deadline.
    private static float? RaidRecharge, RaidBigOneRecharge, RaidNineBangerRecharge;
    internal static void Reset() { RaidRecharge = RaidBigOneRecharge = RaidNineBangerRecharge = null; }
    private static float Valid(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(1f, value);
    internal static float DrawSoundVolume
    {
        get
        {
            var value = DrawVolume?.Value ?? .5f;
            return float.IsNaN(value) || float.IsInfinity(value) ? .5f : Math.Max(0f, Math.Min(1f, value));
        }
    }
    internal static float Interval(VariantDefinition definition) => definition.RuntimeKey == "GR15"
        ? (RaidBigOneRecharge ??= Valid(BigOneRecharge?.Value ?? definition.GrenadeRechargeSeconds, 300f))
        : definition.RuntimeKey == "GR67"
        ? (RaidNineBangerRecharge ??= Valid(NineBangerRecharge?.Value ?? definition.GrenadeRechargeSeconds, 300f))
        : (RaidRecharge ??= Valid(Recharge?.Value ?? definition.GrenadeRechargeSeconds, 180f));
}
