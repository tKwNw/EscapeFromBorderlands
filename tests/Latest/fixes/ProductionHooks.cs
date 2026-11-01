using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EFT.Interactive;
using HarmonyLib;
using UnityEngine;
namespace EscapeFromBorderlands.Client;
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

[HarmonyPatch(typeof(TreeInteractive), "method_0")]
internal static class BackpackFoliageSoundPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var volume = AccessTools.Field(typeof(TreeInteractive), "_lastVolume");
        var replacements = 0;
        for (var i = 0; i < code.Count; i++)
        {
            yield return code[i];
            // Scale the final local copied after BOTH native player branches.
            // The nonlocal branch overwrites float_1 with its occluded volume.
            if (!code[i].LoadsField(volume) || i + 1 >= code.Count) continue;
            var next = code[i + 1].opcode;
            if (next != OpCodes.Stloc && next != OpCodes.Stloc_S && next != OpCodes.Stloc_0 && next != OpCodes.Stloc_1 && next != OpCodes.Stloc_2 && next != OpCodes.Stloc_3) continue;
            yield return new CodeInstruction(OpCodes.Ldarg_3);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BackpackFoliageSoundPatch), nameof(Volume)));
            replacements++;
        }
        if (replacements != 1) throw new InvalidOperationException($"Expected one final native foliage volume load; found {replacements}.");
    }
    internal static float Volume(float volume, object player) => Native.Get(player, "iPlayer") is Player p ? volume * BackpackRuntime.Noise(p, "foliage") : volume;
}


internal static class BackpackSurgeryPatch
{
    private static void Prefix(ActiveHealthController __instance, ref float healthPenalty)
    {
        var s = BackpackRuntime.Get(__instance.Player);
        if (s?.Key == "BP23-2" && BackpackNative.Uses.Values.Any(u => u.From(s))) healthPenalty = 1f - (1f - healthPenalty) * .25f;
    }
}


internal static class BackpackEnergyPatch
{
    private static void Prefix(ActiveHealthController __instance, ref float value)
    {
        value = BackpackMedicalRestoration.Adjust(__instance, value, false);
        var s = BackpackRuntime.Get(__instance.Player); if (s is null) return;
        if (value < 0f && !BackpackMedicalRestoration.InScope && s.Key == "BP06-2" && __instance.Player.IsSprintEnabled && s.Bag.GetAllItems().Where(i => Native.Component<FoodDrinkComponent>(i) is not null && i.Template.ParentId != "5448e8d64bdc2dce718b4568").Select(i => i.StringTemplateId).Distinct().Count() >= 2) value *= .4f;
    }
}


internal static class BackpackHydrationPatch
{
    private static void Prefix(ActiveHealthController __instance, ref float value)
    {
        value = BackpackMedicalRestoration.Adjust(__instance, value, true);
    }
}
