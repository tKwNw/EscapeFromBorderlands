using System;
using System.Collections.Generic;
using EFT.Ballistics;
using EFT.InventoryLogic;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

// Join the native list before ShotMultiProjectileShot submits it. Its cached
// natural pellet loop count is unchanged; generated entries are not iterated
// by CreateMultiProjectileShot and therefore cannot duplicate recursively.
[HarmonyPatch(typeof(BallisticsCalculator), nameof(BallisticsCalculator.CreateMultiProjectileShot))]
internal static class EyewearPelletBatchPatch
{
    internal struct Context
    {
        internal Eyewear.ShotScope? Scope;
        internal List<EftBulletClass>? Previous;
        internal int Generation;
    }
    private static void Prefix(List<EftBulletClass> __4, Item __6, out Context __state)
    {
        __state = default;
        if (Eyewear.CurrentShot is not { Double: true } scope || !ReferenceEquals(scope.Weapon, __6)) return;
        __state = new Context { Scope = scope, Previous = scope.NativePelletBatch, Generation = Eyewear.Generation };
        scope.NativePelletBatch = __4;
    }
    private static Exception? Finalizer(Context __state, Exception? __exception)
    {
        if (__state.Scope is { } scope && __state.Generation == Eyewear.Generation)
        { scope.NativePelletBatch = __state.Previous; }
        return __exception;
    }
}
