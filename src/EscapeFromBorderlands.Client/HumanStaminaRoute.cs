using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EFT;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

// Keep native stamina processing for every actor. Enter the legendary sprint
// helpers only for registered human leg-stamina endpoints. Incompatible patch
// stacks retain the ordinary consolidated prefix/postfix/finalizer instead.
internal static class HumanStaminaRoute
{
    private static readonly MethodInfo Target = AccessTools.Method(typeof(GClass774), "Process", new[] { typeof(float) });
    private static readonly MethodInfo Begin = AccessTools.Method(typeof(ConsolidatedHooks), "SprintBegin");
    private static readonly MethodInfo End = AccessTools.Method(typeof(ConsolidatedHooks), "SprintEnd");
    private static readonly MethodInfo Finally = AccessTools.Method(typeof(ConsolidatedHooks), "SprintFinally");
    private static readonly MethodInfo Rewrite = AccessTools.Method(typeof(HumanStaminaRoute), nameof(Transpiler));
    private static readonly MethodInfo EyewearRewrite = AccessTools.Method(typeof(EyewearLegDrain), "Transpiler");
    private static bool Installed;
    // The game's Harmony references Mono's mscorlib Label; our netstandard
    // reference assembly cannot type that field. They unify in the game host.
    // Reflection is confined to installation, never stamina processing.
    private static readonly FieldInfo Labels = typeof(CodeInstruction).GetField("labels")!;
    private static void LabelOn(CodeInstruction code, Label label) => ((System.Collections.IList)Labels.GetValue(code)!).Add(label);

