using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

internal enum LegendaryGrenadeStatId
{
    FragmentDamage, FlashRadius, FlashDuration, SmokeDuration, SmokeSize,
    SmokeDeployment, ThrowDistance, DrawSpeed, ThrowSpeed, FlashPulses, FlashPulseInterval
}

internal static class LegendaryGrenadeStats
{
    internal static float FuseDelay(float seconds, RarityResolver.ShotRoll roll) =>
        roll.Definition.RuntimeKey == "GR15" || GrenadeEffectRules.ImpactFlash(roll.Definition.RuntimeKey) ? 3f : seconds;
    internal static int SmokeDuration(float seconds, RarityResolver.ShotRoll roll) => (int)Math.Round(seconds * roll.GrenadeSmokeDurationMultiplier);
    internal static float SmokeSize(RarityResolver.ShotRoll roll) => roll.GrenadeSmokeSizeMultiplier * (roll.Definition.RuntimeKey == "GR56" ? 2f : 1f);
    private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Seconds(float value) => Number(value) + " " + LocalizationExtensions.Localized("sec");
    private static string Metres(float value) => Number(value) + " " + LocalizationExtensions.Localized("meters");
    private static string Percent(float value) => Number(value * 100f) + "%";

    internal static void Bind(ThrowWeap item)
    {
        var roll = RarityResolver.Resolve(item);
        if (roll?.Definition.GrenadeChargesPerRaid is not > 0) return;
        // Use the same explosion adapter as the thrown projectile. The inventory
        // getters and shared template remain vanilla, so gameplay is never scaled twice.
        var explosive = new LegendaryGrenadeExplosive(item, new LegendaryGrenadeEffects.Payload { Roll = roll });
        void Native(EItemAttributeId id, Func<float> value, Func<string> text)
        {
            var attribute = item.Attributes.FirstOrDefault(a => a.Id.Equals(id));
            if (attribute == null) return;
            attribute.Base = value;
            attribute.StringValue = text;
        }
        void Extra(LegendaryGrenadeStatId id, string name, Func<float> value, Func<string> text, string tooltip, bool lessIsGood = false)
        {
            var attribute = item.Attributes.FirstOrDefault(a => a.Id.Equals(id));
            if (attribute == null) { attribute = new ItemAttribute(id); item.Attributes.Add(attribute); }
            attribute.Name = name;
            attribute.Base = value;
            attribute.StringValue = text;
            attribute.DisplayType = () => EItemAttributeDisplayType.Compact;
            attribute.Tooltip = () => tooltip;
            attribute.LessIsGood = lessIsGood;
        }

        Native(EItemAttributeId.ExplosionDelay, () => FuseDelay(item.GetExplDelay, roll), () => GrenadeEffectRules.ImpactFlash(roll.Definition.RuntimeKey)
            ? "Impact (0.3 sec arm), 3 sec backup" : Seconds(FuseDelay(item.GetExplDelay, roll)));
        Native(EItemAttributeId.MaximumThrowDamage, () => explosive.GetStrength, () => Number(explosive.GetStrength));
        Native(EItemAttributeId.ExplosionDistance, () => explosive.MinExplosionDistance,
            () => Number(explosive.MinExplosionDistance) + " - " + Metres(explosive.MaxExplosionDistance));
        Native(EItemAttributeId.FragmentsCount, () => explosive.FragmentsCount, () => explosive.FragmentsCount.ToString(CultureInfo.InvariantCulture));

        // Read the fragment ammo template without creating an inventory item.
        // Native Min/MaxFragmentDamage are not the ballistic fragment's damage.
        if (item.FragmentsCount > 0 && Singleton<ItemFactory>.Instance?.ItemTemplates is { } templates &&
            !string.IsNullOrEmpty(item.FragmentType) && templates.TryGetValue(item.FragmentType, out var fragment) && fragment is AmmoTemplate ammo)
            Extra(LegendaryGrenadeStatId.FragmentDamage, "Fragment damage", () => ammo.Damage * roll.GrenadeFragmentDamageMultiplier,
                () => Number(ammo.Damage * roll.GrenadeFragmentDamageMultiplier), "Damage per fragment before armor, travel and target effects.");

        if (explosive.Blindness.y > 0f)
        {
            Extra(LegendaryGrenadeStatId.FlashRadius, "Flash radius", () => explosive.Blindness.y, () => Metres(explosive.Blindness.y),
                "Maximum visual flash radius. Facing and cover still affect the flash.");
            Extra(LegendaryGrenadeStatId.FlashDuration, "Flash duration", () => explosive.Blindness.z, () => Seconds(explosive.Blindness.z),
                "Base flash duration before distance, facing and target effects.");
        }
        var pulses = GrenadeEffectRules.FlashPulseCount(roll.Definition.RuntimeKey);
        if (pulses > 1)
        {
            Extra(LegendaryGrenadeStatId.FlashPulses, "Flash count", () => pulses, () => pulses.ToString(CultureInfo.InvariantCulture),
                "Total native flash pulses from 1 throw. Each throw spends 1 charge.");
            Extra(LegendaryGrenadeStatId.FlashPulseInterval, "Flash interval", () => GrenadeEffectRules.FlashPulseInterval(roll.Definition.RuntimeKey),
                () => Seconds(GrenadeEffectRules.FlashPulseInterval(roll.Definition.RuntimeKey)), "Time between successive flash pulses.");
        }
        if (roll.Definition.GrenadeCategory == "smoke")
        {
            Extra(LegendaryGrenadeStatId.SmokeDuration, "Smoke duration", () => SmokeDuration(item.EmitTime, roll),
                () => Seconds(SmokeDuration(item.EmitTime, roll)), "Emission duration of this grenade's smoke cloud.");
            Extra(LegendaryGrenadeStatId.SmokeSize, "Smoke cloud size", () => SmokeSize(roll), () => Percent(SmokeSize(roll)),
                "Width, height and depth relative to the ordinary grenade's smoke cloud. 100% is the base size.");
            Extra(LegendaryGrenadeStatId.SmokeDeployment, "Smoke build-up time", () => roll.Definition.RuntimeKey == "GR59" ? 0f : roll.GrenadeSmokeBuildTimeMultiplier,
                () => roll.Definition.RuntimeKey == "GR59" ? "Instant" : Percent(roll.GrenadeSmokeBuildTimeMultiplier) + " of base",
                "Cloud build-up time relative to the ordinary grenade. Lower is faster; Pop Smoke reaches full area on activation.", true);
        }
        Extra(LegendaryGrenadeStatId.ThrowDistance, "Throw distance", () => roll.GrenadeThrowDistanceMultiplier, () => Percent(roll.GrenadeThrowDistanceMultiplier),
            "This copy's range multiplier. 100% is the ordinary grenade; trajectory, obstacles and other equipment still affect travel.");
        Extra(LegendaryGrenadeStatId.DrawSpeed, "Grenade draw speed", () => roll.GrenadeDrawSpeedMultiplier, () => Percent(roll.GrenadeDrawSpeedMultiplier),
            "This copy's draw animation speed. 100% is the ordinary grenade.");
        Extra(LegendaryGrenadeStatId.ThrowSpeed, "Throw animation speed", () => roll.GrenadeThrowSpeedMultiplier, () => Percent(roll.GrenadeThrowSpeedMultiplier),
            "This copy's throw animation speed. 100% is the ordinary grenade.");
    }

