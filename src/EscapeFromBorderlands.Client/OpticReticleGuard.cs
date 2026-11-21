using EFT.CameraControl;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

// CanUpdate describes the reticle previously bound by SetOpticSight. The zoom
// handler passes the current scope cache, which can briefly have no reticle.
// Skip only that invalid transform update; valid data resumes on the next call.
[HarmonyPatch(typeof(OpticRetrice), nameof(OpticRetrice.UpdateTransform))]
internal static class OpticReticleGuard
{
    private static bool Prefix(OpticRetrice __instance, OpticSight opticSight) =>
        opticSight != null && opticSight.ScopeData != null &&
        opticSight.ScopeData.Reticle != null && __instance.Renderer != null;
}
