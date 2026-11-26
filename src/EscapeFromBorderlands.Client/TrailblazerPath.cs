using System;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

// Sample horizontal path length rather than time so fast and slow throws keep
// the same cloud spacing. Interpolate height at each crossed 5-metre boundary.
internal sealed class TrailblazerPath
{
    // More than 640 metres between samples is a discontinuity, not a thrown
    // grenade's flight. Do not populate a teleport/corrupt path with smoke.
    internal const int MaximumDropsPerAdvance = 128;
    private Vector3 Previous;
    private float Remainder;
    private bool HasPrevious;
    internal TrailblazerPath(Vector3 start)
    {
        Previous = start;
        HasPrevious = Finite(start);
    }
    private static bool Finite(Vector3 point) =>
        float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z);
    internal void Advance<T>(Vector3 position, T state, Action<T, Vector3> drop)
    {
        if (!Finite(position))
        {
            HasPrevious = false;
            Remainder = 0f;
            return;
        }
        if (!HasPrevious)
        {
            Previous = position;
            HasPrevious = true;
            Remainder = 0f;
            return;
        }
        var start = Previous;
        var delta = position - start;
        var distance = (float)Math.Sqrt(delta.x * delta.x + delta.z * delta.z);
        Previous = position;
        if (!Finite(delta) || !float.IsFinite(distance) || distance > MaximumDropsPerAdvance * 5f)
        {
            Remainder = 0f;
            return;
        }
        if (distance <= .00001f)
            return;
        var next = 5f - Remainder;
        for (var emitted = 0; emitted < MaximumDropsPerAdvance && next <= distance + .00001f; emitted++)
        {
            drop(state, start + delta * Math.Min(1f, next / distance));
            next += 5f;
        }
        Remainder = Math.Max(0f, distance - (next - 5f));
    }
}
