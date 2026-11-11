using Ammo = AmmoItemClass;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class WeaponCombatState
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo PlayerField = typeof(Player.ItemHandsController).GetField("_player", InstanceFields)!;
    private static readonly FieldInfo AccuracyField = typeof(Player.FirearmController).GetField("float_3", InstanceFields)!;
    private static readonly FieldInfo DispersionField = typeof(Player.FirearmController).GetField("float_4", InstanceFields)!;
    private sealed class PreviousInputs { internal (float recoil, float ergo, float accuracy) Value; }
    private sealed class PreviousInterval { internal float Value; }
    private static ConditionalWeakTable<Player.FirearmController, PreviousInputs> Inputs = new();
    private static readonly HashSet<Player.FirearmController> Refreshing = new();
    private static ConditionalWeakTable<Player.FirearmController.GenericFireOperationClass, PreviousInterval> SingleIntervals = new();

    public static void ResetSession()
    {
        Inputs = new();
        Refreshing.Clear();
        SingleIntervals = new();
        WeaponAttachmentReadBatch.ResetCompatibility();
    }

    public static void RecordSingleStart(Player.FirearmController.GenericFireOperationClass operation)
    {
        if (TracksInterval(operation.Weapon_0))
            SingleIntervals.GetValue(operation, static _ => new PreviousInterval()).Value = operation.Float_0;
    }

    public static void RefreshPreparedSingleInterval(Player.FirearmController controller, Weapon weapon)
    {
        if (controller.CurrentOperation is not Player.FirearmController.GenericFireOperationClass operation ||
            !SingleIntervals.TryGetValue(operation, out var original)) return;
        var interval = 60f / Math.Max(1, weapon.SingleFireRate);
        var elapsed = Math.Max(0f, original.Value - operation.Float_0);
        operation.Float_0 = Math.Max(0f, interval - elapsed);
        original.Value = interval;
    }

    internal static Player? Owner(Player.FirearmController controller) => PlayerField.GetValue(controller) as Player;

    public static void Refresh(Player.FirearmController controller) =>
        Refresh(controller, controller.Item, Owner(controller));

    internal static void Refresh(Player.FirearmController controller, Weapon? weapon, Player? player, bool forShot = false)
    {
        RarityResolver.TryGetDefinition(weapon, out var definition);
        Refresh(controller, weapon, player, definition, forShot);
    }
    internal static WeaponPerformanceTrace.Route Refresh(Player.FirearmController controller, Weapon? weapon, Player? player, VariantDefinition? definition, bool forShot = false)
    {
        var trace = default(WeaponPerformanceTrace);
        return Refresh(controller, weapon, player, definition, ref trace, forShot);
    }
    internal static WeaponPerformanceTrace.Route Refresh(Player.FirearmController controller, Weapon? weapon, Player? player,
        VariantDefinition? definition, ref WeaponPerformanceTrace trace, bool forShot = false, bool livePoll = false)
    {
        if (weapon is null) return WeaponPerformanceTrace.Route.Neutral;
        var active = definition is not null || HasGearEffects(player ?? BackpackRuntime.Actor(controller));
        var route = definition is not null ? WeaponPerformanceTrace.Route.Legendary :
            active ? WeaponPerformanceTrace.Route.Gear : WeaponPerformanceTrace.Route.Neutral;
        Inputs.TryGetValue(controller, out var previous);
        if ((!active && previous is null) || !Refreshing.Add(controller)) return route;
        if (!active) route = WeaponPerformanceTrace.Route.Restore;
        try
        {
            (float recoil, float ergo, float accuracy) inputs;
            bool changed;
            var intervalRefreshed = false;
            using (new WeaponStatReadBatch.Scope(weapon, player, definition))
            {
                var statStart = trace.Stamp();
                using (var attachments = new WeaponAttachmentReadBatch.Scope(weapon, active && livePoll && !forShot))
                {
                    inputs = (weapon.RecoilDelta, weapon.ErgonomicsDelta, weapon.CenterOfImpactDelta);
                    trace.ReadStats(statStart, attachments.Used);
                }
                // These controller fields are read by the native cartridge
                // method. Sample at that boundary, or once to restore vanilla.
                if (forShot || !active)
                {
                    AccuracyField.SetValue(controller, weapon.GetTotalCenterOfImpact(false));
                    DispersionField.SetValue(controller, weapon.TotalShotgunDispersion * Headwear.Factor(player, HatStat.Dispersion));
                }
                changed = previous is null || previous.Value != inputs;
                if (active && !changed && controller.CurrentOperation is Player.FirearmController.GClass2029 current)
                {
                    // No native animator callback intervened in this scope.
                    var intervalStart = trace.Stamp();
                    RefreshInterval(current, weapon);
                    trace.Interval(intervalStart);
                    intervalRefreshed = true;
                }
            }
            if (changed)
            {
                var applyStart = trace.Stamp();
                (previous ?? Inputs.GetValue(controller, static _ => new PreviousInputs())).Value = inputs;
                controller.RecalculateErgonomic();
                player?.ProceduralWeaponAnimation?.UpdateWeaponVariables();
                controller.UpdateHipInaccuracy();
                trace.Apply(applyStart);
            }
            if (!active) Inputs.Remove(controller);
            if (!intervalRefreshed && controller.CurrentOperation is Player.FirearmController.GClass2029 automatic)
            {
                var intervalStart = trace.Stamp();
                RefreshInterval(automatic, weapon);
                trace.Interval(intervalStart);
            }
        }
        finally { Refreshing.Remove(controller); }
        return route;
    }

    private static bool HasGearEffects(Player? player)
    {
        if (player is null) return false;
        var observed = PlayerRefresh.CanObserve(player);
        return Headwear.Get(player) is not null || ChestRigRuntime.Get(player, observed) is not null || GrenadeBuffs.ChangesFirearm(player, observed) ||
            EarpieceRuntime.ChangesFirearm(player, observed);
    }

    internal static bool TracksInterval(Weapon weapon)
    {
        if (RarityResolver.TryGetDefinition(weapon, out _)) return true;
        var owner = Headwear.Owner(weapon);
        return Headwear.Get(owner) is not null || ChestRigRuntime.Get(owner) is not null || EarpieceRuntime.Get(owner) is not null;
    }

    internal static int? PreparedRemaining(Player.FirearmController controller, Weapon weapon, Ammo ammo)
    {
        // Double-tap can remove both cartridges before firing. Retain the
        // native ammo identity and per-barrel count without a LINQ closure.
        if (controller.CurrentOperation is not Player.FirearmController.FireOnlyBarrelFireOperation barrels) return null;
        foreach (var shot in barrels.List_1)
        {
            if (shot.AmmoToFire != ammo) continue;
            return shot.AmmoCountInChamberBeforeShot > 0
                ? shot.AmmoCountInChamberBeforeShot + weapon.GetCurrentMagazineCount() : null;
        }
        return null;
    }

    public static void RefreshInterval(Player.FirearmController.GClass2029 operation, Weapon weapon)
    {
        var heat = weapon.MalfState.OverheatFirerateMult;
        var interval = 60f / (Math.Max(1, weapon.FireRate) * (heat > 0f ? heat : 1f));
        if (Math.Abs(interval - operation.Float_5) <= 0.000001f) return;
        var progress = operation.Float_5 > 0f ? operation.Float_7 / operation.Float_5 : 0f;
        operation.Float_5 = interval;
        operation.Float_7 = progress * interval;
    }
}

