using System;
using System.Collections.Generic;
using System.Linq;

namespace EscapeFromBorderlands.Client;

internal static class EffectStatusModel
{
    private static readonly object NoticeLock = new();
    private static readonly Dictionary<(string Owner, string Key), (string Name, string Detail, float Until)> TriggerNotices = new();
    // A short receipt for an instantaneous proc, not a fabricated buff duration.
    internal static void Notify(string owner, string key, string name, float now, string detail)
    { lock (NoticeLock) TriggerNotices[(owner, key)] = (name, "Triggered · " + detail, now + 2f); }
    internal static void ResetNotices() { lock (NoticeLock) TriggerNotices.Clear(); }
    internal static IEnumerable<Status> Notices(string owner, float now)
    {
        lock (NoticeLock)
        {
            foreach (var key in TriggerNotices.Where(p => p.Value.Until <= now).Select(p => p.Key).ToArray()) TriggerNotices.Remove(key);
            return TriggerNotices.Where(p => p.Key.Owner == owner).Select(p => new Status(p.Key.Key, p.Value.Name, true, detail: p.Value.Detail)).ToArray();
        }
    }
    internal readonly struct Cooldown
    {
        internal Cooldown(string key, string name, string active = "") { Key = key; Name = name; ActiveKey = active; }
        internal string Key { get; }
        internal string Name { get; }
        internal string ActiveKey { get; }
    }
    internal readonly struct Status
    {
        internal Status(string key, string name, bool active, float seconds = 0f, float ready = 0f, string detail = "", bool visibleWhenInactive = false)
        { Key = key; Name = name; Active = active; Seconds = seconds; Ready = ready; Detail = detail; VisibleWhenInactive = visibleWhenInactive; }
        internal string Key { get; }
        internal string Name { get; }
        internal bool Active { get; }
        internal float Seconds { get; }
        internal float Ready { get; }
        internal string Detail { get; }
        internal bool VisibleWhenInactive { get; }
        internal bool Visible => Active || Ready > 0f || VisibleWhenInactive;
        internal string Label => Ready > 0f
            ? Active ? Name + "  " + (Detail.Length > 0 ? Detail : "Active") + (Seconds > 0f ? " " + Clock(Seconds) : "") + "  ·  Cooldown " + Clock(Ready) : Name + "  " + (Detail.Length > 0 ? Detail : "Cooldown") + " " + Clock(Ready)
            : Detail.Length > 0 ? Name + (Key.StartsWith("grenade:", StringComparison.Ordinal) ? " " : "  ") + Detail + (Seconds > 0f ? " " + Clock(Seconds) : "") : Seconds > 0f ? Name + "  Active " + Clock(Seconds) : Active ? Name + "  Active" : string.Empty;
    }
    internal static List<Status> Visible(IEnumerable<Status> source) => source.Where(s => s.Visible)
        .GroupBy(s => s.Key, StringComparer.Ordinal)
        .Select(g => new Status(g.Key, g.First().Name, g.Any(s => s.Active), g.Max(s => s.Seconds), g.Max(s => s.Ready),
            g.FirstOrDefault(s => s.Detail.Length > 0).Detail ?? string.Empty, g.Any(s => s.VisibleWhenInactive))).ToList();
    internal static readonly Cooldown[] Cooldowns = {
        new("BP03-3", "Freight Brake"), new("BP07-1", "Fireball"),
        new("BP10-3", "Sling Away", "throwDistance"), new("BP20-3", "Roach Out", "smoke"),
        new("BP29-2", "Season's Recovery"), new("BP34-2", "Roadside Lunch"), new("BP43-3", "Reserve Tank")
    };
    internal static float Remaining(IReadOnlyDictionary<string, float> timers, string key, float now) =>
        timers.TryGetValue(key, out var end) ? Math.Max(0f, end - now) : 0f;
    internal static List<Status> Collect(IReadOnlyDictionary<string, float> timers, float now)
    {
        var statuses = new List<Status>();
        foreach (var definition in Cooldowns)
        {
            var remaining = Remaining(timers, "cd:" + definition.Key, now);
            var active = Remaining(timers, definition.ActiveKey, now);
            if (remaining <= 0f && active <= 0f) continue;
            statuses.Add(new Status(definition.Key, definition.Name, active > 0f, active, remaining));
        }
        return statuses;
    }
    internal static string Clock(float seconds)
    {
        var whole = (int)Math.Ceiling(Math.Max(0f, seconds));
        return whole >= 60 ? (whole / 60) + ":" + (whole % 60).ToString("00") : whole + "s";
    }
}
