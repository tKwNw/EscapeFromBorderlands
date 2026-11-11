using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using EFT;
using EFT.Particles;
using HarmonyLib;
using Systems.Effects;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class LegendaryExplosionFx
{
    internal readonly struct Request
    {
        internal readonly float Radius, Reference;
        internal readonly string? Label;
        internal Request(float radius, float reference, string? label = null) { Radius = radius; Reference = reference; Label = label; }
    }
    private sealed class Scale { internal float Value; internal string Label; internal Scale(float value, string label) { Value = value; Label = label; } }
    private sealed class Visual
    {
        internal Component Template = null!, Particle = null!;
        internal GameObject Root = null!;
        internal Vector3 OriginalScale;
        internal bool Active;
        internal float Until, LastUse;
    }
    [ThreadStatic] internal static Request Current;
    [ThreadStatic] internal static float PlayingScale;
    private static ConditionalWeakTable<Effects.Effect, Scale> Tagged = new();
    private static readonly List<Visual> Visuals = new();
    private static readonly List<Visual> VisualSnapshot = new();
    private static readonly MethodInfo Clone = AccessTools.Method(typeof(object), "MemberwiseClone");
    private static GameWorld? World;
    private static bool Reported;
    private static readonly HashSet<string> Reports = new(StringComparer.Ordinal);
    internal static float Factor => PlayingScale > 0f ? PlayingScale : 1f;
    internal static float Ratio(float radius, float reference)
    {
        var value = radius / reference;
        return radius > 0f && reference > 0f && value > 0f && !float.IsNaN(value) && !float.IsInfinity(value) ? value : 1f;
    }
    internal static Request BeginGrenade(Grenade grenade)
    {
        var old = Current; Current = default;
        try
        {
            var hat = grenade.WeaponSource is { } item ? Headwear.BlastRadius(item) : 1f;
            if (LegendaryGrenadeEffects.Try(grenade.WeaponSource, out var payload) && !payload.SmokeChild && payload.Roll.Definition.GrenadeCategory != "smoke")
            {
                var source = (IExplosiveItem)grenade.WeaponSource;
                var effective = LegendaryGrenadeEffects.Explosion(source, grenade.WeaponSource);
                Current = source.MaxExplosionDistance > 0f ? new Request(effective.MaxExplosionDistance * hat, source.MaxExplosionDistance, payload.Roll.Definition.RuntimeKey)
                    : new Request(effective.Blindness.y, source.Blindness.y, payload.Roll.Definition.RuntimeKey);
            }
            else if (hat > 1f && grenade.WeaponSource is IExplosiveItem blast && blast.MaxExplosionDistance > 0)
            {
                Current = new Request(blast.MaxExplosionDistance * hat, blast.MaxExplosionDistance, "HW066-3");
            }
        }
        catch (Exception e) { Report(e); }
        return old;
    }
    internal static void Impact(Effects effects, Vector3 position, Vector3 normal, float radius, float volume)
    {
        var old = Current; Current = new Request(radius, 0f, "impact");
        try { Queue(effects, "Grenade_new", position, normal, volume); }
        finally { Current = old; }
    }
    // Enter at the actual native EmitGrenade call site rather than depending
    // on a small callback entering a patched prefix under Mono.
    internal static void GrenadeVisual(Effects effects, string name, Vector3 position, Vector3 normal, float volume, Grenade grenade)
    {
        var old = BeginGrenade(grenade);
        try { Queue(effects, name, position, normal, volume); }
        catch (Exception e) { Report(e); }
        finally { Current = old; }
    }
    private static void Queue(Effects effects, string name, Vector3 position, Vector3 normal, float volume)
    {
        // Always enter the public native method: renderer replacement mods
        // (including HollywoodFX) attach their actual visual dispatch here.
        effects.EmitGrenade(name, position, normal, volume);
    }
    internal static void CaptureAndQueue(Effects effects, Effects.Effect effect, Vector3 position, Vector3 normal,
        EFT.Ballistics.BallisticCollider hitCollider, bool withDecal, float volume, bool isKnife,
        bool isHitPointVisible, bool isGrenade, EPointOfView pov)
    {
        Capture(ref effect, isGrenade);
        effects.AddEffectEmit(effect, position, normal, hitCollider, withDecal, volume, isKnife, isHitPointVisible, isGrenade, pov);
    }
    internal static void Play(Effects.Effect effect, Vector3 position, Vector3 normal, EFT.Ballistics.BallisticCollider hitCollider,
        bool withDecal, float volume, bool isKnife, bool isHitPointVisible, bool isGrenade, EPointOfView pov)
    {
        var old = Enter(effect);
        try { effect.Emit(position, normal, hitCollider, withDecal, volume, isKnife, isHitPointVisible, isGrenade, pov); }
        finally { PlayingScale = old; }
    }
    internal static void Capture(ref Effects.Effect effect, bool isGrenade)
    {
        if (!isGrenade || Current.Radius <= 0f || Tagged.TryGetValue(effect, out _)) return;
        var original = effect;
        try
        {
            var reference = Current.Reference > 0f ? Current.Reference : ReferenceRadius(effect);
            var scale = Ratio(Current.Radius, reference);
            if (Math.Abs(scale - 1f) < .0001f) return;
            // The descriptor is queued for a later Effects.Update. Give this
            // event its own identity; never edit the shared prefab.
            effect = (Effects.Effect)Clone.Invoke(effect, null)!;
            effect.LightRange *= scale; effect.WindRadius *= scale; effect.ParticlesShift *= scale;
            var label = Current.Label ?? "legendary";
            Tagged.Add(effect, new Scale(scale, label));
            Trace("queue:" + label + ":" + scale, $"EFB explosion FX {label}: queued {scale:F2}x visual scale; radius {Current.Radius:F2} m, reference {reference:F2} m; basic particles {effect.BasicParticleSystemMediator != null}, emitter groups {effect.Particles?.Length ?? 0}.");
        }
        catch (Exception e) { effect = original; Report(e); }
    }
    private static float ReferenceRadius(Effects.Effect effect)
    {
        // Impact procs specify metres, rather than a vanilla grenade modifier.
        // Estimate the authored particle envelope from native sampled size,
        // speed and lifetime curves; decorative sparks do not set this radius.
        var radius = 0f;
        foreach (var entry in effect.Particles ?? Array.Empty<Effects.Effect.ParticleSys>())
        {
            if (entry.Particle is not ParticleSystemAdapter adapter) continue;
            var lifetime = Native.Get(adapter, "_lifeTimes") as float[];
            var speed = Native.Get(adapter, "_speeds") as float[];
            var size = Native.Get(adapter, "_sizes") as float[];
            if (lifetime is null || speed is null || size is null) continue;
            var count = Math.Min(lifetime.Length, Math.Min(speed.Length, size.Length));
            for (var i = 0; i < count; i++) radius = Math.Max(radius, Math.Abs(speed[i]) * lifetime[i] + size[i] * .5f);
        }
        return radius > .001f ? radius : 1f;
    }
    internal static float Enter(Effects.Effect effect)
    {
        var old = PlayingScale;
        if (Tagged.TryGetValue(effect, out var scale))
        {
            PlayingScale = scale.Value;
            Trace("play:" + scale.Label + ":" + scale.Value, $"EFB explosion FX {scale.Label}: playing {scale.Value:F2}x visual scale.");
        }
        else PlayingScale = 1f;
        return old;
    }
    internal static void Emit(Emitter emitter, Vector3 position, Vector3 velocity)
    {
        var scale = Factor;
        if (emitter is ParticleSystemAdapter adapter && scale != 1f) adapter.Emit(position, velocity * scale, scale);
        else emitter.Emit(position, velocity * scale);
    }
    internal static void EmitBasic(BasicParticleSystemMediator mediator, Vector3 position, Quaternion rotation)
    { if (Mediator(mediator, position, rotation)) mediator.Emit(position, rotation); }
    internal static bool Mediator(BasicParticleSystemMediator mediator, Vector3 position, Quaternion rotation)
    {
        if (Factor == 1f) return true;
        GameObject? creating = null;
        try
        {
            var particles = Native.Get(mediator, "_particleSystems") as Array;
            if (particles is null || particles.Length == 0) return true;
            var index = Convert.ToInt32(Native.Get(mediator, "_index"));
            if (particles.GetValue(index) is not Component template) return true;
            var visual = Visuals.FirstOrDefault(v => !v.Active && v.Root != null && ReferenceEquals(v.Template, template));
            if (visual is null)
            {
                var root = UnityEngine.Object.Instantiate(template.gameObject);
                creating = root;
                root.SetActive(false);
                var particle = root.GetComponent(template.GetType());
                visual = new Visual { Template = template, Root = root, Particle = particle, OriginalScale = template.transform.lossyScale };
                foreach (var component in root.GetComponentsInChildren(template.GetType(), true))
                {
                    var main = Native.Get(component, "main")!;
                    var mode = Native.Get(main, "scalingMode")!;
                    Native.Set(main, "scalingMode", Enum.Parse(mode.GetType(), "Hierarchy"));
                    Native.Call(component, "Clear", true);
                }
                Visuals.Add(visual);
                creating = null;
            }
            visual.Root.transform.SetPositionAndRotation(position, rotation);
            visual.Root.transform.localScale = visual.OriginalScale * Factor;
            visual.Root.SetActive(true);
            Native.Call(visual.Particle, "Clear", true);
            Native.Call(visual.Particle, "Play", true);
            visual.Active = true; visual.Until = Time.time + 30f; visual.LastUse = Time.time;
            Trace("basic:" + Factor, $"EFB explosion FX: applied {Factor:F2}x to private basic-particle hierarchy.");
            return false;
        }
        catch (Exception e)
        {
            if (creating != null) UnityEngine.Object.Destroy(creating);
            Report(e);
            return true;
        }
    }
    internal static void Maintain(GameWorld? world)
    {
        if (world != World) { Reset(); World = world; }
        if (Visuals.Count == 0) return;
        VisualSnapshot.AddRange(Visuals);
        var now = Time.time;
        try
        {
            foreach (var v in VisualSnapshot)
            {
                try
                {
                    if (v.Root == null || v.Particle == null) { if (v.Root != null) UnityEngine.Object.Destroy(v.Root); Visuals.Remove(v); continue; }
                    if (v.Active && (now >= v.Until || Native.Call(v.Particle, "IsAlive", true) is false))
                    { Native.Call(v.Particle, "Stop", true); Native.Call(v.Particle, "Clear", true); v.Root.SetActive(false); v.Active = false; v.LastUse = now; }
                    if (!v.Active && now - v.LastUse > 60f) { UnityEngine.Object.Destroy(v.Root); Visuals.Remove(v); }
                }
                catch (Exception e)
                {
                    if (v.Root != null) UnityEngine.Object.Destroy(v.Root);
                    Visuals.Remove(v); Report(e);
                }
            }
        }
        finally { VisualSnapshot.Clear(); }
    }
    private static void Trace(string key, string message)
    { if (Reports.Add(key)) Plugin.LogSource.LogInfo(message); }
    private static void Report(Exception e)
    { if (!Reported) { Reported = true; Plugin.LogSource.LogWarning("Legendary explosion visual scaling unavailable: " + e.Message); } }
    internal static void Reset()
    {
        foreach (var v in Visuals) if (v.Root != null) UnityEngine.Object.Destroy(v.Root);
        LegendaryHollywoodFx.Reset();
        Visuals.Clear(); Tagged = new(); Current = default; PlayingScale = 0f; World = null; Reported = false; Reports.Clear();
    }
}
[HarmonyPatch(typeof(Grenade), "Explosion", new Type[] { })]
internal static class LegendaryGrenadeFxScopePatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var target = AccessTools.Method(typeof(Effects), nameof(Effects.EmitGrenade)); var count = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(target))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendaryExplosionFx), nameof(LegendaryExplosionFx.GrenadeVisual)); count++;
            }
            yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException($"Expected 1 native grenade visual dispatch, found {count}.");
    }
}
[HarmonyPatch(typeof(Effects), nameof(Effects.AddEffectEmit))]
internal static class LegendaryExplosionFxCapturePatch
{
    private static void Prefix(ref Effects.Effect effect, bool isGrenade) => LegendaryExplosionFx.Capture(ref effect, isGrenade);
}
[HarmonyPatch(typeof(Effects.Effect), nameof(Effects.Effect.Emit))]
internal static class LegendaryExplosionFxPlaybackPatch
{
    private static void Prefix(Effects.Effect __instance, out float __state) => __state = LegendaryExplosionFx.Enter(__instance);
    private static Exception? Finalizer(float __state, Exception? __exception) { LegendaryExplosionFx.PlayingScale = __state; return __exception; }
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var target = AccessTools.Method(typeof(BasicParticleSystemMediator), nameof(BasicParticleSystemMediator.Emit)); var count = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(target))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendaryExplosionFx), nameof(LegendaryExplosionFx.EmitBasic)); count++;
            }
            yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException($"Expected 1 native basic-particle emission, found {count}.");
    }

}
[HarmonyPatch(typeof(Effects.Effect.ParticleSys), nameof(Effects.Effect.ParticleSys.Emit))]
internal static class LegendaryExplosionFxParticlePatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var target = AccessTools.Method(typeof(Emitter), nameof(Emitter.Emit), new[] { typeof(Vector3), typeof(Vector3) }); var count = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(target)) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(LegendaryExplosionFx), nameof(LegendaryExplosionFx.Emit)); count++; }
            yield return instruction;
        }
        if (count != 6) throw new InvalidOperationException($"Expected 6 native explosion particle branches, found {count}.");
    }
}
[HarmonyPatch(typeof(BasicParticleSystemMediator), nameof(BasicParticleSystemMediator.Emit))]
internal static class LegendaryExplosionFxMediatorPatch
{
    private static bool Prefix(BasicParticleSystemMediator __instance, Vector3 position, Quaternion rotation) => LegendaryExplosionFx.Mediator(__instance, position, rotation);
}

[HarmonyPatch(typeof(Effects), "Update")]
internal static class LegendaryExplosionFxQueuePlaybackPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var target = AccessTools.Method(typeof(Effects.Effect), nameof(Effects.Effect.Emit)); var count = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(target))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendaryExplosionFx), nameof(LegendaryExplosionFx.Play)); count++;
            }
            yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException($"Expected 1 native deferred effect playback, found {count}.");
    }
}

[HarmonyPatch(typeof(Effects), nameof(Effects.EmitGrenade))]
internal static class LegendaryExplosionFxNativeQueuePatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var target = AccessTools.Method(typeof(Effects), nameof(Effects.AddEffectEmit)); var count = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(target))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendaryExplosionFx), nameof(LegendaryExplosionFx.CaptureAndQueue)); count++;
            }
            yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException($"Expected 1 native grenade queue insertion, found {count}.");
    }
}
