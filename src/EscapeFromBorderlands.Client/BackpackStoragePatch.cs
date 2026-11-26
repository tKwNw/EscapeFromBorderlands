using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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
        if (!RarityResolver.TryGetDefinition(item, out var definition))
            return true;
        var storage = definition.BackpackEffect;
        var rig = definition.ChestRigEffect;
        var changed = storage is not null && (storage.ExtraGrids.Count > 0 || storage.ExtraColumns > 0 || storage.ExtraRows > 0) ||
            rig is not null && (rig.ExtraGrids.Count > 0 || rig.ExtraColumns > 0 || rig.ExtraRows > 0 || rig.ExpandEveryGrid);
        if (!changed)
            return true;
        // Generate enough native views for every grid, then arrange them using
        // the source backpack prefab rather than a generic horizontal row.
        __result = (ContainedGridsView)(object)UnityEngine.Object.Instantiate((UnityEngine.Object)(object)containedGridsTemplate);
        return false;
    }
}

internal static class BackpackVanillaGridLayout
{
    private static readonly Dictionary<string, BackpackGridLayout.Frame[]> Sources = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, BackpackGridLayout.Frame[]> Shapes = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Warnings = new(StringComparer.Ordinal);
    private static readonly MethodInfo Pop = AccessTools.Method(typeof(EFT.Utilities.ResourcesCache), "Pop").MakeGenericMethod(typeof(ContainedGridsView));
    private static bool? TemplateGeometry;

    // Only native template/resource paths may be prepared before an opening.
    // Foreign factory/resource patches keep their original call timing.
    internal static void Prepare(VariantDefinition definition)
    {
        if (!CanReadTemplate())
            return;
        if (Harmony.GetPatchInfo(Pop) is { } patches && patches.Owners.Any(owner => owner != Plugin.PluginGuid))
            return;
        if (Harmony.GetPatchInfo(Pop.GetGenericMethodDefinition()) is { } generic && generic.Owners.Any(owner => owner != Plugin.PluginGuid))
            return;
        Source(definition);
    }

    private static bool CanReadTemplate()
    {
        if (TemplateGeometry is { } known)
            return known;
        // A foreign constructor/factory patch may intentionally change an
        // instance's grids. Preserve that path instead of guessing its result.
        foreach (var method in Harmony.GetAllPatchedMethods())
            if ((method.DeclaringType == typeof(EFT.ItemFactory) && method.Name == "CreateItem" ||
                method.IsConstructor && method.DeclaringType is { } type &&
                (typeof(Item).IsAssignableFrom(type) || type == typeof(GridLayoutComponent) || type == typeof(Grid))) &&
                Harmony.GetPatchInfo(method) is { } patches && patches.Owners.Any(owner => owner != Plugin.PluginGuid))
                return (TemplateGeometry = false).Value;
        return (TemplateGeometry = true).Value;
    }

    internal static BackpackGridLayout.Frame[]? Source(VariantDefinition definition)
    {
        if (Sources.TryGetValue(definition.SourceTemplateId, out var cached))
            return cached;
        try
        {
            var factory = Singleton<EFT.ItemFactory>.Instance;
            Grid[] grids;
            string? layout;
            if (CanReadTemplate() && factory.ItemTemplates.TryGetValue(definition.SourceTemplateId, out var template) && template is CompoundItemTemplate compound)
            {
                // Native compound items clone these same grids. Geometry needs
                // neither an item instance, its components nor its attributes.
                grids = compound.Grids;
                layout = (template as IGridLayoutComponentTemplate)?.LayoutName;
            }
            else
            {
                var source = factory.CreateItem(Guid.NewGuid().ToString("N").Substring(0, 24), definition.SourceTemplateId, null) as CompoundItem;
                if (source is null)
                    return null;
                grids = source.Grids;
                layout = Native.Component<GridLayoutComponent>(source)?.Template.LayoutName;
            }
            if (grids.Length == 0)
                return null;
            if (string.IsNullOrEmpty(layout))
            {
                if (grids.Length != 1)
                    return null;
                var size = ItemViewFactory.GetCellPixelSize(new IntVec2(grids[0].GridWidth, grids[0].GridHeight));
                return Sources[definition.SourceTemplateId] = new[] { new BackpackGridLayout.Frame(0f, 0f, size.X, size.Y) };
            }
            // Several native templates use identical prefab geometry. Cache
            // by layout AND dimensions so expanded or modded shapes cannot mix.
            var shape = layout + ":" + string.Join(";", grids.Select(grid => grid.GridWidth + "x" + grid.GridHeight));
            if (Shapes.TryGetValue(shape, out var shared))
                return Sources[definition.SourceTemplateId] = shared;
            var prefab = Pop.Invoke(null, new object[] { "UI/Rig Layouts/" + layout });
            if (prefab is null)
                throw new InvalidOperationException("Vanilla grid prefab was not found.");
            return Sources[definition.SourceTemplateId] = Shapes[shape] = ReadPrefab(prefab, grids);
        }
        catch (Exception error)
        {
            if (Warnings.Add(definition.SourceTemplateId))
                Plugin.LogSource.LogWarning($"Backpack pocket layout unavailable for {definition.SourceShortName}: {error.Message}");
            return null;
        }
    }

