using System;
using System.Collections.Generic;
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
        internal string? Template, Input, Output;
        internal string BaseText = "", Rendered = "";
        internal string? AbilityBinding;
    }
    private static readonly ConditionalWeakTable<ItemInfoWindowLabels, Binding> Items = new();
    private static readonly List<WeakReference<ItemInfoWindowLabels>> OpenLabels = new();
    private static readonly MethodInfo SetDescription = AccessTools.Method(typeof(ItemInfoWindowLabels), "SetDescriptionText", new[] { typeof(string) });
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
        if (Items.TryGetValue(labels, out var old) && ReferenceEquals(old.Item, item) && old.Examined == examined) return;
        OpenLabels.RemoveAll(reference => !reference.TryGetTarget(out var target) || (UnityEngine.Object)(object)target == null || ReferenceEquals(target, labels));
        OpenLabels.Add(new WeakReference<ItemInfoWindowLabels>(labels));
        Items.Remove(labels);
        Items.Add(labels, new Binding { Item = item, Examined = examined });
    }

    internal static string ForLabels(ItemInfoWindowLabels labels, string description)
    {
        if (!Items.TryGetValue(labels, out var binding) || !binding.Examined) return description;
        if (description != binding.Rendered) binding.BaseText = description;
        description = binding.BaseText;
        var item = binding.Item;
        var template = item.StringTemplateId;
        var abilityBinding = HeadwearSettings.Label;
        if (binding.Template == template && binding.Input == description && binding.AbilityBinding == abilityBinding && binding.Output is { } output) return output;
        var composed = Compose(item, description);
        if (RarityResolver.TryGetDefinition(item, out var definition) && definition.GrenadeChargesPerRaid == 0)
        {
            binding.Template = template;
            binding.Input = description;
            binding.Output = composed;
            binding.AbilityBinding = abilityBinding;
        }
        return binding.Rendered = composed;
    }

    internal static void RefreshHeadwear()
    {
        OpenLabels.RemoveAll(reference => !reference.TryGetTarget(out var target) || (UnityEngine.Object)(object)target == null);
        foreach (var reference in OpenLabels)
            if (reference.TryGetTarget(out var labels) && (UnityEngine.Object)(object)labels != null && Items.TryGetValue(labels, out var binding) && binding.Examined &&
                RarityResolver.TryGetDefinition(binding.Item, out var definition) && definition.IsHeadwear)
                try { SetDescription.Invoke(labels, new object[] { binding.BaseText }); }
                catch (Exception e) { ReportFailure("headwear shortcut refresh", e); }
    }

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
        if (roll.Definition.IsHeadwear || roll.Definition.IsEarpiece || roll.Definition.BackpackEffect is not null || roll.Definition.ChestRigEffect is not null || roll.Definition.GrenadeChargesPerRaid > 0)
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
            // A reused window can preserve text only for the same bound item.
            if (ItemField?.GetValue(__instance) is not Item item || !RarityResolver.TryGetDefinition(item, out _))
            { ItemDescriptionPresentation.Unbind(labels); return; }
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
