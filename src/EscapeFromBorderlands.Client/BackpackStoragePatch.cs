using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

[HarmonyPatch(typeof(ContainedGridsView), "CreateGrids")]
internal static class BackpackGeneratedGridPatch
{
    private static bool Prefix(Item item, ContainedGridsView containedGridsTemplate, ref ContainedGridsView __result)
    {
        if (!RarityResolver.TryGetDefinition(item, out var definition)) return true;
        var storage = definition.BackpackEffect;
        var rig = definition.ChestRigEffect;
        var changed = storage is not null && (storage.ExtraGrids.Count > 0 || storage.ExtraColumns > 0 || storage.ExtraRows > 0) ||
            rig is not null && (rig.ExtraGrids.Count > 0 || rig.ExtraColumns > 0 || rig.ExtraRows > 0 || rig.ExpandEveryGrid);
        if (!changed) return true;
        // Generate enough native views for every grid, then arrange them using
        // the source backpack prefab rather than a generic horizontal row.
        __result = (ContainedGridsView)(object)UnityEngine.Object.Instantiate((UnityEngine.Object)(object)containedGridsTemplate);
        return false;
    }
}

internal static class BackpackVanillaGridLayout
{
    private static readonly Dictionary<string, BackpackGridLayout.Frame[]> Sources = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Warnings = new(StringComparer.Ordinal);

    internal static BackpackGridLayout.Frame[]? Source(VariantDefinition definition)
    {
        if (Sources.TryGetValue(definition.SourceTemplateId, out var cached)) return cached;
        try
        {
            // Read prefab geometry only. A temporary item has no inventory
            // owner, so binding a live GridView to it would be invalid.
            var source = Singleton<EFT.ItemFactory>.Instance.CreateItem(
                Guid.NewGuid().ToString("N").Substring(0, 24), definition.SourceTemplateId, null) as CompoundItem;
            if (source is null || source.Grids.Length == 0) return null;
            var component = Native.Component<GridLayoutComponent>(source);
            if (component is null || string.IsNullOrEmpty(component.Template.LayoutName))
            {
                if (source.Grids.Length != 1) return null;
                var size = ItemViewFactory.GetCellPixelSize(new IntVec2(source.Grids[0].GridWidth, source.Grids[0].GridHeight));
                return Sources[definition.SourceTemplateId] = new[] { new BackpackGridLayout.Frame(0f, 0f, size.X, size.Y) };
            }
            var prefab = AccessTools.Method(typeof(EFT.Utilities.ResourcesCache), "Pop").MakeGenericMethod(typeof(ContainedGridsView))
                .Invoke(null, new object[] { "UI/Rig Layouts/" + component.Template.LayoutName });
            if (prefab is null) throw new InvalidOperationException("Vanilla grid prefab was not found.");
            return Sources[definition.SourceTemplateId] = ReadPrefab(prefab, source);
        }
        catch (Exception error)
        {
            if (Warnings.Add(definition.SourceTemplateId)) Plugin.LogSource.LogWarning($"Backpack pocket layout unavailable for {definition.SourceShortName}: {error.Message}");
            return null;
        }
    }

