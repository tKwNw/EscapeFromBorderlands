using EFT;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

internal readonly struct BackpackPaidStaminaSnapshot
{
    internal readonly Player? Player;
    internal readonly float Current;
    internal BackpackPaidStaminaSnapshot(Stamina stamina)
    {
        Player = BackpackRuntime.Actor(stamina);
        Current = Player is null ? 0f : stamina.Current;
    }
}

[HarmonyPatch(typeof(Stamina), nameof(Stamina.Consume))]
internal static class BackpackPaidTraversalPatch
{
    private static void Prefix(Stamina __instance, out BackpackPaidStaminaSnapshot __state) => __state = new(__instance);
    private static void Postfix(Stamina __instance, Physical.Consumption consumption, BackpackPaidStaminaSnapshot __state)
    {
        if (__state.Player is null)
            return;
        var player = BackpackRuntime.Actor(__instance);
        if (!ReferenceEquals(player, __state.Player) || player is null || !ReferenceEquals(__instance, player.Physical.Stamina) || __instance.Current >= __state.Current)
            return;
        var action = consumption.ConsumptionType == Physical.EConsumptionType.Jump ? "jump" :
            consumption.ConsumptionType is Physical.EConsumptionType.VaultLegs or Physical.EConsumptionType.ClimbLegs ? "vault" : "";
        if (action.Length > 0)
            BackpackRuntime.Traversal(player, action);
    }
}

[HarmonyPatch(typeof(Stamina), nameof(Stamina.Process))]
internal static class BackpackPaidSprintPatch
{
    private static void Prefix(Stamina __instance, out BackpackPaidStaminaSnapshot __state) => __state = new(__instance);
    private static void Postfix(Stamina __instance, float dt, BackpackPaidStaminaSnapshot __state) => Observe(__instance, dt, __state);
    internal static void Observe(Stamina __instance, float dt, BackpackPaidStaminaSnapshot __state)
    {
        if (__state.Player is null)
            return;
        var player = BackpackRuntime.Actor(__instance);
        if (!ReferenceEquals(player, __state.Player))
            return;
        var state = BackpackRuntime.Get(player);
        if (state?.Key != "BP40-2" || player is null || !ReferenceEquals(__instance, player.Physical.Stamina))
            return;
        Physical.Consumption? sprint = null;
        foreach (var consumption in __instance.Consumptions)
            if (consumption.ConsumptionType == Physical.EConsumptionType.Sprint)
            {
                sprint = consumption;
                break;
            }
        if (sprint is null)
        {
            state.SprintCostObserved = false;
            return;
        }
        if (state.SprintCostObserved || dt <= 0f || __state.Current <= 0f || __instance.ForceMode || __instance.Multiplier <= 0f || sprint.Delta.Value <= 0f)
            return;
        state.SprintCostObserved = true;
        BackpackRuntime.Traversal(player, "sprint");
    }
}
