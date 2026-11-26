using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class BackpackSearch
{
    internal static Player? Owner(GClass3515 operation)
    {
        var world = Singleton<GameWorld>.Instance;
        if (world is null)
            return null;
        var id = operation.Profile_0.Id;
        // Preserve registered-player order and dead-owner fallback, without
        // allocating an OfType iterator, predicate and captured operation.
        foreach (var candidate in world.RegisteredPlayers)
            if (candidate is Player player && player.ProfileId == id)
                return player;
        return null;
    }
    internal static bool Corpse(Item item)
    {
        return CorpseRoot(item) is not null;
    }
    internal static InventoryEquipment? CorpseRoot(Item item) => item.Parent is null ? null : GClass3113.GetOwner(item.Parent)?.RootItem as InventoryEquipment;
    internal static float TimeFactor(Player p, Item searched)
    {
        var rigFactor = (ChestRigRuntime.Is(p, "TR19-2") ? 1f / 1.75f : 1f) / Headwear.Factor(p, HatStat.Search);
        var s = BackpackRuntime.Get(p);
        if (s is null)
            return rigFactor;
        var time = s.Roll.Backpack.SearchTime;
        if (s.Key == "BP08-3" && Corpse(searched) &&
            p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem is null &&
            p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem is null)
            time /= 1.7f;
        if (s.Key == "BP24-2")
            time /= 1f + .1f * Math.Min(6, s.Record.Categories.Count);
        if (s.Key == "BP38-1" && Corpse(searched))
            time *= .5f;
        return time * rigFactor;
    }
    internal static async Task Search(GClass3515 operation, Player p)
    {
        var world = PlayerRefresh.ActiveWorld(Singleton<GameWorld>.Instance);
        var generation = PlayerRefresh.Generation;
        if (!PlayerRefresh.IsCurrentSession(world, generation))
            return;
        if (!operation.IplayerSearchController_0.ContainsUnknownItems(operation.Item))
            return;
        while (operation.method_7(out _))
        {
            if (!PlayerRefresh.IsCurrentSession(world, generation))
                return;
            var skills = operation.Profile_0.SkillsInfo;
            var speed = 1f + skills.AttentionLootSpeedValue + (Corpse(operation.Item) ? skills.SearchBuffSpeedValue : 0f);
            var seconds = operation.Bool_0 ? 0f : UnityEngine.Random.Range(1, 3) / speed * TimeFactor(p, operation.Item);
            await Task.Delay((int)(seconds * 1000f), operation.CancellationTokenSource_0.Token);
            if (!PlayerRefresh.IsCurrentSession(world, generation))
                return;
            if (operation.Boolean_0)
                return;
            if (!operation.method_7(out var item))
                break;
            operation.DiscoverItem(item);
        }
        if (PlayerRefresh.IsCurrentSession(world, generation))
            operation.IplayerSearchController_0.OnItemFullySearched();
    }
}
[HarmonyPatch(typeof(GClass3515), "method_6")]
internal static class BackpackSearchTimingPatch
{
    private static bool Prefix(GClass3515 __instance, ref Task __result)
    {
        var p = BackpackSearch.Owner(__instance);
        if (p is null || BackpackRuntime.Get(p) is null && !ChestRigRuntime.Is(p, "TR19-2") && Headwear.Factor(p, HatStat.Search) == 1f)
            return true;
        __result = BackpackSearch.Search(__instance, p);
        return false;
    }
}
[HarmonyPatch(typeof(GClass3515), "method_5")]
internal static class BackpackFirstSearchItemPatch
{
    private static bool Prefix(GClass3515 __instance, ref Task __result)
    {
        var s = BackpackRuntime.Get(BackpackSearch.Owner(__instance));
        if (s?.Key != "BP41-3" || !s.Record.Active("firstItem"))
            return true;
        if (__instance.Bool_1)
            __instance.SearchItem();
        if (__instance.method_7(out var item))
            __instance.DiscoverItem(item);
        __result = Task.CompletedTask;
        return false;
    }
}

