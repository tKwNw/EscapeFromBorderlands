using ThrowWeap = ThrowWeapItemClass;
using Ammo = AmmoItemClass;
using GlobalEventDispatcher = BotEventHandler;
using Physical = PlayerPhysicalClass;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class ReusableGrenadeRuntime
{
    internal static readonly GrenadeChargeLedger Charges = new(() => Time.time);
    internal static bool FactoryHookReady;
    private sealed class ThrowState
    {
        internal bool Handled;
    }
    private static ConditionalWeakTable<Player.BaseGrenadeHandsController, ThrowState> Controllers = new();

    internal static bool IsReusable(Item? item) => item is ThrowWeap &&
        RarityResolver.TryGetDefinition(item, out var d) && d.GrenadeChargesPerRaid > 0;
    internal static int Remaining(Item item) => RarityResolver.TryGetDefinition(item, out var d) && d.GrenadeChargesPerRaid > 0
        ? Charges.Remaining(item.Id, d.GrenadeChargesPerRaid, LegendaryGrenadeSettings.Interval(d)) : 0;
    internal static bool Usable(Item item) => !IsReusable(item) || Remaining(item) > 0;
    internal static IEnumerable<Item> UsableItems(IEnumerable<Item> items) => items.Where(Usable);
    internal static void Filter(IList<ThrowWeap> items)
    {
        for (var i = items.Count - 1; i >= 0; i--)
            if (!Usable(items[i]))
                items.RemoveAt(i);
    }
    internal static void Reset()
    {
        try
        {
            SessionCleanup.Run("grenade charges", Charges.Reset);
            SessionCleanup.Run(nameof(LegendaryGrenadeEffects), LegendaryGrenadeEffects.Reset);
            SessionCleanup.Run(nameof(GrenadeBuffs), GrenadeBuffs.Reset);
            SessionCleanup.Run(nameof(GrenadePackingSpeed), GrenadePackingSpeed.Reset);
        }
        finally
        {
            Controllers = new();
            GrenadeThrowSkill.Reset();
            CarriedGrenadeDisplay.Reset();
            GrenadeInventoryPath.Reset();
        }
    }
    internal static void Maintain(GameWorld? world, RaidPlayerSnapshot live)
    {
        LegendaryGrenadeEffects.Maintain();
        GrenadeBuffs.Maintain(world, live);
    }
    internal static void Select(InventoryController inventory, IList<ThrowWeap> available)
    {
        var priority = inventory.Inventory.Equipment.TopPriorityGrenade;
        var hadReusable = false;
        for (var i = 0; i < available.Count && !hadReusable; i++)
            hadReusable = IsReusable(available[i]);
        hadReusable |= IsReusable(priority);
        Filter(available);
        if (!hadReusable)
            return;
        if (IsReusable(priority))
        {
            var found = false;
            for (var i = 0; i < available.Count; i++)
                if (available[i].Id == priority!.Id)
                {
                    found = true;
                    break;
                }
            if (!found)
                priority = null;
        }
        var actor = BackpackRuntime.Actor(inventory);
        if (actor is null)
        {
            inventory.Inventory.Equipment.TopPriorityGrenade = priority;
            return;
        }
        // The native key handler randomly chooses an item unless TopPriorityGrenade
        // is populated. Pin an individual copy until its last charge is spent.
        var id = Charges.Select(actor.ProfileId, available, static i => i.Id, priority?.Id);
        ThrowWeap? selected = null;
        for (var i = 0; i < available.Count; i++)
            if (available[i].Id == id)
            {
                selected = available[i];
                break;
            }
        inventory.Inventory.Equipment.TopPriorityGrenade = selected;
    }
    internal static void Selected(Item item, Player? actor)
    {
        if (actor is not null && IsReusable(item))
            _ = GrenadeThrowSkill.For(actor);
        if (actor is not null && item is ThrowWeap && (!IsReusable(item) || Remaining(item) > 0))
            Charges.Pin(actor.ProfileId, item.Id);
    }
    internal static ThrowWeap ProjectileItem(ThrowWeap source)
    {
        if (!RarityResolver.TryGetDefinition(source, out var definition) || definition.GrenadeChargesPerRaid <= 0)
            return source;
        return LegendaryGrenadeEffects.Projectile(RarityResolver.Resolve(source, definition));
    }
    internal static IExplosiveItem Explosion(IExplosiveItem source, Item original) => LegendaryGrenadeEffects.Explosion(source, original);
    internal static bool Release(Player.BaseGrenadeHandsController controller, float prewarm, Vector3 position, Quaternion rotation, Vector3 force, bool lowThrow)
    {
        var source = controller.Item;
        if (source is not ThrowWeap || !RarityResolver.TryGetDefinition(source, out var definition) || definition.GrenadeChargesPerRaid <= 0)
            return false;
        DiagnosticPathTrace trace = default;
        DiagnosticPathTrace.Start(ref trace, DiagnosticPathTrace.Path.Grenade);
        var state = Controllers.GetValue(controller, _ => new ThrowState());
        if (state.Handled)
            return true;
        state.Handled = true;
        var p = GrenadeControllerAccess.Owner(controller);
        if (!FactoryHookReady || p?.HealthController?.IsAlive != true || source!.CurrentAddress is null ||
            !GrenadeInventoryPath.Contains(p.InventoryController.Inventory.Equipment, source))
            return true;
        // The existing per-controller state is already unique for this throw.
        // Reuse its identity for the private reservation; projectile/item IDs
        // remain native and no reservation string or GUID needs allocating.
        var token = state;
        if (!Charges.ReserveToken(source.Id, definition.GrenadeChargesPerRaid, token, LegendaryGrenadeSettings.Interval(definition)))
            return true;
        trace.Phase(0);
        var spawned = false;
        try
        {
            // Preserve the native trajectory, fuse/prewarm, factory registration,
            // hand animation and completion callbacks. Only discard is bypassed.
            var grenade = controller.method_10(position, rotation, force, prewarm);
            trace.Phase(1);
            if (grenade is null)
                return true;
            // Bind the returned native projectile directly. Area effects must
            // not depend on a separate factory postfix being entered on Mono.
            LegendaryGrenadeEffects.Created(grenade, grenade.WeaponSource, p.ProfileId);
            spawned = true;
            Charges.FinishToken(source.Id, token, true);
            trace.Phase(2);
            LegendaryGrenadeEffects.BindSmoke(controller, grenade);
            trace.Phase(3);
            Charges.Pin(p.ProfileId, source.Id);
            GrenadeBuffs.Thrown(p, definition);
            p.ExecuteSkill(GrenadeThrowSkill.For(p));
            trace.Phase(4);
            p.Physical.OnThrow(lowThrow);
            if (GrenadeControllerAccess.Parts(controller) is Transform[] parts)
                foreach (var part in parts)
                    if (part != null)
                        part.gameObject.SetActive(false);
            trace.Phase(5);
            if (Singleton<GlobalEventDispatcher>.Instantiated)
            {
                var dispatcher = Singleton<GlobalEventDispatcher>.Instance;
                if (dispatcher.GetType() == typeof(GlobalEventDispatcher))
                    dispatcher.ThrowGrenade(grenade, position + Vector3.up, force, p.IsAI ? .5f : .6f);
                else
                    Native.Call(dispatcher, "ThrowGrenade", grenade, position + Vector3.up, force, p.IsAI ? .5f : .6f);
            }
            trace.Phase(6);
        }
        catch (Exception e)
        {
            Plugin.LogSource.LogError($"Reusable grenade throw failed: {e.Message}");
        }
        finally
        {
            if (!spawned)
                Charges.FinishToken(source!.Id, token, false);
        }
        return true;
    }
    internal static void AppendStatuses(Player p, List<EffectStatusModel.Status> statuses, ref RaidUpdateTrace trace)
    {
        CarriedGrenadeDisplay.Append(p, statuses, ref trace);
        GrenadeBuffs.AppendStatuses(p, statuses);
        trace.Phase("HUD grenade buffs and smoke");
    }
}

