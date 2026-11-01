using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

internal static class ItemDescriptionPresentation
{
    private sealed class Binding
    {
        internal Item Item = null!;
        internal bool Examined;
    }
    private static readonly ConditionalWeakTable<ItemInfoWindowLabels, Binding> Items = new();

    internal static void Bind(ItemInfoWindowLabels labels, Item item, bool examined)
    {
        Items.Remove(labels);
        Items.Add(labels, new Binding { Item = item, Examined = examined });
    }

    internal static string ForLabels(ItemInfoWindowLabels labels, string description) =>
        Items.TryGetValue(labels, out var binding) && binding.Examined
            ? Compose(binding.Item, description) : description;

    internal static string Compose(Item item, string description)
    {
        var roll = RarityResolver.Resolve(item);
        if (roll is null) return description;
        var block = roll.BuildDescriptionBlock();
        if (roll.Definition.GrenadeChargesPerRaid > 0)
        {
            // A panel may pass its previously composed text back to us after a
            // throw. Strip that old counter before composing the current one.
            if (description.StartsWith(block, StringComparison.Ordinal))
            {
                description = description.Substring(block.Length);
                const string marker = "\n<color=#66D9EF>Charges this raid: ";
                if (description.StartsWith(marker, StringComparison.Ordinal))
                {
                    var end = description.IndexOf("</color>", StringComparison.Ordinal);
                    if (end >= 0) description = description.Substring(end + "</color>".Length);
                }
                if (description.StartsWith("\n\n", StringComparison.Ordinal)) description = description.Substring(2);
            }
            block += "\n<color=#66D9EF>Charges this raid: " + ReusableGrenadeRuntime.Remaining(item) + "/" + roll.Definition.GrenadeChargesPerRaid + "</color>";
        }
        if (description == block || description.StartsWith(block + "\n\n", StringComparison.Ordinal)) return description;
        // Replace the server's signature fallback with this instance's full roll.
        if (roll.Definition.IsEarpiece || roll.Definition.BackpackEffect is not null || roll.Definition.ChestRigEffect is not null || roll.Definition.GrenadeChargesPerRaid > 0)
        {
            var signature = roll.Definition.EffectText;
            foreach (var prefix in new[] { signature, "<color=#66D9EF>" + signature + "</color>" })
            {
                if (description == prefix) description = string.Empty;
                else if (description.StartsWith(prefix + "\n\n", StringComparison.Ordinal)) description = description.Substring(prefix.Length + 2);
            }
        }
        return string.IsNullOrEmpty(description) ? block : block + "\n\n" + description;
    }
}

[HarmonyPatch]
internal static class ItemDescriptionPatch
{
    private static readonly FieldInfo ItemField = AccessTools.Field(typeof(ItemSpecificationPanel), "item_0");
    private static readonly FieldInfo LabelsField = AccessTools.Field(typeof(ItemSpecificationPanel), "_itemLabels");
    private static readonly PropertyInfo ExaminedProperty = AccessTools.Property(typeof(ItemSpecificationPanel), "Boolean_0");
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(ItemSpecificationPanel), "method_1", Type.EmptyTypes);

    private static void Prefix(ItemSpecificationPanel __instance)
    {
        if (ItemField.GetValue(__instance) is Item item && LabelsField.GetValue(__instance) is ItemInfoWindowLabels labels)
            ItemDescriptionPresentation.Bind(labels, item, ExaminedProperty.GetValue(__instance) is true);
    }
}

[HarmonyPatch(typeof(ItemInfoWindowLabels), "method_2", new[] { typeof(string) })]
internal static class ItemDescriptionLabelPatch
{
    private static void Prefix(ItemInfoWindowLabels __instance, ref string value) =>
        value = ItemDescriptionPresentation.ForLabels(__instance, value);
}