[HarmonyPatch(typeof(Player.FirearmController), "method_58")]
internal static class WeaponCartridgePreparationPatch
{
    private static void Prefix(Player.FirearmController __instance, Weapon weapon, Ammo ammo, out LegendaryRuntime.ShotScope? __state)
    {
        var player = WeaponCombatState.Owner(__instance);
        var remaining = WeaponCombatState.PreparedRemaining(__instance, weapon, ammo);
        __state = LegendaryRuntime.BeginShot(weapon, ammo, player?.ProfileId ?? string.Empty, remaining);
        // Gear can change dispersion on an otherwise vanilla firearm as well.
        WeaponCombatState.Refresh(__instance, __instance.Item, player, forShot: true);
        if (__state is not null) WeaponCombatState.RefreshPreparedSingleInterval(__instance, weapon);
    }

    private static Exception? Finalizer(LegendaryRuntime.ShotScope? __state, Exception? __exception)
    {
        LegendaryRuntime.EndShot(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(Player.FirearmController), nameof(Player.FirearmController.InitiateShot))]
internal static class WeaponShellCenterPatch
{
    private static void Prefix(IWeapon weapon, Vector3 shotDirection)
    {
        if (weapon.Item is Weapon receiver) LegendaryRuntime.SetShotCenter(receiver, shotDirection);
    }
}

[HarmonyPatch(typeof(Ammo), nameof(Ammo.ProjectileCount), MethodType.Getter)]
internal static class WeaponTotalProjectileCountPatch
{
    private static void Postfix(Ammo __instance, ref int __result)
    {
        // Instance-scoped only: do not change the shared ammunition template.
        if (LegendaryRuntime.ReplaceNaturalPellets(__instance)) __result = 1;
    }
}

[HarmonyPatch(typeof(Player.FirearmController), nameof(Player.FirearmController.ManualUpdate))]
internal static class WeaponLiveCombatStatePatch
{
    private static void Prefix(Player.FirearmController __instance, Player? ____player, out LegendaryFireModePatch.ModeScope? __state)
    {
        var trace = WeaponPerformanceTrace.Begin();
        var weapon = __instance.Item;
        RarityResolver.TryGetDefinition(weapon, out var definition);
        LegendaryRuntime.UpdateLiveState(____player, weapon, definition);
        trace.Observe();
        __state = weapon is not null && definition is not null ? LegendaryFireModePatch.BeginCarrier(weapon, definition) : null;
        trace.Carrier();
        var route = WeaponCombatState.Refresh(__instance, weapon, ____player, definition, ref trace, livePoll: true);
        trace.Complete(route, definition);
    }

