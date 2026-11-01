using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class BackpackLayoutSizing
{
    private static readonly Type ElementType = AccessTools.TypeByName("UnityEngine.UI.LayoutElement");
    private static readonly Type CanvasType = AccessTools.TypeByName("UnityEngine.Canvas");
    private static readonly MethodInfo MarkLayout = AccessTools.Method(
        AccessTools.TypeByName("UnityEngine.UI.LayoutRebuilder"), "MarkLayoutForRebuild", new[] { typeof(RectTransform) });

    internal static void Apply(RectTransform root, float width, float height)
    {
        // A RectTransform's size does not tell its parent how much space to
        // reserve. Generated grids lose their layout-group provider when we
        // position the compartments ourselves, so supply an explicit one.
        var element = root.gameObject.GetComponent(ElementType) ?? root.gameObject.AddComponent(ElementType);
        Native.Set(element, "enabled", true);
        Native.Set(element, "ignoreLayout", false);
        Native.Set(element, "layoutPriority", 10);
        Native.Set(element, "minWidth", width);
        Native.Set(element, "preferredWidth", width);
        Native.Set(element, "flexibleWidth", 0f);
        Native.Set(element, "minHeight", height);
        Native.Set(element, "preferredHeight", height);
        Native.Set(element, "flexibleHeight", 0f);
        root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);

        // Inventory rows, the scroll content, and standalone bag windows can
        // have intermediate RectTransforms without layout components. Mark
        // each ancestor so the native layout pass updates every wrapper and
        // moves the pouch below the complete bag, including its lower pockets.
        for (Transform? current = root; current is not null; current = current.parent)
        {
            if (current is RectTransform rect) MarkLayout.Invoke(null, new object[] { rect });
            if (CanvasType is not null && current.gameObject.GetComponent(CanvasType) is not null) break;
        }
    }
}
