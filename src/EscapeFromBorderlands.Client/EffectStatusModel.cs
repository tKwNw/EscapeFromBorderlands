using System;
using System.Collections.Generic;

namespace EscapeFromBorderlands.Client;

internal static class EffectStatusModel
{
    private static readonly object NoticeLock = new();
    private static readonly Dictionary<(string Owner, string Key), (string Name, string Detail, float Until)> TriggerNotices = new();
    private static readonly List<(string Owner, string Key)> ExpiredNotices = new();
    private static readonly Dictionary<string, int> NoticesByOwner = new(StringComparer.Ordinal);
    private static float NextNoticeExpiry = float.PositiveInfinity;
    // A short receipt for an instantaneous proc, not a fabricated buff duration.
    internal static void Notify(string owner, string key, string name, float now, string detail)
    {
        lock (NoticeLock)
        {
            var notice = (owner, key);
            if (!TriggerNotices.ContainsKey(notice))
                NoticesByOwner[owner] = NoticesByOwner.TryGetValue(owner, out var count) ? count + 1 : 1;
            var until = now + 2f;
            TriggerNotices[notice] = (name, "Triggered · " + detail, until);
            NextNoticeExpiry = Math.Min(NextNoticeExpiry, until);
        }
    }
    internal static void ResetNotices()
    {
        lock (NoticeLock)
        {
            TriggerNotices.Clear();
            NoticesByOwner.Clear();
            NextNoticeExpiry = float.PositiveInfinity;
        }
    }
    internal static void AppendNotices(List<Status> statuses, string owner, float now)
    {
        lock (NoticeLock)
        {
            if (now < NextNoticeExpiry && !NoticesByOwner.ContainsKey(owner)) return;
            try
            {
                var next = float.PositiveInfinity;
                foreach (var pair in TriggerNotices)
                {
                    if (pair.Value.Until <= now) ExpiredNotices.Add(pair.Key);
                    else
                    {
                        next = Math.Min(next, pair.Value.Until);
                        if (pair.Key.Owner == owner)
                            statuses.Add(new Status(pair.Key.Key, pair.Value.Name, true, detail: pair.Value.Detail));
                    }
                }
                foreach (var key in ExpiredNotices)
                {
                    TriggerNotices.Remove(key);
                    if (NoticesByOwner[key.Owner] == 1) NoticesByOwner.Remove(key.Owner);
                    else NoticesByOwner[key.Owner]--;
                }
                NextNoticeExpiry = next;
            }
            finally { ExpiredNotices.Clear(); }
        }
    }
    internal readonly struct Cooldown
    {
        internal Cooldown(string key, string name, string active = "") { Key = key; Name = name; ActiveKey = active; TimerKey = "cd:" + key; }
        internal string Key { get; }
        internal string Name { get; }
        internal string ActiveKey { get; }
        internal string TimerKey { get; }
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
        internal bool SameLabel(in Status other) => Active == other.Active && Key == other.Key &&
            Name == other.Name && Detail == other.Detail &&
            LabelSeconds(Seconds) == LabelSeconds(other.Seconds) && LabelSeconds(Ready) == LabelSeconds(other.Ready);
        private static int LabelSeconds(float value) => value > 0f ? (int)Math.Ceiling(value) : 0;
        internal string Label => Ready > 0f
            ? Active ? Name + "  " + (Detail.Length > 0 ? Detail : "Active") + (Seconds > 0f ? " " + Clock(Seconds) : "") + "  ·  Cooldown " + Clock(Ready) : Name + "  " + (Detail.Length > 0 ? Detail : "Cooldown") + " " + Clock(Ready)
            : Detail.Length > 0 ? Name + (Key.StartsWith("grenade:", StringComparison.Ordinal) ? " " : "  ") + Detail + (Seconds > 0f ? " " + Clock(Seconds) : "") : Seconds > 0f ? Name + "  Active " + Clock(Seconds) : Active ? Name + "  Active" : string.Empty;
    }
    internal static void CopyVisible(List<Status> source, List<Status> destination, Dictionary<string, int> index)
    {
        destination.Clear();
        index.Clear();
        foreach (var status in source)
        {
            if (!status.Visible) continue;
            if (!index.TryGetValue(status.Key, out var row))
            {
                index.Add(status.Key, destination.Count);
                destination.Add(status);
                continue;
            }
            var previous = destination[row];
            destination[row] = new Status(previous.Key, previous.Name, previous.Active || status.Active,
                MaxDuration(previous.Seconds, status.Seconds), MaxDuration(previous.Ready, status.Ready),
                previous.Detail.Length > 0 ? previous.Detail : status.Detail, previous.VisibleWhenInactive || status.VisibleWhenInactive);
        }
    }
    // Match Enumerable.Max for the floats formerly collected by GroupBy.
    private static float MaxDuration(float previous, float next) =>
        float.IsNaN(previous) || next > previous ? next : previous;
    internal static readonly Cooldown[] Cooldowns = {
        new("BP03-3", "Freight Brake"), new("BP07-1", "Fireball"),
        new("BP10-3", "Sling Away", "throwDistance"), new("BP20-3", "Roach Out", "smoke"),
        new("BP29-2", "Season's Recovery"), new("BP34-2", "Roadside Lunch"), new("BP43-3", "Reserve Tank")
    };
    internal static float Remaining(IReadOnlyDictionary<string, float> timers, string key, float now) =>
        timers.TryGetValue(key, out var end) ? Math.Max(0f, end - now) : 0f;
    internal static void AppendCooldowns(List<Status> statuses, IReadOnlyDictionary<string, float> timers, float now)
    {
        foreach (var definition in Cooldowns)
        {
            var remaining = Remaining(timers, definition.TimerKey, now);
            var active = Remaining(timers, definition.ActiveKey, now);
            if (remaining <= 0f && active <= 0f) continue;
            statuses.Add(new Status(definition.Key, definition.Name, active > 0f, active, remaining));
        }
    }
    internal static string Clock(float seconds)
    {
        var whole = (int)Math.Ceiling(Math.Max(0f, seconds));
        return whole >= 60 ? (whole / 60) + ":" + (whole % 60).ToString("00") : whole + "s";
    }
}
