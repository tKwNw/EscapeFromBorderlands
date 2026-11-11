using System;
using System.Collections.Generic;
using System.Linq;

namespace EscapeFromBorderlands.Client;

// Raid state belongs to an item ID, never to a template, stack, player or slot.
// Keeping spent entries also prevents drop/pickup and transfers from refilling them.
internal sealed class GrenadeChargeLedger
{
    private sealed class Entry
    {
        internal int Capacity, Spent;
        internal double Interval = 180, NextCharge;
        internal string? Reservation;
    }
    private readonly Func<double> Clock;
    internal GrenadeChargeLedger(Func<double>? clock = null) => Clock = clock ?? (() => DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond);
    private readonly object Sync = new();
    private readonly Dictionary<string, Entry> Items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> Selected = new(StringComparer.Ordinal);

    private Entry Get(string itemId, int capacity, double interval)
    {
        if (string.IsNullOrEmpty(itemId) || capacity < 1) throw new ArgumentException("A reusable grenade needs an item ID and positive capacity.");
        if (!Items.TryGetValue(itemId, out var entry)) Items[itemId] = entry = new Entry { Capacity = capacity };
        Update(entry, interval);
        return entry;
    }
    private void Update(Entry entry, double interval)
    {
        SetInterval(entry, interval);
        Advance(entry);
    }
    private static void SetInterval(Entry entry, double interval) =>
        entry.Interval = double.IsNaN(interval) || double.IsInfinity(interval) ? 180 : Math.Max(1, interval);

    private void Advance(Entry e)
    {
        if (e.Spent == 0 || e.NextCharge <= 0) return;
        Advance(e, Clock());
    }
    private static void Advance(Entry e, double now)
    {
        if (e.Spent == 0 || e.NextCharge <= 0 || now < e.NextCharge) return;
        var restored = (int)Math.Min(e.Spent, 1 + Math.Floor((now - e.NextCharge) / e.Interval));
        e.Spent -= restored;
        e.NextCharge = e.Spent == 0 ? 0 : e.NextCharge + restored * e.Interval;
    }
    internal int Remaining(string itemId, int capacity, double interval = 180)
    { lock (Sync) { var e = Get(itemId, capacity, interval); return Math.Max(0, e.Capacity - e.Spent); } }
    internal double SecondsUntil(string itemId, int capacity, double interval = 180)
    { lock (Sync) { var e = Get(itemId, capacity, interval); return e.Spent == 0 ? 0 : Math.Max(0, e.NextCharge - Clock()); } }
    internal (int Remaining, double Seconds) Display(string itemId, int capacity, double interval)
    {
        lock (Sync)
        {
            if (!Items.TryGetValue(itemId, out var entry)) return (capacity, 0d);
            Update(entry, interval);
            return (Math.Max(0, entry.Capacity - entry.Spent),
                entry.Spent == 0 ? 0d : Math.Max(0d, entry.NextCharge - Clock()));
        }
    }
    internal readonly struct DisplayQuery
    {
        internal readonly string Id;
        internal readonly int Capacity;
        internal readonly double Interval;
        internal DisplayQuery(string id, int capacity, double interval) { Id = id; Capacity = capacity; Interval = interval; }
    }
    internal void DisplayBatch(List<DisplayQuery> queries, List<(int Remaining, double Seconds)> output)
    {
        output.Clear();
        lock (Sync)
        {
            var now = Clock();
            foreach (var q in queries)
            {
                if (!Items.TryGetValue(q.Id, out var entry)) { output.Add((q.Capacity, 0d)); continue; }
                SetInterval(entry, q.Interval);
                Advance(entry, now);
                output.Add((Math.Max(0, entry.Capacity - entry.Spent),
                    entry.Spent == 0 ? 0d : Math.Max(0d, entry.NextCharge - now)));
            }
        }
    }
    internal bool Reserve(string itemId, int capacity, string token, double interval = 180)
    {
        if (string.IsNullOrEmpty(token)) throw new ArgumentException("A throw needs a reservation token.");
        lock (Sync)
        {
            var e = Get(itemId, capacity, interval);
            if (e.Spent >= e.Capacity || e.Reservation is not null) return false;
            e.Reservation = token;
            return true;
        }
    }
    internal bool Finish(string itemId, string token, bool spawned)
    {
        lock (Sync)
        {
            if (!Items.TryGetValue(itemId, out var e) || e.Reservation != token) return false;
            Advance(e);
            e.Reservation = null;
            if (spawned)
            {
                if (e.Spent == 0) e.NextCharge = Clock() + e.Interval;
                e.Spent++;
            }
            return true;
        }
    }
    internal bool ReduceRecharge(string itemId, int capacity, double seconds, double interval = 180)
    {
        lock (Sync)
        {
            var e = Get(itemId, capacity, interval);
            if (e.Spent == 0 || seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return false;
            if (e.NextCharge - Clock() > seconds) e.NextCharge -= seconds;
            else
            {
                e.Spent--;
                // Restore at most 1 charge. Discard excess acceleration rather
                // than carrying it into another charge's independent interval.
                e.NextCharge = e.Spent == 0 ? 0 : Clock() + e.Interval;
            }
            return true;
        }
    }
    internal void Pin(string actorId, string itemId) { lock (Sync) Selected[actorId] = itemId; }
    internal string? Pinned(string actorId) { lock (Sync) return Selected.TryGetValue(actorId, out var id) ? id : null; }
    internal string? Select(string actorId, IEnumerable<string> available, string? preferred)
    {
        lock (Sync)
        {
            var ids = available.ToArray();
            if (Selected.TryGetValue(actorId, out var pinned) && ids.Contains(pinned, StringComparer.Ordinal)) return pinned;
            var chosen = preferred is not null && ids.Contains(preferred, StringComparer.Ordinal) ? preferred : ids.FirstOrDefault();
            if (chosen is null) Selected.Remove(actorId); else Selected[actorId] = chosen;
            return chosen;
        }
    }
    internal void Reset() { lock (Sync) { Items.Clear(); Selected.Clear(); } }
}
