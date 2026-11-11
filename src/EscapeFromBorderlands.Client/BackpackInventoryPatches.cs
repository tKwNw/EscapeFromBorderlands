using ThrowWeap = ThrowWeapItemClass;
using Magazine = MagazineItemClass;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;
[HarmonyPatch(typeof(Player), nameof(Player.OnItemAddedOrRemoved), typeof(Item), typeof(ItemAddress), typeof(bool))]
internal static class BackpackContentsChangePatch
{
    private static void Postfix(Player __instance, Item __0, ItemAddress __1)
    {
        CarriedGrenadeDisplay.InventoryChanged(__instance, __0);
        BackpackRuntime.InventoryChanged(__instance, __1);
    }
}

[HarmonyPatch(typeof(InventoryController), nameof(InventoryController.IsAtBindablePlace))]
internal static class BackpackHotbarPatch
{
    private static void Postfix(InventoryController __instance, Item item, ref bool __result)
    {
        var s = BackpackRuntime.Get(BackpackRuntime.Actor(__instance));
        if (s?.Key == "BP04-2" && item is MedsItemClass && item.CurrentAddress is not null && __instance.Examined(item) && BackpackNative.InBag(item, s.Bag)) __result = true;
    }
}
[HarmonyPatch(typeof(InventoryController), nameof(InventoryController.IsAtReachablePlace))]
internal static class BackpackReachabilityPatch
{
    private static void Postfix(InventoryController __instance, Item item, ref bool __result)
    {
        var s = BackpackRuntime.Get(BackpackRuntime.Actor(__instance));
        if (s is null || !(s.Key == "BP02-2" && item is Magazine ||
            s.Key == "BP04-2" && item is MedsItemClass || s.Key == "BP35-1" && item is ThrowWeap)) return;
        if (BackpackNative.InBag(item, s.Bag)) __result = true;
    }
}
[HarmonyPatch(typeof(Player.PlayerInventoryController), nameof(Player.PlayerInventoryController.ThrowItem))]
internal static class BackpackDropPatch
{
    private static void Prefix(Player.PlayerInventoryController __instance, Item item)
    { var p = BackpackRuntime.Actor(__instance); if (p is not null) BackpackNative.QueueDrop(p, item); }
}
[HarmonyPatch(typeof(MovementContext), "get_CanSprint")]
internal static class BackpackBlackLegSprintPatch
{
    private static void Postfix(MovementContext __instance, ref bool __result)
    {
        var p = BackpackRuntime.Actor(__instance);
        if (p is null || !BackpackRuntime.Is(p, "BP07-3") || !BackpackRuntime.BlackLeg(p)) return;
        if (!__instance.PhysicalConditionIs(EPhysicalCondition.SprintDisabled) && !__instance.PhysicalConditionIs(EPhysicalCondition.UsingMeds) && !__instance.PhysicalConditionIs(EPhysicalCondition.HealingLegs)) __result = true;
    }
}
[HarmonyPatch(typeof(Player), nameof(Player.GetRotationMultiplier))]
internal static class BackpackTurningPenaltyPatch
{
    private static void Postfix(Player __instance, ref float __result)
    {
        var s = BackpackRuntime.Get(__instance);
        if (s?.Key != "BP31-2" || !BackpackRuntime.Milestone(s) || __instance.HandsController?.IsAiming == true) return;
        var penalty = Native.Number(__instance, "_mouseSensitivityModifier");
        if (penalty < 0f && penalty > -.99f) __result *= (1f + penalty * .7f) / (1f + penalty);
    }
}
