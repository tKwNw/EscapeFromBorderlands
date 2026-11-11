using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace EscapeFromBorderlands.Client;

// Private test only. Sample about 1 in 32 controller calls without changing
// game RNG. Never format text or log from the native update hook.
internal struct WeaponPerformanceTrace
{
    private static readonly bool DiagnosticRecording = false;

    internal enum Route { Neutral, Gear, Legendary, Restore }
    private struct Summary
    {
        internal long Count, Observe, Carrier, Refresh, Peak, StatReads, BatchedReads, Read, Applies, Applied, Intervals, Interval;
        internal void Add(long observe, long carrier, long refresh, in WeaponPerformanceTrace trace)
        {
            Count++; Observe += observe; Carrier += carrier; Refresh += refresh;
            Peak = Math.Max(Peak, observe + carrier + refresh);
            if (trace.StatsTaken) { StatReads++; Read += trace.StatsTime; }
            if (trace.UsedBatch) BatchedReads++;
            if (trace.ApplyTaken) { Applies++; Applied += trace.ApplyTime; }
            if (trace.IntervalTaken) { Intervals++; Interval += trace.IntervalTime; }
        }
    }

    private static readonly object Sync = new();
    private static readonly Summary[] Routes = new Summary[4];
    private static readonly Dictionary<VariantDefinition, Summary> Effects = new();
    [ThreadStatic] private static uint Sequence;
    private long Start, Observed, Switched;
    private long StatsTime, ApplyTime, IntervalTime;
    private bool StatsTaken, UsedBatch, ApplyTaken, IntervalTaken;

    internal static WeaponPerformanceTrace Begin()
    {
        if (!DiagnosticRecording) return default;

        // High bits of an independent LCG avoid repeatedly sampling the
        // same position in a fixed-length bot update list.
        Sequence = unchecked(Sequence * 1664525u + 1013904223u);
        return Sequence >> 27 == 0 ? new WeaponPerformanceTrace { Start = Stopwatch.GetTimestamp() } : default;
    }
    internal void Observe() { if (Start != 0) Observed = Stopwatch.GetTimestamp(); }
    internal void Carrier() { if (Start != 0) Switched = Stopwatch.GetTimestamp(); }
    internal long Stamp() => Start == 0 ? 0 : Stopwatch.GetTimestamp();
    internal void ReadStats(long start, bool batched)
    {
        if (start == 0) return;
        StatsTime = Stopwatch.GetTimestamp() - start;
        StatsTaken = true;
        UsedBatch = batched;
    }
    internal void Apply(long start)
    {
        if (start == 0) return;
        ApplyTime = Stopwatch.GetTimestamp() - start;
        ApplyTaken = true;
    }
    internal void Interval(long start)
    {
        if (start == 0) return;
        IntervalTime = Stopwatch.GetTimestamp() - start;
        IntervalTaken = true;
    }
    internal void Complete(Route route, VariantDefinition? definition)
    {
        if (Start == 0) return;
        var end = Stopwatch.GetTimestamp();
        var observe = Observed - Start;
        var carrier = Switched - Observed;
        var refresh = end - Switched;
        lock (Sync)
        {
            Routes[(int)route].Add(observe, carrier, refresh, in this);
            if (definition is null) return;
            if (!Effects.TryGetValue(definition, out var summary) && Effects.Count >= 128) return;
            summary.Add(observe, carrier, refresh, in this);
            Effects[definition] = summary;
        }
    }
    private static string Micros(long ticks, long count = 1) =>
        (ticks * (1_000_000d / Stopwatch.Frequency) / Math.Max(1, count)).ToString("F2", CultureInfo.InvariantCulture);
    internal static void Reset()
    {
        lock (Sync)
        {
            try
            {
                for (var i = 0; i < Routes.Length; i++)
                {
                    var s = Routes[i];
                    if (s.Count > 0)
                        Plugin.LogSource.LogInfo($"EFB firearm timing: {(Route)i} | {s.Count} samples | observe {Micros(s.Observe, s.Count)} us | carrier {Micros(s.Carrier, s.Count)} us | refresh {Micros(s.Refresh, s.Count)} us | peak {Micros(s.Peak)} us");
                    if (s.StatReads > 0)
                        Plugin.LogSource.LogInfo($"EFB firearm refresh breakdown: {(Route)i} | stats {Micros(s.Read, s.StatReads)} us ({s.BatchedReads}/{s.StatReads} batched) | native apply {Micros(s.Applied, s.Applies)} us ({s.Applies} calls) | automatic interval {Micros(s.Interval, s.Intervals)} us ({s.Intervals} calls)");
                }
                foreach (var pair in Effects.OrderByDescending(p => p.Value.Observe + p.Value.Carrier + p.Value.Refresh).Take(8))
                {
                    var s = pair.Value;
                    Plugin.LogSource.LogInfo($"EFB firearm signature timing: {pair.Key.ShortName} ({pair.Key.RuntimeKey}) | {s.Count} samples | average {Micros(s.Observe + s.Carrier + s.Refresh, s.Count)} us | peak {Micros(s.Peak)} us");
                }
            }
            finally { Array.Clear(Routes, 0, Routes.Length); Effects.Clear(); }
        }
    }
}
