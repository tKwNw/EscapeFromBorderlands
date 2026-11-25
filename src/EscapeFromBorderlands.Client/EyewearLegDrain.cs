using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

// Only a triggered human Emergency Landing registers an endpoint. The emitted
// native fast path does not call EFB when this collection is empty; bot/arm
// endpoints never register. Restoration and native notifications remain intact.
[HarmonyPatch]
internal static class EyewearLegDrain
{
    private static readonly Dictionary<GClass774, Eyewear.State> Active = new();
    private static int Count;
    private static readonly FieldInfo Labels = typeof(CodeInstruction).GetField("labels")!;
    private static void LabelOn(CodeInstruction code, Label label) => ((IList)Labels.GetValue(code)!).Add(label);
    internal static void Activate(Eyewear.State s)
    {
        if (!s.Human || s.Key != "EW030" || s.Player.Physical?.Stamina is not { } leg) return;
        Active[leg] = s; Count = Active.Count;
    }
    internal static void Forget(Eyewear.State s)
    { if (s.Player.Physical?.Stamina is { } leg) Active.Remove(leg); Count = Active.Count; }
    internal static void Reset() { Active.Clear(); Count = 0; }
    private static bool Enabled(GClass774 stamina)
    {
        if (!Active.TryGetValue(stamina, out var s)) return false;
        if (Time.time < s.Second && Eyewear.Get(s.Player) == s) return true;
        Active.Remove(stamina); Count = Active.Count;
        return false;
    }
    private static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        AccessTools.Method(typeof(GClass774), "Consume", new[] { typeof(PlayerPhysicalClass.GClass773), typeof(bool) }),
        AccessTools.Method(typeof(GClass774), "Process", new[] { typeof(float) })
    };
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase original)
    {
        var body = new List<CodeInstruction>(instructions);
        var active = generator.DeclareLocal(typeof(bool));
        var native = generator.DefineLabel();
        yield return new CodeInstruction(OpCodes.Ldc_I4_0);
        yield return new CodeInstruction(OpCodes.Stloc, active);
        yield return new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(EyewearLegDrain), nameof(Count)));
        yield return new CodeInstruction(OpCodes.Brfalse, native);
        yield return new CodeInstruction(OpCodes.Ldarg_0);
        yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(EyewearLegDrain), nameof(Enabled)));
        yield return new CodeInstruction(OpCodes.Stloc, active);
        if (body.Count == 0) throw new InvalidOperationException("Empty native stamina method.");
        LabelOn(body[0], native);
        var delta = false; var replaced = 0;
        foreach (var code in body)
        {
            if (code.opcode.OperandType == OperandType.ShortInlineBrTarget) code.opcode = LongBranch(code.opcode);
            yield return code;
            if (delta && code.operand is MethodInfo method && method.DeclaringType == typeof(GClass848<float>) && method.Name is "get_Value" or "op_Implicit")
            {
                var keep = generator.DefineLabel();
                yield return new CodeInstruction(OpCodes.Ldloc, active);
                yield return new CodeInstruction(OpCodes.Brfalse, keep);
                // Negative deltas can restore stamina; only positive costs
                // become zero. Both the amount and downtime test see zero.
                yield return new CodeInstruction(OpCodes.Dup);
                yield return new CodeInstruction(OpCodes.Ldc_R4, 0f);
                yield return new CodeInstruction(OpCodes.Ble_Un, keep);
                yield return new CodeInstruction(OpCodes.Pop);
                yield return new CodeInstruction(OpCodes.Ldc_R4, 0f);
                var end = new CodeInstruction(OpCodes.Nop); LabelOn(end, keep); yield return end;
                replaced++;
            }
            delta = code.opcode == OpCodes.Ldfld && code.operand is FieldInfo field && field.DeclaringType == typeof(PlayerPhysicalClass.GClass773) && field.Name == "Delta";
        }
        var expected = original.Name == "Process" ? 2 : 1;
        if (replaced != expected) throw new InvalidOperationException($"Expected {expected} stamina cost reads in {original.Name}, found {replaced}.");
    }
    private static OpCode LongBranch(OpCode code)
    {
        if (code == OpCodes.Br_S) return OpCodes.Br;
        if (code == OpCodes.Brfalse_S) return OpCodes.Brfalse;
        if (code == OpCodes.Brtrue_S) return OpCodes.Brtrue;
        if (code == OpCodes.Beq_S) return OpCodes.Beq;
        if (code == OpCodes.Bge_S) return OpCodes.Bge;
        if (code == OpCodes.Bge_Un_S) return OpCodes.Bge_Un;
        if (code == OpCodes.Bgt_S) return OpCodes.Bgt;
        if (code == OpCodes.Bgt_Un_S) return OpCodes.Bgt_Un;
        if (code == OpCodes.Ble_S) return OpCodes.Ble;
        if (code == OpCodes.Ble_Un_S) return OpCodes.Ble_Un;
        if (code == OpCodes.Blt_S) return OpCodes.Blt;
        if (code == OpCodes.Blt_Un_S) return OpCodes.Blt_Un;
        if (code == OpCodes.Bne_Un_S) return OpCodes.Bne_Un;
        if (code == OpCodes.Leave_S) return OpCodes.Leave;
        throw new InvalidOperationException("Unsupported short stamina branch.");
    }
}