    private static Exception? Finalizer(LegendaryFireModePatch.ModeScope? __state, Exception? __exception)
    {
        LegendaryFireModePatch.EndCarrier(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(Player.FirearmController.GClass2029), nameof(Player.FirearmController.GClass2029.Update))]
internal static class WeaponAutomaticIntervalPatch
{
    private static void Prefix(Player.FirearmController.GClass2029 __instance)
    {
        var weapon = __instance.Weapon_0;
        if (WeaponCombatState.TracksInterval(weapon)) WeaponCombatState.RefreshInterval(__instance, weapon);
    }
}

[HarmonyPatch]
internal static class LegendaryCarrierAnimationPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => AccessTools.GetTypesFromAssembly(typeof(Player.FirearmController).Assembly)
        .Where(type => typeof(Player.FirearmController.GenericFireOperationClass).IsAssignableFrom(type))
        .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        .Where(method => method.Name is "Start" or "PrepareShot" or "OnFireEvent" or "OnAddAmmoInChamber" or "OnFireEndEvent" or "OnIdleStartEvent" or "Update" or "FastForward");

    private static void Prefix(Player.FirearmController.GenericFireOperationClass __instance, out LegendaryFireModePatch.ModeScope? __state) =>
        __state = LegendaryFireModePatch.BeginCarrier(__instance.Weapon_0);

    private static void Postfix(Player.FirearmController.GenericFireOperationClass __instance, MethodBase __originalMethod)
    {
        if (__originalMethod.Name == "Start") WeaponCombatState.RecordSingleStart(__instance);
    }

    private static Exception? Finalizer(LegendaryFireModePatch.ModeScope? __state, Exception? __exception)
    {
        LegendaryFireModePatch.EndCarrier(__state);
        return __exception;
    }
}
