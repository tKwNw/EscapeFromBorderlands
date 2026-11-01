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
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(Player.BaseGrenadeHandsController), "GetPlantingKit");
    private static bool Prefix(Player.BaseGrenadeHandsController __instance, ref EFT.InventoryLogic.PlantingKit plantingKit, ref bool __result)
    {
        var p = BackpackRuntime.Actor(__instance);
        if (!BackpackRuntime.Is(p, "BP33-2")) return true;
        // The native planner requires a non-null marker to enter planting mode.
        // It is never inserted into inventory; the actual transaction below
        // uses a null kit and discards only the player's real grenade.
        plantingKit = (EFT.InventoryLogic.PlantingKit)Singleton<ItemFactory>.Instance.CreateItem(Guid.NewGuid().ToString("N").Substring(0, 24), "666b11055a706400b717cfa5", null);
        __result = true; return false;
    }
}

[HarmonyPatch(typeof(Player.PlayerInventoryController), nameof(Player.PlayerInventoryController.PlantTripwire))]
internal static class BackpackJigsawKitConsumptionPatch
{
    private static void Prefix(Player.PlayerInventoryController __instance, ref EFT.InventoryLogic.PlantingKit plantingKit)
    { if (BackpackRuntime.Is(BackpackRuntime.Actor(__instance), "BP33-2")) plantingKit = null!; }
}

[HarmonyPatch]
internal static class BackpackJigsawPlacementPatch
{
    private static MethodBase TargetMethod() => AccessTools.DeclaredMethod(typeof(EFT.InventoryLogic.Operations.PlantTripwireOperation), nameof(EFT.InventoryLogic.Operations.PlantTripwireOperation.ExecuteInternal), Type.EmptyTypes);
    private static bool Prefix(EFT.InventoryLogic.Operations.PlantTripwireOperation __instance, ref Task<IResult> __result)
    {
        if (__instance._plantingKit is not null || __instance._player is not Player player || player.IsAI || !BackpackRuntime.Is(player, "BP33-2")) return true;
        // In 4.1.6 ExecuteInternal rejects a missing kit for human players before
        // calling ExecuteAndFinish. Only Jigsaw skips that guard. Run the same native
        // composite inventory operation: grenade validation/discard, failure
        // handling, completion events and actual trap creation remain native.
        __result = Task.FromResult(__instance.ExecuteAndFinish());
        return false;
    }
}
