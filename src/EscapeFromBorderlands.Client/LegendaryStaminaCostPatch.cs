using System;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

// One EFB scope owns Consume's shared multiplier. Independent backpack/melee
// finalizers restore in registration order, so they cannot safely nest here.
[HarmonyPatch(typeof(Stamina), nameof(Stamina.Consume))]
internal static class LegendaryStaminaCostPatch
{
    private static void Prefix(Stamina __instance, Physical.Consumption consumption, out float? __state)
    {
        __state = null;
        var jump = consumption.ConsumptionType == Physical.EConsumptionType.Jump;
        var vault = consumption.ConsumptionType is Physical.EConsumptionType.VaultLegs or Physical.EConsumptionType.ClimbLegs;
        if (!jump && !vault) return;

        var meleeOwner = MeleeRuntime.Owner(__instance);
        var p = BackpackRuntime.Actor(__instance);
        if (meleeOwner is null && p is null) return;
        var effect = MeleeRuntime.Effect(meleeOwner);
        var factor = jump ? effect?.JumpStaminaMultiplier ?? 1f : effect?.VaultLegStaminaMultiplier ?? 1f;
        if (p is not null)
        {
            var action = jump ? "jump" : "vault";
            factor *= BackpackRuntime.TraversalFactor(p, action);
            if(jump && ChestRigRuntime.Is(p,"AR25-2"))factor*=.2f;
        }
        if (factor == 1f) return;
        __state = __instance.Multiplier;
        __instance.Multiplier *= factor;
    }

    private static Exception? Finalizer(Stamina __instance, float? __state, Exception? __exception)
    {
        if (__state is { } multiplier) __instance.Multiplier = multiplier;
        return __exception;
    }
}
