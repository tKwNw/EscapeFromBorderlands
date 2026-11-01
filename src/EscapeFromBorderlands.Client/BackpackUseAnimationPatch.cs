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
        EarpieceMedicalAnimation.Record(__instance, p, System.Convert.ToSingle(__args[0]));
        var multiplier = System.Convert.ToSingle(__args[0]) * GrenadeBuffs.Medicine(p) * EarpieceRuntime.MedicalSpeed(p);
        if (p.HandsController is Player.MedsController controller) multiplier /= BackpackNative.UseFactor(p, controller.Item);
        // Native medical initialization assigns HandsAnimator before the new
        // controller necessarily becomes HandsController. The smoke bonus can
        // therefore use the animator's actor even during that transition.
        __args[0] = multiplier;
    }
}
