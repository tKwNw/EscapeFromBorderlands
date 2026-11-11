using System.Collections.Concurrent;
using System.Diagnostics;

namespace EscapeFromBorderlands.Server;

// Private test diagnostics: include conversion work and lock waits, not
// native generation time. Report cumulative totals at sparse milestones.
internal static class ServerPerformanceTrace
{
    private sealed class Counters
    {
        internal long Calls, Ticks, Items, Changed;
        internal int Slow;
    }

    private static readonly ConcurrentDictionary<string, Counters> Totals = new(StringComparer.Ordinal);
    private static Action<string>? Log;
    internal static void Bind(Action<string> logger) => Log = null;
    internal static Timing Measure(string stage) => Log is null ? default : new Timing(stage, Stopwatch.GetTimestamp());

    internal readonly struct Timing(string stage, long start) : IDisposable
    {
        public void Dispose() => Complete(0, 0);

        internal void Complete(int items, int changed)
        {
            if (start == 0 || Log is not { } log) return;
            var elapsed = Stopwatch.GetTimestamp() - start;
            var t = Totals.GetOrAdd(stage, static _ => new());
            var count = Interlocked.Increment(ref t.Calls);
            var ticks = Interlocked.Add(ref t.Ticks, elapsed);
            var totalItems = Interlocked.Add(ref t.Items, items);
            var totalChanged = Interlocked.Add(ref t.Changed, changed);
            var ms = elapsed * (1000d / Stopwatch.Frequency);
            if (count != 1 && (count & (count - 1)) != 0 &&
                (ms < 8d || Volatile.Read(ref t.Slow) >= 12 || Interlocked.Increment(ref t.Slow) > 12)) return;
            try
            {
                log(FormattableString.Invariant($"[Escape From Borderlands] EFB server timing: {stage} | {ms:F2} ms | {count} calls, {ticks * (1000d / Stopwatch.Frequency):F2} ms total | {totalItems} inputs, {totalChanged} changes"));
            }
            catch { /* Diagnostic output must not affect a native operation. */ }
        }
    }
}