    private static BackpackGridLayout.Frame[] ReadPrefab(object prefab, CompoundItem source)
    {
        // Rebuild a private, unbound clone: some vanilla prefabs arrange their
        // pockets through nested layout groups rather than saved positions.
        // Never resize the shared resource or register a temporary item view.
        var snapshot = UnityEngine.Object.Instantiate((UnityEngine.Object)prefab);
        try
        {
            if (Native.Get(snapshot, "_presetGridViews") is not GridView[] views || views.Length != source.Grids.Length)
                throw new InvalidOperationException("Vanilla grid count differs from its template.");
            var root = (RectTransform)((Component)(object)snapshot).transform;
            ((Component)(object)snapshot).gameObject.SetActive(true);
            for (var i = 0; i < views.Length; i++)
            {
                var component = (Component)(object)views[i];
                component.gameObject.SetActive(true);
                var size = ItemViewFactory.GetCellPixelSize(new IntVec2(source.Grids[i].GridWidth, source.Grids[i].GridHeight));
                ((RectTransform)component.transform).sizeDelta = new Vector2(size.X, size.Y);
                foreach (var behaviour in component.GetComponents<Behaviour>())
                    if (behaviour.GetType().Name == "LayoutElement")
                    { Native.Set(behaviour, "minWidth", (float)size.X); Native.Set(behaviour, "minHeight", (float)size.Y); }
            }
            var rebuilder = AccessTools.TypeByName("UnityEngine.UI.LayoutRebuilder");
            AccessTools.Method(rebuilder, "ForceRebuildLayoutImmediate", new[] { typeof(RectTransform) }).Invoke(null, new object[] { root });
            var frames = new BackpackGridLayout.Frame[views.Length];
            for (var i = 0; i < views.Length; i++)
            {
                var rect = (RectTransform)((Component)(object)views[i]).transform;
                var topLeft = root.InverseTransformPoint(rect.TransformPoint(new Vector3(rect.rect.xMin, rect.rect.yMax, 0f)));
                var size = ItemViewFactory.GetCellPixelSize(new IntVec2(source.Grids[i].GridWidth, source.Grids[i].GridHeight));
                frames[i] = new BackpackGridLayout.Frame(topLeft.x, -topLeft.y, size.X, size.Y);
            }
            var left = frames.Min(f => f.X); var top = frames.Min(f => f.Y);
            return frames.Select(f => new BackpackGridLayout.Frame(f.X - left, f.Y - top, f.Width, f.Height)).ToArray();
        }
        finally { UnityEngine.Object.Destroy(snapshot); }
    }
}

[HarmonyPatch(typeof(GeneratedGridsView), "Show")]
internal static class BackpackCompartmentLayoutPatch
{
    private static void Postfix(GeneratedGridsView __instance, CompoundItem compoundItem, ItemUiContext itemUiContext)
    {
        if (!RarityResolver.TryGetDefinition(compoundItem, out var d) || d.BackpackEffect is null && d.ChestRigEffect is null) return;
        var original = BackpackVanillaGridLayout.Source(d);
        var views = __instance.GridViews;
        if (views.Length == 0 || original is null || views.Length < original.Length) return;
        var root = (RectTransform)((Component)(object)__instance).transform;
        // A layout group would put the pockets back in a horizontal row during
        // the next Unity layout pass. All child GridViews remain native.
        foreach (var behaviour in ((Component)(object)__instance).GetComponents<Behaviour>())
            if (behaviour.GetType().Name.Contains("LayoutGroup") || behaviour.GetType().Name == "ContentSizeFitter") behaviour.enabled = false;
        var widths = new float[views.Length]; var heights = new float[views.Length];
        for (var i = 0; i < views.Length; i++)
        {
            var size = ItemViewFactory.GetCellPixelSize(new IntVec2(compoundItem.Grids[i].GridWidth, compoundItem.Grids[i].GridHeight));
            widths[i] = size.X; heights[i] = size.Y;
        }
        var positions = BackpackGridLayout.Preserve(original, widths, heights, 4f, d.BackpackEffect?.Key == "BP11-1");
        float width = 0f, height = 0f;
        for (var i = 0; i < views.Length; i++)
        {
            var rect = (RectTransform)((Component)(object)views[i]).transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(positions[i].X, -positions[i].Y);
            width = Math.Max(width, positions[i].X + widths[i]); height = Math.Max(height, positions[i].Y + heights[i]);
        }
        BackpackLayoutSizing.Apply(root, width, height);
        var backpack = d.BackpackEffect;
        var rig = d.ChestRigEffect;
        if (backpack is not null && (backpack.ExtraGrids.Count > 0 || backpack.ExtraColumns > 0 || backpack.ExtraRows > 0) ||
            rig is not null && (rig.ExtraGrids.Count > 0 || rig.ExtraColumns > 0 || rig.ExtraRows > 0 || rig.ExpandEveryGrid))
            ExpandedStorageNavigation.Register(root, views.Select(view => (RectTransform)((Component)(object)view).transform).ToArray(),
                () => itemUiContext.DragLayer != null && itemUiContext.DragLayer.childCount > 0 &&
                    itemUiContext.DragLayer.GetComponentsInChildren(AccessTools.TypeByName("EFT.UI.DragAndDrop.DraggedItemView"), false).Length > 0, original.Length, d.BackpackEffect?.Key == "BP11-1");
    }
}
