using System;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

internal static class WeaponStatMath
{
    // Deltas describe multipliers of the assembled weapon, not percentage
    // points subtracted from the receiver before attachments are counted.
    public static float Compose(float assembledDelta, float rollDelta, float effectDelta, float minimum = 0f) =>
        Math.Max(minimum, Math.Max(0f, 1f + assembledDelta) * Math.Max(0f, 1f + rollDelta) * Math.Max(0f, 1f + effectDelta)) - 1f;
}

[HarmonyPatch(typeof(Weapon), nameof(Weapon.RecoilDelta), MethodType.Getter)]
internal static class WeaponRecoilDeltaPatch
{
    private static void Postfix(Weapon __instance, ref float __result)
    {
        var roll = WeaponStatReadBatch.ResolveRoll(__instance);
        if (roll is null) return;
        __result = WeaponStatMath.Compose(__result, roll.RecoilDeltaModifier, LegendaryRuntime.GetStatModifiers(__instance).RecoilDelta);
    }
}

[HarmonyPatch(typeof(Weapon), nameof(Weapon.ErgonomicsDelta), MethodType.Getter)]
internal static class WeaponErgonomicsDeltaPatch
{
    private static void Postfix(Weapon __instance, ref float __result)
    {
        var roll = WeaponStatReadBatch.ResolveRoll(__instance);
        if (roll is null) return;
        __result = WeaponStatMath.Compose(__result, roll.ErgonomicsDeltaModifier, LegendaryRuntime.GetStatModifiers(__instance).ErgonomicsDelta);
    }
}

[HarmonyPatch(typeof(Weapon), nameof(Weapon.CenterOfImpactDelta), MethodType.Getter)]
internal static class WeaponAccuracyDeltaPatch
{
    private static void Postfix(Weapon __instance, ref float __result)
    {
        var roll = WeaponStatReadBatch.ResolveRoll(__instance);
        if (roll is null) return;
        __result = WeaponStatMath.Compose(__result, roll.CenterOfImpactDeltaModifier, LegendaryRuntime.GetStatModifiers(__instance).AccuracyDelta, 0.01f);
    }
}

[HarmonyPatch(typeof(Weapon), nameof(Weapon.VelocityDelta), MethodType.Getter)]
internal static class WeaponVelocityDeltaPatch
{
    private static void Postfix(Weapon __instance, ref float __result)
    {
        var roll = WeaponStatReadBatch.ResolveRoll(__instance);
        if (roll is null) return;
        var runtimeVelocity = LegendaryRuntime.GetVelocityMultiplier(__instance);
        __result = Math.Max(0.05f, (1f + __result) * roll.VelocityMultiplier * runtimeVelocity) - 1f;
    }
}

[HarmonyPatch(typeof(Weapon), nameof(Weapon.FireRate), MethodType.Getter)]
internal static class WeaponFireRatePatch
{
    private static void Postfix(Weapon __instance, ref int __result)
    {
        var roll = WeaponStatReadBatch.ResolveRoll(__instance);
        if (roll is null) return;
        __result = Math.Max(1, (int)Math.Round(
            __result * roll.FireRateMultiplier * LegendaryRuntime.GetStatModifiers(__instance).EffectiveFireRateMultiplier));
    }
}

[HarmonyPatch(typeof(Weapon), nameof(Weapon.SingleFireRate), MethodType.Getter)]
internal static class WeaponSingleFireRatePatch
{
    private static void Postfix(Weapon __instance, ref int __result)
    {
        var roll = WeaponStatReadBatch.ResolveRoll(__instance);
        if (roll is null) return;
        // EFT clamps this getter to 240 RPM; explicit EFB values bypass that
        // floor without modifying any other weapon's native limit.
        if (roll.Definition.SingleFireRate is > 0) __result = roll.Definition.SingleFireRate.Value;
        __result = Math.Max(1, (int)Math.Round(
            __result * roll.FireRateMultiplier * LegendaryRuntime.GetStatModifiers(__instance).EffectiveFireRateMultiplier));
    }
}

[HarmonyPatch]
internal static class WeaponFireRateAttributePatch
{
    private static MethodBase TargetMethod() =>
        AccessTools.Constructor(typeof(Weapon), new[] { typeof(string), typeof(WeaponTemplate) });

    private static void Postfix(Weapon __instance)
    {
        if (RarityResolver.Resolve(__instance) is null) return;
        var attribute = __instance.Attributes.FirstOrDefault(value => value.Id.Equals(EItemAttributeId.FireRate));
        if (attribute is null) return;
        attribute.Base = () => __instance.FireRate;
        attribute.StringValue = () => $"{__instance.FireRate} {LocalizationExtensions.Localized("rpm")}";
    }
}

[HarmonyPatch(typeof(Player.FirearmController), nameof(Player.FirearmController.GetWeaponReloadAnimationSpeed))]
internal static class WeaponReloadSpeedPatch
{
    private static void Postfix(Player.FirearmController __instance, ref float __result)
    {
        __result *= LegendaryRuntime.GetReloadSpeedMultiplier(__instance.Item) * ChestRigNative.Reload(__instance) * GrenadeBuffs.Reload(BackpackRuntime.Actor(__instance)) * EarpieceRuntime.Reload(BackpackRuntime.Actor(__instance)) * Headwear.Factor(WeaponCombatState.Owner(__instance), HatStat.Reload);
    }
}

[HarmonyPatch(typeof(Player.FirearmController), nameof(Player.FirearmController.SetAnimatorAndProceduralValues))]
internal static class WeaponReloadAnimatorPatch
{
    private static void Prefix(Player.FirearmController __instance, out float __state)
    {
        __state = __instance.BuffInfo?.ReloadSpeed ?? 1f;
        if (__instance.BuffInfo is null)
        {
            return;
        }

        __instance.BuffInfo.ReloadSpeed = __state * LegendaryRuntime.GetReloadSpeedMultiplier(__instance.Item) * ChestRigNative.Reload(__instance) * GrenadeBuffs.Reload(BackpackRuntime.Actor(__instance)) * EarpieceRuntime.Reload(BackpackRuntime.Actor(__instance)) * Headwear.Factor(WeaponCombatState.Owner(__instance), HatStat.Reload);
    }

    private static void Postfix(Player.FirearmController __instance, float __state)
    {
        if (__instance.BuffInfo is not null)
        {
            __instance.BuffInfo.ReloadSpeed = __state;
        }
    }
}
