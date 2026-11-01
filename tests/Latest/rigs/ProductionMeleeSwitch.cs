using System;using System.Collections.Generic;using EFT;using EFT.Ballistics;using EFT.InventoryLogic;using UnityEngine;
namespace EscapeFromBorderlands.Client;
internal static class MeleeWeaponSwitch
{
    private static readonly Dictionary<Player, (string Target, float Factor, float Until)> Pending = new();
    internal static void Reset() => Pending.Clear();
    internal static void Queue(Player player, Item? next, float factor)
    {
        Pending.Remove(player);
        if (next is Weapon && factor > 1f) Pending[player] = (next.Id, factor, Time.time + 10f);
    }
    internal static float Consume(Player player, Item next)
    {
        if (!Pending.TryGetValue(player, out var entry)) return 1f;
        Pending.Remove(player);
        return entry.Target == next.Id && Time.time < entry.Until ? entry.Factor : 1f;
    }
}
internal static class LegendaryMeleeFirearmDrawPatch
{
    private static void Prefix(Player.FirearmController __instance, ref float animationSpeed, ref Action callback)
    {
        var player = MeleeRuntime.Owner(__instance);
        if (player is null) return;
        var factor = MeleeWeaponSwitch.Consume(player, __instance.Item);
        if (factor == 1f) return;
        var original = animationSpeed;
        var completed = callback;
        animationSpeed *= factor;
        callback = () =>
        {
            __instance.FirearmsAnimator.SetAnimationSpeed(original);
            completed?.Invoke();
        };
    }
}
