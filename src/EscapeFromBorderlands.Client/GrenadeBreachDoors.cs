using System;
using System.Collections.Generic;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EscapeFromBorderlands.Client;

internal static class GrenadeBreachDoors
{
    private static readonly HashSet<Door> Known = new();
    private static Door[]? Ordered;

    internal static void Track(WorldInteractiveObject interactive)
    {
        // Awake runs even for a disabled component on an active GameObject,
        // so these doors remain eligible just as in the old Unity search.
        if (interactive.GetType() == typeof(Door) && Known.Add((Door)interactive)) Ordered = null;
    }

    internal static void SceneUnloaded(Scene scene)
    {
        if (Known.RemoveWhere(static door => door == null) > 0) Ordered = null;
    }

    internal static void Dispose() { Known.Clear(); Ordered = null; }

    private static Door[] Snapshot()
    {
        // Preserve the original lookup if the door lifecycle hasn't run.
        if (Known.Count == 0) return UnityEngine.Object.FindObjectsOfType<Door>();
        if (Ordered is { } existing) return existing;
        var snapshot = new Door[Known.Count];
        Known.CopyTo(snapshot);
        // FindObjectsOfType ordered results by instance ID. Keep that order
        // and use a snapshot so door callbacks can register new objects safely.
        Array.Sort(snapshot, static (left, right) => left.GetInstanceID().CompareTo(right.GetInstanceID()));
        return Ordered = snapshot;
    }

    internal static void OpenNear(Vector3 position)
    {
        foreach (var door in Snapshot())
        {
            if (door == null || door.GetType() != typeof(Door) || door.DoorState != EDoorState.Shut || !door.Operatable) continue;
            if (!door.gameObject.activeInHierarchy || (door.hideFlags & HideFlags.DontSave) != 0 ||
                !(Vector3.Distance(door.transform.position, position) <= 3f)) continue;
            // Reopen a previously broken door without rebreaking it.
            if (door.IsBroken) door.Interact(new InteractionResult(EInteractionType.Open));
            else door.KickOpen(position);
        }
    }
}

[HarmonyPatch(typeof(WorldInteractiveObject), nameof(WorldInteractiveObject.Awake))]
internal static class GrenadeBreachDoorRegistrationPatch
{
    private static void Postfix(WorldInteractiveObject __instance) => GrenadeBreachDoors.Track(__instance);
}
