using EFT.Ballistics;
using EFT.InventoryLogic;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

[HarmonyPatch(typeof(BallisticsCalculator), nameof(BallisticsCalculator.CreateShot))]
internal static class BallisticEffectPatch
{
    private static void Postfix(
        BallisticsCalculator __instance,
        Ammo ammo,
        int fireIndex,
        string player,
        Item weapon,
        int fragmentIndex,
        Shot __result)
    {
        if (__result is null)
        {
            return;
        }

        var roll = RarityResolver.Resolve(weapon);
        var application = roll is not null && weapon is Weapon firearm
            ? LegendaryRuntime.RegisterShot(firearm, ammo, player, fireIndex, roll.Definition)
            : EffectApplication.Empty;
        if(roll is not null)
        {
            __result.Damage *= roll.DamageMultiplier;
            __result.PenetrationPower *= roll.PenetrationMultiplier;
            __result.RicochetChance = Clamp01(__result.RicochetChance + roll.RicochetChanceBonus);
            __result.FragmentationChance = Clamp01(__result.FragmentationChance + roll.FragmentationChanceBonus);
        }
        if(application.Active)
        {
            __result.Damage *= application.DamageMultiplier;
            __result.PenetrationPower *= application.PenetrationMultiplier;
            __result.ArmorDamage *= application.ArmorDamageMultiplier;
            __result.RicochetChance = Clamp01(__result.RicochetChance + application.RicochetChanceBonus);
            __result.FragmentationChance = Clamp01(__result.FragmentationChance + application.FragmentationChanceBonus);
        }
        ChestRigBallistics.Apply(__result,ammo,player,ref application);
        if (HatCartridge.Current is { } hat && weapon == hat.Gun) __result.PenetrationPower *= hat.Penetration;
        if (EarpieceRuntime.CurrentShot is { } earpiece && weapon == earpiece.Weapon) __result.Damage *= earpiece.Damage;
        LegendaryGrenadeEffects.Fragment(__result, weapon);
        if(application.Active && application.ProjectileCount>1)
        {
            __result.Damage *= application.ProjectileDamageMultiplier;
            if (fragmentIndex == 0)
            {
                using var timing = GrenadeSpikeTrace.MeasureWeapon(application.Definition, "extra projectiles");
                ProjectileEffectSpawner.Spawn(__instance, __result, application);
            }
        }
        LegendaryRuntime.Associate(__result,application);
        if(roll is not null)RarityResolver.LogRollOnce(weapon,roll);
    }

    private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
}
