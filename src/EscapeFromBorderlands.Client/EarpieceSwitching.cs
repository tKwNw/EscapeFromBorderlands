using System;
using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class EarpieceSwitching
{
    internal sealed class Transition
    {
        internal EarpieceRuntime.State State = null!;
        internal string Target = "";
        internal bool Melee;
        internal float Until;
        internal float Speed => !Melee && State.Key == "EP10-3" ? 1.75f : 1f;
    }
    private static readonly Dictionary<Player, Transition> Pending = new();
    internal static void Reset() => Pending.Clear();
    internal static Transition? Queue(Player? p, Item? previous, Item? next, bool melee)
    {
        if (p is null) return null;
        Pending.Remove(p);
        if (next is not Weapon || next.Id == previous?.Id || EarpieceRuntime.Get(p) is not { } s) return null;
        return Pending[p] = new Transition { State = s, Target = next.Id, Melee = melee, Until = Time.time + 15f };
    }
    internal static Transition? Take(Player? p, Item next)
    {
        if (p is null || !Pending.TryGetValue(p, out var t)) return null;
        Pending.Remove(p);
        return t.Target == next.Id && Time.time < t.Until && EarpieceRuntime.Get(p) == t.State ? t : null;
    }
}
[HarmonyPatch(typeof(Player.FirearmController), nameof(Player.FirearmController.Drop))]
internal static class EarpieceFirearmHolsterPatch
{
    private static void Prefix(Player.FirearmController __instance, Item nextControllerItem, ref float animationSpeed)
    {
        var transition = EarpieceSwitching.Queue(BackpackRuntime.Actor(__instance), __instance.Item, nextControllerItem, false);
        if (transition is not null) animationSpeed *= transition.Speed;
    }
}
[HarmonyPatch(typeof(Player.BaseKnifeController), nameof(Player.BaseKnifeController.Drop))]
internal static class EarpieceMeleeSwitchPatch
{
    private static void Prefix(Player.BaseKnifeController __instance, Item nextControllerItem) =>
        EarpieceSwitching.Queue(BackpackRuntime.Actor(__instance), __instance.Item, nextControllerItem, true);
}
[HarmonyPatch(typeof(Player.FirearmController), nameof(Player.FirearmController.Spawn))]
internal static class EarpieceFirearmSwitchPatch
{
    private static void Prefix(Player.FirearmController __instance, ref float animationSpeed, ref Action callback)
    {
        var transition = EarpieceSwitching.Take(BackpackRuntime.Actor(__instance), __instance.Item);
        if (transition is null) return;
        var original = animationSpeed; var completed = callback;
        animationSpeed *= transition.Speed;
        callback = () =>
        {
            if (transition.Speed != 1f) __instance.FirearmsAnimator.SetAnimationSpeed(original);
            if (EarpieceRuntime.Get(transition.State.Player) == transition.State) transition.State.Effect.Switched(transition.Melee, true);
            completed?.Invoke();
        };
    }
}
[HarmonyPatch(typeof(Player.FirearmController.FixMalfunctionOperationClass), nameof(Player.FirearmController.FixMalfunctionOperationClass.OnMalfunctionOffEvent))]
internal static class EarpieceMalfunctionRepairedPatch
{
    private static void Postfix(Player.FirearmController.FixMalfunctionOperationClass __instance) =>
        EarpieceRuntime.Get(BackpackNative.WeaponOwner(__instance.Weapon_0))?.Effect.MalfunctionCleared();
}
