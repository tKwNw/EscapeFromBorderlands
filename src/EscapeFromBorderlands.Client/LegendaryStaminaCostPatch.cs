using System;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

// One EFB scope owns Consume's shared multiplier. Independent backpack/melee
// finalizers restore in registration order, so they cannot safely nest here.
[HarmonyPatch(typeof(GClass774), nameof(GClass774.Consume))]
internal static class LegendaryStaminaCostPatch
{
    private static void Prefix(GClass774 __instance, PlayerPhysicalClass.GClass773 consumption, out float? __state)
    {
        __state = null;
        var jump = consumption.ConsumptionType == PlayerPhysicalClass.EConsumptionType.Jump;
        var vault = consumption.ConsumptionType is PlayerPhysicalClass.EConsumptionType.VaultLegs or PlayerPhysicalClass.EConsumptionType.ClimbLegs;
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

    private static Exception? Finalizer(GClass774 __instance, float? __state, Exception? __exception)
    {
        if (__state is { } multiplier) __instance.Multiplier = multiplier;
        return __exception;
    }
}
