using System;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

[HarmonyPatch]
internal static class BackpackJigsawKitAvailabilityPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(Player.BaseGrenadeHandsController), "method_11");
    private static bool Prefix(Player.BaseGrenadeHandsController __instance, ref PlantingKitsItemClass plantingKit, ref bool __result)
    {
        var p = BackpackRuntime.Actor(__instance);
        if (!BackpackRuntime.Is(p, "BP33-2")) return true;
        // The native planner requires a non-null marker to enter planting mode.
        // It is never inserted into inventory; the actual transaction below
        // uses a null kit and discards only the player's real grenade.
        plantingKit = (PlantingKitsItemClass)Singleton<ItemFactoryClass>.Instance.CreateItem(Guid.NewGuid().ToString("N").Substring(0, 24), "666b11055a706400b717cfa5", null);
        __result = true; return false;
    }
}

[HarmonyPatch(typeof(Player.PlayerInventoryController), nameof(Player.PlayerInventoryController.PlantTripwire))]
internal static class BackpackJigsawKitConsumptionPatch
{
    private static void Prefix(Player.PlayerInventoryController __instance, ref PlantingKitsItemClass plantingKit)
    { if (BackpackRuntime.Is(BackpackRuntime.Actor(__instance), "BP33-2")) plantingKit = null!; }
}

[HarmonyPatch]
internal static class BackpackJigsawPlacementPatch
{
    private static MethodBase TargetMethod() => AccessTools.DeclaredMethod(typeof(GClass3492), nameof(GClass3492.ExecuteInternal), Type.EmptyTypes);
    private static bool Prefix(GClass3492 __instance, ref Task<IResult> __result)
    {
        if (__instance.PlantingKitsItemClass is not null || __instance.Iplayer_0 is not Player player || player.IsAI || !BackpackRuntime.Is(player, "BP33-2")) return true;
        // In 4.0.13 ExecuteInternal rejects a missing kit for human players before
        // calling method_6. Only Jigsaw skips that guard. Run the same native
        // composite inventory operation: grenade validation/discard, failure
        // handling, completion events and actual trap creation remain native.
        __result = Task.FromResult(__instance.method_6());
        return false;
    }
}
