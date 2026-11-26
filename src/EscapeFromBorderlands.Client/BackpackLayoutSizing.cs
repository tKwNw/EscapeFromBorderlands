using System;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeFromBorderlands.Client;

internal static class BackpackLayoutSizing
{
    internal static void Apply(RectTransform root, float width, float height)
    {
        // A RectTransform's size does not tell its parent how much space to
        // reserve. Generated grids lose their layout-group provider when we
        // position the compartments ourselves, so supply an explicit one.
        var element = root.gameObject.GetComponent<LayoutElement>() ?? root.gameObject.AddComponent<LayoutElement>();
        if (element.GetType() != typeof(LayoutElement))
        {
            // Preserve setters hidden by a foreign LayoutElement subclass.
            Native.Set(element, "enabled", true);
            Native.Set(element, "ignoreLayout", false);
            Native.Set(element, "layoutPriority", 10);
            Native.Set(element, "minWidth", width);
            Native.Set(element, "preferredWidth", width);
            Native.Set(element, "flexibleWidth", 0f);
            Native.Set(element, "minHeight", height);
            Native.Set(element, "preferredHeight", height);
            Native.Set(element, "flexibleHeight", 0f);
        }
        else
        {
            if (!element.enabled)
                element.enabled = true;
            if (element.ignoreLayout)
                element.ignoreLayout = false;
            if (element.layoutPriority != 10)
                element.layoutPriority = 10;
            if (element.minWidth != width)
                element.minWidth = width;
            if (element.preferredWidth != width)
                element.preferredWidth = width;
            if (element.flexibleWidth != 0f)
                element.flexibleWidth = 0f;
            if (element.minHeight != height)
                element.minHeight = height;
            if (element.preferredHeight != height)
                element.preferredHeight = height;
            if (element.flexibleHeight != 0f)
                element.flexibleHeight = 0f;
        }
        if (root.rect.width != width)
            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        if (root.rect.height != height)
            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);

        // Inventory rows, the scroll content, and standalone bag windows can
        // have intermediate RectTransforms without layout components. Mark
        // each ancestor so the native layout pass updates every wrapper and
        // moves the pouch below the complete bag, including its lower pockets.
        for (Transform? current = root; current is not null; current = current.parent)
        {
            if (current is RectTransform rect)
                LayoutRebuilder.MarkLayoutForRebuild(rect);
            if (current.gameObject.GetComponent<Canvas>() is not null)
                break;
        }
    }
}
