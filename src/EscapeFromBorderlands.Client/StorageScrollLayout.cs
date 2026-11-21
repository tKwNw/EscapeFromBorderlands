using System;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeFromBorderlands.Client;

internal static class StorageScrollLayout
{
    private static readonly FieldInfo? Rebuilt = AccessTools.Field(typeof(ScrollRect), "m_HasRebuiltLayout");
    private static int Compatible;
    private static readonly Dictionary<ScrollRect, Alignment> Pending = new();
    private sealed class Alignment : ICanvasElement
    {
        internal readonly ScrollRect Scroll;
        internal bool Cancelled;
        internal Alignment(ScrollRect scroll) => Scroll = scroll;
        public Transform transform => Scroll.transform;
        public bool IsDestroyed() => Cancelled || Scroll == null;
        public void Rebuild(CanvasUpdate phase) { }
        public void GraphicUpdateComplete() { }
        public void LayoutComplete()
        {
            if (Pending.TryGetValue(Scroll, out var current) && ReferenceEquals(current, this)) Pending.Remove(Scroll);
            if (Cancelled || Scroll == null || !Scroll.isActiveAndEnabled) return;
            // Unity calls LayoutComplete after every queued PostLayout, before
            // graphic rebuilding/rendering. No extra canvas or subtree pass.
            DiagnosticPathTrace trace = default;
            DiagnosticPathTrace.Start(ref trace, DiagnosticPathTrace.Path.ScrollAlignment);
                Scroll.horizontalNormalizedPosition = 0f;
                var velocity = Scroll.velocity;
                if (velocity.x != 0f) Scroll.velocity = new Vector2(0f, velocity.y);
                trace.Phase(0);
        }
    }
    internal static bool TryDefer(Component component)
    {
        if (component is not ScrollRect scroll || !scroll.isActiveAndEnabled ||
            (scroll.GetType() != typeof(ScrollRect) && scroll.GetType() != typeof(EFT.UI.ScrollRectNoDrag)) ||
            Rebuilt is null || Rebuilt.GetValue(scroll) is not false || CanvasUpdateRegistry.IsRebuildingLayout() || !CanPrepare()) return false;
        if (Pending.ContainsKey(scroll)) return true;
        var alignment = new Alignment(scroll);
        Pending.Add(scroll, alignment);
        try
        {
            if (CanvasUpdateRegistry.TryRegisterCanvasElementForLayoutRebuild(alignment)) return true;
        }
        catch (Exception) { Compatible = -1; }
        alignment.Cancelled = true;
        Pending.Remove(scroll);
        return false;
    }
    internal static void Cancel(Component component)
    {
        if (component is ScrollRect scroll && Pending.TryGetValue(scroll, out var alignment))
        { alignment.Cancelled = true; Pending.Remove(scroll); }
    }
    internal static void Reset()
    {
        // Do not mutate Unity's queue while it may be dispatching callbacks.
        foreach (var alignment in Pending.Values) alignment.Cancelled = true;
        Pending.Clear(); Compatible = 0;
    }

    // A newly enabled native ScrollRect forces EVERY canvas to rebuild before
    // setting its normalized position. EFB has just sized this panel explicitly.
    // Resolve its own layout and native scroll bookkeeping instead. The normal
    // canvas pass remains scheduled, including parent layout and other windows.
    internal static void Prepare(Component component)
    {
        if (component is not ScrollRect scroll || !scroll.isActiveAndEnabled ||
            (scroll.GetType() != typeof(ScrollRect) && scroll.GetType() != typeof(EFT.UI.ScrollRectNoDrag)) ||
            Rebuilt is null || Rebuilt.GetValue(scroll) is not false || CanvasUpdateRegistry.IsRebuildingLayout() ||
            !CanPrepare()) return;
        try
        {
            scroll.Rebuild(CanvasUpdate.Prelayout);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)scroll.transform);
            scroll.Rebuild(CanvasUpdate.PostLayout);
        }
        catch (Exception) { Compatible = -1; } // The caller still runs the native setter.
    }

    private static bool CanPrepare()
    {
        if (Compatible != 0) return Compatible > 0;
        // Hidden/overridden custom scroll behavior is excluded above. Foreign
        // patches on the native layout boundary also keep the original path.
        foreach (var method in new[] {
            AccessTools.Method(typeof(ScrollRect), "Rebuild"),
            AccessTools.Method(typeof(ScrollRect), "SetNormalizedPosition"),
            AccessTools.Method(typeof(ScrollRect), "EnsureLayoutHasRebuilt"),
            AccessTools.Method(typeof(CanvasUpdateRegistry), "PerformUpdate"),
            AccessTools.Method(typeof(LayoutRebuilder), "ForceRebuildLayoutImmediate") })
        {
            if (method is null) { Compatible = -1; return false; }
            if (Harmony.GetPatchInfo(method) is { } patches)
                foreach (var owner in patches.Owners)
                    if (owner != Plugin.PluginGuid) { Compatible = -1; return false; }
        }
        Compatible = 1;
        return true;
    }
}
