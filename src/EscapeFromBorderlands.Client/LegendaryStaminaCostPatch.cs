using System;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

// One EFB scope owns Consume's shared multiplier. Independent backpack/melee
// finalizers restore in registration order, so they cannot safely nest here.
[HarmonyPatch(typeof(GClass774), nameof(GClass774.Consume))]
internal static class LegendaryStaminaCostPatch
{
    private static void Prefix(GClass774 __instance, PlayerPhysicalClass.GClass773 consumption, out float __state)
    {
        __state = __instance.Multiplier;
        var jump = consumption.ConsumptionType == PlayerPhysicalClass.EConsumptionType.Jump;
        var vault = consumption.ConsumptionType is PlayerPhysicalClass.EConsumptionType.VaultLegs or PlayerPhysicalClass.EConsumptionType.ClimbLegs;
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

    private static Exception? Finalizer(GClass774 __instance, float __state, Exception? __exception)
    {
        __instance.Multiplier = __state;
        return __exception;
    }
}
