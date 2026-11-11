using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

[HarmonyPatch(typeof(Player), nameof(Player.Jump))]
internal static class LegendaryMeleeExtraJumpPatch
{
    private static bool Prefix(Player __instance) => !AdvancedMeleeRuntime.ExtraJump(__instance);
}

[HarmonyPatch(typeof(MovementContext), "method_17")]
internal static class LegendaryMeleeVaultSpeedPatch
{
    private static void Postfix(MovementContext __instance)
    {
        var effect = MeleeRuntime.Effect(MeleeRuntime.Owner(__instance));
        var factor = effect?.VaultSpeedMultiplier ?? 1f;
        if (factor == 1f) return;
        __instance.VaultingSpeed *= factor;
        __instance.PlayerAnimator.SetVaultingSpeed(__instance.VaultingSpeed);
    }
}

internal static class MeleeWeightScope
{
    [ThreadStatic] public static Inventory? Inventory;
    [ThreadStatic] public static float Multiplier;
}

[HarmonyPatch(typeof(PlayerPhysicalClass), nameof(PlayerPhysicalClass.OnWeightUpdated))]
internal static class LegendaryMeleeWeightPatch
{
    private static readonly FieldInfo PlayerField = AccessTools.Field(typeof(PlayerPhysicalClass), "Player_0");
    internal sealed class Snapshot
    {
        public Inventory? Inventory;
        public float Multiplier;
    }
    private static void Prefix(PlayerPhysicalClass __instance, out Snapshot __state)
    {
        __state = new Snapshot { Inventory = MeleeWeightScope.Inventory, Multiplier = MeleeWeightScope.Multiplier };
        var player = PlayerField.GetValue(__instance) as Player;
        MeleeWeightScope.Inventory = player?.InventoryController?.Inventory;
        MeleeWeightScope.Multiplier = AdvancedMeleeRuntime.Resetting ? 1f : MeleeRuntime.Effect(player)?.WeightMultiplier ?? 1f;
    }
    private static Exception? Finalizer(Snapshot __state, Exception? __exception)
    {
        MeleeWeightScope.Inventory = __state.Inventory; MeleeWeightScope.Multiplier = __state.Multiplier;
        return __exception;
    }
}

[HarmonyPatch(typeof(Player.BaseKnifeController), "method_6")]
internal static class LegendaryMeleeDoorUnlockPatch
{
    private static readonly FieldInfo HitCollider = AccessTools.Field(typeof(Player.GStruct182), "collider");
    private static void Prefix(Player.BaseKnifeController __instance, Player.GStruct182 __0)
    {
        var player = MeleeRuntime.Owner(__instance);
        MasterKeyRuntime.TryUnlock(player, MasterKeyRuntime.DoorFrom(HitCollider.GetValue(__0) as Component));
    }
}

[HarmonyPatch(typeof(KnifeCollider), nameof(KnifeCollider.ManualUpdate))]
internal static class LegendaryMeleeDoorProbePatch
{
    private static void Prefix(KnifeCollider __instance, Player ___player_0) => MasterKeyRuntime.Probe(__instance, ___player_0);
}
