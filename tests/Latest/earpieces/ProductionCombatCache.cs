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

