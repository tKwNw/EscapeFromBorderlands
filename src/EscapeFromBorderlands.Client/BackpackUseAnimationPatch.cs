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
        var p = Singleton<GameWorld>.Instance?.RegisteredPlayers.OfType<Player>().FirstOrDefault(p => ReferenceEquals(p.HandsAnimator, __instance));
        if (p is null) return;
        var controller = ServiceWindow.MedicalController(p, __instance);
        EarpieceMedicalAnimation.Record(__instance, p, System.Convert.ToSingle(__args[0]));
        var multiplier = System.Convert.ToSingle(__args[0]) * GrenadeBuffs.Medicine(p) * EarpieceRuntime.MedicalSpeed(p, controller?.Item);
        if (controller?.Item is { } item) multiplier /= BackpackNative.UseFactor(p, item);
        __args[0] = multiplier;
    }
}
