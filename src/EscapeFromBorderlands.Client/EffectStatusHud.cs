using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class EffectStatusHud
{
    private sealed class Row
    {
        internal GameObject Root = null!;
        internal Component Icon = null!, Label = null!;
        internal EffectStatusText.Layout TextLayout = null!;
        internal Color LastColor;
    }
    private static CharacterHealthPanel? Panel;
    private static GameObject? Root;
    private static Component? TextTemplate;
    private static RectTransform? Anchor;
    private static Sprite? ClockSprite;
    private static Texture2D? ClockTexture;
    private static readonly List<Row> Rows = new();
    private static float NextUpdate, NextFailureReport;
    private static bool PreviousAutoHide;
    internal static bool HoldingVisible { get; private set; }
    internal static void Attach(CharacterHealthPanel panel)
    {
        Reset(); Panel = panel;
        var template = Native.Get(panel, "_effectIconTemplate");
        TextTemplate = Native.Get(template, "_stackCount") as Component;
    }
    internal static void Reset()
    {
        EffectStatusModel.ResetNotices();
        ClearVisuals();
        NextUpdate = NextFailureReport = 0f;
    }
    private static void ClearVisuals()
    {
        if (HoldingVisible && (UnityEngine.Object?)(object?)Panel != null)
            try { HoldingVisible = false; Native.Call(Panel!, "AnimatedShow", PreviousAutoHide); } catch { /* native UI may already be closing */ }
        if (Root != null) UnityEngine.Object.Destroy(Root);
        if (ClockSprite != null) UnityEngine.Object.Destroy(ClockSprite);
        if (ClockTexture != null) UnityEngine.Object.Destroy(ClockTexture);
        Root = null; Anchor = null; ClockSprite = null; ClockTexture = null;
        Rows.Clear(); HoldingVisible = false; TextTemplate = null;
    }
    private static void Hide()
    {
        if (Root != null) Root.SetActive(false);
        if (!HoldingVisible) return;
        HoldingVisible = false;
        if ((UnityEngine.Object?)(object?)Panel != null) Native.Call(Panel!, "AnimatedShow", PreviousAutoHide);
    }
    internal static void Maintain(GameWorld? world)
    {
        if (StatusHudSettings.Enabled?.Value == false) { Hide(); return; }
        if (Time.time < NextUpdate || (UnityEngine.Object?)(object?)Panel == null) return;
        NextUpdate = Time.time + .05f;
        try
        {
        var player = world?.MainPlayer;
        var statuses = new List<EffectStatusModel.Status>();
        if (player?.HealthController?.IsAlive == true)
        {
            if (BackpackRuntime.Records.TryGetValue(player.ProfileId, out var record)) statuses = EffectStatusModel.Collect(record.Until, Time.time);
            BackpackEffectStatus.Add(statuses, BackpackRuntime.Get(player));
            statuses.AddRange(BackpackNative.UseStatuses(player));
            if (player.HandsController is Player.FirearmController firearm && LegendaryRuntime.CurrentStatus(player, firearm.Weapon) is { } gun) statuses.Add(gun);
            statuses.AddRange(AdvancedMeleeRuntime.Statuses(player));
            statuses.AddRange(AdvancedLegendaryActions.SmokeStatuses(player));
            statuses.AddRange(ReusableGrenadeRuntime.Statuses(player));
            statuses.AddRange(EffectStatusModel.Notices(player.ProfileId, Time.time));
        }
            statuses.AddRange(ChestRigRuntime.Statuses(player));
            statuses.AddRange(EarpieceRuntime.Statuses(player));
            statuses = EffectStatusModel.Visible(statuses).Where(StatusHudSettings.Show).ToList();
            if (statuses.Count == 0)
            {
                Hide();
                return;
            }
            if (!TryTextSource(out var textAssets))
            { Hide(); NextUpdate = Time.time + .25f; return; }
            if (Root == null) Create();
            if (Root == null) return;
            var layout = (RectTransform)Root.transform;
            var parent = (RectTransform)layout.parent;
            var origin = Anchor != null ? parent.InverseTransformPoint(Anchor.TransformPoint(new Vector3(Anchor.rect.xMin, Anchor.rect.yMin, 0f))) : Vector3.zero;
            layout.anchoredPosition = new Vector2(origin.x - parent.rect.xMin + StatusHudSettings.HorizontalOffset.Value,
                origin.y - parent.rect.yMax - 8f - StatusHudSettings.VerticalOffset.Value);
            layout.localScale = Vector3.one * StatusHudSettings.Scale.Value;
            if (!Root.activeSelf) Root.SetActive(true);
            if (!HoldingVisible) { PreviousAutoHide = Native.Get(Panel, "AutoHide") is true; HoldingVisible = true; Native.Call(Panel!, "AnimatedShow", false); }
            var heights = new float[statuses.Count];
            for (var i = 0; i < statuses.Count; i++)
            {
                if (i == Rows.Count) Rows.Add(CreateRow());
                var row = Rows[i]; var status = statuses[i];
                if (!row.Root.activeSelf) row.Root.SetActive(true);
                var color = status.Active ? new Color(.4f, .851f, .937f, 1f) : new Color(.68f, .68f, .68f, 1f);
                if (row.LastColor != color)
                { row.LastColor = color; Native.Set(row.Icon, "color", color); Native.Set(row.Label, "color", color); }
                if (!row.TextLayout.Update(textAssets, status.Label, Time.frameCount))
                { Hide(); NextUpdate = Time.time + .25f; return; }
                heights[i] = row.TextLayout.Height;
                var textRect = (RectTransform)row.Label.transform;
                textRect.sizeDelta = new Vector2(EffectStatusLayout.TextWidth, heights[i]);
                ((RectTransform)row.Root.transform).sizeDelta = new Vector2(EffectStatusLayout.Width, heights[i]);
            }
            var offsets = EffectStatusLayout.Offsets(heights, out var total);
            for (var i = 0; i < statuses.Count; i++)
                ((RectTransform)Rows[i].Root.transform).anchoredPosition = new Vector2(0f, -offsets[i]);
            layout.sizeDelta = new Vector2(EffectStatusLayout.Width, total);
            for (var i = statuses.Count; i < Rows.Count; i++) Rows[i].Root.SetActive(false);
        }
        catch (Exception e)
        {
            // Preserve gameplay timers/notices and retry the visual binding.
            // A transient unloaded font must not disable this HUD for the raid.
            ClearVisuals();
            NextUpdate = Time.time + 1f;
            if (Time.unscaledTime >= NextFailureReport)
            {
                NextFailureReport = Time.unscaledTime + 30f;
                Plugin.LogSource.LogWarning($"EFB effect-status HUD will retry after a display error: {e}");
            }
        }
    }

    internal static bool KeepVisible(CharacterHealthPanel panel) => HoldingVisible && ReferenceEquals(Panel, panel);
    private static bool TryTextSource(out EffectStatusText.Assets assets)
    {
        var template = Native.Get(Panel, "_effectIconTemplate");
        var candidate = Native.Get(template, "_stackCount") as Component;
        if (EffectStatusText.TryAssets(candidate, out assets)) { TextTemplate = candidate; return true; }
        if (EffectStatusText.TryAssets(TextTemplate, out assets)) return true;
        var type = AccessTools.TypeByName("TMPro.TMP_Text");
        if (type is not null && (UnityEngine.Object?)(object?)Panel != null)
            foreach (var text in ((Component)(object)Panel!).GetComponentsInChildren(type, true).OfType<Component>())
                if ((Root == null || !text.transform.IsChildOf(Root.transform)) && EffectStatusText.TryAssets(text, out assets))
                { TextTemplate = text; return true; }
        assets = default;
        return false;
    }
    private static void Create()
    {
        if (TextTemplate == null) throw new InvalidOperationException("Native health-effect text template was not found.");
        var effects = Native.Get(Panel, "_effectsPanel") as GameObject;
        var parent = ((Component)(object)Panel!).transform;
        Anchor = effects != null ? effects.transform as RectTransform : parent as RectTransform;
        Root = new GameObject("EFB Effect Status", typeof(RectTransform));
        Root.layer = parent.gameObject.layer;
        var rect = (RectTransform)Root.transform; rect.SetParent(parent, false);
        // Keep the whole stack outside the native horizontal effect-row layout.
        // Follow that row's lower edge without becoming one of its columns.
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(0f, -8f); rect.sizeDelta = new Vector2(EffectStatusLayout.Width, 25f);
        rect.SetAsLastSibling();
        // Ignore the native row's layout, which belongs to real health effects.
        var layoutElement = AccessTools.TypeByName("UnityEngine.UI.LayoutElement");
        if (layoutElement is not null)
        {
            var element = Root.AddComponent(layoutElement);
            Native.Set(element, "ignoreLayout", true); Native.Set(element, "layoutPriority", 10);
        }
        ClockTexture = new Texture2D(28, 28);
        var pixels = new Color[28 * 28];
        for (var y = 0; y < 28; y++) for (var x = 0; x < 28; x++)
        {
            var dx = x - 13.5f; var dy = y - 13.5f; var radius = Math.Sqrt(dx * dx + dy * dy);
            var ring = radius >= 10.3 && radius <= 12;
            var hands = Math.Abs(dx) <= .8 && dy >= -.5 && dy <= 7 || Math.Abs(dy) <= .8 && dx >= -.5 && dx <= 5;
            pixels[y * 28 + x] = ring || hands ? Color.white : Color.clear;
        }
        ClockTexture.SetPixels(pixels); ClockTexture.Apply();
        ClockSprite = Sprite.Create(ClockTexture, new Rect(0, 0, 28, 28), new Vector2(.5f, .5f));
    }
    private static Row CreateRow()
    {
        var row = new Row { Root = new GameObject("EFB timer", typeof(RectTransform)) };
        row.Root.SetActive(false);
        row.Root.layer = Root!.layer;
        var rect = (RectTransform)row.Root.transform; rect.SetParent(Root!.transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f); rect.sizeDelta = new Vector2(EffectStatusLayout.Width, 25f);
        var icon = new GameObject("Clock", typeof(RectTransform)); icon.layer = Root!.layer; var iconRect = (RectTransform)icon.transform;
        iconRect.SetParent(rect, false); iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(0f, 1f); iconRect.sizeDelta = new Vector2(22f, 22f);
        row.Icon = icon.AddComponent(AccessTools.TypeByName("UnityEngine.UI.Image"));
        Native.Set(row.Icon, "sprite", ClockSprite); Native.Set(row.Icon, "raycastTarget", false);
        var labelObject = UnityEngine.Object.Instantiate(TextTemplate!.gameObject, rect, false);
        labelObject.name = "Effect name and timer"; labelObject.SetActive(true);
        row.Label = labelObject.GetComponent(TextTemplate.GetType());
        row.TextLayout = new EffectStatusText.Layout(row.Label, Time.frameCount);
        var labelRect = (RectTransform)labelObject.transform;
        labelRect.anchorMin = labelRect.anchorMax = labelRect.pivot = new Vector2(0f, 1f);
        labelRect.anchoredPosition = new Vector2(28f, 0f); labelRect.sizeDelta = new Vector2(EffectStatusLayout.TextWidth, 25f);
        labelRect.localScale = Vector3.one;
        foreach (var behaviour in labelObject.GetComponents<Behaviour>())
            if (behaviour.GetType().Name.Contains("LayoutGroup") || behaviour.GetType().Name is "ContentSizeFitter" or "LayoutElement") behaviour.enabled = false;
        Native.Set(row.Label, "fontSize", 14f); Native.Set(row.Label, "enableAutoSizing", false);
        Native.Set(row.Label, "enableWordWrapping", true); Native.Set(row.Label, "raycastTarget", false);
        var alignment = AccessTools.Property(row.Label.GetType(), "alignment");
        alignment?.SetValue(row.Label, Enum.Parse(alignment.PropertyType, "Left"));
        return row;
    }
}

[HarmonyPatch(typeof(CharacterHealthPanel), "Show")]
internal static class EffectStatusHealthPanelPatch
{
    private static void Postfix(CharacterHealthPanel __instance) => EffectStatusHud.Attach(__instance);
}
[HarmonyPatch(typeof(CharacterHealthPanel), "AnimatedHide")]
internal static class EffectStatusKeepVisiblePatch
{
    private static bool Prefix(CharacterHealthPanel __instance) => !EffectStatusHud.KeepVisible(__instance);
}

[HarmonyPatch(typeof(CharacterHealthPanel), "AnimatedShow")]
internal static class EffectStatusPreserveVisibilityPatch
{
    private static void Prefix(CharacterHealthPanel __instance, ref bool autohide)
    { if (EffectStatusHud.KeepVisible(__instance)) autohide = false; }
}
