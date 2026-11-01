using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

// Optional renderer adapter. No HollywoodFX assembly or asset is required.
// Each HFX blast owns a private emitter set. Keep its scale through the
// deferred ConfinedBlast coroutine, then reset it on ordinary pool reuse.
internal static class LegendaryHollywoodFx
{
    private sealed class Mode
    {
        internal Component Particle = null!;
        internal object Original = null!;
    }
    private sealed class Root
    {
        internal Component Particle = null!;
        internal Transform? Parent;
        internal GameObject Wrapper = null!;
        internal Mode[] Modes = Array.Empty<Mode>();
    }
    private sealed class Lease { internal readonly List<Root> Roots = new(); }
    private static readonly Dictionary<object, Lease> Leases = new();
    private static readonly Dictionary<Component, object> Owners = new();
    private static readonly HashSet<string> Reports = new(StringComparer.Ordinal);
    private static bool Failed;
    private const string Bundle = "HollywoodFX.Particles.EffectBundle";
    internal static Type? Find(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType(name, false)).FirstOrDefault(t => t is not null);
    internal static IEnumerable<MethodBase> Targets()
    {
        foreach (var name in new[] { "HollywoodFX.Explosion.Blast", "HollywoodFX.Explosion.ConfinedBlast" })
            if (Find(name) is { } type && AccessTools.DeclaredMethod(type, "Emit", new[] { typeof(Vector3), typeof(Vector3) }) is { } method)
                yield return method;
    }
    private static IEnumerable<Component> Emitters(object blast)
    {
        // Reflect only the two supported blast classes' declared EffectBundle
        // fields, including primary-constructor captures. Do not scan the game.
        foreach (var field in AccessTools.GetDeclaredFields(blast.GetType()))
        {
            if (field.FieldType.FullName == Bundle)
            {
                foreach (var main in BundleEmitters(field.GetValue(blast))) yield return main;
            }
            else if (field.FieldType.IsArray && field.FieldType.GetElementType()?.FullName == Bundle && field.GetValue(blast) is Array bundles)
                foreach (var bundle in bundles)
                    foreach (var main in BundleEmitters(bundle)) yield return main;
        }
    }
    private static IEnumerable<Component> BundleEmitters(object? bundle)
    {
        if (bundle is null || AccessTools.Field(bundle.GetType(), "Emitters")?.GetValue(bundle) is not Array emitters) yield break;
        foreach (var emitter in emitters)
            if (emitter is not null && AccessTools.Field(emitter.GetType(), "Main")?.GetValue(emitter) is Component main) yield return main;
    }
    private static float Scale(object blast)
    {
        var request = LegendaryExplosionFx.Current;
        if (request.Radius <= 0f) return 1f;
        var reference = request.Reference;
        if (reference <= 0f)
        {
            // HFX 2.0.0's big-blast constructor supplies its authored radius
            // (6 metres). Impact procs use their absolute gameplay radius.
            var radius = AccessTools.GetDeclaredFields(blast.GetType()).FirstOrDefault(f =>
                f.FieldType == typeof(float) && f.Name.IndexOf("radius", StringComparison.OrdinalIgnoreCase) >= 0);
            reference = radius?.GetValue(blast) is float value && value > 0f ? value : 6f;
        }
        return LegendaryExplosionFx.Ratio(request.Radius, reference);
    }
    internal static void Prepare(object blast)
    {
        try
        {
            var scale = Scale(blast);
            if (!Leases.TryGetValue(blast, out var lease))
            {
                if (scale == 1f) return;
                var emitters = Emitters(blast).Distinct().ToArray();
                if (emitters.Length == 0) throw new InvalidOperationException("HollywoodFX blast exposes no supported particle emitters.");
                var roots = emitters.Where(p => !emitters.Any(other => other != p && p.transform.IsChildOf(other.transform))).ToArray();
                if (roots.Any(p => Owners.TryGetValue(p, out var owner) && !ReferenceEquals(owner, blast)))
                    throw new InvalidOperationException("HollywoodFX emitter is shared between blast pool entries.");
                lease = new Lease(); Leases.Add(blast, lease);
                foreach (var particle in roots)
                {
                    var root = new Root { Particle = particle, Parent = particle.transform.parent, Wrapper = new GameObject("EFB explosion visual scale") };
                    lease.Roots.Add(root); Owners[particle] = blast;
                    root.Wrapper.transform.SetParent(root.Parent, false);
                    root.Wrapper.transform.localPosition = Vector3.zero;
                    root.Wrapper.transform.localRotation = Quaternion.identity;
                    root.Wrapper.transform.localScale = Vector3.one;
                    root.Modes = particle.gameObject.GetComponentsInChildren(particle.GetType(), true).Select(p =>
                        new Mode { Particle = p, Original = Native.Get(Native.Get(p, "main"), "scalingMode")! }).ToArray();
                    particle.transform.SetParent(root.Wrapper.transform, false);
                }
            }
            foreach (var root in lease.Roots)
            {
                if (root.Particle == null || root.Wrapper == null) continue;
                root.Wrapper.transform.localScale = Vector3.one * scale;
                foreach (var mode in root.Modes)
                    if (mode.Particle != null)
                        Native.Set(Native.Get(mode.Particle, "main")!, "scalingMode", scale == 1f ? mode.Original : Enum.Parse(mode.Original.GetType(), "Hierarchy"));
            }
            if (scale != 1f)
            {
                var label = LegendaryExplosionFx.Current.Label ?? "legendary";
                var key = label + ":" + scale;
                if (Reports.Add(key)) Plugin.LogSource.LogInfo($"EFB explosion FX {label}: HollywoodFX renderer prepared at {scale:F2}x; {lease.Roots.Count} private emitter roots, including deferred particles.");
            }
        }
        catch (Exception e)
        {
            if (Leases.TryGetValue(blast, out var lease)) { Restore(lease); Leases.Remove(blast); }
            if (!Failed) { Failed = true; Plugin.LogSource.LogWarning("HollywoodFX legendary size adapter unavailable; preserving its normal explosion: " + e.Message); }
        }
    }
    private static void Restore(Lease lease)
    {
        foreach (var root in lease.Roots)
        {
            try
            {
                if (root.Particle != null)
                {
                    var scale = root.Particle.transform.localScale;
                    root.Particle.transform.SetParent(root.Parent, true);
                    root.Particle.transform.localScale = scale;
                    foreach (var mode in root.Modes)
                        if (mode.Particle != null && mode.Original is not null) Native.Set(Native.Get(mode.Particle, "main")!, "scalingMode", mode.Original);
                    Owners.Remove(root.Particle);
                }
                if (root.Wrapper != null) UnityEngine.Object.Destroy(root.Wrapper);
            }
            catch (Exception e)
            {
                // Cleanup must never interrupt the third-party renderer or
                // destroy a still-attached particle hierarchy.
                if (root.Wrapper != null) root.Wrapper.transform.localScale = Vector3.one;
                if (!Failed) { Failed = true; Plugin.LogSource.LogWarning("HollywoodFX visual scale cleanup deferred: " + e.Message); }
            }
        }
    }

    internal static void Reset()
    {
        foreach (var lease in Leases.Values) Restore(lease);
        Leases.Clear(); Owners.Clear(); Reports.Clear(); Failed = false;
    }
}
[HarmonyPatch]
internal static class LegendaryHollywoodFxBlastPatch
{
    private static bool Prepare() => LegendaryHollywoodFx.Targets().Any();
    private static IEnumerable<MethodBase> TargetMethods() => LegendaryHollywoodFx.Targets();
    private static void Prefix(object __instance) => LegendaryHollywoodFx.Prepare(__instance);
}
