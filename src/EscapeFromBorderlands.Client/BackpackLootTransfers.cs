using System;
using System.Runtime.CompilerServices;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

// Observe concrete, completed inventory operations. New item IDs alone are not
// loot: splitting owned stacks and packing magazines creates IDs too.
internal static class BackpackLootTransfers
{
    private sealed class Transfer
    {
        internal Player Player = null!;
        internal Item Source = null!, Received = null!;
        internal bool External;
    }
    private static ConditionalWeakTable<object, Transfer> Pending = new();
    private static bool ReportedFailure;
    internal static void Reset() { Pending = new(); ReportedFailure = false; }
    private static bool Relevant(Player p) => ChestRigRuntime.Get(p) is not null || BackpackRuntime.States.ContainsKey(p) ||
        RarityResolver.TryGetDefinition(BackpackRuntime.Worn(p), out var definition) && definition.BackpackEffect is not null;
    internal static void Observe(object operation, IItemOwner controller, CommandStatus status, Item source, Item received, ItemAddress? from, ItemAddress? to)
    {
        try { ObserveCore(operation, controller, status, source, received, from, to); }
        catch (Exception e)
        {
            Pending.Remove(operation);
            if (ReportedFailure) return;
            ReportedFailure = true;
            Plugin.LogSource.LogWarning($"Backpack loot tracking skipped an unavailable inventory transfer: {e.Message}");
        }
    }
    private static void ObserveCore(object operation, IItemOwner controller, CommandStatus status, Item source, Item received, ItemAddress? from, ItemAddress? to)
    {
        if (status == CommandStatus.Begin)
        {
            Pending.Remove(operation);
            if (controller is not InventoryController inventory || BackpackRuntime.Actor(inventory) is not { } p || p.HealthController?.IsAlive != true || !Relevant(p) || from is null || to is null) return;
            var origin = GClass3113.GetOwner(from);
            var destination = GClass3113.GetOwner(to);
            if (origin is null || destination?.ID != inventory.ID) return;
            Pending.Add(operation, new Transfer { Player = p, Source = source, Received = received, External = origin.ID != inventory.ID });
            return;
        }
        if (!Pending.TryGetValue(operation, out var transfer))
        {
            // A transfer out of the equipment may have no eligible destination,
            // but it must still invalidate equip-local charges/checklist steps.
            if (status == CommandStatus.Succeed && BackpackRuntime.Actor(controller) is { } p && Relevant(p)) _ = BackpackRuntime.Get(p);
            return;
        }
        Pending.Remove(operation);
        if (status != CommandStatus.Succeed || transfer.Player.HealthController?.IsAlive != true) return;
        // The owner/address may have changed since Begin. Only a successful
        // transfer that really ended in this equipment can grant progress.
        if (transfer.Received.CurrentAddress is not { } address || GClass3113.GetOwner(address)?.ID != transfer.Player.InventoryController.ID) return;
        BackpackRuntime.Acquired(transfer.Player, transfer.Source, transfer.Received, transfer.External);
        ChestRigInventory.Acquired(transfer.Player, transfer.Source, transfer.Received, transfer.External);
    }
}

[HarmonyPatch(typeof(GClass3411), nameof(GClass3411.RaiseEvents))]
internal static class BackpackLootMovePatch
{
    private static void Postfix(GClass3411 __instance, IItemOwner itemController, CommandStatus status) =>
        BackpackLootTransfers.Observe(__instance, itemController, status, __instance.Item, __instance.Item, __instance.From, __instance.To);
}

[HarmonyPatch(typeof(GClass3424), nameof(GClass3424.RaiseEvents))]
internal static class BackpackLootSplitPatch
{
    private static void Postfix(GClass3424 __instance, IItemOwner controller, CommandStatus status) =>
        BackpackLootTransfers.Observe(__instance, controller, status, __instance.Item, __instance.ResultItem, __instance.From, __instance.To);
}

[HarmonyPatch(typeof(GClass3425), nameof(GClass3425.RaiseEvents))]
internal static class BackpackLootMergePatch
{
    private static void Postfix(GClass3425 __instance, IItemOwner controller, CommandStatus status)
    {
        if (__instance.Count > 0)
            BackpackLootTransfers.Observe(__instance, controller, status, __instance.Item, __instance.TargetItem, __instance.From, __instance.TargetItem.CurrentAddress);
    }
}
