using System;
using Comfort.Common;
using EFT;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

[HarmonyPatch(typeof(Player.FirearmController), nameof(Player.FirearmController.ReloadBarrels))]
internal static class ElephantGunReloadPatch
{
    // Install the selected incoming ammo before the existing reload hook
    // refreshes the native animator, including when both barrels are empty.
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Player.FirearmController __instance, EFT.InventoryLogic.AmmoPack ammoPack,
        ref Callback callback, out ElephantGunRules.Reload? __state)
    {
        __state = null;
        if (__instance.Blindfire || ammoPack.AmmoCount <= 0 || !__instance.CanStartReload()) return;
        var weapon = __instance.Item;
        var reload = ElephantGunRules.BeginReload(weapon, ammoPack.GetAmmoToReload(0)?.AmmoTemplate);
        __state = reload;
        if (reload is null) return;
        var original = callback;
        callback = result =>
        {
            if (ElephantGunRules.EndReload(weapon, reload)) __instance.SetAnimatorAndProceduralValues();
            original?.Invoke(result);
        };
    }

    private static Exception? Finalizer(Player.FirearmController __instance, ElephantGunRules.Reload? __state, Exception? __exception)
    {
        if (__exception is not null && ElephantGunRules.EndReload(__instance.Item, __state))
            __instance.SetAnimatorAndProceduralValues();
        return __exception;
    }
}