internal sealed class ReusableGrenadeExplosive : IExplosiveItem
{
    private readonly IExplosiveItem Source;
    private readonly float Damage, Radius, Fragments;
    internal ReusableGrenadeExplosive(IExplosiveItem source, float damage, float radius, float fragments)
    {
        Source = source;
        Damage = damage;
        Radius = radius;
        Fragments = fragments;
    }
    public Vector3 Blindness => Source.Blindness;
    public Vector3 Contusion => Source.Contusion;
    public Vector3 ArmorDistanceDistanceDamage => new(Source.ArmorDistanceDistanceDamage.x * Radius, Source.ArmorDistanceDistanceDamage.y * Radius, Source.ArmorDistanceDistanceDamage.z * Damage);
    public float MinExplosionDistance => Source.MinExplosionDistance * Radius;
    public float MaxExplosionDistance => Source.MaxExplosionDistance * Radius;
    public int FragmentsCount => (int)Math.Round(Source.FragmentsCount * Fragments, MidpointRounding.AwayFromZero);
    public float GetStrength => Source.GetStrength * Damage;
    public bool IsDummy => Source.IsDummy;
    public Ammo CreateFragment() => Source.CreateFragment();
}

[HarmonyPatch(typeof(Player.BaseGrenadeHandsController), "method_10")]
internal static class ReusableGrenadeProjectilePatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        ReusableGrenadeRuntime.FactoryHookReady = false;
        var code = instructions.ToList();
        var count = 0;
        foreach (var instruction in code)
        {
            yield return instruction;
            if (instruction.operand is MethodInfo method && method.Name == "get_Item" && method.ReturnType == typeof(ThrowWeap))
            {
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ReusableGrenadeRuntime), nameof(ReusableGrenadeRuntime.ProjectileItem)));
                count++;
            }
        }
        if (count != 1)
            throw new InvalidOperationException($"Expected one grenade item read at the native projectile factory, found {count}.");
        ReusableGrenadeRuntime.FactoryHookReady = true;
    }
}

