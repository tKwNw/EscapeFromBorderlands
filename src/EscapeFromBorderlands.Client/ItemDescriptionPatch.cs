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
    private static readonly System.Collections.Generic.HashSet<string> ReportedFailures = new(StringComparer.Ordinal);

    internal static void Unbind(ItemInfoWindowLabels labels) => Items.Remove(labels);

    internal static void ReportFailure(string stage, Exception error)
    {
        // Presentation is optional: a failed decoration must never interrupt
        // the game's own inspection or inventory-name initialization.
        try
        {
            if (ReportedFailures.Add(stage))
                Plugin.LogSource.LogWarning($"Escape From Borderlands {stage} presentation failed; native item text is retained: {error.GetBaseException().Message}");
        }
        catch { /* Logging must not turn a presentation fallback into a UI failure. */ }
    }

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
    private static readonly FieldInfo ItemField = AccessTools.Field(typeof(ItemSpecificationPanel), "_item");
    private static readonly FieldInfo LabelsField = AccessTools.Field(typeof(ItemSpecificationPanel), "_itemLabels");
    private static readonly PropertyInfo? ExaminedProperty = AccessTools.Property(typeof(ItemSpecificationPanel), "Examined");
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(ItemSpecificationPanel), "method_1", Type.EmptyTypes);

    private static void Prefix(ItemSpecificationPanel __instance)
    {
        ItemInfoWindowLabels? labels = null;
        try
        {
            labels = LabelsField?.GetValue(__instance) as ItemInfoWindowLabels;
            if (labels is null) return;
            // Windows reuse their labels. Clear the previous item even when
            // the next item is vanilla, unexamined or no longer available.
            ItemDescriptionPresentation.Unbind(labels);
            if (ItemField?.GetValue(__instance) is not Item item || !RarityResolver.TryGetDefinition(item, out _)) return;
            if (ExaminedProperty is null) throw new MissingMemberException(typeof(ItemSpecificationPanel).FullName, "Examined");
            ItemDescriptionPresentation.Bind(labels, item, ExaminedProperty.GetValue(__instance) is true);
        }
        catch (Exception error)
        {
            if (labels is not null) ItemDescriptionPresentation.Unbind(labels);
            ItemDescriptionPresentation.ReportFailure("description binding", error);
        }
    }
}

[HarmonyPatch(typeof(ItemInfoWindowLabels), "SetDescriptionText", new[] { typeof(string) })]
internal static class ItemDescriptionLabelPatch
{
    private static void Prefix(ItemInfoWindowLabels __instance, ref string value)
    {
        try { value = ItemDescriptionPresentation.ForLabels(__instance, value); }
        catch (Exception error) { ItemDescriptionPresentation.ReportFailure("description", error); }
    }
}
