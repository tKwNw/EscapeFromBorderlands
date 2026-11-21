using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

// Item construction can run on a worker. Queue identifiers there; only the
// Unity update thread touches templates/resources. No inventory traversal.
internal static class BackpackLayoutWarmup
{
    private static readonly ConcurrentDictionary<string, byte> Seen = new(StringComparer.Ordinal);
    private static readonly ConcurrentQueue<VariantDefinition> Pending = new();
    private static readonly Dictionary<string, ResourceRequest> Requests = new(StringComparer.Ordinal);
    private static readonly Queue<(VariantDefinition Definition, ResourceRequest? Request)> Geometry = new();
    internal static void Queue(Item? item)
    {
        if (item is not CompoundItem || !RarityResolver.TryGetDefinition(item, out var definition) ||
            definition.BackpackEffect is null && definition.ChestRigEffect is null) return;
        if (Seen.TryAdd(definition.SourceTemplateId, 0)) Pending.Enqueue(definition);
    }
    internal static void Maintain()
    {
        PrepareNext();
        if (Pending.IsEmpty || Singleton<EFT.ItemFactory>.Instance is not { } factory) return;
        // One asynchronous request per frame. UI still uses the native Pop
        // method; an unready asset or a foreign resource patch keeps its normal
        // fallback instead of showing incomplete or rearranging compartments.
        if (!Pending.TryDequeue(out var definition)) return;
        try
        {
            ResourceRequest? request = null;
            if (factory.ItemTemplates.TryGetValue(definition.SourceTemplateId, out var template) && template is IGridLayoutComponentTemplate layout &&
                !string.IsNullOrEmpty(layout.LayoutName) && !Requests.TryGetValue(layout.LayoutName, out request))
                Requests[layout.LayoutName] = request = Resources.LoadAsync("UI/Rig Layouts/" + layout.LayoutName, typeof(EFT.UI.DragAndDrop.ContainedGridsView));
            Geometry.Enqueue((definition, request));
        }
        catch (Exception error)
        {
            Plugin.LogSource.LogWarning($"EFB layout prefetch unavailable; native loading retained: {error.Message}");
        }
    }
    private static void PrepareNext()
    {
        if (Geometry.Count == 0) return;
        var game = Singleton<AbstractGame>.Instance;
        // Prefab clones/rebuilds belong in menus or raid setup, never as new
        // background work while moving/fighting. First-open fallback remains.
        if (game is not null && (game is not LocalGame raid ||
            raid.Status is GameStatus.Started or GameStatus.Stopping or GameStatus.SoftStopping)) return;
        var next = Geometry.Dequeue();
        if (next.Request is not null && !next.Request.isDone) { Geometry.Enqueue(next); return; }
        try { BackpackVanillaGridLayout.Prepare(next.Definition); }
        catch (Exception error)
        { Plugin.LogSource.LogWarning($"EFB layout preparation unavailable; native opening retained: {error.Message}"); }
    }
}

[HarmonyPatch(typeof(EFT.ItemFactory), nameof(EFT.ItemFactory.CreateItem))]
internal static class BackpackLayoutItemCreatedPatch
{
    private static void Postfix(Item __result) => BackpackLayoutWarmup.Queue(__result);
}