[HarmonyPatch(typeof(Grenade), nameof(Grenade.Explosion))]
internal static class ReusableGrenadeExplosionPatch
{
    private static void Prefix(ref IExplosiveItem grenadeItem, Item originalWeaponItem) =>
        grenadeItem = ReusableGrenadeRuntime.Explosion(grenadeItem, originalWeaponItem);
}

[HarmonyPatch(typeof(Player), nameof(Player.TryProceed))]
internal static class ReusableGrenadeSelectedPatch
{
    private static void Prefix(Player __instance, Item item) => ReusableGrenadeRuntime.Selected(item, __instance);
}

[HarmonyPatch(typeof(GClass3373), nameof(GClass3373.GetThrowablePriorityGrenadesList))]
[HarmonyPriority(Priority.Last)]
internal static class ReusableGrenadeQuickSelectPatch
{
    // Both the held-key selector and the grenade quick-slot view use this list.
    // Copy it before filtering so another caller's cached list cannot be changed.
    private static void Postfix(ref List<ThrowWeap> __result)
    {
        if (!__result.Any(i => !ReusableGrenadeRuntime.Usable(i)))
            return;
        __result = __result.Where(ReusableGrenadeRuntime.Usable).ToList();
    }
}

[HarmonyPatch]
internal static class ReusableGrenadeSelectorResultPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("EFT.UI.GrenadeSelector"), "GetGrenade");
    private static void Postfix(ref ThrowWeap __result)
    {
        if (__result is not null && !ReusableGrenadeRuntime.Usable(__result))
            __result = null!;
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.TryProceed))]
internal static class ReusableGrenadeEmptyDrawPatch
{
    // Reject stale quick-slot/selector results before an empty draw animation.
    private static bool Prefix(Item item, Callback<IHandsController> completeCallback)
    {
        if (item is null || ReusableGrenadeRuntime.Usable(item))
            return true;
        completeCallback?.Invoke(new Result<IHandsController>(null!, "No charges remaining this raid."));
        return false;
    }
}

[HarmonyPatch]
internal static class ReusableGrenadeBotInventoryPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] {
        AccessTools.Method(typeof(BotGrenadeController), nameof(BotGrenadeController.HaveGrenadeOfType)),
        AccessTools.Method(typeof(BotGrenadeController), nameof(BotGrenadeController.GetGrenadeCount)),
        AccessTools.Method(typeof(BotGrenadeController), "method_1"),
        AccessTools.Method(typeof(BotGrenadeController), "method_2") };
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var count = 0;
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.operand is MethodInfo method && method.Name == "GetPlayerItems" && method.ReturnType == typeof(IEnumerable<Item>))
            {
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ReusableGrenadeRuntime), nameof(ReusableGrenadeRuntime.UsableItems)));
                count++;
            }
        }
        if (count != 1)
            throw new InvalidOperationException($"Expected one grenade-specific bot inventory search, found {count}.");
    }
}