    internal static bool Eligible(GClass774 stamina) => MeleeRuntime.Owner(stamina) is not null;
    private static bool Only(Patch[] patches, MethodInfo method) => patches.Length == 1 && patches[0].PatchMethod == method &&
        patches[0].priority == Priority.Normal && patches[0].before.Length == 0 && patches[0].after.Length == 0;
    internal static void Install(Harmony harmony)
    {
        if (Installed || Target is null || Target.ReturnType != typeof(void) || Target.IsStatic ||
            Target.GetMethodBody() is not { } body || body.ExceptionHandlingClauses.Count != 0) return;
        var info = Harmony.GetPatchInfo(Target);
        if (info is null || info.Owners.Any(owner => owner != Plugin.PluginGuid) || info.Transpilers.Count > 1 || info.Transpilers.Any(p => p.PatchMethod != EyewearRewrite) ||
            !Only(info.Prefixes.ToArray(), Begin) || !Only(info.Postfixes.ToArray(), End) || !Only(info.Finalizers.ToArray(), Finally)) return;
        try
        {
            harmony.Unpatch(Target, Begin); harmony.Unpatch(Target, End); harmony.Unpatch(Target, Finally);
            harmony.Patch(Target, transpiler: new HarmonyMethod(Rewrite));
            Installed = true;
            Plugin.LogSource?.LogInfo("EFB human leg-stamina route enabled; bot and arm/oxygen processing bypass legendary sprint callbacks.");
        }
        catch (Exception error)
        {
            harmony.Unpatch(Target, Rewrite);
            Restore(harmony);
            Plugin.LogSource?.LogWarning($"EFB retained consolidated stamina hooks: {error.Message}");
        }
    }
    internal static void Reconcile(Harmony harmony)
    {
        if (!Installed) return;
        var info = Harmony.GetPatchInfo(Target);
        if (info is not null && info.Owners.All(owner => owner == Plugin.PluginGuid) &&
            info.Transpilers.Count is 1 or 2 && info.Transpilers.Count(p => p.PatchMethod == Rewrite) == 1 &&
            info.Transpilers.All(p => p.PatchMethod == Rewrite || p.PatchMethod == EyewearRewrite) &&
            info.Prefixes.Count == 0 && info.Postfixes.Count == 0 && info.Finalizers.Count == 0) return;
        harmony.Unpatch(Target, Rewrite);
        Restore(harmony);
        Installed = false;
        Plugin.LogSource?.LogInfo("EFB restored consolidated stamina hooks for a changed patch stack.");
    }
    private static void Restore(Harmony harmony) => harmony.Patch(Target,
        prefix: new HarmonyMethod(Begin) { priority = Priority.Normal },
        postfix: new HarmonyMethod(End) { priority = Priority.Normal },
        finalizer: new HarmonyMethod(Finally) { priority = Priority.Normal });

    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var body = instructions.ToList();
        if (body.Count == 0 || body.Any(code => code.blocks.Count != 0) || !body.Any(code => code.opcode == OpCodes.Ret))
            throw new InvalidOperationException("Unsupported stamina control flow.");
        var run = generator.DeclareLocal(typeof(bool));
        var state = generator.DeclareLocal(typeof(ConsolidatedHooks.SprintState));
        var native = generator.DefineLabel();
        var observe = generator.DefineLabel();
        var done = generator.DefineLabel();
        var finish = generator.DefineLabel();
        var afterRestore = generator.DefineLabel();
        var output = new List<CodeInstruction>
        {
            new(OpCodes.Ldarg_0),
            new(OpCodes.Call, AccessTools.Method(typeof(HumanStaminaRoute), nameof(Eligible))),
            new(OpCodes.Stloc, run),
            new(OpCodes.Ldloca, state), new(OpCodes.Initobj, typeof(ConsolidatedHooks.SprintState))
        };
        var start = new CodeInstruction(OpCodes.Ldloc, run);
        start.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        output.Add(start);
        output.Add(new CodeInstruction(OpCodes.Brfalse, native));
        output.Add(new CodeInstruction(OpCodes.Ldarg_0)); output.Add(new CodeInstruction(OpCodes.Ldloca, state));
        output.Add(new CodeInstruction(OpCodes.Call, Begin));
        LabelOn(body[0], native);
        foreach (var code in body)
        {
            if (code.opcode == OpCodes.Ret) { code.opcode = OpCodes.Br; code.operand = observe; }
            // Replacing ret can lengthen a native short branch's span.
            else if (code.opcode.OperandType == OperandType.ShortInlineBrTarget)
                code.opcode = LongBranch(code.opcode);
            output.Add(code);
        }
        // Returns stay inside this try until the ordinary postfix has run.
        var postfix = new CodeInstruction(OpCodes.Ldloc, run); LabelOn(postfix, observe); output.Add(postfix);
        output.Add(new CodeInstruction(OpCodes.Brfalse, done));
        output.Add(new CodeInstruction(OpCodes.Ldarg_0)); output.Add(new CodeInstruction(OpCodes.Ldarg_1));
        output.Add(new CodeInstruction(OpCodes.Ldloc, state)); output.Add(new CodeInstruction(OpCodes.Call, End));
        var leave = new CodeInstruction(OpCodes.Leave, finish); LabelOn(leave, done); output.Add(leave);
        var cleanup = new CodeInstruction(OpCodes.Ldloc, run);
        cleanup.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginFinallyBlock)); output.Add(cleanup);
        output.Add(new CodeInstruction(OpCodes.Brfalse, afterRestore));
        output.Add(new CodeInstruction(OpCodes.Ldloc, state)); output.Add(new CodeInstruction(OpCodes.Ldnull));
        output.Add(new CodeInstruction(OpCodes.Call, Finally)); output.Add(new CodeInstruction(OpCodes.Pop));
        var endFinally = new CodeInstruction(OpCodes.Nop); LabelOn(endFinally, afterRestore);
        endFinally.blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock)); output.Add(endFinally);
        var ret = new CodeInstruction(OpCodes.Ret); LabelOn(ret, finish); output.Add(ret);
        return output;
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
        throw new InvalidOperationException("Unsupported short branch in stamina processing.");
    }
}
