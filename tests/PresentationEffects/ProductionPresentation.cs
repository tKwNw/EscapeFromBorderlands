using System; using System.Linq; using System.Reflection;
using System.Collections.Generic; using Comfort.Common; using EFT;
using EFT.Animations; using EFT.InventoryLogic; using HarmonyLib; using UnityEngine;
namespace EscapeFromBorderlands.Client;
internal static class BackpackPresentation {
internal static bool FullCompartments(Item bag)
    {
        var grids = Native.Items(Native.Get(bag, "Grids")).ToArray();
        return grids.Length == 6 && grids.All(g => Native.Items(Native.Get(g, "Items")).Any());
    }
internal static bool NightVisionOn(Player? player) => player?.NightVisionObserver?.Component?.Togglable?.On == true;
}
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
