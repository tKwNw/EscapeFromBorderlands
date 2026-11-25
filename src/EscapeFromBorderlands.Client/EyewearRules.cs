using EFT.Ballistics;
using System;

namespace EscapeFromBorderlands.Client;

// Pure rules shared by the native runtime and offline boundary tests.
internal static class EyewearRules
{
    internal static bool BotCanUse(string key) => key is "EW001" or "EW004" or "EW011" or "EW012" or "EW013" or
        "EW016" or "EW019" or "EW022" or "EW023" or "EW024" or "EW025" or "EW026" or "EW027" or "EW028" or "EW030" or "EW031" or "EW038";
    internal static int Charged(int charges, float deadline, float now) => charges > 0 || now >= deadline ? 1 : 0;
    internal static float PauseRecharge(int charges, float deadline, float now, float period) =>
        charges > 0 ? period : Math.Max(0, deadline - now);
    internal static bool NeedsMovementRead(string key, float hip, float movingSway, bool victoryActive, bool reserveActive) =>
        hip > 0 || movingSway > 0 || (key switch
        {
            "EW005" => victoryActive,
            "EW020" => reserveActive,
            _ => true
        });
    internal static bool TravelValid(float length, float elapsed) => !float.IsNaN(length) && !float.IsInfinity(length) && length >= 0 &&
        elapsed >= 0 && elapsed <= 1 && length <= Math.Max(2, elapsed * 18);
    internal static int VictoryMeters(ref float distance, ref int rewarded, float length)
    {
        if (length < 0 || float.IsNaN(length) || float.IsInfinity(length) || rewarded >= 50) return 0;
        distance += length;
        var meters = Math.Min(50 - rewarded, (int)distance);
        if (meters > 0) { distance -= meters; rewarded += meters; }
        return meters;
    }
    internal static float HealShare(float budget, float missing, float partMissing) => missing > 0 && partMissing > 0 ?
        Math.Min(partMissing, Math.Max(0, Math.Min(budget, missing)) * partMissing / missing) : 0;
}