    private static BackpackGridLayout.Frame[] ReadPrefab(object prefab, Grid[] grids)
    {
        // Rebuild a private, unbound clone: some vanilla prefabs arrange their
        // pockets through nested layout groups rather than saved positions.
        // Never resize the shared resource or register a temporary item view.
        var snapshot = UnityEngine.Object.Instantiate((UnityEngine.Object)prefab);
        try
        {
            if (Native.Get(snapshot, "_presetGridViews") is not GridView[] views || views.Length != grids.Length)
                throw new InvalidOperationException("Vanilla grid count differs from its template.");
            var root = (RectTransform)((Component)(object)snapshot).transform;
            ((Component)(object)snapshot).gameObject.SetActive(true);
            for (var i = 0; i < views.Length; i++)
            {
                var component = (Component)(object)views[i];
                component.gameObject.SetActive(true);
                var size = ItemViewFactory.GetCellPixelSize(new IntVec2(grids[i].GridWidth, grids[i].GridHeight));
                ((RectTransform)component.transform).sizeDelta = new Vector2(size.X, size.Y);
                foreach (var behaviour in component.GetComponents<Behaviour>())
                    if (behaviour.GetType().Name == "LayoutElement")
                    {
                        if (behaviour is UnityEngine.UI.LayoutElement element && element.GetType() == typeof(UnityEngine.UI.LayoutElement))
                        {
                            element.minWidth = size.X;
                            element.minHeight = size.Y;
                        }
                        else
                        {
                            Native.Set(behaviour, "minWidth", (float)size.X);
                            Native.Set(behaviour, "minHeight", (float)size.Y);
                        }
                    }
            }
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(root);
            var frames = new BackpackGridLayout.Frame[views.Length];
            for (var i = 0; i < views.Length; i++)
            {
                var rect = (RectTransform)((Component)(object)views[i]).transform;
                var topLeft = root.InverseTransformPoint(rect.TransformPoint(new Vector3(rect.rect.xMin, rect.rect.yMax, 0f)));
                var size = ItemViewFactory.GetCellPixelSize(new IntVec2(grids[i].GridWidth, grids[i].GridHeight));
                frames[i] = new BackpackGridLayout.Frame(topLeft.x, -topLeft.y, size.X, size.Y);
            }
            var left = frames.Min(f => f.X);
            var top = frames.Min(f => f.Y);
            return frames.Select(f => new BackpackGridLayout.Frame(f.X - left, f.Y - top, f.Width, f.Height)).ToArray();
        }
        finally
        {
            UnityEngine.Object.Destroy(snapshot);
        }
    }
}

