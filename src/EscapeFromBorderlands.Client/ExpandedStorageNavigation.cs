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
    private static readonly Type ScrollType = typeof(UnityEngine.UI.ScrollRect);
    private static readonly Dictionary<RectTransform, Binding> Roots = new();
    private static readonly Dictionary<Component, Session> Sessions = new();
    private static readonly Dictionary<RectTransform, Window> Windows = new();
    private static readonly List<Binding> Bindings = new();
    private static readonly List<Session> OpenSessions = new();
    private static readonly List<RectTransform> DeadWindows = new();
    private static readonly Dictionary<(Type, string), PropertyInfo> Properties = new();
    private static bool Warned;
    private static long BindingOrder;
    private static int NavigationDepth;
    private static int WindowRevision;

    internal static void Register(RectTransform root, RectTransform[] grids, Func<bool>? dragging = null, int originalCount = -1, bool sidecar = false)
    {
        DiagnosticPathTrace trace = default;
        DiagnosticPathTrace.Start(ref trace, DiagnosticPathTrace.Path.Navigation);
        var canReuse = NavigationDepth == 0;
        NavigationDepth++;
        try
        {
            Binding binding;
            Session? previousSession = null;
            if (Roots.TryGetValue(root, out var old))
            {
                previousSession = old.AttachedSession;
                old.Detach();
                old.RestoreScales();
                Bindings.Remove(old);
                // A Unity callback can reopen a view while Maintain/Register still
                // uses its old binding. Keep that invocation's object independent.
                if (canReuse)
                {
                    binding = old;
                    binding.Rebind(grids, dragging, originalCount, sidecar);
                }
                else
                    binding = new Binding(root, grids, dragging, originalCount, sidecar);
            }
            else
                binding = new Binding(root, grids, dragging, originalCount, sidecar);
            Roots[root] = binding;
            Bindings.Add(binding);
            trace.Phase(0);
            // Initialize this view before it is shown. Other windows are independent;
            // shared content still updates as one session. LateUpdate reconciles all
            // roots, including unrelated native resize/reparent operations.
            if (!canReuse)
                Maintain(); // Preserve nested native callback ordering.
            else
            {
                var session = Resolve(binding);
                trace.Phase(1);
                session?.Update(measure: true);
                trace.Phase(2);
                if (previousSession is not null && previousSession != session)
                {
                    if (previousSession.Bindings.Count == 0)
                        RemoveSession(previousSession);
                    else
                        previousSession.Update();
                }
            }
        }
        catch (Exception error)
        {
            Report(error);
        }
        finally
        {
            NavigationDepth--;
            trace.Finish();
        }
    }

    private static void Report(Exception error)
    {
        if (!Warned)
        {
            Warned = true;
            Plugin.LogSource.LogWarning($"Expanded storage layout: {error}");
        }
    }

    private static Session? Resolve(Binding binding)
    {
        var root = binding.Root;
        if (!root.gameObject.activeInHierarchy)
        {
            binding.Detach();
            return null;
        }
        if (!binding.Resolved || binding.Parent != root.parent)
        {
            binding.Scroll = FindScroll(root);
            if (binding.Scroll == null && InGridWindow(root))
                binding.Scroll = CreateWindow(binding);
            binding.Parent = root.parent;
            binding.Resolved = true;
            binding.RefreshBudget = true;
        }
        var scroll = binding.Scroll;
        if (scroll == null)
        {
            binding.Detach();
            var parentWidth = (root.parent as RectTransform)?.rect.width ?? BackpackGridLayout.MaximumWidth;
            binding.Apply(Math.Max(1f, Math.Min(BackpackGridLayout.MaximumWidth, parentWidth - Gap)));
            return null;
        }
        // Attachment is invalidated by Detach/RemoveSession. Keep observing
        // visibility/reparenting above, without re-looking up a stable session.
        if (binding.AttachedSession is { } attached && ReferenceEquals(attached.Scroll, scroll))
            return attached;
        if (!Sessions.TryGetValue(scroll, out var session))
        {
            Sessions[scroll] = session = new Session(scroll);
            OpenSessions.Add(session);
        }
        binding.Attach(session);
        return session;
    }

    private static void RemoveSession(Session session)
    {
        while (session.Bindings.Count > 0)
            session.Bindings[session.Bindings.Count - 1].Detach();
        session.Dispose();
        Sessions.Remove(session.Scroll);
        OpenSessions.Remove(session);
    }

    internal static void Maintain()
    {
        NavigationDepth++;
        try
        {
            if (Bindings.Count == 0 && OpenSessions.Count == 0 && Windows.Count == 0)
                return;
            for (var i = Bindings.Count - 1; i >= 0; i--)
            {
                var binding = Bindings[i];
                var root = binding.Root;
                if (root == null)
                {
                    binding.Detach();
                    Roots.Remove(root!);
                    Bindings.RemoveAt(i);
                    continue;
                }
                Resolve(binding);
            }
            for (var i = OpenSessions.Count - 1; i >= 0; i--)
            {
                var session = OpenSessions[i];
                if (session.Scroll == null || session.Bindings.Count == 0)
                    RemoveSession(session);
                else
                    session.Update();
            }
            DeadWindows.Clear();
            foreach (var pair in Windows)
                if (pair.Key == null)
                {
                    if (pair.Value.Wrapper != null)
                        UnityEngine.Object.Destroy(pair.Value.Wrapper.gameObject);
                    DeadWindows.Add(pair.Key!);
                }
            foreach (var key in DeadWindows)
            {
                Windows.Remove(key);
                WindowRevision++;
            }

        }
        catch (Exception error)
        {
            Report(error);
        }
        finally
        {
            NavigationDepth--;
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
            Get(component, "content") is not RectTransform content)
            return;
        content.anchoredPosition = new Vector2(x.Value, content.anchoredPosition.y);
        StopHorizontal(component);
    }

    internal static void Reset()
    {
        StorageScrollLayout.Reset();
        BackpackWrapCache.ResetShared();
        BackpackLayoutPlan.ResetShared();
        try
        {
            foreach (var session in Sessions.Values.ToArray())
                SessionCleanup.Run("container scroll restore", session, static s => s.Dispose());
            foreach (var binding in Roots.Values.ToArray())
                SessionCleanup.Run("container binding restore", binding, static b => b.Restore());
            foreach (var pair in Windows.ToArray())
            {
                SessionCleanup.Run("container window restore", pair, static p =>
                {
                    var root = p.Key;
                    var window = p.Value;
                    try
                    {
                        if (root != null && window.Parent != null)
                        {
                            root.SetParent(window.Parent, false);
                            root.anchorMin = window.Min;
                            root.anchorMax = window.Max;
                            root.pivot = window.Pivot;
                            root.anchoredPosition = window.Position;
                            root.SetSiblingIndex(window.Index);
                        }
                    }
                    finally
                    {
                        if (window.Wrapper != null)
                            UnityEngine.Object.Destroy(window.Wrapper.gameObject);
                    }
                });
            }
        }
        finally
        {
            Sessions.Clear();
            OpenSessions.Clear();
            Roots.Clear();
            Bindings.Clear();
            Windows.Clear();
            DeadWindows.Clear();
            WindowRevision++;
            Warned = false;
        }
    }

    private sealed class Binding
    {
        internal readonly RectTransform Root;
        private long Order;
        private Session? Attached;
        internal Session? AttachedSession => Attached;
        internal float NaturalWidth, NaturalHeight;
        private RectTransform[] Grids = Array.Empty<RectTransform>();
        private BackpackGridLayout.Placement[] Original = Array.Empty<BackpackGridLayout.Placement>();
        private float[] Widths = Array.Empty<float>(), Heights = Array.Empty<float>();
        private Vector3[] Scales = Array.Empty<Vector3>();
        private Func<bool>? Dragging;
        private int OriginalCount;
        private bool Sidecar;
        private readonly BackpackWrapCache WrapCache = new();
        internal Transform? Parent;
        internal Component? Scroll;
        internal bool Resolved, RefreshBudget = true, Pending;
        internal int Settling = 2;
        internal float Limit;
        private float LastLimit = -1f;
        private int WindowSeen = -1;
        private Window? CachedWindow;
        internal Window? Window
        {
            get
            {
                if (WindowSeen != WindowRevision)
                {
                    Windows.TryGetValue(Root, out CachedWindow);
                    WindowSeen = WindowRevision;
                }
                return CachedWindow;
            }
        }

        internal Binding(RectTransform root, RectTransform[] grids, Func<bool>? dragging, int originalCount, bool sidecar)
        {
            Root = root;
            Rebind(grids, dragging, originalCount, sidecar);
        }
        internal void Rebind(RectTransform[] grids, Func<bool>? dragging, int originalCount, bool sidecar)
        {
            Order = ++BindingOrder;
            Grids = grids;
            Dragging = dragging;
            OriginalCount = originalCount;
            Sidecar = sidecar;
            Parent = null;
            Scroll = null;
            Resolved = Pending = false;
            RefreshBudget = true;
            Settling = 2;
            Limit = 0f;
            LastLimit = -1f;
            NaturalWidth = Root.rect.width;
            NaturalHeight = Root.rect.height;
            if (Original.Length != grids.Length)
            {
                Original = new BackpackGridLayout.Placement[grids.Length];
                Widths = new float[grids.Length];
                Heights = new float[grids.Length];
                Scales = new Vector3[grids.Length];
            }
            for (var i = 0; i < grids.Length; i++)
            {
                var grid = grids[i];
                Scales[i] = grid.localScale;
                Original[i] = new BackpackGridLayout.Placement(grid.anchoredPosition.x, -grid.anchoredPosition.y);
                Widths[i] = grid.rect.width * Scales[i].x;
                Heights[i] = grid.rect.height * Scales[i].y;
            }
        }

        internal void Detach()
        {
            if (Attached is null)
                return;
            Attached.Bindings.Remove(this);
            Attached = null;
        }

        internal void Attach(Session session)
        {
            if (ReferenceEquals(Attached, session))
                return;
            Detach();
            Attached = session;
            var i = session.Bindings.Count;
            // Maintain the same reverse-registration order as the old rebuild.
            while (i > 0 && session.Bindings[i - 1].Order < Order)
                i--;
            session.Bindings.Insert(i, this);
        }

        internal bool Apply(float limit)
        {
            limit = Math.Min(BackpackGridLayout.MaximumWidth, Math.Max(1f, limit));
            if (Math.Abs(LastLimit - limit) < .5f)
                return true;
            if (Dragging?.Invoke() == true)
                return false;
            foreach (var grid in Grids)
                if (grid == null)
                    return false;
            var layout = WrapCache.Read(Original, Widths, Heights, limit, Gap, OriginalCount, Sidecar);
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
        {
            for (var i = 0; i < Grids.Length; i++)
                if (Grids[i] != null)
                    Grids[i].localScale = Scales[i];
        }

        internal void Restore()
        {
            RestoreScales();
            for (var i = 0; i < Grids.Length; i++)
                if (Grids[i] != null)
                    Grids[i].anchoredPosition = new Vector2(Original[i].X, -Original[i].Y);
            if (Root != null)
                BackpackLayoutSizing.Apply(Root, NaturalWidth, NaturalHeight);
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
                Get(scroll, "content") is RectTransform content && root.IsChildOf(content))
                return scroll;
        return null;
    }

    private static bool InGridWindow(RectTransform root)
    {
        for (var parent = root.parent; parent != null; parent = parent.parent)
            if (parent.GetComponents<Component>().Any(c => c != null && c.GetType().FullName == "EFT.UI.GridWindow"))
                return true;
        return false;
    }

    private static Component CreateWindow(Binding binding)
    {
        var root = binding.Root;
        var parent = root.parent ?? throw new InvalidOperationException("Expanded container has no window parent.");
        var window = new Window
        {
            Parent = parent,
            Min = root.anchorMin,
            Max = root.anchorMax,
            Pivot = root.pivot,
            Position = root.anchoredPosition,
            Index = root.GetSiblingIndex(),
            Width = Math.Min(binding.NaturalWidth, BackpackGridLayout.MaximumWidth) + VerticalBarWidth + Gap
        };
        var wrapper = Rect("EFB container viewport", parent);
        wrapper.SetSiblingIndex(root.GetSiblingIndex());
        wrapper.anchorMin = root.anchorMin;
        wrapper.anchorMax = root.anchorMax;
        wrapper.pivot = root.pivot;
        wrapper.anchoredPosition = root.anchoredPosition;
        var viewport = Rect("Viewport", wrapper);
        Stretch(viewport);
        viewport.offsetMax = new Vector2(-VerticalBarWidth, 0f);
        viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        root.SetParent(viewport, false);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0f, 1f);
        root.anchoredPosition = Vector2.zero;
        var scroll = wrapper.gameObject.AddComponent<EFT.UI.ScrollRectNoDrag>();
        scroll.content = root;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        scroll.verticalScrollbar = (UnityEngine.UI.Scrollbar)VerticalScrollbar(wrapper);
        // Reserve the bar gutter even while hidden to avoid width/reflow loops.
        scroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;
        window.Wrapper = wrapper;
        var canvasType = typeof(Canvas);
        for (var ancestor = wrapper.parent; ancestor != null; ancestor = ancestor.parent)
            if (ancestor.gameObject.GetComponent(canvasType) != null)
            {
                window.Canvas = ancestor as RectTransform;
                break;
            }
        Windows[root] = window;
        WindowRevision++;
        SizeWindow(root, window);
        return scroll;
    }

    private static void SizeWindow(RectTransform root, Window window)
    {
        var canvas = window.Canvas;
        var canvasRect = canvas?.rect;
        var width = Math.Min(window.Width, Math.Max(128f, (canvasRect?.width ?? 1920f) * .85f));
        var height = Math.Min(root.rect.height, Math.Max(128f, (canvasRect?.height ?? 1080f) * .75f));
        var wrapperRect = window.Wrapper.rect;
        if (Math.Abs(wrapperRect.width - width) > .5f || Math.Abs(wrapperRect.height - height) > .5f)
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
            Scroll = scroll;
            Content = (RectTransform)Get(scroll, "content")!;
            Viewport = Get(scroll, "viewport") as RectTransform ?? (RectTransform)scroll.transform;
            OriginalHorizontal = Get(scroll, "horizontal") is true;
            OriginalBar = Get(scroll, "horizontalScrollbar") as Component;
            OriginalBarActive = OriginalBar != null && OriginalBar.gameObject.activeSelf;
            NativeSizeX = Content.sizeDelta.x;
            Set(scroll, "horizontal", false);
            Set(scroll, "horizontalScrollbar", null);
            if (OriginalBar != null)
                OriginalBar.gameObject.SetActive(false);
        }

        internal void Update(bool measure = false)
        {
            if (Content == null || Viewport == null)
                return;
            DiagnosticPathTrace trace = default;
            if (measure)
                DiagnosticPathTrace.Start(ref trace, DiagnosticPathTrace.Path.SessionLayout);
            foreach (var binding in Bindings)
                if (binding.Window is { } window)
                    SizeWindow(binding.Root, window);
            var viewportRect = Viewport.rect;
            var width = viewportRect.width;
            var height = viewportRect.height;
            var resized = Math.Abs(width - LastViewportWidth) > .5f || Math.Abs(height - LastViewportHeight) > .5f;
            var standalone = false;
            foreach (var binding in Bindings)
                if (binding.Root == Content)
                {
                    standalone = true;
                    break;
                }
            var widthChanged = !standalone && Math.Abs(Content.rect.width - width) > .5f;
            if (widthChanged)
            {
                if (float.IsNaN(LastForcedX) || Math.Abs(Content.sizeDelta.x - LastForcedX) > .5f)
                    NativeSizeX = Content.sizeDelta.x;
                Content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                LastForcedX = Content.sizeDelta.x;
            }
            trace.Phase(0);
            // SetNormalizedPosition traverses bounds and can rebuild native UI.
            // Do this only when the panel actually changes, never on idle frames.
            if (resized || widthChanged)
            {
                // Our own viewport uses a left-anchored, left-pivot content at
                // x=0. Align it directly instead of forcing ScrollRect's global
                // canvas/bounds rebuild. Foreign/native layouts keep that path.
                if (standalone && Windows.ContainsKey(Content) && Content.parent == Viewport &&
                    Content.anchorMin.x == 0f && Content.anchorMax.x == 0f && Content.pivot.x == 0f &&
                    Content.localRotation == Quaternion.identity && Content.localScale.x == 1f && Content.localScale.y == 1f)
                {
                    var position = Content.anchoredPosition;
                    if (position.x != 0f)
                        Content.anchoredPosition = new Vector2(0f, position.y);
                }
                else
                {
                    var deferred = StorageScrollLayout.TryDefer(Scroll);
                    if (!deferred)
                        StorageScrollLayout.Prepare(Scroll);
                    trace.Phase(1);
                    if (!deferred)
                        Set(Scroll, "horizontalNormalizedPosition", 0f);
                }
                StopHorizontal(Scroll);
            }
            trace.Phase(2);
            foreach (var binding in Bindings)
            {
                var root = binding.Root;
                if (resized || binding.RefreshBudget || binding.Settling > 0)
                {
                    var left = Content.InverseTransformPoint(root.TransformPoint(new Vector3(root.rect.xMin, root.rect.yMin, 0f))).x - Content.rect.xMin;
                    var scale = Math.Abs(Content.InverseTransformVector(root.TransformVector(new Vector3(1f, 0f, 0f))).x);
                    binding.Limit = Math.Max(1f, (width - Math.Max(0f, left) - Gap) / Math.Max(.001f, scale));
                    binding.RefreshBudget = false;
                    if (binding.Settling > 0)
                        binding.Settling--;
                    binding.Pending = !binding.Apply(binding.Limit);
                }
                else if (binding.Pending)
                    binding.Pending = !binding.Apply(binding.Limit);
                if (binding.Window is { } window)
                    SizeWindow(root, window);
            }
            LastViewportWidth = width;
            LastViewportHeight = height;
            trace.Phase(3);
        }

        internal void Dispose()
        {
            StorageScrollLayout.Cancel(Scroll);
            if (Scroll == null)
                return;
            Set(Scroll, "horizontal", OriginalHorizontal);
            Set(Scroll, "horizontalScrollbar", OriginalBar);
            if (OriginalBar != null)
                OriginalBar.gameObject.SetActive(OriginalBarActive);
            if (Content != null && !float.IsNaN(LastForcedX) && Math.Abs(Content.sizeDelta.x - LastForcedX) < .5f)
                Content.sizeDelta = new Vector2(NativeSizeX, Content.sizeDelta.y);
        }
    }

    private static void StopHorizontal(Component scroll)
    {
        if (Get(scroll, "velocity") is Vector2 velocity && velocity.x != 0f)
            Set(scroll, "velocity", new Vector2(0f, velocity.y));
    }

    private static Component VerticalScrollbar(RectTransform parent)
    {
        var rect = Rect("EFB vertical scrollbar", parent);
        rect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 0f);
        rect.sizeDelta = new Vector2(VerticalBarWidth, 0f);
        rect.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(.08f, .08f, .08f, .96f);
        var slide = Rect("Sliding area", rect);
        Stretch(slide);
        slide.offsetMin = new Vector2(3f, 3f);
        slide.offsetMax = new Vector2(-3f, -3f);
        var handle = Rect("Handle", slide);
        Stretch(handle);
        var graphic = handle.gameObject.AddComponent<UnityEngine.UI.Image>();
        graphic.color = new Color(.7f, .65f, .5f, 1f);
        var bar = rect.gameObject.AddComponent<UnityEngine.UI.Scrollbar>();
        bar.handleRect = handle;
        bar.targetGraphic = graphic;
        bar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
        return bar;
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }
    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
    }
    private static PropertyInfo Property(object obj, string name)
    {
        var key = (obj.GetType(), name);
        if (!Properties.TryGetValue(key, out var property))
            Properties[key] = property = AccessTools.Property(key.Item1, name);
        return property;
    }
    private static object? Get(object obj, string property)
    {
        var member = Property(obj, property);
        // Native and inherited ScrollRect properties can use their typed API.
        // A foreign hidden/overridden property keeps its original reflection path.
        if (obj is UnityEngine.UI.ScrollRect scroll && member.DeclaringType == ScrollType)
            switch (property)
            {
                case "content":
                    return scroll.content;
                case "viewport":
                    return scroll.viewport;
                case "horizontal":
                    return scroll.horizontal;
                case "horizontalScrollbar":
                    return scroll.horizontalScrollbar;
                case "velocity":
                    return scroll.velocity;
            }
        return member.GetValue(obj);
    }
    private static void Set(object obj, string property, object? value)
    {
        var member = Property(obj, property);
        if (obj is UnityEngine.UI.ScrollRect scroll && member.DeclaringType == ScrollType)
            switch (property)
            {
                case "horizontal":
                    scroll.horizontal = (bool)value!;
                    return;
                case "horizontalScrollbar":
                    scroll.horizontalScrollbar = (UnityEngine.UI.Scrollbar?)value;
                    return;
                case "horizontalNormalizedPosition":
                    scroll.horizontalNormalizedPosition = (float)value!;
                    return;
                case "velocity":
                    scroll.velocity = (Vector2)value!;
                    return;
            }
        member.SetValue(obj, value);
    }
    private static void EnumValue(object obj, string property, string value)
    {
        var p = AccessTools.Property(obj.GetType(), property);
        p.SetValue(obj, Enum.Parse(p.PropertyType, value));
    }
}
