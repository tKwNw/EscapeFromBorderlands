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
    internal static Player? Owner(GClass3515 operation) => Singleton<GameWorld>.Instance?.RegisteredPlayers.OfType<Player>()
        .FirstOrDefault(p => p.ProfileId == operation.Profile_0.Id);
    internal static bool Corpse(Item item)
    {
        return CorpseRoot(item) is not null;
    }
    internal static InventoryEquipment? CorpseRoot(Item item) => item.Parent is null ? null : GClass3113.GetOwner(item.Parent)?.RootItem as InventoryEquipment;
    internal static float TimeFactor(Player p, Item searched)
    {
        var rigFactor = ChestRigRuntime.Is(p,"TR19-2") ? 1f/1.75f : 1f;
        var s = BackpackRuntime.Get(p); if (s is null) return rigFactor;
        var time = s.Roll.Backpack.SearchTime;
        if (s.Key == "BP08-3" && Corpse(searched) && new[] { EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon }
            .All(slot => p.InventoryController.Inventory.Equipment.GetSlot(slot).ContainedItem is null)) time /= 1.7f;
        if (s.Key == "BP24-2" && searched.Template.ParentId == "5448e53e4bdc2d60728b4567") time /= 1f + .1f * Math.Min(6, s.Record.Categories.Count);
        if (s.Key == "BP38-1" && Corpse(searched)) time *= .5f;
        return time*rigFactor;
    }
    internal static async Task Search(GClass3515 operation, Player p)
    {
        if (!operation.IplayerSearchController_0.ContainsUnknownItems(operation.Item)) return;
        while (operation.method_7(out _))
        {
            var skills = operation.Profile_0.SkillsInfo;
            var speed = 1f + skills.AttentionLootSpeedValue + (Corpse(operation.Item) ? skills.SearchBuffSpeedValue : 0f);
            var seconds = operation.Bool_0 ? 0f : UnityEngine.Random.Range(1, 3) / speed * TimeFactor(p, operation.Item);
            await Task.Delay((int)(seconds * 1000f), operation.CancellationTokenSource_0.Token);
            if (operation.Boolean_0) return;
            if (!operation.method_7(out var item)) break;
            operation.DiscoverItem(item);
        }
        operation.IplayerSearchController_0.OnItemFullySearched();
    }
}
[HarmonyPatch(typeof(GClass3515), "method_6")]
internal static class BackpackSearchTimingPatch
{
    private static bool Prefix(GClass3515 __instance, ref Task __result)
    {
        var p = BackpackSearch.Owner(__instance);
        if (p is null || BackpackRuntime.Get(p) is null && !ChestRigRuntime.Is(p,"TR19-2")) return true;
        __result = BackpackSearch.Search(__instance, p); return false;
    }
}
[HarmonyPatch(typeof(GClass3515), "method_5")]
internal static class BackpackFirstSearchItemPatch
{
    private static bool Prefix(GClass3515 __instance, ref Task __result)
    {
        var s = BackpackRuntime.Get(BackpackSearch.Owner(__instance));
        if (s?.Key != "BP41-3" || !s.Record.Active("firstItem")) return true;
        if (__instance.Bool_1) __instance.SearchItem();
        if (__instance.method_7(out var item)) __instance.DiscoverItem(item);
        __result = Task.CompletedTask; return false;
    }
}

