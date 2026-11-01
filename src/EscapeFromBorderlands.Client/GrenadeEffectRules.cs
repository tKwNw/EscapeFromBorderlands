using System;
using System.Collections.Generic;

namespace EscapeFromBorderlands.Client;

// Shared decisions used by the native adapters and behavior tests.
internal static class GrenadeEffectRules
{
    internal static int FlashPulseCount(string key) => key == "GR66" ? 2 : key == "GR67" ? 9 : 1;
    internal static float FlashPulseInterval(string key) => key == "GR67" ? .25f : 1f;
    internal static bool ImpactFlash(string key) => key == "GR66";
    internal static List<int> BonusPool(string category) => category == "smoke" ? new() { 3, 5, 6, 9, 10, 11 }
        : category == "flash" ? new() { 3, 5, 6, 7, 8 } : new() { 0, 1, 2, 3, 4, 5, 6, 12 };
    internal static float BlastMultiplier(string key, float distance, bool leg, bool directTarget, bool pistolOnly) => key switch
    {
        "GR01" when distance <= 3f => 1.75f,
        "GR24" when leg => 1.75f,
        "GR27" when directTarget => 2f,
        "GR36" when pistolOnly => 1.6f,
        _ => 1f
    };
    internal static string Countdown(double seconds)
    {
        var whole = (int)Math.Ceiling(Math.Max(0, seconds));
        return (whole / 60) + ":" + (whole % 60).ToString("00");
    }
    internal static bool ShowCharges(int remaining, int capacity, bool held) =>
        remaining > 0 && (held || remaining < capacity);
    internal static string ChargeText(int remaining, int capacity, bool held, double seconds)
    {
        var showCharges = ShowCharges(remaining, capacity, held);
        return (showCharges ? remaining + "/" + capacity + " charges" : "") +
            (seconds > 0 ? (showCharges ? " · " : "") + "+1 charge in " + Countdown(seconds) : "");
    }
}
