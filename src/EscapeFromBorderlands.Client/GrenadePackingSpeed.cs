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
        var inventory = Native.Get(operation, "_inventoryController") as InventoryController;
        var player = inventory is null ? null : BackpackRuntime.Actor(inventory);
        var current = GrenadeBuffs.Packing(player) * ServiceWindow.Speed(player);
        // The native delay includes creation-time bonuses and elite progression.
        // Replace only the live grenade/menu factors, retaining other bonuses.
        return nativeSeconds * initial / current;
    }
}

[HarmonyPatch]
internal static class GrenadePackingDelayPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] {
        AccessTools.Method(typeof(Player.PlayerInventoryController.LoadMagazineProcess), "method_5"),
        AccessTools.Method(typeof(Player.PlayerInventoryController.UnloadMagazineProcess), "method_4") };
    private static void Prefix(object __instance, out float __state)
    {
        __state = Native.Number(__instance, "_cooldown");
        Native.Set(__instance, "_cooldown", GrenadePackingSpeed.Seconds(__instance, __state));
    }
    private static Exception? Finalizer(object __instance, float __state, Exception? __exception)
    { Native.Set(__instance, "_cooldown", __state); return __exception; }
}
