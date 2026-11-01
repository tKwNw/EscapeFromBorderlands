using System;
using System.Linq;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

// Native smoke systems can ignore their parent's scale (Local/Shape modes).
// Opt every system into hierarchy scaling for this emission only, including
// inactive fill systems, and restore the prefab settings before pool reuse.
internal sealed class SmokeParticleScale
{
    private readonly Component Particle;
    private readonly object OriginalMode;
    private SmokeParticleScale(Component particle, object originalMode)
    { Particle = particle; OriginalMode = originalMode; }

    internal static SmokeParticleScale[] Apply(GrenadeEmission emission)
    {
        if (Native.Get(emission, "_particleSystem") is not Component root) return Array.Empty<SmokeParticleScale>();
        return emission.GetComponentsInChildren(root.GetType(), true).Select(system =>
        {
            var main = Native.Get(system, "main")!;
            var original = Native.Get(main, "scalingMode")!;
            Native.Set(main, "scalingMode", Enum.Parse(original.GetType(), "Hierarchy"));
            return new SmokeParticleScale(system, original);
        }).ToArray();
    }

    internal void Restore()
    {
        if (Particle != null) Native.Set(Native.Get(Particle, "main")!, "scalingMode", OriginalMode);
    }
}
