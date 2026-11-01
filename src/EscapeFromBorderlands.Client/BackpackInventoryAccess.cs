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
    internal static void Grenades(InventoryController inventory, IList<ThrowWeap> destination, Predicate<ThrowWeap>? predicate)
    {
        // This adapter is called only inside the native grenade-key handler,
        // after its hand-input/interaction restrictions have already passed.
        if (WornKey(inventory) == "BP07-1" && BackpackRuntime.Actor(inventory) is { } player && BackpackGrenadeAction.TryThrow(player)) return;
        // Keep Use Items Anywhere's configured grenade slots instead of replacing
        // its inventory query with the vanilla pocket/rig search.
        UseItemsAnywhereCompatibility.CollectGrenades(inventory, destination, predicate);
        if (WornKey(inventory) == "BP35-1") Add(inventory, destination, predicate);
        ReusableGrenadeRuntime.Select(inventory, destination);
    }
    internal static void Magazines(InventoryController inventory, IList<Magazine> destination, Predicate<Magazine>? predicate) =>
        Magazines(inventory, destination, predicate, false);
    internal static void BotMagazines(InventoryController inventory, IList<Magazine> destination, Predicate<Magazine>? predicate) =>
        Magazines(inventory, destination, predicate, true);
    private static void Magazines(InventoryController inventory, IList<Magazine> destination, Predicate<Magazine>? predicate, bool bot)
    {
        // Preserve Uia's separate player/bot reload search behavior.
        UseItemsAnywhereCompatibility.CollectMagazines(inventory, destination, predicate, bot);
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
// Uia's normal-priority transpiler must replace the native call first.
// Our lower-priority transpiler wraps its result instead of competing with it.
[HarmonyPriority(Priority.Last)]
[HarmonyAfter("com.cj.useFromAnywhere.inventory-queries")]
internal static class BackpackConcreteInventorySearchPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] {
        AccessTools.Method(typeof(EFT.PlayerInputTranslator), "vmethod_1"),
        AccessTools.Method(typeof(EFT.FirearmHandsInputTranslator), "ReloadExternalMagazine"),
        AccessTools.Method(typeof(BotReload), "GetMagazineForReload"),
        AccessTools.Method(typeof(BotReload), "AddAmmoToMagazines") };
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var replacements = 0;
        foreach (var instruction in instructions)
        {
            if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                instruction.operand is MethodInfo method && method.IsGenericMethod &&
                (method.Name == "GetReachableItemsOfTypeNonAlloc" && method.DeclaringType == typeof(InventoryController) ||
                 UseItemsAnywhereCompatibility.IsCollector(method)))
            {
                var item = method.GetGenericArguments()[0];
                var adapter = item == typeof(ThrowWeap) ? nameof(BackpackInventoryAccess.Grenades) :
                    item == typeof(Magazine) ?
                        (original.DeclaringType == typeof(BotReload) ? nameof(BackpackInventoryAccess.BotMagazines) : nameof(BackpackInventoryAccess.Magazines)) : null;
                if (adapter is not null)
                {
                    instruction.opcode = OpCodes.Call;
                    // Explicit signature resolves the overload of Magazines.
                    instruction.operand = AccessTools.Method(typeof(BackpackInventoryAccess), adapter,
                        new[] { typeof(InventoryController),
                                item == typeof(ThrowWeap) ? typeof(IList<ThrowWeap>) : typeof(IList<Magazine>),
                                item == typeof(ThrowWeap) ? typeof(Predicate<ThrowWeap>) : typeof(Predicate<Magazine>) });
                    replacements++;
                }
            }
            yield return instruction;
        }
        if (replacements != 1) throw new InvalidOperationException($"Expected one typed inventory search in {original.DeclaringType?.Name}.{original.Name}, found {replacements}.");
    }
}
