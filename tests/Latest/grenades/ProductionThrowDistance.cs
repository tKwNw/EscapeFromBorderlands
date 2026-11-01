using System;
using EFT;
using HarmonyLib;
namespace EscapeFromBorderlands.Client;
[HarmonyPatch(typeof(Player.BaseGrenadeHandsController), "ThrowGrenadeInner")]
internal static class BackpackGrenadeDistancePatch
{
    private static void Prefix(Player.BaseGrenadeHandsController __instance, ref float lowHighThrow)
    {
        if (RarityResolver.Resolve(__instance.Item) is { } roll && roll.Definition.GrenadeChargesPerRaid > 0)
            lowHighThrow *= (float)Math.Sqrt(roll.GrenadeThrowDistanceMultiplier);
        if (ChestRigCombat.GrenadeBonus(__instance,"TR07-2")) lowHighThrow *= (float)Math.Sqrt(1.35f);
        var p = BackpackRuntime.Actor(__instance);
        if (p != null && BackpackRuntime.Records.TryGetValue(p.ProfileId, out var r) && r.Active("throwDistance")) lowHighThrow *= (float)Math.Sqrt(1.5f);
    }
}
