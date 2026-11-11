using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.UI;
using EFT.UI.Screens;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class EffectStatusHud
{
    private const int PreparedRowCount = 4;
    private sealed class Row
    {
        internal GameObject Root = null!;
        internal Component Icon = null!, Label = null!;
        internal EffectStatusText.Layout TextLayout = null!;
        internal Color LastColor;
        internal float Height = float.NaN;
        internal EffectStatusModel.Status LastStatus;
        internal string? LastLabel;
    }
    private static CharacterHealthPanel? Panel;
    private static GameObject? Root;
    private static Component? TextTemplate;
    private static RectTransform? Anchor;
    private static Sprite? ClockSprite;
    private static Texture2D? ClockTexture;
    private static readonly List<Row> Rows = new();
    private static readonly List<EffectStatusModel.Status> Gathered = new(), Visible = new();
    private static readonly Dictionary<string, int> StatusIndex = new(StringComparer.Ordinal);
    private static int LaidOutRows = -1;
    private static float NextUpdate, NextFailureReport, NextPreparation;
    private static bool PreviousAutoHide, PanelShown, AwaitingNativeShow, PreparationSettled;
    private static int PanelChanges, PanelReopens, RootsCreated, RowsCreated, ClocksCreated, PreparedRoots, PreparedRows;
    internal static bool HoldingVisible { get; private set; }
    internal static void Attach(CharacterHealthPanel panel)
    {
        if (ReferenceEquals(Panel, panel))
        {
            if (AwaitingNativeShow) PanelChanges++;
            else PanelReopens++;
        }
        else
        {
            ClearVisuals();
            Panel = panel;
            PanelChanges++;
            NextFailureReport = 0f;
        }
        // Native Show closes its animation even when it reuses the same
        // panel. Re-anchor and reveal the existing rows on the next update.
        AwaitingNativeShow = false;
        PanelShown = true;
        NextUpdate = 0f;
    }
    internal static void PrepareDuringSetup(GameWorld world)
    {
        if (PreparationSettled || Singleton<AbstractGame>.Instance is not LocalGame raid ||
            !ReferenceEquals(raid.GameWorld, world)) return;
        var status = raid.Status;
        if (status is GameStatus.Started or GameStatus.Stopping or GameStatus.SoftStopping)
        {
            PreparationSettled = true;
            return;
        }
        if (status is not (GameStatus.Running or GameStatus.Runned or GameStatus.Starting) ||
            StatusHudSettings.Enabled?.Value == false || Time.unscaledTime < NextPreparation) return;

        var trace = RaidUpdateTrace.Begin();
        var preparingVisuals = false;
        try
        {
            // The registered BattleUI exists before LocalGame.Spawn shows it.
            // Its health panel is the same owner the Show postfix will attach.
            if (!EftScreenManager.Instance.TryGetScreen(EEftScreenType.BattleUI, out var screen) ||
                (object?)screen is not EftBattleUIScreen ||
                (UnityEngine.Object?)(object?)screen == null ||
                Native.Get(screen, "_characterHealthPanel") is not CharacterHealthPanel panel ||
                (UnityEngine.Object?)(object?)panel == null ||
                ((Component)(object)panel).transform is not RectTransform parent)
            {
                NextPreparation = Time.unscaledTime + .25f;
                return;
            }
            if (!ReferenceEquals(Panel, panel))
            {
                ClearVisuals();
                Panel = panel;
                PanelShown = false;
            }
            if (!PanelShown && Root == null) AwaitingNativeShow = true;
            if (HoldingVisible || Root != null && Root.activeSelf)
            {
                PreparationSettled = true;
                return;
            }
            preparingVisuals = true;
            var clockReady = ClockSprite != null && ClockTexture != null;
            if (!CanReuseRows(parent) || Root != null && !clockReady) ClearVisuals();
            if (!TryTextSource(out _))
            {
                NextPreparation = Time.unscaledTime + .25f;
                return;
            }
            trace.Phase("HUD setup text source");
            if (Root == null)
            {
                Create(parent, startHidden: true);
                PreparedRoots++;
                trace.Phase("HUD setup root creation");
                if (!clockReady)
                {
                    CreateClock();
                    trace.Phase("HUD setup clock artwork");
                }
                return;
            }
            if (Rows.Count < PreparedRowCount)
            {
                Rows.Add(CreateRow());
                PreparedRows++;
                trace.Phase("HUD setup row creation");
            }
            PreparationSettled = Rows.Count >= PreparedRowCount;
        }
        catch (Exception error)
        {
            if (preparingVisuals) ClearVisuals();
            NextPreparation = Time.unscaledTime + 1f;
            ReportFailure("setup", error);
        }
        finally { trace.Finish("combined HUD setup"); }
    }
    internal static void Reset()
    {
        EffectStatusModel.ResetNotices();
        ClearVisuals();
        if (ClockSprite != null) UnityEngine.Object.Destroy(ClockSprite);
        if (ClockTexture != null) UnityEngine.Object.Destroy(ClockTexture);
        ClockSprite = null; ClockTexture = null;
        // Show can run before the first Update of a raid. Retain a live
        // binding until the panel is replaced; no visual survives this reset.
        if (AwaitingNativeShow || (UnityEngine.Object?)(object?)Panel == null) Panel = null;
        PanelShown = false;
        AwaitingNativeShow = PreparationSettled = false;
        NextUpdate = NextFailureReport = NextPreparation = 0f;
        if (PanelChanges + PanelReopens + RootsCreated > 0)
        {
            Plugin.LogSource?.LogInfo($"EFB HUD reuse: {PanelChanges} panel bindings | {PanelReopens} same-panel shows | {RootsCreated} roots | {RowsCreated} rows | {ClocksCreated} clock assets");
            Plugin.LogSource?.LogInfo($"EFB HUD preparation: {PreparedRoots} roots and {PreparedRows} rows during setup | {RootsCreated - PreparedRoots} roots and {RowsCreated - PreparedRows} rows on demand");
        }
        PanelChanges = PanelReopens = RootsCreated = RowsCreated = ClocksCreated = 0;
        PreparedRoots = PreparedRows = 0;
    }
    internal static void ReleasePanel() => Panel = null;
    private static void ClearVisuals()
    {
        try { Hide(); } catch { /* native UI may already be closing */ }
        if (Root != null) UnityEngine.Object.Destroy(Root);
        Root = null; Anchor = null;
        Rows.Clear(); LaidOutRows = -1; HoldingVisible = false; TextTemplate = null;
    }
    private static void Hide()
    {
        if (Root != null && Root.activeSelf) Root.SetActive(false);
        if (!HoldingVisible) return;
        HoldingVisible = false;
        if ((UnityEngine.Object?)(object?)Panel != null) Native.Call(Panel!, "AnimatedShow", PreviousAutoHide);
    }
    internal static void Maintain(GameWorld? world)
    {
        if (StatusHudSettings.Enabled?.Value == false) { HeadwearDiagnostics.Hud("availability", "disabled"); Hide(); return; }
        var now = Time.time;
        if (now < NextUpdate || Panel is null || AwaitingNativeShow) return;
        if ((UnityEngine.Object?)(object?)Panel == null)
        {
            ClearVisuals(); Panel = null; PanelShown = false;
            return;
        }
        HeadwearDiagnostics.Hud("availability", "panel present");
        NextUpdate = now + .05f;
        if (!((Component)(object)Panel).gameObject.activeInHierarchy) return;
        var trace = RaidUpdateTrace.Begin();
        try
        {
            var player = world?.MainPlayer;
            var visibility = StatusHudSettings.ReadVisibility();
            var statuses = Gathered;
            statuses.Clear();
            if (player?.HealthController?.IsAlive == true)
            {
                if (visibility.Backpacks)
                {
                    if (BackpackRuntime.Records.TryGetValue(player.ProfileId, out var record)) EffectStatusModel.AppendCooldowns(statuses, record.Until, now);
                    BackpackEffectStatus.Add(statuses, BackpackRuntime.Get(player));
                    BackpackNative.AppendUseStatuses(player, statuses);
                }
                trace.Phase("HUD backpack status");
                if (visibility.Guns && player.HandsController is Player.FirearmController firearm &&
                    LegendaryRuntime.CurrentStatus(player, firearm.Weapon) is { } gun) statuses.Add(gun);
                trace.Phase("HUD firearm status");
                if (visibility.Melee) statuses.AddRange(AdvancedMeleeRuntime.Statuses(player));
                if (visibility.Guns || visibility.Backpacks) AdvancedLegendaryActions.AppendSmokeStatuses(player, statuses);
                trace.Phase("HUD melee and short smoke status");
                if (visibility.Grenades) ReusableGrenadeRuntime.AppendStatuses(player, statuses, ref trace);
                EffectStatusModel.AppendNotices(statuses, player.ProfileId, now);
            }
            trace.Phase("HUD notices");
            if (visibility.Rigs) statuses.AddRange(ChestRigRuntime.Statuses(player));
            trace.Phase("HUD rig status");
            if (visibility.Earpieces) statuses.AddRange(EarpieceRuntime.Statuses(player));
            if (visibility.Headwear) statuses.AddRange(Headwear.Status(player));
            trace.Phase("HUD headset status");
            EffectStatusModel.CopyVisible(statuses, Visible, StatusIndex);
            statuses = Visible;
            for (var i = statuses.Count - 1; i >= 0; i--)
                if (!visibility.Allows(statuses[i].Key)) statuses.RemoveAt(i);
            HeadwearDiagnostics.Rows(statuses);
            if (statuses.Count == 0)
            {
                Hide();
                return;
            }
            trace.Phase("HUD visibility and status merge");
            var parent = (RectTransform)((Component)(object)Panel!).transform;
            var clockReady = ClockSprite != null && ClockTexture != null;
            if ((Root == null ? Rows.Count > 0 : !clockReady) ||
                PanelShown && !CanReuseRows(parent)) ClearVisuals();
            if (PanelShown || Anchor == null)
            {
                var effects = Native.Get(Panel, "_effectsPanel") as GameObject;
                Anchor = effects != null ? effects.transform as RectTransform : parent;
            }
            var ready = TryTextSource(out var textAssets);
            trace.Phase("HUD text source lookup");
            if (!ready) { HeadwearDiagnostics.Hud("render", "text assets unavailable"); Hide(); NextUpdate = Time.time + .25f; return; }
            if (Root == null)
            {
                Create(parent);
                trace.Phase("HUD root creation");
            }
            if (!clockReady)
            {
                CreateClock();
                trace.Phase("HUD clock artwork");
            }
            if (Root == null) return;
            var layout = (RectTransform)Root.transform;
            var origin = Anchor != null ? parent.InverseTransformPoint(Anchor.TransformPoint(new Vector3(Anchor.rect.xMin, Anchor.rect.yMin, 0f))) : Vector3.zero;
            var position = new Vector2(origin.x - parent.rect.xMin + StatusHudSettings.HorizontalOffset.Value,
                origin.y - parent.rect.yMax - 8f - StatusHudSettings.VerticalOffset.Value);
            if (layout.anchoredPosition != position) layout.anchoredPosition = position;
            var scale = Vector3.one * StatusHudSettings.Scale.Value;
            if (layout.localScale != scale) layout.localScale = scale;
            var needsTextRebind = PanelShown || !Root.activeSelf;
            if (!Root.activeSelf) Root.SetActive(true);
            if (needsTextRebind)
            {
                foreach (var row in Rows) row.TextLayout.Revalidate();
            }
            if (!HoldingVisible) PreviousAutoHide = Native.Get(Panel, "AutoHide") is true;
            if (!HoldingVisible || PanelShown)
            {
                HoldingVisible = true;
                Native.Call(Panel!, "AnimatedShow", false);
            }
            PanelShown = false;
            var layoutChanged = LaidOutRows != statuses.Count;
            trace.Phase("HUD anchor and visibility");
            if (Rows.Count < statuses.Count)
            {
                do { Rows.Add(CreateRow()); } while (Rows.Count < statuses.Count);
                trace.Phase("HUD row creation");
            }
            var frame = Time.frameCount;
            for (var i = 0; i < statuses.Count; i++)
            {
                var row = Rows[i]; var status = statuses[i];
                if (!row.Root.activeSelf) { row.Root.SetActive(true); row.TextLayout.Revalidate(); }
                var color = status.Active ? new Color(.4f, .851f, .937f, 1f) : new Color(.68f, .68f, .68f, 1f);
                if (row.LastColor != color)
                { row.LastColor = color; Native.Set(row.Icon, "color", color); Native.Set(row.Label, "color", color); }
                if (row.LastLabel is null || !status.SameLabel(in row.LastStatus))
                {
                    row.LastStatus = status;
                    row.LastLabel = status.Label;
                }
                var laidOut = row.TextLayout.Update(textAssets, row.LastLabel, frame);
                trace.Phase("HUD row text and height measurement");
                if (!laidOut) { HeadwearDiagnostics.Hud("render", "text layout unavailable"); LaidOutRows = -1; Hide(); NextUpdate = Time.time + .25f; return; }
                var height = row.TextLayout.Height;
                if (height != row.Height)
                {
                    row.Height = height;
                    ((RectTransform)row.Label.transform).sizeDelta = new Vector2(EffectStatusLayout.TextWidth, height);
                    ((RectTransform)row.Root.transform).sizeDelta = new Vector2(EffectStatusLayout.Width, height);
                    layoutChanged = true;
                }
            }
            if (layoutChanged)
            {
                var offset = 0f;
                for (var i = 0; i < statuses.Count; i++)
                {
                    ((RectTransform)Rows[i].Root.transform).anchoredPosition = new Vector2(0f, -offset);
                    offset += EffectStatusLayout.Advance(Rows[i].Height, i + 1 < statuses.Count);
                }
                layout.sizeDelta = new Vector2(EffectStatusLayout.Width, offset);
                for (var i = statuses.Count; i < Rows.Count; i++)
                    if (Rows[i].Root.activeSelf) Rows[i].Root.SetActive(false);
                LaidOutRows = statuses.Count;
            }
            HeadwearDiagnostics.Hud("render", "visible rows updated");
            trace.Phase("HUD row positioning");
        }
        catch (Exception e)
        {
            // Preserve gameplay timers/notices and retry the visual binding.
            // A transient unloaded font must not disable this HUD for the raid.
            ClearVisuals();
            NextUpdate = Time.time + 1f;
            HeadwearDiagnostics.Hud("render", "exception: " + e.GetType().Name);
            ReportFailure("display", e);
        }
        finally
        {
            Gathered.Clear(); Visible.Clear(); StatusIndex.Clear();
            trace.Finish("combined effect HUD");
        }
    }

    private static void ReportFailure(string stage, Exception error)
    {
        if (Time.unscaledTime < NextFailureReport) return;
        NextFailureReport = Time.unscaledTime + 30f;
        Plugin.LogSource?.LogWarning($"EFB effect-status HUD will retry after a {stage} error: {error}");
    }
    internal static bool KeepVisible(CharacterHealthPanel panel) => HoldingVisible && ReferenceEquals(Panel, panel);
    private static bool CanReuseRows(RectTransform parent)
    {
        if (Root == null) return Rows.Count == 0;
        if (Root.transform.parent != parent || Root.layer != parent.gameObject.layer) return false;
        foreach (var row in Rows)
            if (row.Root == null || row.Icon == null || row.Label == null) return false;
        return true;
    }
    private static bool TryTextSource(out EffectStatusText.Assets assets)
    {
        var template = Native.Get(Panel, "_effectIconTemplate");
        var candidate = Native.Get(template, "_stackCount") as Component;
        if (EffectStatusText.TryAssets(candidate, out assets)) { TextTemplate = candidate; return true; }
        if (EffectStatusText.TryAssets(TextTemplate, out assets)) return true;
        var type = AccessTools.TypeByName("TMPro.TMP_Text");
        if (type is not null && (UnityEngine.Object?)(object?)Panel != null)
            foreach (var text in ((Component)(object)Panel!).GetComponentsInChildren(type, true))
                if ((Root == null || !text.transform.IsChildOf(Root.transform)) && EffectStatusText.TryAssets(text, out assets))
                { TextTemplate = text; return true; }
        assets = default;
        return false;
    }
    private static void Create(RectTransform parent, bool startHidden = false)
    {
        if (TextTemplate == null) throw new InvalidOperationException("Native health-effect text template was not found.");
        Root = new GameObject("EFB Effect Status", typeof(RectTransform));
        if (startHidden) Root.SetActive(false);
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
        RootsCreated++;
    }
    private static void CreateClock()
    {
        if (ClockSprite != null) UnityEngine.Object.Destroy(ClockSprite);
        if (ClockTexture != null) UnityEngine.Object.Destroy(ClockTexture);
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
        ClocksCreated++;
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
        RowsCreated++;
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
