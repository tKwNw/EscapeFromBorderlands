using System;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

// One EFB scope owns Consume's shared multiplier. Independent backpack/melee
// finalizers restore in registration order, so they cannot safely nest here.
[HarmonyPatch(typeof(Stamina), nameof(Stamina.Consume))]
internal static class LegendaryStaminaCostPatch
{
    private static void Prefix(Stamina __instance, Physical.Consumption consumption, out float __state)
    {
        __state = __instance.Multiplier;
        var jump = consumption.ConsumptionType == Physical.EConsumptionType.Jump;
        var vault = consumption.ConsumptionType is Physical.EConsumptionType.VaultLegs or Physical.EConsumptionType.ClimbLegs;
        if (!jump && !vault) return;

        var effect = MeleeRuntime.Effect(MeleeRuntime.Owner(__instance));
        var factor = jump ? effect?.JumpStaminaMultiplier ?? 1f : effect?.VaultLegStaminaMultiplier ?? 1f;
        var p = BackpackRuntime.Actor(__instance);
        if (p is not null)
        {
            var action = jump ? "jump" : "vault";
            factor *= BackpackRuntime.TraversalFactor(p, action);
            if(jump && ChestRigRuntime.Is(p,"AR25-2"))factor*=.2f;
        }
        __instance.Multiplier *= factor;
    }

    private static Exception? Finalizer(Stamina __instance, float __state, Exception? __exception)
    {
        __instance.Multiplier = __state;
        return __exception;
    }
}
