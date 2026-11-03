using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

// Getters discover equipment while EFT may still be building or disposing a
// player. Apply their native refreshes once the player is ready, outside getters.
internal static class PlayerRefresh
{
    [Flags]
    private enum Work { None = 0, Weight = 1, Equipment = 2 }
    private sealed class Pending
    {
        internal Work Work;
        internal string Site = "";
        internal int Failures;
        internal float NextAttempt, NextReport;
    }

    private static readonly Dictionary<Player, Pending> Requests = new();
    private static readonly HashSet<Player> Disposed = new();
    private static readonly FieldInfo PhysicalOwner = AccessTools.Field(typeof(PlayerPhysicalClass), "Player_0");
    private static readonly FieldInfo PhysicalBridge = AccessTools.Field(typeof(BasePhysicalClass), "IobserverToPlayerBridge_0");
    private static GameWorld? ClosingWorld;
    internal static bool Resetting;

    internal static GameWorld? ActiveWorld(GameWorld? world) =>
        world == null || ReferenceEquals(world, ClosingWorld) ? null : world;

    internal static bool CloseWorld(GameWorld world)
    {
        if (ReferenceEquals(world, ClosingWorld)) return false;
        ClosingWorld = world;
        return true;
    }

    internal static bool CanObserve([NotNullWhen(true)] Player? player) => !Resetting &&
        ActiveWorld(Singleton<GameWorld>.Instance) != null &&
        player != null && !player.Destroyed && !Disposed.Contains(player) &&
        player.HealthController?.IsAlive == true && player.ActiveHealthController is not null &&
        player.InventoryController?.Inventory?.Equipment is not null &&
        player.MovementContext is not null && player.Physical?.Stamina is not null &&
        player.Physical.HandsStamina is not null && player.Physical.Oxygen is not null;

    internal static void Forget(Player player)
    {
        Disposed.Add(player);
        Requests.Remove(player);
    }

    internal static void Reset()
    {
        Requests.Clear();
        Disposed.Clear();
    }

    internal static void Weight(Player player, string site) => Request(player, Work.Weight, site);
    internal static void Equipment(Player player) => Request(player, Work.Equipment | Work.Weight, "chest-rig equipment");
    private static void Request(Player player, Work work, string site)
    {
        if (!CanObserve(player)) return;
        if (!Requests.TryGetValue(player, out var pending)) Requests[player] = pending = new Pending();
        pending.Work |= work;
        pending.Site = site;
    }

    private static bool Ready(Player player)
    {
        var physical = player.Physical;
        var inventory = player.InventoryController.Inventory;
        // These are native OnWeightUpdated dependencies, not merely IsAlive.
        // Inspect only when work is pending, rather than for every stat getter.
        var bridge = PhysicalBridge.GetValue(physical);
        if (bridge is null || Native.Get(bridge, "Skills") is null ||
            inventory.TotalWeight is null || inventory.TotalWeightEliteSkill is null ||
            physical.Stamina.TotalCapacity is null || physical.HandsStamina.TotalCapacity is null ||
            physical.Oxygen.TotalCapacity is null) return false;
        if (physical is PlayerPhysicalClass full &&
            (!ReferenceEquals(PhysicalOwner.GetValue(full), player) || full.Consumptions is null)) return false;
        return player.HandsController is not Player.FirearmController || player.ProceduralWeaponAnimation is not null;
    }

    internal static void Maintain(GameWorld? world)
    {
        if (Resetting || ActiveWorld(world) == null || Requests.Count == 0) return;
        foreach (var pair in Requests.ToArray())
        {
            var player = pair.Key;
            var pending = pair.Value;
            if (!CanObserve(player) || !world!.RegisteredPlayers.OfType<Player>().Contains(player))
            { Requests.Remove(player); continue; }
            if (Time.unscaledTime < pending.NextAttempt) continue;
            var work = pending.Work;
            try
            {
                if (!Ready(player)) { pending.NextAttempt = Time.unscaledTime + .25f; continue; }
                pending.Work = Work.None;
                if ((work & Work.Equipment) != 0)
                {
                    player.UpdateSpeedLimitByHealth();
                    player.UpdateArmsCondition();
                    player.ProceduralWeaponAnimation?.PhysicalConditionUpdated(EPhysicalCondition.LeftArmDamaged, player.MovementContext.PhysicalCondition);
                    if (player.HandsController is Player.FirearmController firearm)
                    {
                        firearm.RecalculateErgonomic();
                        player.ProceduralWeaponAnimation!.UpdateWeaponVariables();
                        firearm.UpdateHipInaccuracy();
                    }
                }
                if ((work & Work.Weight) != 0) player.Physical.OnWeightUpdated();
                if (pending.Failures > 0) Plugin.LogSource?.LogInfo("EFB deferred player refresh recovered after " + pending.Failures + " failure(s).");
                pending.Failures = 0;
                pending.NextAttempt = 0f;
                if (pending.Work == Work.None) Requests.Remove(player);
            }
            catch (Exception error)
            {
                // A third-party patch can still fail with otherwise valid native
                // state. Preserve the request and back off; never throw/log at 20 Hz.
                pending.Work |= work;
                pending.Failures++;
                pending.NextAttempt = Time.unscaledTime + Math.Min(30f, 5f * pending.Failures);
                if (pending.Failures == 1 || Time.unscaledTime >= pending.NextReport)
                {
                    pending.NextReport = Time.unscaledTime + 30f;
                    Plugin.LogSource?.LogWarning($"EFB deferred {pending.Site} refresh failed; {Describe(player)}. Retrying with backoff. {error}");
                }
            }
        }
    }

    private static string Describe(Player player)
    {
        try
        {
            var rig = ChestRigRuntime.Worn(player)?.StringTemplateId ?? "none";
            return $"player={(player.IsYourPlayer ? "local" : player.IsAI ? "AI" : "other")}, rig={rig}";
        }
        catch { return "player state became unavailable while reporting the failure"; }
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.Dispose))]
internal static class LegendaryPlayerDisposalPatch
{
    private static void Prefix(Player __instance) => PlayerRefresh.Forget(__instance);
}
