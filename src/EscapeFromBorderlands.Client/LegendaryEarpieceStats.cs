using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

internal enum LegendaryEarpieceStatId
{ Hearing, Ambient, OwnStep, OwnEquipment, Contusion, AimCost, Draw, LegRecovery, Aim, Movement, Weather, GrenadeDraw }

internal static class LegendaryEarpieceStats
{
    private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Percent(float value) => Number(value * 100f) + "%";
    internal static float Hearing(RarityResolver.ShotRoll roll) => EarpieceEffectRules.Hearing(roll.Definition.RuntimeKey, roll.Earpiece.Hearing, 1f, false, "step", null);
    internal static float Self(RarityResolver.ShotRoll roll) => roll.Earpiece.SelfNoise * (roll.Definition.RuntimeKey == "EP01-2" ? .5f : 1f);
    internal static float Ambient(HeadphonesItemClass item, RarityResolver.ShotRoll roll) => (float)Math.Pow(10d, item.Template.AmbientVolume / 20d) * roll.Earpiece.Ambient;
    internal static void Bind(HeadphonesItemClass item)
    {
        if (RarityResolver.Resolve(item) is not { Definition.IsEarpiece: true } roll) return;
        var key = roll.Definition.RuntimeKey; var b = roll.Earpiece;
        void Row(LegendaryEarpieceStatId id, string name, Func<float> value, Func<string> text, string tooltip, bool less = false)
        {
            var attribute = item.Attributes.FirstOrDefault(a => a.Id.Equals(id));
            if (attribute is null) { attribute = new ItemAttributeClass(id); item.Attributes.Add(attribute); }
            attribute.Name = name; attribute.Base = value; attribute.StringValue = text;
            attribute.DisplayType = () => EItemAttributeDisplayType.Compact;
            attribute.Tooltip = () => tooltip; attribute.LessIsGood = less;
        }
        const string permanent = "This copy's permanent value, including its signature and random bonuses. Situational and triggered effects apply under their described conditions.";
        Row(LegendaryEarpieceStatId.Hearing, "Hearing range", () => item.Template.RolloffMultiplier * Hearing(roll),
            () => Number(item.Template.RolloffMultiplier * Hearing(roll)) + "×", "Includes the native headset hearing multiplier and this copy's permanent bonuses. Player skills and other equipment apply separately. " + (key == "EP02-1" ? "Moss Code adds its separate +50% on Woods, Shoreline and Reserve." : ""));
        Row(LegendaryEarpieceStatId.Ambient, "Ambient volume", () => Ambient(item, roll),
            () => Percent(Ambient(item, roll)), "Final background ambience volume after the base headset and Noise Gate. 100% is the ambient channel before headset attenuation; lower is quieter. Dead Channel adds its further reduction while stationary.", true);
        Row(LegendaryEarpieceStatId.OwnStep, "Own footstep volume", () => Self(roll), () => Percent(Self(roll)), "Local footstep volume relative to this native headset. Includes Inside Voice and Self Muffle; AI hearing is separate.", true);
        Row(LegendaryEarpieceStatId.OwnEquipment, "Own equipment volume", () => Self(roll), () => Percent(Self(roll)), "Local equipment volume relative to this native headset, after permanent signature and random bonuses.", true);
        Row(LegendaryEarpieceStatId.Contusion, "Grenade contusion duration", () => EarpieceEffectRules.Contusion(key, b.Contusion), () => Percent(EarpieceEffectRules.Contusion(key, b.Contusion)), permanent, true);
        Row(LegendaryEarpieceStatId.AimCost, "Aiming Arm Stamina cost", () => b.AimCost, () => Percent(b.AimCost), permanent, true);
        Row(LegendaryEarpieceStatId.Draw, "Firearm draw speed", () => b.Draw, () => Percent(b.Draw), permanent);
        Row(LegendaryEarpieceStatId.LegRecovery, "Leg Stamina regeneration", () => b.LegRecovery, () => Percent(b.LegRecovery), permanent);
        Row(LegendaryEarpieceStatId.Aim, "Aiming speed", () => b.Aim, () => Percent(b.Aim), permanent);
        if (key == "EP06-1") Row(LegendaryEarpieceStatId.Movement, "Movement speed", () => .8f, () => "80%", "Matamata Turtle's walking and sprinting speed relative to your normal movement while this headset is worn.");
        if (key == "EP02-2") Row(LegendaryEarpieceStatId.Weather, "Rain and wind volume", () => .4f, () => "40%", "Rain Check's local weather-volume signature.", true);
        if (key == "EP07-2") Row(LegendaryEarpieceStatId.GrenadeDraw, "Grenade draw/throw speed", () => 1.6f, () => "160%", "Fast Forward's grenade-animation signature; per-grenade bonuses multiply separately.");
    }
    internal static Enum Icon(Enum id) => id is LegendaryEarpieceStatId stat ? stat switch
    {
        LegendaryEarpieceStatId.Hearing => EItemAttributeId.EffectiveDistance,
        LegendaryEarpieceStatId.Ambient or LegendaryEarpieceStatId.OwnStep or LegendaryEarpieceStatId.OwnEquipment or LegendaryEarpieceStatId.Weather => EItemAttributeId.Loudness,
        LegendaryEarpieceStatId.Movement => EItemAttributeId.ChangeMovementSpeed,
        LegendaryEarpieceStatId.Contusion => EItemAttributeId.BlindnessProtection,
        _ => EItemAttributeId.Ergonomics
    } : id;
}
[HarmonyPatch]
internal static class LegendaryEarpieceAttributePatch
{
    private static MethodBase TargetMethod() => AccessTools.Constructor(typeof(HeadphonesItemClass), new[] { typeof(string), typeof(HeadphonesTemplateClass) });
    private static void Postfix(HeadphonesItemClass __instance) => LegendaryEarpieceStats.Bind(__instance);
}
[HarmonyPatch(typeof(ItemSpecificationPanel), "method_5")]
internal static class LegendaryEarpieceInspectStatsPatch
{
    private static readonly FieldInfo ItemField = AccessTools.Field(typeof(ItemSpecificationPanel), "item_0");
    private static void Prefix(ItemSpecificationPanel __instance)
    { if (ItemField.GetValue(__instance) is HeadphonesItemClass item) LegendaryEarpieceStats.Bind(item); }
}
[HarmonyPatch(typeof(ItemSpecificationPanel), "method_6")]
internal static class LegendaryEarpieceCompareStatsPatch
{
    private static void Prefix(Item compareItem)
    { if (compareItem is HeadphonesItemClass item) LegendaryEarpieceStats.Bind(item); }
}
[HarmonyPatch(typeof(StaticIcons), nameof(StaticIcons.GetAttributeIcon))]
internal static class LegendaryEarpieceStatIconPatch
{
    private static void Prefix(ref Enum id) => id = LegendaryEarpieceStats.Icon(id);
}
