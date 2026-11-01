using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.SynchronizableObjects;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;
internal static class BackpackTripwire
{
    internal static readonly Dictionary<string, (Player Player, Item Bag)> Recover = new(StringComparer.Ordinal);
    internal static ItemAddress? Space(Item bag, Item grenade) => Native.Items(Native.Get(bag, "Grids")).OfType<StashGridClass>()
        .Select(grid => grid.FindLocationForItem(grenade)).FirstOrDefault(address => address is not null);
    internal static async Task Store(Player p, Item bag, Item item)
    {
        try
        {
            if (BackpackRuntime.Worn(p)?.Id != bag.Id || Space(bag, item) is not { } address) return;
            var move = InteractionsHandlerClass.Move(item, address, p.InventoryController, simulate: true);
            if (!move.Failed) await p.InventoryController.TryRunNetworkTransaction(move);
        }
        catch (Exception e) { Plugin.LogSource.LogWarning($"No Loose Ends left the recovered grenade on the ground: {e.Message}"); }
    }
}
[HarmonyPatch]
internal static class BackpackDisarmSpeedPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("GetActionsClass+Class1768"), "method_0");
    private static bool Prefix(object __instance, out float __state)
    {
        __state = Native.Number(__instance, "plantTime");
        var p = BackpackRuntime.Actor(Native.Get(__instance, "owner"));
        var s = BackpackRuntime.Get(p);
        if (s?.Key != "BP33-1") return true;
        var grenade = Native.Get(Native.Get(__instance, "tripwire"), "_originalGrenade") as Item;
        if (grenade is null || BackpackTripwire.Space(s.Bag, grenade) is null) return false;
        Native.Set(__instance, "plantTime", __state / 1.5f); return true;
    }
    private static Exception? Finalizer(object __instance, float __state, Exception? __exception) { Native.Set(__instance, "plantTime", __state); return __exception; }
}
[HarmonyPatch(typeof(LocalPlayer), "vmethod_4")]
internal static class BackpackDisarmRecoveryPatch
{
    private static void Prefix(LocalPlayer __instance, TripwireSynchronizableObject tripwire)
    {
        var s = BackpackRuntime.Get(__instance);
        if (s?.Key == "BP33-1" && Native.Get(tripwire, "_originalGrenade") is Item grenade && BackpackTripwire.Space(s.Bag, grenade) is not null)
            BackpackTripwire.Recover[grenade.Id] = (__instance, s.Bag);
    }
}
[HarmonyPatch]
internal static class BackpackDisarmedGrenadePatch
{
    private static IEnumerable<MethodBase> TargetMethods() => typeof(GameWorld).GetMethods().Where(m => m.Name == "ThrowItem" && m.GetParameters().Length == 9);
    private static void Postfix(Item item)
    {
        if (!BackpackTripwire.Recover.TryGetValue(item.Id, out var owner)) return;
        BackpackTripwire.Recover.Remove(item.Id);
        _ = BackpackTripwire.Store(owner.Player, owner.Bag, item);
    }
}
