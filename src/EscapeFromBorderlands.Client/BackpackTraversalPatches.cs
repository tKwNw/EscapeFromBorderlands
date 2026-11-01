using EFT;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

[HarmonyPatch(typeof(Stamina), nameof(Stamina.Consume))]
internal static class BackpackPaidTraversalPatch
{
    private static void Prefix(Stamina __instance, out float __state) => __state = __instance.Current;
    private static void Postfix(Stamina __instance, Physical.Consumption consumption, float __state)
    {
        var player = BackpackRuntime.Actor(__instance);
        if (player is null || !ReferenceEquals(__instance, player.Physical.Stamina) || __instance.Current >= __state) return;
        var action = consumption.ConsumptionType == Physical.EConsumptionType.Jump ? "jump" :
            consumption.ConsumptionType is Physical.EConsumptionType.VaultLegs or Physical.EConsumptionType.ClimbLegs ? "vault" : "";
        if (action.Length > 0) BackpackRuntime.Traversal(player, action);
    }
}

[HarmonyPatch(typeof(Stamina), nameof(Stamina.Process))]
internal static class BackpackPaidSprintPatch
{
    private static void Prefix(Stamina __instance, out float __state) => __state = __instance.Current;
    private static void Postfix(Stamina __instance, float dt, float __state)
    {
        var player = BackpackRuntime.Actor(__instance); var state = BackpackRuntime.Get(player);
        if (state?.Key != "BP40-2" || player is null || !ReferenceEquals(__instance, player.Physical.Stamina)) return;
        Physical.Consumption? sprint = null;
        foreach (var consumption in __instance.Consumptions)
            if (consumption.ConsumptionType == Physical.EConsumptionType.Sprint) { sprint = consumption; break; }
        if (sprint is null) { state.SprintCostObserved = false; return; }
        if (state.SprintCostObserved || dt <= 0f || __state <= 0f || __instance.ForceMode || __instance.Multiplier <= 0f || sprint.Delta.Value <= 0f) return;
        state.SprintCostObserved = true;
        BackpackRuntime.Traversal(player, "sprint");
    }
}
