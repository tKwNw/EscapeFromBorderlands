using System.Diagnostics;
using System.Globalization;

namespace EscapeFromBorderlands.Client;

// Private test: recording does not format messages or write logs during play.
internal struct RaidUpdateTrace
{
    private static readonly bool DiagnosticRecording = false;

    private static readonly double MillisecondsPerTick = 1000d / Stopwatch.Frequency;
    private static readonly (string Phase, int Frame, double Milliseconds)[] Records = new (string, int, double)[64];
    private static int Count;
    private long _started, _last;
    private bool _reported;

    internal static RaidUpdateTrace Begin()
    {
        if (!DiagnosticRecording) return default;

        if (Count == Records.Length) return default;
        var now = Stopwatch.GetTimestamp();
        return new RaidUpdateTrace { _started = now, _last = now };
    }
    internal void Phase(string name)
    {
        if (_started == 0 || Count == Records.Length) return;
        var now = Stopwatch.GetTimestamp();
        var milliseconds = (now - _last) * MillisecondsPerTick;
        _last = now;
        if (milliseconds < 8d) return;
        Add(name, milliseconds);
        _reported = true;
    }
    internal void Finish(string combined = "combined update")
    {
        if (_started == 0 || _reported || Count == Records.Length) return;
        var milliseconds = (Stopwatch.GetTimestamp() - _started) * MillisecondsPerTick;
        if (milliseconds >= 16d) Add(combined, milliseconds);
    }
    private static void Add(string phase, double milliseconds) =>
        Records[Count++] = (phase, UnityEngine.Time.frameCount, milliseconds);
    internal static void Reset()
    {
        var count = Count; Count = 0;
        for (var i = 0; i < count; i++)
        {
            var record = Records[i]; Records[i] = default;
            Plugin.LogSource.LogInfo($"EFB update timing: frame {record.Frame} | {record.Phase} | {record.Milliseconds.ToString("F2", CultureInfo.InvariantCulture)} ms");
        }
    }
}
