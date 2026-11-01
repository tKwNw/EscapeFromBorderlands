using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

internal static class BackpackInventoryAccess
{
    internal static string? WornKey(InventoryController inventory)
    {
        var bag = inventory.Inventory?.Equipment?.GetSlot(EquipmentSlot.Backpack)?.ContainedItem;
        return bag is not null && RarityResolver.TryGetDefinition(bag, out var definition) ? definition.BackpackEffect?.Key : null;
    }
    internal static void Grenades(InventoryController inventory, IList<ThrowWeapItemClass> destination, Predicate<ThrowWeapItemClass>? predicate)
    {
        // This adapter is called only inside the native grenade-key handler,
        // after its hand-input/interaction restrictions have already passed.
        if (WornKey(inventory) == "BP07-1" && BackpackRuntime.Actor(inventory) is { } player && BackpackGrenadeAction.TryThrow(player)) return;
        // The original generic search remains unpatched, including its native
        // generic context and all normal carried-grenade selection behavior.
        inventory.GetReachableItemsOfTypeNonAlloc(destination, predicate);
        if (WornKey(inventory) == "BP35-1") Add(inventory, destination, predicate);
        ReusableGrenadeRuntime.Select(inventory, destination);
    }
    internal static void Magazines(InventoryController inventory, IList<MagazineItemClass> destination, Predicate<MagazineItemClass>? predicate)
    {
        inventory.GetReachableItemsOfTypeNonAlloc(destination, predicate);
        if (WornKey(inventory) != "BP02-2") return;
        Add(inventory, destination, predicate);
    }
    private static void Add<T>(InventoryController inventory, IList<T> destination, Predicate<T>? predicate) where T : Item
    {
        var bag = inventory.Inventory.Equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem;
        if (bag is null) return;
        foreach (var item in bag.GetAllItems().OfType<T>())
            if ((predicate is null || predicate(item)) && !destination.Any(existing => existing.Id == item.Id)) destination.Add(item);
    }
    internal static float MagazineTime(InventoryController inventory, float seconds) => WornKey(inventory) == "BP38-2" ? seconds * .5f : seconds;
}

[HarmonyPatch]
internal static class BackpackConcreteInventorySearchPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] {
        AccessTools.Method(typeof(Class1725), "vmethod_1"),
        AccessTools.Method(typeof(Class1730), "method_17"),
        AccessTools.Method(typeof(BotReload), "GetMagazineForReload"),
        AccessTools.Method(typeof(BotReload), "method_1") };
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var replacements = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.Name == "GetReachableItemsOfTypeNonAlloc" && method.IsGenericMethod)
            {
                var item = method.GetGenericArguments()[0];
                var adapter = item == typeof(ThrowWeapItemClass) ? nameof(BackpackInventoryAccess.Grenades) : item == typeof(MagazineItemClass) ? nameof(BackpackInventoryAccess.Magazines) : null;
                if (adapter is not null)
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(BackpackInventoryAccess), adapter); replacements++; }
            }
            yield return instruction;
        }
        if (replacements != 1) throw new InvalidOperationException($"Expected one typed inventory search in {original.DeclaringType?.Name}.{original.Name}, found {replacements}.");
    }
}
