using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class ExpandedStorageNavigation
{
    private const float Gap = 4f, VerticalBarWidth = 18f;
    private static readonly Type ScrollType = AccessTools.TypeByName("UnityEngine.UI.ScrollRect");
    private static readonly Dictionary<RectTransform, Binding> Roots = new();
    private static readonly Dictionary<Component, Session> Sessions = new();
    private static readonly Dictionary<RectTransform, Window> Windows = new();
    private static readonly List<Binding> Bindings = new();
    private static readonly List<Session> OpenSessions = new();
    private static readonly List<RectTransform> DeadWindows = new();
    private static readonly Dictionary<(Type, string), PropertyInfo> Properties = new();
    private static bool Warned;
    private static long BindingOrder;

    internal static void Register(RectTransform root, RectTransform[] grids, Func<bool>? dragging = null, int originalCount = -1, bool sidecar = false)
    {
        if (Roots.TryGetValue(root, out var old)) { old.Detach(); old.RestoreScales(); Bindings.Remove(old); }
        var binding = new Binding(root, grids, dragging, originalCount, sidecar);
        Roots[root] = binding; Bindings.Add(binding);
        // Apply the initial width budget before the first visible layout pass.
        Maintain();
    }

    internal static void Maintain()
    {
        try
        {
            if (Bindings.Count == 0 && OpenSessions.Count == 0 && Windows.Count == 0) return;
            for (var i = Bindings.Count - 1; i >= 0; i--)
            {
                var binding = Bindings[i]; var root = binding.Root;
                if (root == null) { binding.Detach(); Roots.Remove(root!); Bindings.RemoveAt(i); continue; }
                if (!root.gameObject.activeInHierarchy) { binding.Detach(); continue; }
                if (!binding.Resolved || binding.Parent != root.parent)
                {
                    binding.Scroll = FindScroll(root);
                    if (binding.Scroll == null && InGridWindow(root)) binding.Scroll = CreateWindow(binding);
                    binding.Parent = root.parent; binding.Resolved = true; binding.RefreshBudget = true;
                }
                var scroll = binding.Scroll;
                if (scroll == null)
                {
                    binding.Detach();
                    var parentWidth = (root.parent as RectTransform)?.rect.width ?? BackpackGridLayout.MaximumWidth;
                    binding.Apply(Math.Max(1f, Math.Min(BackpackGridLayout.MaximumWidth, parentWidth - Gap)));
                    continue;
                }
                if (!Sessions.TryGetValue(scroll, out var session))
                { Sessions[scroll] = session = new Session(scroll); OpenSessions.Add(session); }
                binding.Attach(session);
            }
            for (var i = OpenSessions.Count - 1; i >= 0; i--)
            {
                var session = OpenSessions[i];
                if (session.Scroll == null || session.Bindings.Count == 0)
                {
                    while (session.Bindings.Count > 0) session.Bindings[session.Bindings.Count - 1].Detach();
                    session.Dispose(); Sessions.Remove(session.Scroll!); OpenSessions.RemoveAt(i);
                }
                else session.Update();
            }
            DeadWindows.Clear();
            foreach (var pair in Windows)
                if (pair.Key == null)
                { if (pair.Value.Wrapper != null) UnityEngine.Object.Destroy(pair.Value.Wrapper.gameObject); DeadWindows.Add(pair.Key!); }
            foreach (var key in DeadWindows) Windows.Remove(key);

        }
        catch (Exception error)
        {
            if (!Warned) { Warned = true; Plugin.LogSource.LogWarning($"Expanded storage layout: {error}"); }
        }
    }

    // The native item-drag helper moves both axes even if horizontal scrolling
    // is disabled. Preserve only its vertical movement in managed EFB panels.
    internal static float? BeforeAutoPan(object scroll) => scroll is Component component &&
        Sessions.ContainsKey(component) && Get(component, "content") is RectTransform content
            ? content.anchoredPosition.x : null;

    internal static void AfterAutoPan(object scroll, float? x)
    {
        if (x is null || scroll is not Component component || !Sessions.ContainsKey(component) ||
            Get(component, "content") is not RectTransform content) return;
        content.anchoredPosition = new Vector2(x.Value, content.anchoredPosition.y);
        StopHorizontal(component);
    }

    internal static void Reset()
    {
        foreach (var session in Sessions.Values) session.Dispose();
        Sessions.Clear(); OpenSessions.Clear();
        foreach (var binding in Roots.Values) binding.Restore();
        Roots.Clear(); Bindings.Clear();
        foreach (var pair in Windows)
        {
            var root = pair.Key; var window = pair.Value;
            if (root != null && window.Parent != null)
            {
                root.SetParent(window.Parent, false);
                root.anchorMin = window.Min; root.anchorMax = window.Max; root.pivot = window.Pivot;
                root.anchoredPosition = window.Position; root.SetSiblingIndex(window.Index);
            }
            if (window.Wrapper != null) UnityEngine.Object.Destroy(window.Wrapper.gameObject);
        }
        Windows.Clear(); Warned = false;
    }

    private sealed class Binding
    {
        internal readonly RectTransform Root;
        private readonly long Order = ++BindingOrder;
        private Session? Attached;
        internal readonly float NaturalWidth, NaturalHeight;
        private readonly RectTransform[] Grids;
        private readonly BackpackGridLayout.Placement[] Original;
        private readonly float[] Widths, Heights;
        private readonly Vector3[] Scales;
        private readonly Func<bool>? Dragging;
        private readonly int OriginalCount;
        private readonly bool Sidecar;
        internal Transform? Parent;
        internal Component? Scroll;
        internal bool Resolved, RefreshBudget = true, Pending;
        internal int Settling = 2;
        internal float Limit;
        private float LastLimit = -1f;

        internal Binding(RectTransform root, RectTransform[] grids, Func<bool>? dragging, int originalCount, bool sidecar)
        {
            Root = root; Grids = grids; Dragging = dragging; OriginalCount = originalCount; Sidecar = sidecar;
            NaturalWidth = root.rect.width; NaturalHeight = root.rect.height;
            Original = new BackpackGridLayout.Placement[grids.Length];
            Widths = new float[grids.Length]; Heights = new float[grids.Length]; Scales = new Vector3[grids.Length];
            for (var i = 0; i < grids.Length; i++)
            {
                var grid = grids[i]; Scales[i] = grid.localScale;
                Original[i] = new BackpackGridLayout.Placement(grid.anchoredPosition.x, -grid.anchoredPosition.y);
                Widths[i] = grid.rect.width * Scales[i].x; Heights[i] = grid.rect.height * Scales[i].y;
            }
        }

        internal void Detach()
        {
            if (Attached is null) return;
            Attached.Bindings.Remove(this);
            Attached = null;
        }

        internal void Attach(Session session)
        {
            if (ReferenceEquals(Attached, session)) return;
            Detach();
            Attached = session;
            var i = session.Bindings.Count;
            // Maintain the same reverse-registration order as the old rebuild.
            while (i > 0 && session.Bindings[i - 1].Order < Order) i--;
            session.Bindings.Insert(i, this);
        }

        internal bool Apply(float limit)
        {
            limit = Math.Min(BackpackGridLayout.MaximumWidth, Math.Max(1f, limit));
            if (Math.Abs(LastLimit - limit) < .5f) return true;
            if (Dragging?.Invoke() == true) return false;
            foreach (var grid in Grids) if (grid == null) return false;
            var layout = BackpackGridLayout.Wrap(Original, Widths, Heights, limit, Gap, OriginalCount, Sidecar);
            for (var i = 0; i < Grids.Length; i++)
            {
                Grids[i].anchoredPosition = new Vector2(layout.Positions[i].X, -layout.Positions[i].Y);
                Grids[i].localScale = new Vector3(Scales[i].x * layout.Scales[i], Scales[i].y * layout.Scales[i], Scales[i].z);
            }
            BackpackLayoutSizing.Apply(Root, layout.Width, layout.Height);
            LastLimit = limit;
            return true;
        }

        internal void RestoreScales()
        { for (var i = 0; i < Grids.Length; i++) if (Grids[i] != null) Grids[i].localScale = Scales[i]; }

        internal void Restore()
        {
            RestoreScales();
            for (var i = 0; i < Grids.Length; i++)
                if (Grids[i] != null) Grids[i].anchoredPosition = new Vector2(Original[i].X, -Original[i].Y);
            if (Root != null) BackpackLayoutSizing.Apply(Root, NaturalWidth, NaturalHeight);
        }
    }

    private sealed class Window
    {
        internal RectTransform Wrapper = null!;
        internal Transform Parent = null!;
        internal Vector2 Min, Max, Pivot, Position;
        internal int Index;
        internal float Width;
        internal RectTransform? Canvas;
    }

    private static Component? FindScroll(RectTransform root)
    {
        for (var parent = root.parent; parent != null; parent = parent.parent)
            if (parent.gameObject.GetComponent(ScrollType) is Component scroll &&
                Get(scroll, "content") is RectTransform content && root.IsChildOf(content)) return scroll;
        return null;
    }

    private static bool InGridWindow(RectTransform root)
    {
        for (var parent = root.parent; parent != null; parent = parent.parent)
            if (parent.GetComponents<Component>().Any(c => c != null && c.GetType().FullName == "EFT.UI.GridWindow")) return true;
        return false;
    }

    private static Component CreateWindow(Binding binding)
    {
        var root = binding.Root;
        var parent = root.parent ?? throw new InvalidOperationException("Expanded container has no window parent.");
        var window = new Window { Parent = parent, Min = root.anchorMin, Max = root.anchorMax,
            Pivot = root.pivot, Position = root.anchoredPosition, Index = root.GetSiblingIndex(),
            Width = Math.Min(binding.NaturalWidth, BackpackGridLayout.MaximumWidth) + VerticalBarWidth + Gap };
        var wrapper = Rect("EFB container viewport", parent);
        wrapper.SetSiblingIndex(root.GetSiblingIndex());
        wrapper.anchorMin = root.anchorMin; wrapper.anchorMax = root.anchorMax; wrapper.pivot = root.pivot;
        wrapper.anchoredPosition = root.anchoredPosition;
        var viewport = Rect("Viewport", wrapper);
        Stretch(viewport); viewport.offsetMax = new Vector2(-VerticalBarWidth, 0f);
        viewport.gameObject.AddComponent(AccessTools.TypeByName("UnityEngine.UI.RectMask2D"));
        root.SetParent(viewport, false);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0f, 1f); root.anchoredPosition = Vector2.zero;
        var scroll = wrapper.gameObject.AddComponent(AccessTools.TypeByName("EFT.UI.ScrollRectNoDrag"));
        Set(scroll, "content", root); Set(scroll, "viewport", viewport);
        Set(scroll, "horizontal", false); Set(scroll, "vertical", true);
        EnumValue(scroll, "movementType", "Clamped");
        Set(scroll, "verticalScrollbar", VerticalScrollbar(wrapper));
        // Reserve the bar gutter even while hidden to avoid width/reflow loops.
        EnumValue(scroll, "verticalScrollbarVisibility", "AutoHide");
        window.Wrapper = wrapper;
        var canvasType = AccessTools.TypeByName("UnityEngine.Canvas");
        for (var ancestor = wrapper.parent; ancestor != null; ancestor = ancestor.parent)
            if (ancestor.gameObject.GetComponent(canvasType) != null) { window.Canvas = ancestor as RectTransform; break; }
        Windows[root] = window;
        SizeWindow(root, window);
        return scroll;
    }

    private static void SizeWindow(RectTransform root, Window window)
    {
        var canvas = window.Canvas;
        var width = Math.Min(window.Width, Math.Max(128f, (canvas?.rect.width ?? 1920f) * .85f));
        var height = Math.Min(root.rect.height, Math.Max(128f, (canvas?.rect.height ?? 1080f) * .75f));
        if (Math.Abs(window.Wrapper.rect.width - width) > .5f || Math.Abs(window.Wrapper.rect.height - height) > .5f)
            BackpackLayoutSizing.Apply(window.Wrapper, width, height);
    }

    private sealed class Session
    {
        internal readonly Component Scroll;
        internal readonly List<Binding> Bindings = new();
        private readonly RectTransform Content, Viewport;
        private float LastViewportWidth = -1f, LastViewportHeight = -1f;
        private readonly bool OriginalHorizontal;
        private readonly Component? OriginalBar;
        private readonly bool OriginalBarActive;
        private float NativeSizeX, LastForcedX = float.NaN;

        internal Session(Component scroll)
        {
            Scroll = scroll; Content = (RectTransform)Get(scroll, "content")!;
            Viewport = Get(scroll, "viewport") as RectTransform ?? (RectTransform)scroll.transform;
            OriginalHorizontal = Get(scroll, "horizontal") is true;
            OriginalBar = Get(scroll, "horizontalScrollbar") as Component;
            OriginalBarActive = OriginalBar != null && OriginalBar.gameObject.activeSelf;
            NativeSizeX = Content.sizeDelta.x;
            Set(scroll, "horizontal", false); Set(scroll, "horizontalScrollbar", null);
            if (OriginalBar != null) OriginalBar.gameObject.SetActive(false);
        }

        internal void Update()
        {
            if (Content == null || Viewport == null) return;
            foreach (var binding in Bindings)
                if (Windows.TryGetValue(binding.Root, out var window)) SizeWindow(binding.Root, window);
            var width = Viewport.rect.width; var height = Viewport.rect.height;
            var resized = Math.Abs(width - LastViewportWidth) > .5f || Math.Abs(height - LastViewportHeight) > .5f;
            var standalone = false;
            foreach (var binding in Bindings) if (binding.Root == Content) { standalone = true; break; }
            var widthChanged = !standalone && Math.Abs(Content.rect.width - width) > .5f;
            if (widthChanged)
            {
                if (float.IsNaN(LastForcedX) || Math.Abs(Content.sizeDelta.x - LastForcedX) > .5f) NativeSizeX = Content.sizeDelta.x;
                Content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                LastForcedX = Content.sizeDelta.x;
            }
            // SetNormalizedPosition traverses bounds and can rebuild native UI.
            // Do this only when the panel actually changes, never on idle frames.
            if (resized || widthChanged)
            { Set(Scroll, "horizontalNormalizedPosition", 0f); StopHorizontal(Scroll); }
            foreach (var binding in Bindings)
            {
                var root = binding.Root;
                if (resized || binding.RefreshBudget || binding.Settling > 0)
                {
                    var left = Content.InverseTransformPoint(root.TransformPoint(new Vector3(root.rect.xMin, root.rect.yMin, 0f))).x - Content.rect.xMin;
                    var scale = Math.Abs(Content.InverseTransformVector(root.TransformVector(new Vector3(1f, 0f, 0f))).x);
                    binding.Limit = Math.Max(1f, (width - Math.Max(0f, left) - Gap) / Math.Max(.001f, scale));
                    binding.RefreshBudget = false;
                    if (binding.Settling > 0) binding.Settling--;
                    binding.Pending = !binding.Apply(binding.Limit);
                }
                else if (binding.Pending) binding.Pending = !binding.Apply(binding.Limit);
                if (Windows.TryGetValue(root, out var window)) SizeWindow(root, window);
            }
            LastViewportWidth = width; LastViewportHeight = height;

        }

        internal void Dispose()
        {
            if (Scroll == null) return;
            Set(Scroll, "horizontal", OriginalHorizontal); Set(Scroll, "horizontalScrollbar", OriginalBar);
            if (OriginalBar != null) OriginalBar.gameObject.SetActive(OriginalBarActive);
            if (Content != null && !float.IsNaN(LastForcedX) && Math.Abs(Content.sizeDelta.x - LastForcedX) < .5f)
                Content.sizeDelta = new Vector2(NativeSizeX, Content.sizeDelta.y);
        }
    }

    private static void StopHorizontal(Component scroll)
    { if (Get(scroll, "velocity") is Vector2 velocity && velocity.x != 0f) Set(scroll, "velocity", new Vector2(0f, velocity.y)); }

    private static Component VerticalScrollbar(RectTransform parent)
    {
        var rect = Rect("EFB vertical scrollbar", parent);
        Set(rect.gameObject.AddComponent(AccessTools.TypeByName("UnityEngine.UI.LayoutElement")), "ignoreLayout", true);
        rect.anchorMin = new Vector2(1f, 0f); rect.anchorMax = new Vector2(1f, 1f); rect.pivot = new Vector2(1f, 0f);
        rect.sizeDelta = new Vector2(VerticalBarWidth, 0f);
        var imageType = AccessTools.TypeByName("UnityEngine.UI.Image");
        Set(rect.gameObject.AddComponent(imageType), "color", new Color(.08f, .08f, .08f, .96f));
        var slide = Rect("Sliding area", rect); Stretch(slide); slide.offsetMin = new Vector2(3f, 3f); slide.offsetMax = new Vector2(-3f, -3f);
        var handle = Rect("Handle", slide); Stretch(handle);
        var graphic = handle.gameObject.AddComponent(imageType); Set(graphic, "color", new Color(.7f, .65f, .5f, 1f));
        var bar = rect.gameObject.AddComponent(AccessTools.TypeByName("UnityEngine.UI.Scrollbar"));
        Set(bar, "handleRect", handle); Set(bar, "targetGraphic", graphic); EnumValue(bar, "direction", "BottomToTop");
        return bar;
    }

    private static RectTransform Rect(string name, Transform parent)
    { var go = new GameObject(name, typeof(RectTransform)); go.layer = parent.gameObject.layer; var rect = (RectTransform)go.transform; rect.SetParent(parent, false); return rect; }
    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero; rect.anchoredPosition = Vector2.zero; }
    private static PropertyInfo Property(object obj, string name)
    { var key = (obj.GetType(), name); if (!Properties.TryGetValue(key, out var property)) Properties[key] = property = AccessTools.Property(key.Item1, name); return property; }
    private static object? Get(object obj, string property) => Property(obj, property).GetValue(obj);
    private static void Set(object obj, string property, object? value) => Property(obj, property).SetValue(obj, value);
    private static void EnumValue(object obj, string property, string value)
    { var p = AccessTools.Property(obj.GetType(), property); p.SetValue(obj, Enum.Parse(p.PropertyType, value)); }
}
