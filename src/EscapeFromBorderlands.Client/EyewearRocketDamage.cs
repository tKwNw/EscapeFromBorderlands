using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using EFT.RocketLauncher;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

// Rockets process contact, expanding blast and backblast on different native
// objects, sometimes after the wearer has changed equipment. Capture only the
// numeric outgoing factor at launch; no owner or inventory reference is held.
internal static class EyewearRocketDamage
{
    private sealed class Snapshot { internal float Factor; }
    private static ConditionalWeakTable<object, Snapshot> Sources = new();

    internal static void Reset() => Sources = new();
    internal static void Capture(object source, IPlayer? owner)
    {
        var factor = owner is Player player ? Eyewear.Outgoing(player) : 1;
        if (factor == 1) Sources.Remove(source); // Also clears pooled old launches.
        else Sources.GetOrCreateValue(source).Factor = factor;
    }
    internal static EyewearDamageSources.NativeScope Begin(object source) =>
        EyewearDamageSources.BeginNative(Sources.TryGetValue(source, out var saved) ? saved.Factor : 1);
}

[HarmonyPatch]
internal static class EyewearRocketSource
{
    private static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        AccessTools.Method(typeof(RocketProjectile), nameof(RocketProjectile.Initialize)),
        AccessTools.Method(typeof(EFT.RocketLauncher.Explosion.CollisionsHandlerModel), nameof(EFT.RocketLauncher.Explosion.CollisionsHandlerModel.Initialize))
    };
    // Both native initializers receive their owner as argument index one.
    private static void Prefix(object __instance, IPlayer? __1) => EyewearRocketDamage.Capture(__instance, __1);
}

[HarmonyPatch(typeof(BackblastModel), nameof(BackblastModel.Initialize))]
internal static class EyewearBackblastSource
{
    private static void Prefix(BackblastModel __instance, IPlayer? __2) => EyewearRocketDamage.Capture(__instance, __2);
}

[HarmonyPatch]
internal static class EyewearRocketImpact
{
    private static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        AccessTools.Method(typeof(RocketProjectile), "ProceedDamage"),
        AccessTools.Method(typeof(EFT.RocketLauncher.Explosion.CollisionsHandlerModel), "TryPlayerCollision"),
        AccessTools.Method(typeof(BackblastModel), "TryDamagePlayer")
    };
    private static void Prefix(object __instance, out EyewearDamageSources.NativeScope __state) =>
        __state = EyewearRocketDamage.Begin(__instance);
    private static Exception? Finalizer(EyewearDamageSources.NativeScope __state, Exception? __exception)
    { EyewearDamageSources.EndNative(__state); return __exception; }
}
