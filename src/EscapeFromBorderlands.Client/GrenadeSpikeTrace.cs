using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace EscapeFromBorderlands.Client;

// Private test diagnostics. Time only executed EFB effects, and queue just
// slow paths so logging cannot add to the detonation or flash callback.
internal static class GrenadeSpikeTrace
{
    private static readonly bool DiagnosticRecording = false;
    private const double MinimumMilliseconds = 8d;
    private const int Limit = 24;
    private static readonly double TickToMilliseconds = 1000d / Stopwatch.Frequency;
    private static readonly Queue<(string Name, string Key, string Path, double Elapsed, bool Weapon)> Pending = new(Limit);
    private static int Recorded;

    internal static Timing Measure(LegendaryGrenadeEffects.Payload payload, string path) =>
        DiagnosticRecording && Recorded < Limit ? new Timing(payload.Roll.Definition, path, Stopwatch.GetTimestamp(), false) : default;

    internal static Timing MeasureWeapon(VariantDefinition? definition, string path) =>
        definition is not null && DiagnosticRecording && Recorded < Limit ? new Timing(definition, path, Stopwatch.GetTimestamp(), true) : default;

    internal readonly struct Timing : IDisposable
    {
        private readonly VariantDefinition? _definition;
        private readonly bool _weapon;
        private readonly string? _path;
        private readonly long _start;

        internal Timing(VariantDefinition definition, string path, long start, bool weapon)
        { _definition = definition; _path = path; _start = start; _weapon = weapon; }

        public void Dispose()
        {
            if (_definition is null || Recorded >= Limit) return;
            var elapsed = (Stopwatch.GetTimestamp() - _start) * TickToMilliseconds;
            if (elapsed < MinimumMilliseconds) return;
            Pending.Enqueue((_definition.ShortName, _definition.RuntimeKey, _path!, elapsed, _weapon));
            Recorded++;
        }
    }

    internal static void Flush()
    {
        while (Pending.Count > 0)
        {
            var entry = Pending.Dequeue();
            var kind = entry.Weapon ? "effect" : "grenade";
            Plugin.LogSource.LogInfo($"EFB {kind} timing: {entry.Name} ({entry.Key}) | {entry.Path} | {entry.Elapsed.ToString("F2", CultureInfo.InvariantCulture)} ms");
        }
    }

    internal static void Reset()
    {
        try { Flush(); }
        finally { Pending.Clear(); Recorded = 0; }
    }
}
