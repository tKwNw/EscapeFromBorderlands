using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Animations;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;
internal static class BackpackPresentation
{
    private static bool NightReduced;
    internal static bool NightVisionOn(Player? player) => player?.NightVisionObserver?.Component?.Togglable?.On == true;
    internal static void Reset() { NightReduced = false; RefreshNight(); }
    internal static void Restore(Player p) { if (Singleton<GameWorld>.Instance?.MainPlayer == p) { NightReduced = false; RefreshNight(); } }
    internal static void Maintain(BackpackRuntime.State s)
    {
        if (Singleton<GameWorld>.Instance?.MainPlayer != s.Player) return;
        var reduced = s.Key == "BP15-1";
        if (reduced == NightReduced) return;
        NightReduced = reduced; RefreshNight();
    }
    private static void RefreshNight()
    {
        var effect = EFT.CameraControl.CameraManager.Instance?.NightVision;
        if (effect != null) effect.ApplySettings();
    }
    internal static bool FullCompartments(Item bag)
    {
        var grids = Native.Items(Native.Get(bag, "Grids")).ToArray();
        return grids.Length == 6 && grids.All(g => Native.Items(Native.Get(g, "Items")).Any());
    }
}
[HarmonyPatch(typeof(BreathEffector), nameof(BreathEffector.Process))]
internal static class BackpackSwayPatch
{
    private static void Prefix(BreathEffector __instance, out float __state)
    {
        __state = __instance.Intensity;
        var p = BackpackRuntime.Actor(__instance.Physical);
        if (BackpackRuntime.Is(p, "BP11-2") && p!.MovementContext.IsInPronePose && p.InputDirection.sqrMagnitude < .0001f) __instance.Intensity *= .3f;
    }
    private static Exception? Finalizer(BreathEffector __instance, float __state, Exception? __exception) { __instance.Intensity = __state; return __exception; }
}
[HarmonyPatch]
internal static class BackpackRecoilSettlingPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] {
        AccessTools.Method(typeof(EFT.Animations.NewRecoil.NewRecoilShotEffect), "FixedUpdate"),
        AccessTools.Method(typeof(OldRecoilShotEffect), "FixedUpdate") };
    private static void Prefix(Player.FirearmController ____firearmController, ref float deltaTime)
    {
        var p = BackpackRuntime.Actor(____firearmController);
        var s = BackpackRuntime.Get(p);
        if (s?.Key == "BP41-2" && BackpackPresentation.FullCompartments(s.Bag)) deltaTime *= 1.3f;
    }
}
[HarmonyPatch(typeof(RootMotion.FinalIK.HitReaction), "Hit")]
internal static class BackpackAimPunchPatch
{
    private static void Prefix(RootMotion.FinalIK.HitReaction __instance, ref Vector3 force)
    {
        var p = Singleton<GameWorld>.Instance?.RegisteredPlayers.OfType<Player>().FirstOrDefault(p => ReferenceEquals(p.HitReaction, __instance));
        if (BackpackRuntime.Is(p, "BP09-3")) force *= .5f;
    }
}
[HarmonyPatch(typeof(BSG.CameraEffects.NightVision), "ApplySettings")]
internal static class BackpackNightVisionPatch
{
    private static void Prefix(BSG.CameraEffects.NightVision __instance, out float __state)
    {
        __state = __instance.NoiseIntensity;
        if (__instance.On && BackpackRuntime.Is(Singleton<GameWorld>.Instance?.MainPlayer, "BP15-1")) __instance.NoiseIntensity *= .2f;
    }
    private static Exception? Finalizer(BSG.CameraEffects.NightVision __instance, float __state, Exception? __exception)
    { __instance.NoiseIntensity = __state; return __exception; }
}
[HarmonyPatch(typeof(UltimateBloom), "OnRenderImage")]
internal static class BackpackBloomPatch
{
    private static void Prefix(UltimateBloom __instance, out float __state)
    {
        __state = __instance.m_BloomIntensity;
        var p = Singleton<GameWorld>.Instance?.MainPlayer;
        if (BackpackRuntime.Is(p, "BP15-1") && BackpackPresentation.NightVisionOn(p)) __instance.m_BloomIntensity *= .2f;
    }
    private static Exception? Finalizer(UltimateBloom __instance, float __state, Exception? __exception)
    { __instance.m_BloomIntensity = __state; return __exception; }
}
