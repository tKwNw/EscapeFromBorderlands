using ItemFactory = ItemFactoryClass;
using ThrowWeap = ThrowWeapItemClass;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

// Grenade clones have no separate model. Reuse their base item's native icon
// framing/cache instead of independently rendering the same prefab for each
// Legendary ID. Only the item passed to the icon creator changes; callers,
// labels, rarity, inventory instances, charges and projectile factories do not.
internal static class LegendaryGrenadeIcons
{
    private const int Capacity = 32;
    private static readonly Dictionary<string, Item> Sources = new(StringComparer.Ordinal);
    private static ItemFactory? Factory;

    internal static Item ForRender(Item item)
    {
        if (item is not ThrowWeap || !RarityResolver.TryGetDefinition(item, out var definition) ||
            definition.GrenadeChargesPerRaid <= 0 || string.IsNullOrEmpty(definition.SourceTemplateId) ||
            definition.SourceTemplateId == item.StringTemplateId) return item;
        try
        {
            var factory = Singleton<ItemFactory>.Instance;
            if (factory is null) return item;
            if (!ReferenceEquals(Factory, factory)) { Sources.Clear(); Factory = factory; }
            if (!Sources.TryGetValue(definition.SourceTemplateId, out var source))
            {
                if (Sources.Count >= Capacity) return item;
                source = factory.CreateItem(Guid.NewGuid().ToString("N").Substring(0, 24), definition.SourceTemplateId, null);
                if (source is not ThrowWeap) return item;
                Sources.Add(definition.SourceTemplateId, source);
            }
            // Preserve another mod's replacement model or dimensions. Recheck
            // live values rather than caching this decision per template.
            var prefab = item.Prefab;
            var original = source.Prefab;
            if (prefab is null || original is null || string.IsNullOrEmpty(prefab.path) ||
                !string.Equals(prefab.path, original.path, StringComparison.Ordinal) ||
                !string.Equals(prefab.rcid, original.rcid, StringComparison.Ordinal)) return item;
            var size = item.CalculateCellSize();
            var sourceSize = source.CalculateCellSize();
            if (size.X != sourceSize.X || size.Y != sourceSize.Y) return item;
            return source;
        }
        catch { return item; } // Optional presentation; native rendering remains available.
    }
}

[HarmonyPatch(typeof(ItemViewFactory), nameof(ItemViewFactory.LoadItemIcon))]
internal static class LegendaryGrenadeIconPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var loads = new List<int>();
        for (var i = 0; i < code.Count - 1; i++)
        {
            if (code[i].operand is not MethodInfo getter || getter.Name != "get_Instance" ||
                getter.DeclaringType?.IsGenericType != true ||
                getter.DeclaringType.GetGenericTypeDefinition() != typeof(Singleton<>) ||
                code[i + 1].opcode != OpCodes.Ldarg_0) continue;
            var renderer = getter.DeclaringType.GetGenericArguments()[0];
            if (!code.Skip(i + 2).Any(c => c.operand is MethodInfo call &&
                call.DeclaringType == renderer && call.Name == "GetItemIcon" &&
                call.GetParameters().Length == 3 && call.GetParameters()[0].ParameterType == typeof(Item))) continue;
            loads.Add(i + 1);
        }
        if (loads.Count != 1) throw new InvalidOperationException($"Expected one native icon item load, found {loads.Count}.");
        // Keep the original argument for pixel sizing and every other Harmony
        // patch. This helper only substitutes the renderer's stack argument.
        code.Insert(loads[0] + 1, new CodeInstruction(OpCodes.Call,
            typeof(LegendaryGrenadeIcons).GetMethod(nameof(LegendaryGrenadeIcons.ForRender), BindingFlags.Static | BindingFlags.NonPublic)));
        return code;
    }
}