    // Native compact panels ask StaticIcons for every Enum id. Map only these
    // extra rows to supported grenade icons to avoid unknown-id errors.
    internal static Enum Icon(Enum id) => id is LegendaryGrenadeStatId stat ? stat switch
    {
        LegendaryGrenadeStatId.FragmentDamage => EItemAttributeId.MaximumThrowDamage,
        LegendaryGrenadeStatId.FlashRadius or LegendaryGrenadeStatId.SmokeSize or LegendaryGrenadeStatId.ThrowDistance => EItemAttributeId.ExplosionDistance,
        _ => EItemAttributeId.ExplosionDelay
    } : id;
}

[HarmonyPatch]
internal static class LegendaryGrenadeAttributePatch
{
    private static MethodBase TargetMethod() => AccessTools.Constructor(typeof(ThrowWeap), new[] { typeof(string), typeof(ThrowWeapTemplate) });
    private static void Postfix(ThrowWeap __instance) => LegendaryGrenadeStats.Bind(__instance);
}

[HarmonyPatch(typeof(ItemSpecificationPanel), "RecreateAttributeBars")]
internal static class LegendaryGrenadeInspectStatsPatch
{
    private static readonly FieldInfo ItemField = AccessTools.Field(typeof(ItemSpecificationPanel), "_item");
    private static void Prefix(ItemSpecificationPanel __instance)
    { if (ItemField.GetValue(__instance) is ThrowWeap item) LegendaryGrenadeStats.Bind(item); }
}

[HarmonyPatch(typeof(ItemSpecificationPanel), "Compare")]
internal static class LegendaryGrenadeCompareStatsPatch
{
    private static void Prefix(Item compareItem)
    { if (compareItem is ThrowWeap item) LegendaryGrenadeStats.Bind(item); }
}

[HarmonyPatch(typeof(StaticIcons), nameof(StaticIcons.GetAttributeIcon))]
internal static class LegendaryGrenadeStatIconPatch
{
    private static void Prefix(ref Enum id) => id = LegendaryGrenadeStats.Icon(id);
}
