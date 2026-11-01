using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.AssetsManager;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class BackpackGrenadeAction
{
    private const string Rgn = "617fd91e5539a84ec44ce155";
    private static Task? AssetLoad;
    private static GameWorld? World;
    private static bool Pending;
    private static float PendingUntil, RetryAfter;
    private static bool Disabled;
    private sealed class FreeThrow
    {
        internal Player Player = null!;
        internal BackpackRuntime.State State = null!;
        internal ThrowWeapItemClass Item = null!;
        internal Item? PreviousItem;
        internal Player.QuickGrenadeThrowHandsController? Controller;
        internal float Deadline, ReleasedAt, RetryRestoreAt;
        internal bool Ready, DrawBound, DrawNotified;
        internal bool Restoring;
        internal bool Released;
    }
    private static readonly Dictionary<string, FreeThrow> Throws = new(StringComparer.Ordinal);
    internal static bool IsTemporary(Item? item) => item is not null && Throws.ContainsKey(item.Id);
    internal static void Reset() { AssetLoad = null; World = null; Pending = false; PendingUntil = RetryAfter = 0f; Disabled = false; Throws.Clear(); }
    internal static void Preload(GameWorld world)
    {
        if (World == world && AssetLoad is not null) return;
        Reset(); World = world;
        try
        {
            var item = Singleton<ItemFactoryClass>.Instance.CreateItem(Guid.NewGuid().ToString("N").Substring(0, 24), Rgn, null);
            AssetLoad = Singleton<PoolManagerClass>.Instance.LoadBundlesAndCreatePools(PoolManagerClass.PoolsCategory.Raid,
                PoolManagerClass.AssemblyType.Local, new[] { item.Prefab }, JobPriorityClass.Low, ct: CancellationToken.None);
        }
        catch (Exception e)
        {
            Disabled = true; AssetLoad = Task.FromException(e);
            Plugin.LogSource.LogWarning($"Fireball assets could not load; normal grenade controls remain available: {e.Message}");
        }
    }
    internal static bool TryThrow(Player p)
    {
        var s = BackpackRuntime.Get(p); var world = Singleton<GameWorld>.Instance;
        // Cooldown is checked before input ownership. A previous Fireball animation
        // must never consume ordinary grenade commands once its cooldown has begun.
        if (s?.Key != "BP07-1" || !s.Record.Ready(s.Key) || world?.MainPlayer != p || world.GrenadeFactory is null) return false;
        if (Disabled || Time.time < RetryAfter) return false;
        if (Pending && Time.time < PendingUntil) return true;
        if (p.HandsController is null || p.HandsController is Player.BaseGrenadeHandsController || p.HandsController.IsInInteractionStrictCheck()) return false;
        if (p.IsInBufferZone && !p.CanManipulateWithHandsInBufferZone) return false;
        Preload(world);
        if (Disabled || AssetLoad?.IsFaulted == true || AssetLoad?.IsCanceled == true) return false;
        Pending = true; PendingUntil = Time.time + 15f;
        world.StartCoroutine(ThrowReady(world, s)); return true;
    }
    private static IEnumerator ThrowReady(GameWorld world, BackpackRuntime.State state)
    {
        var p = state.Player;
        var deadline = Time.time + 10f;
        while (AssetLoad?.IsCompleted != true && Time.time < deadline && Singleton<GameWorld>.Instance == world) yield return null;
        if (Singleton<GameWorld>.Instance != world || p.HealthController?.IsAlive != true || AssetLoad?.Status != TaskStatus.RanToCompletion || BackpackRuntime.Get(p)?.Bag.Id != state.Bag.Id)
        { Pending = false; if (AssetLoad?.IsCompleted != true) Disabled = true; yield break; }
        string? id = null;
        try
        {
            var item = (ThrowWeapItemClass)Singleton<ItemFactoryClass>.Instance.CreateItem(Guid.NewGuid().ToString("N").Substring(0, 24), Rgn, null);
            id = item.Id;
            var entry = new FreeThrow { Player = p, State = state, Item = item,
                PreviousItem = p.HandsController?.Item, Deadline = PendingUntil };
            Throws[id] = entry;
            // Native quick-use hides the firearm, plays the overhand throw and
            // computes the trajectory from the animated hand and aim direction.
            // The temporary grenade never occupies or removes an inventory item.
            p.SetInHandsForQuickUse(item, result =>
            {
                if (result.Failed || result.Value is not Player.QuickGrenadeThrowHandsController)
                { Fail(entry, $"Fireball quick throw could not start: {result.Error}"); return; }
                entry.Ready = true;
                entry.Controller = result.Value as Player.QuickGrenadeThrowHandsController;
                // This callback runs after the draw operation has been created.
                // Restore the item actually held before Fireball, rather than a
                // possibly stale PlayerOwner.PreviousItem. HandsChangedEvent still
                // installs the native translators when that item is drawn again.
                result.Value?.SetOnUsedCallback(_ => Restore(entry));
            });
        }
        catch (Exception e)
        {
            if (id is not null && Throws.TryGetValue(id, out var entry)) Fail(entry, $"Fireball could not start its quick throw: {e.Message}");
            else { Pending = false; RetryAfter = Time.time + 5f; Plugin.LogSource.LogError($"Fireball could not start its quick throw: {e.Message}"); }
        }
        // Release is not completion: keep the temporary grenade registered until
        // the normal hand controller is back, including after the bag is removed.
        // Maintain also runs outside this coroutine to recover a missing finish event.
        while (id is not null && Throws.ContainsKey(id) && Singleton<GameWorld>.Instance == world)
        { Maintain(world); yield return null; }
    }
    internal static void Maintain(GameWorld? world)
    {
        if (World != world || world is null) { Reset(); return; }
        foreach (var entry in Throws.Values.ToArray())
        {
            var p = entry.Player;
            if (p.HealthController?.IsAlive != true) { Forget(entry); continue; }
            var current = p.HandsController;
            var ownHands = current is Player.BaseGrenadeHandsController grenade && grenade.Item?.Id == entry.Item.Id;
            if (ownHands && current is Player.QuickGrenadeThrowHandsController quick) BindDraw(entry, quick);
            if (!ownHands && entry.Controller is not null) { Forget(entry); continue; }
            if (Time.time < entry.RetryRestoreAt) continue;
            if (!(entry.Released && Time.time >= entry.ReleasedAt + 1.5f) && Time.time < entry.Deadline) continue;
            if (!ownHands)
            {
                // The scheduled native process can still create its controller.
                // Keep ownership of its ID until it has definitively completed.
                if (p.ProcessStatus == Player.EProcessStatus.None) Forget(entry);
                continue;
            }
            entry.RetryRestoreAt = Time.time + 2f;
            try
            {
                // Native FastForward marks the throw finished before calling its
                // completion callback, so HideGrenade cannot wait for a lost event.
                CompleteDraw(entry);
                entry.Controller?.FastForwardCurrentState();
                Restore(entry);
            }
            catch (Exception e) { entry.Restoring = false; Plugin.LogSource.LogWarning($"Fireball hand recovery will retry: {e.Message}"); }
        }
    }
    private static void BindDraw(FreeThrow entry, Player.QuickGrenadeThrowHandsController controller)
    {
        entry.Controller = controller;
        if (entry.DrawBound || controller.Class1281_0?.Action_0 is not { } onDraw) return;
        entry.DrawBound = true; entry.DrawNotified = entry.Ready;
        controller.Class1281_0.Action_0 = () =>
        {
            if (entry.DrawNotified) return;
            entry.DrawNotified = true;
            onDraw();
        };
    }
    private static void CompleteDraw(FreeThrow entry)
    {
        if (!entry.DrawNotified) entry.Controller?.Class1281_0?.OnDrawCompleteAction();
    }
    private static void Restore(FreeThrow entry)
    {
        var p = entry.Player;
        if (entry.Restoring || p.HealthController?.IsAlive != true || p.HandsController?.Item?.Id != entry.Item.Id) return;
        entry.Restoring = true;
        Pending = false;
        try
        {
        ClearActivity(entry);
        // Use Tarkov's hand process, not a direct controller assignment or input
        // override. Its draw event restores firearm/grenade/empty-hand controls.
        if (entry.PreviousItem?.CurrentAddress is not null)
            p.SetInHands(entry.PreviousItem, result =>
            {
                ClearActivity(entry);
                if (result.Failed) { entry.Restoring = false; Plugin.LogSource.LogWarning($"Fireball could not restore the previous item: {result.Error}"); }
            });
        else p.SetEmptyHands(result => { ClearActivity(entry); if (result.Failed) entry.Restoring = false; });
        }
        catch (Exception e)
        {
            entry.Restoring = false; entry.RetryRestoreAt = Time.time + 2f;
            Plugin.LogSource.LogWarning($"Fireball could not finish restoring hands; recovery will retry: {e.Message}");
        }
    }
    private static void ClearActivity(FreeThrow entry)
    {
        // Only events for our out-of-inventory grenade may be removed. Never
        // unlock a real item, reload, or another mod's pending hand transition.
        var inventory = entry.Player.InventoryController;
        foreach (var activity in inventory.SelectEvents(entry.Item).ToArray())
            if (activity is GEventArgs9 || activity is GEventArgs10) inventory.RemoveActiveEvent(activity);
    }
    private static void Forget(FreeThrow entry)
    {
        ClearActivity(entry); Throws.Remove(entry.Item.Id); Pending = false;
    }
    private static void Fail(FreeThrow entry, string message)
    {
        Pending = false; RetryAfter = Time.time + 5f;
        // A failed transition may already have created the temporary controller.
        // Leave it registered for the same scoped recovery as a missed finish.
        if (entry.Player.HandsController?.Item?.Id == entry.Item.Id)
        { entry.Controller = entry.Player.HandsController as Player.QuickGrenadeThrowHandsController; entry.Deadline = Time.time; }
        else Forget(entry);
        Plugin.LogSource.LogWarning(message);
    }
    internal static bool Release(Player.BaseGrenadeHandsController controller, float prewarm, Vector3 position, Quaternion rotation, Vector3 force, bool lowThrow)
    {
        if (controller.Item is null || !Throws.TryGetValue(controller.Item.Id, out var entry)) return false;
        if (entry.Released) return true;
        entry.Released = true; entry.ReleasedAt = Time.time;
        try
        {
            // Some quick-throw animations reach release without their draw event.
            // Finish the native Process first; otherwise its BeginSetInHands event
            // and Scheduled status block all subsequent grenade/equipment input.
            // The guarded callback also ignores a late duplicate animation event.
            if (controller is Player.QuickGrenadeThrowHandsController quick) BindDraw(entry, quick);
            CompleteDraw(entry);
            // Only our temporary, free grenade bypasses the inventory-discard
            // transaction. Position, orientation, force and fuse prewarm are native.
            var grenade = controller.method_10(position, rotation, force, prewarm);
            entry.State.Record.Cooldown(entry.State.Key, 300f);
            entry.Player.Physical.OnThrow(lowThrow);
            if (Native.Get(controller, "transform_2") is Transform[] parts)
                foreach (var part in parts) if (part != null) part.gameObject.SetActive(false);
            if (Singleton<BotEventHandler>.Instantiated)
                Native.Call(Singleton<BotEventHandler>.Instance, "ThrowGrenade", grenade, position + Vector3.up, force, entry.Player.IsAI ? .5f : .6f);
        }
        catch (Exception e) { RetryAfter = Time.time + 5f; Plugin.LogSource.LogError($"Fireball quick throw failed: {e.Message}"); }
        finally { Pending = false; }
        return true;
    }
}

