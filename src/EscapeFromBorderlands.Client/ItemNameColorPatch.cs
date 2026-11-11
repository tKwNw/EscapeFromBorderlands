using System;
using System.Reflection;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

internal static class ItemNameColor
{
    public static string Wrap(string value, string colorHex) =>
        $"<color={colorHex}>{value}</color>";
}

[HarmonyPatch]
internal static class InspectItemNameColorPatch
{
    private static readonly FieldInfo ItemField =
        AccessTools.Field(typeof(ItemSpecificationPanel), "item_0");

    private static readonly FieldInfo LabelsField =
        AccessTools.Field(typeof(ItemSpecificationPanel), "_itemLabels");

    private static readonly FieldInfo CaptionField =
        AccessTools.Field(typeof(ItemInfoWindowLabels), "_caption");

    private static readonly MethodInfo SetCaptionMethod =
        AccessTools.Method(typeof(ItemInfoWindowLabels), "method_0", new[] { typeof(string) });

    private static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(ItemSpecificationPanel), "method_0", Type.EmptyTypes);

    private static void Postfix(ItemSpecificationPanel __instance)
    {
        try { Apply(__instance); }
        catch (Exception error) { ItemDescriptionPresentation.ReportFailure("inspection name", error); }
    }

    private static void Apply(ItemSpecificationPanel __instance)
    {
        var item = ItemField.GetValue(__instance) as Item;
        var roll = RarityResolver.Resolve(item);
        if (roll is null)
        {
            return;
        }

        var labels = LabelsField.GetValue(__instance) as ItemInfoWindowLabels;
        if (labels is null)
        {
            return;
        }

        var captionControl = CaptionField.GetValue(labels);
        var textProperty = captionControl is null
            ? null
            : AccessTools.Property(captionControl.GetType(), "text");
        var originalCaption = textProperty?.GetValue(captionControl) as string;
        if (string.IsNullOrEmpty(originalCaption))
        {
            return;
        }

        SetCaptionMethod.Invoke(labels, new object[]
        {
            ItemNameColor.Wrap(originalCaption, roll.ColorHex),
        });
    }
}

[HarmonyPatch]
internal static class GridItemNameColorPatch
{
    private const string DefaultColorPrefix = "<color=#b6c1c7>";

    private static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(GridItemView), "method_29", new[] { typeof(ItemAddress) });

    private static void Postfix(GridItemView __instance, ref string __result)
    {
        try { Apply(__instance, ref __result); }
        catch (Exception error) { ItemDescriptionPresentation.ReportFailure("inventory name", error); }
    }

    private static void Apply(GridItemView __instance, ref string __result)
    {
        var roll = RarityResolver.Resolve(__instance.Item);
        if (roll is null || string.IsNullOrEmpty(__result))
        {
            return;
        }

        if (__result.StartsWith(DefaultColorPrefix, StringComparison.OrdinalIgnoreCase))
        {
            __result = $"<color={roll.ColorHex}>" + __result.Substring(DefaultColorPrefix.Length);
            return;
        }

        __result = ItemNameColor.Wrap(__result, roll.ColorHex);
    }
}
