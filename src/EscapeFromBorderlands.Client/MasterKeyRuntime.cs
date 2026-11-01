using System;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

/// <summary>One successful unlock per actor in the current world, independent of held-item state.</summary>
internal static class MasterKeyRuntime
{
    private static readonly HashSet<string> UsedPlayers = new(StringComparer.Ordinal);
    private static GameWorld? _world;
    private static readonly PropertyInfo InteractionRay = AccessTools.Property(typeof(Player), "InteractionRay");
    private static readonly MethodInfo FindInteractable = AccessTools.Method(typeof(GameWorld), "FindInteractable");
    private static readonly PropertyInfo HitDistance = FindInteractable.GetParameters()[1].ParameterType.GetElementType()!.GetProperty("distance")!;

    public static void Reset() { UsedPlayers.Clear(); _world = null; }
    internal static bool Available(Player player) => Ready(player);

    private static bool Ready(Player? player)
    {
        if (!Singleton<GameWorld>.Instantiated || player?.HealthController?.IsAlive != true) return false;
        var world = Singleton<GameWorld>.Instance;
        if (!ReferenceEquals(_world, world)) { UsedPlayers.Clear(); _world = world; }
        return !UsedPlayers.Contains(player.ProfileId) && MeleeRuntime.Effect(player)?.UnlocksDoorOncePerRaid == true;
    }

    public static Door? DoorFrom(Component? component) => component?.GetComponentInParent<Door>() ??
        component?.GetComponentInParent<InteractiveProxy>()?.Link as Door;

    public static bool TryUnlock(Player? player, Door? door)
    {
        if (!Ready(player) || door == null || !door.Operatable || door.NoInteractionsAllowed ||
            door.DoorState != EDoorState.Locked) return false;
        // Reserve before native callbacks. A refused unlock releases the reservation; a successfully
        // unlocked door spends it even if another interaction interrupts the opening animation.
        var id = player!.ProfileId;
        UsedPlayers.Add(id);
        try
        {
            door.SetUser(player);
            // EFT's public Open interaction first force-unlocks a locked door, then runs its normal
            // opening/state/event handling. This avoids both breach eligibility and key consumption.
            door.Interact(EInteractionType.Open);
        }
        catch (Exception error)
        {
            if (door.DoorState == EDoorState.Locked) UsedPlayers.Remove(id);
            Plugin.LogSource.LogWarning($"Emergency Access door interaction failed: {error.GetType().Name}.");
            return door.DoorState != EDoorState.Locked;
        }
        if (door.DoorState != EDoorState.Locked) return true;
        UsedPlayers.Remove(id);
        return false;
    }

    public static void Probe(KnifeCollider collider, Player? player)
    {
        // The native callback covers real collision hits, including bots. The player-only probe
        // runs during the native strike window and uses EFT's occlusion-checked interaction ray.
        if (player == null || player.IsAI || collider.OnHit == null || !Ready(player)) return;
        var args = new object?[] { InteractionRay.GetValue(player), null };
        var target = FindInteractable.Invoke(null, args) as GameObject;
        if (target == null || args[1] == null) return;
        var distance = (float)HitDistance.GetValue(args[1])!;
        if (distance < 0f || distance > collider.MaxDistance + EFTHardSettings.Instance.BEHIND_CAST) return;
        TryUnlock(player, DoorFrom(target.transform));
    }
}
