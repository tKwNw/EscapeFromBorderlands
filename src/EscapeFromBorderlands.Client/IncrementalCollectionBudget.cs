using System;

namespace EscapeFromBorderlands.Client;

// Main-thread only. Retain the requested collection budget, use at most one
// slice per frame, and stop as soon as Unity reports that the cycle completed.
internal sealed class IncrementalCollectionBudget
{
    internal ulong Remaining
    {
        get; private set;
    }
    private int LastFrame = int.MinValue;
    internal bool Request(ulong amount, int frame, ulong slice, Func<ulong, bool> collect)
    {
        if (amount == 0)
            return collect(0);
        if (ulong.MaxValue - Remaining < amount)
            Flush(collect);
        Remaining += amount;
        return Drain(frame, slice, collect);
    }
    internal bool Drain(int frame, ulong slice, Func<ulong, bool> collect)
    {
        if (Remaining == 0)
            return false;
        if (LastFrame == frame || slice == 0)
            return true;
        var amount = Math.Min(Remaining, slice);
        LastFrame = frame;
        // Update before invoking native code so reentrant requests cannot run
        // another slice or be overwritten when this call returns.
        Remaining -= amount;
        if (collect(amount))
            return true;
        Remaining = 0;
        return false;
    }
    internal void Flush(Func<ulong, bool> collect)
    {
        var amount = Remaining;
        Remaining = 0;
        LastFrame = int.MinValue;
        if (amount != 0)
            collect(amount);
    }
}
