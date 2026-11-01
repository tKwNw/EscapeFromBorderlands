using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class WeaponCombatState
{
    internal static readonly FieldInfo PlayerField = AccessTools.Field(typeof(Player.ItemHandsController), "_player");
    private static readonly FieldInfo AccuracyField = AccessTools.Field(typeof(Player.FirearmController), "float_3");
    private static readonly FieldInfo DispersionField = AccessTools.Field(typeof(Player.FirearmController), "float_4");
    private static readonly Dictionary<Player.FirearmController, (float recoil, float ergo, float accuracy)> Inputs = new();
    private static readonly HashSet<Player.FirearmController> Refreshing = new();
    private static readonly Dictionary<Player.FirearmController.FireOperation, float> SingleIntervals = new();

    public static void ResetSession() { Inputs.Clear(); Refreshing.Clear(); SingleIntervals.Clear(); }

    public static void RecordSingleStart(Player.FirearmController.FireOperation operation)
    {
        if (RarityResolver.TryGetDefinition(operation.Weapon, out _) || ChestRigNative.For(operation.Weapon) is not null || EarpieceRuntime.Get(BackpackNative.WeaponOwner(operation.Weapon)) is not null) SingleIntervals[operation] = operation._singleFireCooldownTime;
    }

    public static void RefreshPreparedSingleInterval(Player.FirearmController controller, Weapon weapon)
    {
        if (controller.CurrentOperation is not Player.FirearmController.FireOperation operation ||
            !SingleIntervals.TryGetValue(operation, out var original)) return;
        var interval = 60f / Math.Max(1, weapon.SingleFireRate);
        var elapsed = Math.Max(0f, original - operation._singleFireCooldownTime);
        operation._singleFireCooldownTime = Math.Max(0f, interval - elapsed);
        SingleIntervals[operation] = interval;
    }

    public static void Refresh(Player.FirearmController controller)
    {
        var weapon = controller.Item;
        var active = weapon is not null && (RarityResolver.Resolve(weapon) is not null || ChestRigRuntime.Get(BackpackRuntime.Actor(controller)) is not null || GrenadeBuffs.HasState(BackpackRuntime.Actor(controller)) || EarpieceRuntime.Get(BackpackRuntime.Actor(controller)) is not null);
        if (weapon is null || !active && !Inputs.ContainsKey(controller) || !Refreshing.Add(controller)) return;
        try
        {
            // EFT caches these values in the controller and recoil effector.
            // Refresh before spread/speed/recoil are read for THIS cartridge.
            var inputs = (weapon.RecoilDelta, weapon.ErgonomicsDelta, weapon.CenterOfImpactDelta);
            AccuracyField.SetValue(controller, weapon.GetTotalCenterOfImpact(false));
            DispersionField.SetValue(controller, weapon.TotalShotgunDispersion);
            if (!Inputs.TryGetValue(controller, out var old) || old != inputs)
            {
                Inputs[controller] = inputs;
                controller.RecalculateErgonomic();
                var player = PlayerField.GetValue(controller) as Player;
                player?.ProceduralWeaponAnimation?.UpdateWeaponVariables();
                controller.UpdateHipInaccuracy();
            }
            if (!active) Inputs.Remove(controller);
            if (controller.CurrentOperation is Player.FirearmController.AutomaticFireOperation automatic) RefreshInterval(automatic, weapon);
        }
        finally { Refreshing.Remove(controller); }
    }

    public static void RefreshInterval(Player.FirearmController.AutomaticFireOperation operation, Weapon weapon)
    {
        var heat = weapon.MalfState.OverheatFirerateMult;
        var interval = 60f / (Math.Max(1, weapon.FireRate) * (heat > 0f ? heat : 1f));
        if (Math.Abs(interval - operation._shotsTime) <= 0.000001f) return;
        var progress = operation._shotsTime > 0f ? operation.float_7 / operation._shotsTime : 0f;
        operation._shotsTime = interval;
        operation.float_7 = progress * interval;
    }
}

[HarmonyPatch(typeof(Player.FirearmController), nameof(Player.FirearmController.Shot))]
internal static class WeaponCartridgePreparationPatch
{
    private static void Prefix(Player.FirearmController __instance, Weapon weapon, Ammo ammo, out LegendaryRuntime.ShotScope? __state)
    {
        var player = WeaponCombatState.PlayerField.GetValue(__instance) as Player;
        int? remaining = null;
        // Double-tap prepares/removes BOTH cartridges before either barrel
        // fires. Its per-barrel snapshots retain the correct 2, then 1 count.
        if (__instance.CurrentOperation is Player.FirearmController.FireOnlyBarrelFireOperation barrels)
        {
            var prepared = barrels._singleShotsData.FirstOrDefault(value => value.AmmoToFire == ammo);
            if (prepared.AmmoCountInChamberBeforeShot > 0)
                remaining = prepared.AmmoCountInChamberBeforeShot + weapon.GetCurrentMagazineCount();
        }
        __state = LegendaryRuntime.BeginShot(weapon, ammo, player?.ProfileId ?? string.Empty, remaining);
        if (__state is not null)
        {
            WeaponCombatState.Refresh(__instance);
            WeaponCombatState.RefreshPreparedSingleInterval(__instance, weapon);
        }
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
    private static void Prefix(Player.FirearmController __instance, out LegendaryFireModePatch.ModeScope? __state)
    {
        var player = WeaponCombatState.PlayerField.GetValue(__instance) as Player;
        LegendaryRuntime.UpdateLiveState(player, __instance.Item);
        __state = LegendaryFireModePatch.BeginCarrier(__instance.Item);
        WeaponCombatState.Refresh(__instance);
    }

    private static Exception? Finalizer(LegendaryFireModePatch.ModeScope? __state, Exception? __exception)
    {
        LegendaryFireModePatch.EndCarrier(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(Player.FirearmController.AutomaticFireOperation), nameof(Player.FirearmController.AutomaticFireOperation.Update))]
internal static class WeaponAutomaticIntervalPatch
{
    private static void Prefix(Player.FirearmController.AutomaticFireOperation __instance)
    {
        if (RarityResolver.TryGetDefinition(__instance.Weapon, out _) || ChestRigNative.For(__instance.Weapon) is not null || EarpieceRuntime.Get(BackpackNative.WeaponOwner(__instance.Weapon)) is not null) WeaponCombatState.RefreshInterval(__instance, __instance.Weapon);
    }
}

[HarmonyPatch]
internal static class LegendaryCarrierAnimationPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => AccessTools.GetTypesFromAssembly(typeof(Player.FirearmController).Assembly)
        .Where(type => typeof(Player.FirearmController.FireOperation).IsAssignableFrom(type))
        .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        .Where(method => method.Name is "Start" or "PrepareShot" or "OnFireEvent" or "OnAddAmmoInChamber" or "OnFireEndEvent" or "OnIdleStartEvent" or "Update" or "FastForward");

    private static void Prefix(Player.FirearmController.FireOperation __instance, out LegendaryFireModePatch.ModeScope? __state) =>
        __state = LegendaryFireModePatch.BeginCarrier(__instance.Weapon);

    private static void Postfix(Player.FirearmController.FireOperation __instance, MethodBase __originalMethod)
    {
        if (__originalMethod.Name == "Start") WeaponCombatState.RecordSingleStart(__instance);
    }

    private static Exception? Finalizer(LegendaryFireModePatch.ModeScope? __state, Exception? __exception)
    {
        LegendaryFireModePatch.EndCarrier(__state);
        return __exception;
    }
}
