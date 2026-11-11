using System.Collections.Generic;

namespace EscapeFromBorderlands.Client;

// Bound duplicate tracking so multi-collider callbacks cannot count as two hits.
internal sealed class HeadwearImpactWindow
{
    private readonly HashSet<(string, int)> Seen = new();
    private readonly Queue<(string, int)> Order = new();
    private float Last = float.NegativeInfinity;
    internal bool Hit(string source, int shot, float now, bool ready)
    {
        var key = (source, shot);
        if (!Seen.Add(key)) return false;
        Order.Enqueue(key);
        if (Order.Count > 64) Seen.Remove(Order.Dequeue());
        if (!ready) { Last = float.NegativeInfinity; return false; }
        if (now - Last <= 5f) { Last = float.NegativeInfinity; return true; }
        Last = now;
        return false;
    }
}
