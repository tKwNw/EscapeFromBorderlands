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

internal static class BackpackWeightScope
{
    [ThreadStatic] public static Player? Player;
}


internal static class BackpackPhysicalWeightPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var field = AccessTools.Field(typeof(PhysicalBase), "WalkSpeedLimit");
        var count = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == System.Reflection.Emit.OpCodes.Stfld && Equals(instruction.operand, field))
            {
                // Correct only the native weight limit, before native movement
                // combines it with aiming, terrain and all other restrictions.
                var opcode = instruction.opcode; var operand = instruction.operand;
                instruction.opcode = System.Reflection.Emit.OpCodes.Ldarg_0; instruction.operand = null;
                yield return instruction;
                yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Call, AccessTools.Method(typeof(BackpackPhysicalWeightPatch), nameof(WalkLimit)));
                yield return new CodeInstruction(opcode, operand);
                count++;
                continue;
            }
            yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException($"Expected one native walking weight limit assignment; found {count}.");
    }
    // Feather Light removes only its own template penalties and empty weight.
    // Contents and all other carried gear still incur their ordinary weight penalties.
    internal static float WalkLimit(float value, Physical physical) => value;
    internal sealed class Snapshot
    {
        public Player? Previous;
        public Vector2 Base, Walk, Sprint, Speed;
    }
    private static void Prefix(Physical __instance, out Snapshot __state)
    {
        __state = new Snapshot { Previous = BackpackWeightScope.Player, Base = __instance.BaseOverweightLimits, Walk = __instance.WalkOverweightLimits, Sprint = __instance.SprintOverweightLimits, Speed = __instance.WalkSpeedOverweightLimits };
        var p = BackpackRuntime.Actor(__instance); BackpackWeightScope.Player = p;
        var factor = BackpackRuntime.Get(p)?.Roll.Backpack.OverweightThreshold ?? 1f;
        __instance.BaseOverweightLimits *= factor; __instance.WalkOverweightLimits *= factor;
        __instance.SprintOverweightLimits *= factor; __instance.WalkSpeedOverweightLimits *= factor;
    }
    private static Exception? Finalizer(Physical __instance, Snapshot __state, Exception? __exception)
    {
        __instance.BaseOverweightLimits = __state.Base; __instance.WalkOverweightLimits = __state.Walk;
        __instance.SprintOverweightLimits = __state.Sprint; __instance.WalkSpeedOverweightLimits = __state.Speed;
        BackpackWeightScope.Player = __state.Previous; return __exception;
    }
}


internal static class BackpackBreachRewardPatch
{
    private static void Prefix(WorldInteractiveObject interactive, out bool __state) => __state = (interactive as Door)?.IsBroken == true;
    private static void Postfix(WorldInteractiveObject interactive, InteractionResult interactionResult, Player user, bool __state)
    {
        if (__state || interactionResult.InteractionType != EInteractionType.Breach || interactive is not Door { IsBroken: true }) return;
        var s = BackpackRuntime.Get(user); if (s?.Key == "BP35-3") s.Record.Arm("meleeReach", 20f);
    }
}
