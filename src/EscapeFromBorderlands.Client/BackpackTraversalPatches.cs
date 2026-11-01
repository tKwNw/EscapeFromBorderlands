using EFT;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

[HarmonyPatch(typeof(GClass774), nameof(GClass774.Consume))]
internal static class BackpackPaidTraversalPatch
{
    private static void Prefix(GClass774 __instance, out float __state) => __state = __instance.Current;
    private static void Postfix(GClass774 __instance, PlayerPhysicalClass.GClass773 consumption, float __state)
    {
        var player = BackpackRuntime.Actor(__instance);
        if (player is null || !ReferenceEquals(__instance, player.Physical.Stamina) || __instance.Current >= __state) return;
        var action = consumption.ConsumptionType == PlayerPhysicalClass.EConsumptionType.Jump ? "jump" :
            consumption.ConsumptionType is PlayerPhysicalClass.EConsumptionType.VaultLegs or PlayerPhysicalClass.EConsumptionType.ClimbLegs ? "vault" : "";
        if (action.Length > 0) BackpackRuntime.Traversal(player, action);
    }
}

[HarmonyPatch(typeof(GClass774), nameof(GClass774.Process))]
internal static class BackpackPaidSprintPatch
{
    private static void Prefix(GClass774 __instance, out float __state) => __state = __instance.Current;
    private static void Postfix(GClass774 __instance, float dt, float __state)
    {
        var player = BackpackRuntime.Actor(__instance); var state = BackpackRuntime.Get(player);
        if (state?.Key != "BP40-2" || player is null || !ReferenceEquals(__instance, player.Physical.Stamina)) return;
        PlayerPhysicalClass.GClass773? sprint = null;
        foreach (var consumption in __instance.Consumptions)
            if (consumption.ConsumptionType == PlayerPhysicalClass.EConsumptionType.Sprint) { sprint = consumption; break; }
        if (sprint is null) { state.SprintCostObserved = false; return; }
        if (state.SprintCostObserved || dt <= 0f || __state <= 0f || __instance.ForceMode || __instance.Multiplier <= 0f || sprint.Delta.Value <= 0f) return;
        state.SprintCostObserved = true;
        BackpackRuntime.Traversal(player, "sprint");
    }
}
