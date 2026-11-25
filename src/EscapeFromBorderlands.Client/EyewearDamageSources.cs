using EFT.Ballistics;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

// Native bleeding intentionally uses an unattributed DamageInfo value. Retain
// only the numeric penalty on the actual effect, without changing kill credit
// or keeping its attacker alive. The weak entry vanishes with the native effect.
internal static class EyewearDamageSources
{
    internal sealed class Penalty { internal float Factor; }
    private static ConditionalWeakTable<ActiveHealthController.Effect, Penalty> Bleeds = new();
    private static bool HasBleeds;
    internal static int Generation { get; private set; }
    private sealed class ProjectileSource
    {
        internal Item Weapon = null!;
        internal int FireIndex, References;
        internal float Factor;
    }
    private static readonly Dictionary<Shot, ProjectileSource> Projectiles = new();
    internal static bool HasProjectileSources => Projectiles.Count != 0;
    private static readonly Dictionary<(Item Weapon, int FireIndex), ProjectileSource> Volleys = new();
    private static readonly Stack<ProjectileSource> SpareSources = new();
    private static ConditionalWeakTable<Item, Penalty> Grenades = new();
    internal readonly struct NativeScope
    {
        internal readonly float Factor;
        internal readonly bool Active;
        internal readonly int Generation;
        internal bool IsCurrent => Generation == EyewearDamageSources.Generation;
        internal NativeScope(float factor, bool active) { Factor = factor; Active = active; Generation = EyewearDamageSources.Generation; }
    }
    [ThreadStatic] private static float NativeFactor;
    [ThreadStatic] private static bool InNative;
    [ThreadStatic] internal static Player? Owner;
    [ThreadStatic] internal static float TickFactor;
    [ThreadStatic] internal static bool InTick;
    internal static void Reset()
    {
        Generation++; Bleeds = new(); Grenades = new(); Projectiles.Clear(); Volleys.Clear(); SpareSources.Clear(); HasBleeds = false;
        Owner = null; TickFactor = NativeFactor = 1; InTick = InNative = false;
    }
    internal static NativeScope BeginNative(float factor)
    { var old = new NativeScope(NativeFactor, InNative); NativeFactor = factor; InNative = true; return old; }
    internal static void EndNative(NativeScope old) { if (old.IsCurrent) { NativeFactor = old.Factor; InNative = old.Active; } }
    internal static float OutgoingForDamage(in DamageInfo info)
    {
        if (InNative) return NativeFactor;
        if (info.Weapon is { } item)
        {
            if (Volleys.Count != 0 && Volleys.TryGetValue((item, info.FireIndex), out var volley)) return volley.Factor;
            if (Grenades.TryGetValue(item, out var grenade)) return grenade.Factor;
            // Native projectiles are resolved at launch; subsequently equipping
            // different eyewear must not retroactively alter an ordinary shot.
            if (item is Weapon or ThrowWeap or Ammo) return 1;
        }
        return info.Player?.iPlayer is Player owner ? Eyewear.Outgoing(owner) : Unattributed(info.DamageType);
    }
    internal static void ProjectileCreated(Shot shot)
    {
        if (Projectiles.Count != 0 && Projectiles.ContainsKey(shot)) ProjectileReleased(shot);
        if (shot.Weapon is null) return;
        var factor = shot.Parent is { } parent && Projectiles.TryGetValue(parent, out var inherited) ? inherited.Factor :
            InNative ? NativeFactor : Eyewear.CurrentShot is { } scope && ReferenceEquals(shot.Weapon, scope.Weapon)
                ? scope.Printer ? .15f : scope.Double ? .7f : 1 : 1;
        if (factor == 1) return;
        var key = (Weapon: shot.Weapon, FireIndex: shot.FireIndex);
        if (!Volleys.TryGetValue(key, out var source))
        {
            source = SpareSources.Count > 0 ? SpareSources.Pop() : new ProjectileSource();
            source.Weapon = key.Weapon; source.FireIndex = key.FireIndex; source.Factor = factor; source.References = 0;
            Volleys[key] = source;
        }
        Projectiles[shot] = source; source.References++;
    }
    internal static void ProjectileReleased(Shot shot)
    {
        if (Projectiles.Count == 0 || !Projectiles.TryGetValue(shot, out var source)) return;
        Projectiles.Remove(shot);
        if (--source.References == 0)
        {
            Volleys.Remove((source.Weapon, source.FireIndex)); source.Weapon = null!;
            if (SpareSources.Count < 128) SpareSources.Push(source);
        }
    }
    internal static NativeScope BeginImpact(Shot shot) => BeginNative(Projectiles.TryGetValue(shot, out var source) ? source.Factor : 1);
    internal static void CaptureGrenade(Item item, float factor)
    {
        if (factor != 1) Grenades.GetOrCreateValue(item).Factor = factor;
        else Grenades.Remove(item);
        if (LegendaryGrenadeEffects.HasPayloads && LegendaryGrenadeEffects.Try(item, out var payload)) payload.EyewearOutgoing = factor;
    }
    internal static float GrenadeFactor(Item item) => Grenades.TryGetValue(item, out var source) ? source.Factor : InNative ? NativeFactor : 1;
    internal static void GrenadeCreated(Item item, string owner)
    {
        if (InNative) { CaptureGrenade(item, NativeFactor); return; }
        if (Eyewear.HasPrinter && Singleton<GameWorld>.Instance is { } world)
            CaptureGrenade(item, Eyewear.Outgoing(CombatActorLookup.Find(world, owner)));
    }
    internal static void Created(ActiveHealthController.Effect effect)
    {
        if (effect.GetType().Name is not ("LightBleeding" or "HeavyBleeding")) return;
        var factor = InNative ? NativeFactor : Owner is not null ? Eyewear.Outgoing(Owner) : 1;
        if (factor == 1) return;
        Bleeds.GetOrCreateValue(effect).Factor = factor; HasBleeds = true;
    }
    internal static float Factor(ActiveHealthController.Effect effect) => HasBleeds && Bleeds.TryGetValue(effect, out var penalty) ? penalty.Factor : 1;
    internal static float Unattributed(EDamageType type) => InTick && type is EDamageType.LightBleeding or EDamageType.HeavyBleeding ? TickFactor : 1;
}
[HarmonyPatch]
internal static class EyewearNativeImpactSourcePatch
{
    // Native explosive ammunition detonates inside ShotDelegate, after the
    // Shot.Tick postfix's source scope has ended. Cover that entire native hit,
    // including native blasts/fragments, rather than looking up current gear.
    private static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        AccessTools.DeclaredMethod(typeof(GameWorld), "ShotDelegate"),
        AccessTools.DeclaredMethod(typeof(ClientGameWorld), "ShotDelegate")
    };
    private static void Prefix(Shot __0, out (bool Entered, EyewearDamageSources.NativeScope Previous) __state)
    {
        __state = default;
        if (EyewearDamageSources.HasProjectileSources)
            __state = (true, EyewearDamageSources.BeginImpact(__0));
    }
    private static Exception? Finalizer((bool Entered, EyewearDamageSources.NativeScope Previous) __state, Exception? __exception)
    {
        if (__state.Entered) EyewearDamageSources.EndNative(__state.Previous);
        return __exception;
    }
}
[HarmonyPatch(typeof(Shot), nameof(Shot.Create))]
internal static class EyewearProjectileSourcePatch
{
    private static void Postfix(Shot __result) { if (__result is not null) EyewearDamageSources.ProjectileCreated(__result); }
}
[HarmonyPatch(typeof(GrenadeFactory), nameof(GrenadeFactory.Create))]
internal static class EyewearGrenadeSourcePatch
{
    private static void Postfix(ThrowWeap item, string profileId) => EyewearDamageSources.GrenadeCreated(item, profileId);
}
[HarmonyPatch(typeof(ActiveHealthController.Effect), nameof(ActiveHealthController.Effect.Init))]
internal static class EyewearBleedCreatedPatch
{
    private static void Postfix(ActiveHealthController.Effect __instance) => EyewearDamageSources.Created(__instance);
}
[HarmonyPatch]
internal static class EyewearBleedTickPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(ActiveHealthController).GetNestedType("Bleeding", BindingFlags.Public | BindingFlags.NonPublic), "BleedHealth");
    private static void Prefix(ActiveHealthController.Effect __instance, out (float Factor, bool Active, int Generation) __state)
    {
        __state = (EyewearDamageSources.TickFactor, EyewearDamageSources.InTick, EyewearDamageSources.Generation);
        EyewearDamageSources.InTick = true; EyewearDamageSources.TickFactor = EyewearDamageSources.Factor(__instance);
    }
    private static Exception? Finalizer((float Factor, bool Active, int Generation) __state, Exception? __exception)
    { if (__state.Generation == EyewearDamageSources.Generation) { EyewearDamageSources.TickFactor = __state.Factor; EyewearDamageSources.InTick = __state.Active; } return __exception; }
}
