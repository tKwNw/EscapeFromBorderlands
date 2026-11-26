using System.Linq;
using Comfort.Common;
using EFT;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

[HarmonyPatch(typeof(FirearmsAnimator), "SetUseTimeMultiplier")]
internal static class BackpackUseAnimationPatch
{
    private static void Prefix(FirearmsAnimator __instance, object[] __args)
    {
        var p = ActorLookup.Find(__instance, ActorMember.HandsAnimator);
        if (p is null)
            return;
        var controller = ServiceWindow.MedicalController(p, __instance);
        EarpieceMedicalAnimation.Record(__instance, p, System.Convert.ToSingle(__args[0]));
        var multiplier = System.Convert.ToSingle(__args[0]) * GrenadeBuffs.Medicine(p) * EarpieceRuntime.MedicalSpeed(p, controller?.Item);
        if (controller?.Item is { } item)
            multiplier /= BackpackNative.UseFactor(p, item);
        __args[0] = multiplier;
    }
}
