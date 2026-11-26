using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EFT;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

// Move the existing postfix into the native return path only on compatible
// stacks. Ordinary cached values do not enter legendary effect callbacks.
internal static class HumanCachedValueRoute
{
    private sealed class Route
    {
        internal readonly MethodInfo Target, Postfix, Guard;
        internal bool Installed;
        internal Route(Type type, Type patch, string callback, string guard)
        {
            Target = AccessTools.PropertyGetter(type, "Value");
            Postfix = AccessTools.Method(patch, callback);
            Guard = AccessTools.Method(typeof(HumanCachedValueRoute), guard);
        }
    }
    private static readonly Route[] Routes = {
        new(typeof(GClass848<float>), typeof(BackpackCachedValuePatch), "Postfix", nameof(Registered)),
        new(typeof(GClass849<float>), typeof(ConsolidatedHooks), "CachedWeight", nameof(Eligible))
    };
    private static readonly MethodInfo Rewrite = AccessTools.Method(typeof(HumanCachedValueRoute), nameof(Transpiler));
    // Unity's Harmony uses Mono's Label type; reflection is installation-only.
    private static readonly FieldInfo Labels = typeof(CodeInstruction).GetField("labels")!;

    internal static bool Registered(GClass848<float> value) => BackpackRuntime.Values.Count != 0 && BackpackRuntime.Values.ContainsKey(value);
    internal static bool Eligible(GClass849<float> value)
    {
        if (Registered(value))
            return true;
        var player = BackpackWeightScope.Player;
        if (player is not null)
        {
            var inventory = player.InventoryController.Inventory;
            if (ReferenceEquals(value, inventory.TotalWeight) || ReferenceEquals(value, inventory.TotalWeightEliteSkill))
                return true;
        }
        var melee = MeleeWeightScope.Inventory;
        return melee is not null && (ReferenceEquals(value, melee.TotalWeight) || ReferenceEquals(value, melee.TotalWeightEliteSkill));
    }
    internal static void Install(Harmony harmony)
    {
        foreach (var route in Routes)
        {
            var target = route.Target;
            if (route.Installed || target is null || target.IsStatic || target.ReturnType != typeof(float) ||
                target.GetMethodBody() is not { } body || body.ExceptionHandlingClauses.Count != 0)
                continue;
            var info = Harmony.GetPatchInfo(target);
            if (info is null || info.Owners.Any(o => o != Plugin.PluginGuid) || info.Prefixes.Count != 0 ||
                info.Transpilers.Count != 0 || info.Finalizers.Count != 0 || info.Postfixes.Count != 1)
                continue;
            var patch = info.Postfixes[0];
            if (patch.PatchMethod != route.Postfix || patch.priority != Priority.Normal || patch.before.Length != 0 || patch.after.Length != 0)
                continue;
            try
            {
                harmony.Unpatch(target, route.Postfix);
                harmony.Patch(target, transpiler: new HarmonyMethod(Rewrite));
                route.Installed = true;
                Plugin.LogSource?.LogInfo($"EFB human cached-value route enabled for {target.DeclaringType?.Name}.");
            }
            catch (Exception error)
            {
                harmony.Unpatch(target, Rewrite);
                Restore(harmony, route);
                Plugin.LogSource?.LogWarning($"EFB retained cached-value callback: {error.Message}");
            }
        }
    }
    internal static void Reconcile(Harmony harmony)
    {
        foreach (var route in Routes)
        {
            if (!route.Installed)
                continue;
            var info = Harmony.GetPatchInfo(route.Target);
            if (info is not null && info.Owners.All(o => o == Plugin.PluginGuid) && info.Prefixes.Count == 0 &&
                info.Postfixes.Count == 0 && info.Finalizers.Count == 0 && info.Transpilers.Count == 1 && info.Transpilers[0].PatchMethod == Rewrite)
                continue;
            harmony.Unpatch(route.Target, Rewrite);
            Restore(harmony, route);
            route.Installed = false;
            Plugin.LogSource?.LogInfo("EFB restored cached-value callback for a changed patch stack.");
        }
    }
    private static void Restore(Harmony harmony, Route route)
    {
        if (Harmony.GetPatchInfo(route.Target)?.Postfixes.Any(p => p.PatchMethod == route.Postfix) != true)
            harmony.Patch(route.Target, postfix: new HarmonyMethod(route.Postfix) { priority = Priority.Normal });
    }
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase __originalMethod)
    {
        var route = Routes.Single(r => r.Target == __originalMethod);
        var body = instructions.ToList();
        if (body.Count == 0 || body.Any(c => c.blocks.Count != 0) || !body.Any(c => c.opcode == OpCodes.Ret))
            throw new InvalidOperationException("Unsupported cached-value control flow.");
        var result = generator.DeclareLocal(typeof(float));
        var output = new List<CodeInstruction>();
        foreach (var code in body)
        {
            if (code.opcode != OpCodes.Ret)
            {
                if (code.opcode.OperandType == OperandType.ShortInlineBrTarget)
                    code.opcode = LongBranch(code.opcode);
                output.Add(code);
                continue;
            }
            // Retain labels on every native return, including early returns.
            code.opcode = OpCodes.Stloc;
            code.operand = result;
            output.Add(code);
            var done = generator.DefineLabel();
            output.Add(new CodeInstruction(OpCodes.Ldarg_0));
            output.Add(new CodeInstruction(OpCodes.Call, route.Guard));
            output.Add(new CodeInstruction(OpCodes.Brfalse, done));
            output.Add(new CodeInstruction(OpCodes.Ldarg_0));
            output.Add(new CodeInstruction(OpCodes.Ldloca, result));
            output.Add(new CodeInstruction(OpCodes.Call, route.Postfix));
            var load = new CodeInstruction(OpCodes.Ldloc, result);
            ((System.Collections.IList)Labels.GetValue(load)!).Add(done);
            output.Add(load);
            output.Add(new CodeInstruction(OpCodes.Ret));
        }
        return output;
    }
    private static OpCode LongBranch(OpCode code)
    {
        // Resolve the matching long form once while Harmony emits the getter.
        if (code == OpCodes.Leave_S)
            return OpCodes.Leave;
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (field.GetValue(null) is OpCode candidate && candidate.OperandType == OperandType.InlineBrTarget && candidate.Name == code.Name?.Replace(".s", ""))
                return candidate;
        throw new InvalidOperationException("Unsupported cached-value short branch.");
    }
}
