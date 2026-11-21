using EFT;
using EFT.Animations;

namespace EscapeFromBorderlands.Client;

internal static class RecoilOwnerLookup
{
    internal static Player? Find(RecoilProcessBase process)
    {
        // Keep dictionary first-match order and inspect live endpoints. A stale
        // reverse cache could apply another actor's effect after weapon swaps,
        // removal or a foreign mod sharing/replacing recoil objects.
        // The concrete key enumerator avoids LINQ boxing and a captured predicate.
        foreach (var player in EarpieceRuntime.States.Keys)
        {
            var effect = player.ProceduralWeaponAnimation?.Shootingg?.CurrentRecoilEffect;
            if (effect is not null && (ReferenceEquals(effect.HandPositionRecoilEffect, process) ||
                ReferenceEquals(effect.HandRotationRecoilEffect, process) || ReferenceEquals(effect.CameraRotationRecoilEffect, process))) return player;
        }
        return null;
    }
}
