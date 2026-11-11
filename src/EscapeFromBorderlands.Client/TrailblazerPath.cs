using System;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

// Sample horizontal path length rather than time so fast and slow throws keep
// the same cloud spacing. Interpolate height at each crossed 5-metre boundary.
internal sealed class TrailblazerPath
{
    private Vector3 Previous;
    private float Remainder;
    internal TrailblazerPath(Vector3 start) => Previous = start;
    internal void Advance<T>(Vector3 position, T state, Action<T, Vector3> drop)
    {
        var start = Previous;
        var delta = position - start;
        var distance = (float)Math.Sqrt(delta.x * delta.x + delta.z * delta.z);
        Previous = position;
        if (distance <= .00001f) return;
        var next = 5f - Remainder;
        while (next <= distance + .00001f)
        {
            drop(state, start + delta * Math.Min(1f, next / distance));
            next += 5f;
        }
        Remainder = Math.Max(0f, distance - (next - 5f));
    }
}
