using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

internal static class GrenadePackingSpeed
{
    private sealed class Entry { internal float InitialFactor; }
    private static ConditionalWeakTable<object, Entry> Operations = new();
    internal static void Reset() => Operations = new();
    internal static T Track<T>(T operation, float factor) where T : class
    { Operations.Add(operation, new Entry { InitialFactor = factor }); return operation; }
    internal static float Seconds(object operation, float nativeSeconds)
    {
        var initial = Operations.TryGetValue(operation, out var entry) ? entry.InitialFactor : 1f;
        var inventory = Native.Get(operation, "InventoryController_0") as InventoryController;
        var player = inventory is null ? null : BackpackRuntime.Actor(inventory);
        var current = GrenadeBuffs.Packing(player) * ServiceWindow.Speed(player);
        // Float_1 includes creation-time bonuses and native elite progression.
        // Replace only the live grenade/menu factors, retaining other bonuses.
        return nativeSeconds * initial / current;
    }
}

[HarmonyPatch]
internal static class GrenadePackingDelayPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] {
        AccessTools.Method(typeof(Player.PlayerInventoryController.Class1204), "method_5"),
        AccessTools.Method(typeof(Player.PlayerInventoryController.Class1207), "method_4") };
    private static void Prefix(object __instance, out float __state)
    {
        __state = Native.Number(__instance, "Float_1");
        Native.Set(__instance, "Float_1", GrenadePackingSpeed.Seconds(__instance, __state));
    }
    private static Exception? Finalizer(object __instance, float __state, Exception? __exception)
    { Native.Set(__instance, "Float_1", __state); return __exception; }
}
