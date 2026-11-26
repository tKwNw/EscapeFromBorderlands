using System;

namespace EscapeFromBorderlands.Client;

// Keep the disposed-world tombstone without keeping the world alive. The
// current raid has a fast reference path; only transitions inspect the weak ref.
internal sealed class SessionLifetime<T> where T : class
{
    private WeakReference<T>? Closed;
    private T? Active;
    internal int Generation
    {
        get; private set;
    }
    internal T? Current(T? candidate)
    {
        if (candidate is null)
        {
            Active = null;
            return null;
        }
        if (ReferenceEquals(candidate, Active))
            return candidate;
        if (Closed is not null && Closed.TryGetTarget(out var closed) && ReferenceEquals(candidate, closed))
            return null;
        return Active = candidate;
    }
    internal bool Close(T world)
    {
        if (Closed is not null && Closed.TryGetTarget(out var closed) && ReferenceEquals(world, closed))
            return false;
        Active = null;
        Closed = new WeakReference<T>(world);
        return true;
    }
    internal void Reset()
    {
        Active = null;
        unchecked
        {
            Generation++;
        }
    }
    internal bool IsCurrent(T? candidate, int generation) => generation == Generation && candidate is not null && ReferenceEquals(Current(candidate), candidate);
}
