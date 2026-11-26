using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Scripting;

namespace EscapeFromBorderlands.Client;

// Optional compatibility boundary for the two verified SPT versions. Preserve
// the inventory condition and GCMode setter in SPT; only spread its explicit
// 25 ms CollectIncremental request. Never disable GC or alter global settings.
[HarmonyPatch]
internal static class InventoryCollectionSmoothing
{
    private static readonly IncrementalCollectionBudget Budget = new();
    private static readonly Func<ulong, bool> Collect = CollectSlice;
    private static readonly Func<ulong, bool> CollectUpdate = Collect, CollectBoundary = Collect, CollectImmediate = Collect;
    private static GameWorld? World;
    private static MethodBase? Target;
    private static bool Prepare()
    {
        Target = AccessTools.Method(AccessTools.TypeByName("SPT.Custom.Patches.MemoryCollectionPatch"), "PatchPostfix");
        return Target is MethodInfo method && method.IsStatic && method.ReturnType == typeof(void);
    }
    private static MethodBase TargetMethod() => Target!;
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var native = AccessTools.Method(typeof(GarbageCollector), nameof(GarbageCollector.CollectIncremental), new[] { typeof(ulong) });
        var matches = new List<int>();
        for (var i = 2; i < code.Count; i++)
            if (code[i].Calls(native) && code[i - 1].opcode == OpCodes.Conv_I8 &&
                code[i - 2].opcode == OpCodes.Ldc_I4 && Equals(code[i - 2].operand, 25_000_000))
                matches.Add(i);
        // Modified/unknown SPT implementations keep all their existing code.
        if (matches.Count != 1 || code.Count(c => c.Calls(native)) != 1)
            return code;
        code[matches[0]].operand = AccessTools.Method(typeof(InventoryCollectionSmoothing), nameof(Request));
        return code;
    }
    private static bool Active(GameWorld? world) => PlayerRefresh.ActiveWorld(world) is { MainPlayer: { } player } &&
        player.HealthController?.IsAlive == true && GarbageCollector.isIncremental &&
        GarbageCollector.GCMode == GarbageCollector.Mode.Enabled;
    private static ulong Slice => Math.Max(3_000_000UL, GarbageCollector.incrementalTimeSliceNanoseconds);
    private static bool Request(ulong amount)
    {
        var world = Singleton<GameWorld>.Instance;
        if (!Active(world))
        {
            Reset();
            return CollectImmediate(amount);
        }
        if (World != world)
        {
            Reset();
            World = world;
        }
        return Budget.Request(amount, Time.frameCount, Slice, Collect);
    }
    private static bool CollectSlice(ulong amount) => GarbageCollector.CollectIncremental(amount);
    internal static void Maintain(GameWorld? world)
    {
        if (Budget.Remaining == 0)
            return;
        if (world != World || !Active(world))
        {
            Reset();
            return;
        }
        Budget.Drain(Time.frameCount, Slice, CollectUpdate);
    }
    internal static void Reset()
    {
        World = null;
        // Finish any outstanding request at a session boundary, rather than
        // carrying it into another raid. CollectIncremental respects GCMode.
        Budget.Flush(CollectBoundary);
    }
}
