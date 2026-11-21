using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using EFT;
using EFT.Ballistics;

namespace EscapeFromBorderlands.Client;

// Inert interfaces retained by the release gameplay paths. There are no
// recorders, timing buffers, logging sinks or file exports in this source.
internal struct DiagnosticPathTrace
{
    internal enum Path { HumanMovement, BotMovement, Grenade, Container, GrenadeFactory, Navigation, SessionLayout, ScrollAlignment, HitImpact, GrenadeFlash }
    [Conditional("EFB_PERFORMANCE_DIAGNOSTICS")] internal static void Start(ref DiagnosticPathTrace trace, Path path) { }
    [Conditional("EFB_PERFORMANCE_DIAGNOSTICS")] internal void Phase(int stage) { }
    [Conditional("EFB_PERFORMANCE_DIAGNOSTICS")] internal void Finish() { }
    [Conditional("EFB_PERFORMANCE_DIAGNOSTICS")] internal static void Reset() { }
}
internal static class NativeFrameTrace
{
    [Conditional("EFB_PERFORMANCE_DIAGNOSTICS")] internal static void Observe() { }
    [Conditional("EFB_PERFORMANCE_DIAGNOSTICS")] internal static void Reset() { }
}
internal static class DiagnosticOutput
{
    [Conditional("EFB_PERFORMANCE_DIAGNOSTICS")] internal static void Export(string kind, Action<TextWriter> write) { }
    [Conditional("EFB_PERFORMANCE_DIAGNOSTICS")] internal static void Info(string message) { }
    [Conditional("EFB_PERFORMANCE_DIAGNOSTICS")] internal static void Warning(string message) { }
}
internal struct RaidUpdateTrace
{
    internal static RaidUpdateTrace Begin() => default;
    [Conditional("EFB_PERFORMANCE_DIAGNOSTICS")] internal void Phase(string name) { }
    [Conditional("EFB_PERFORMANCE_DIAGNOSTICS")] internal void Finish(string combined = "combined update") { }
    internal static void Reset() { }
}
internal struct WeaponPerformanceTrace
{
    internal enum Route { Neutral, Gear, Legendary, Restore }
    internal static WeaponPerformanceTrace Begin() => default;
    internal long Stamp() => 0;
    [Conditional("EFB_PERFORMANCE_DIAGNOSTICS")] internal void Observe() { }
    [Conditional("EFB_PERFORMANCE_DIAGNOSTICS")] internal void Carrier() { }
    [Conditional("EFB_PERFORMANCE_DIAGNOSTICS")] internal void StatPart(int part, long start) { }
    [Conditional("EFB_PERFORMANCE_DIAGNOSTICS")] internal void ReadStats(long start, bool batched) { }
    [Conditional("EFB_PERFORMANCE_DIAGNOSTICS")] internal void Apply(long start) { }
    [Conditional("EFB_PERFORMANCE_DIAGNOSTICS")] internal void Interval(long start) { }
    [Conditional("EFB_PERFORMANCE_DIAGNOSTICS")] internal void Complete(Route route, VariantDefinition? definition) { }
    internal static void Reset() { }
}
internal static class GrenadeSpikeTrace
{
    internal static Timing Measure(LegendaryGrenadeEffects.Payload payload, string path) => default;
    internal static Timing MeasureWeapon(VariantDefinition? definition, string path) => default;
    internal readonly struct Timing : IDisposable { public void Dispose() { } }
    internal static void Flush() { }
    internal static void Reset() { }
}
internal static class HeadwearDiagnostics
{
    internal static void Reset() { }
    internal static void Damage(Headwear.State? s, DamageInfo hit, string stage, float amount) { }
    // Retain the original release-path lookup even though recording is absent.
    internal static void Rows(List<EffectStatusModel.Status> statuses) { _ = Headwear.Get(Headwear.Local); }
    internal static void Hud(string stage, string value) { _ = Headwear.Get(Headwear.Local); }
}