[HarmonyPatch(typeof(Player.BaseGrenadeHandsController), "vmethod_2")]
internal static class BackpackFreeGrenadeReleasePatch
{
    private static bool Prefix(Player.BaseGrenadeHandsController __instance, float timeSinceSafetyLevelRemoved, Vector3 position, Quaternion rotation, Vector3 force, bool lowThrow) =>
        !ReusableGrenadeRuntime.Release(__instance, timeSinceSafetyLevelRemoved, position, rotation, force, lowThrow) &&
        !BackpackGrenadeAction.Release(__instance, timeSinceSafetyLevelRemoved, position, rotation, force, lowThrow);
}

[HarmonyPatch]
internal static class BackpackFreeGrenadeSelectionPatch
{
    private static MethodBase TargetMethod() => AccessTools.DeclaredMethod(AccessTools.TypeByName("EFT.UI.DragAndDrop.FastAccessGrenadeItemView"), "OnSetInHands", new[] { typeof(GEventArgs9) });
    // Native hand events still complete the animation/process. This inventory
    // view alone must ignore the free grenade: otherwise it stores that item
    // as TopPriorityGrenade even though it cannot be thrown from inventory.
    private static bool Prefix(GEventArgs9 args) => !BackpackGrenadeAction.IsTemporary(args.Item);
}