[HarmonyPatch(typeof(GeneratedGridsView), "Show")]
internal static class BackpackCompartmentLayoutPatch
{
    private static readonly ConditionalWeakTable<GeneratedGridsView, ViewLayout> Layouts = new();
    private static readonly Type DraggedItemType = AccessTools.TypeByName("EFT.UI.DragAndDrop.DraggedItemView");
    private sealed class ViewLayout
    {
        internal BackpackLayoutPlan? Plan;
        internal RectTransform[] Rects = Array.Empty<RectTransform>();
        internal ItemUiContext Context = null!;
        internal readonly Func<bool> Dragging;
        internal ViewLayout() => Dragging = IsDragging;
        private bool IsDragging() => Context.DragLayer != null && Context.DragLayer.childCount > 0 &&
            Context.DragLayer.GetComponentsInChildren(DraggedItemType, false).Length > 0;
    }
    private static void Postfix(GeneratedGridsView __instance, CompoundItem compoundItem, ItemUiContext itemUiContext)
    {
        if (!RarityResolver.TryGetDefinition(compoundItem, out var d) || d.BackpackEffect is null && d.ChestRigEffect is null)
            return;
        DiagnosticPathTrace trace = default;
        DiagnosticPathTrace.Start(ref trace, DiagnosticPathTrace.Path.Container);
        var original = BackpackVanillaGridLayout.Source(d);
        trace.Phase(0);
        var views = __instance.GridViews;
        if (views.Length == 0 || original is null || views.Length < original.Length)
            return;
        var root = (RectTransform)((Component)(object)__instance).transform;
        // A layout group would put the pockets back in a horizontal row during
        // the next Unity layout pass. All child GridViews remain native.
        foreach (var behaviour in ((Component)(object)__instance).GetComponents<Behaviour>())
            if (behaviour.GetType().Name.Contains("LayoutGroup") || behaviour.GetType().Name == "ContentSizeFitter")
                behaviour.enabled = false;
        var layout = Layouts.GetValue(__instance, _ => new ViewLayout());
        if (layout.Plan is null || layout.Plan.Count != views.Length)
        {
            layout.Plan = new BackpackLayoutPlan(views.Length);
            layout.Rects = new RectTransform[views.Length];
        }
        trace.Phase(1);
        var plan = layout.Plan;
        for (var i = 0; i < views.Length; i++)
        {
            var size = ItemViewFactory.GetCellPixelSize(new IntVec2(compoundItem.Grids[i].GridWidth, compoundItem.Grids[i].GridHeight));
            plan.SetSize(i, size.X, size.Y);
        }
        plan.Resolve(original, d.BackpackEffect?.Key == "BP11-1");
        trace.Phase(2);
        var positions = plan.Positions;
        var replacedRects = false;
        for (var i = 0; i < views.Length; i++)
        {
            var rect = (RectTransform)((Component)(object)views[i]).transform;
            if (!ReferenceEquals(layout.Rects[i], rect))
            {
                // A prior navigation binding retains the old array until
                // Register restores its scales. Do not mutate that array.
                if (!replacedRects)
                {
                    layout.Rects = (RectTransform[])layout.Rects.Clone();
                    replacedRects = true;
                }
                layout.Rects[i] = rect;
            }
            // Native Show or a window reparent can change the transforms even
            // when geometry is unchanged. Always restore the actual view.
            var corner = new Vector2(0f, 1f);
            var position = new Vector2(positions[i].X, -positions[i].Y);
            if (!rect.anchorMin.Equals(corner))
                rect.anchorMin = corner;
            if (!rect.anchorMax.Equals(corner))
                rect.anchorMax = corner;
            if (!rect.pivot.Equals(corner))
                rect.pivot = corner;
            if (!rect.anchoredPosition.Equals(position))
                rect.anchoredPosition = position;
        }
        trace.Phase(3);
        BackpackLayoutSizing.Apply(root, plan.Width, plan.Height);
        trace.Phase(4);
        var backpack = d.BackpackEffect;
        var rig = d.ChestRigEffect;
        if (backpack is not null && (backpack.ExtraGrids.Count > 0 || backpack.ExtraColumns > 0 || backpack.ExtraRows > 0) ||
            rig is not null && (rig.ExtraGrids.Count > 0 || rig.ExtraColumns > 0 || rig.ExtraRows > 0 || rig.ExpandEveryGrid))
        {
            layout.Context = itemUiContext;
            // Binding teardown/restoration still runs on every Show.
            ExpandedStorageNavigation.Register(root, layout.Rects, layout.Dragging, original.Length, d.BackpackEffect?.Key == "BP11-1");
        }
        trace.Phase(5);
    }
}
