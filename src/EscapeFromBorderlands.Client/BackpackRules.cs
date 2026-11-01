using System;

namespace EscapeFromBorderlands.Client;

internal static class BackpackRules
{
    internal static float WalkWeightLimit(float nativeLimit) => .75f + .25f * Math.Max(0f, Math.Min(1f, nativeLimit));
    internal static bool IsWoods(string? location) => string.Equals(location, "woods", StringComparison.OrdinalIgnoreCase)
        || string.Equals(location, "5704e3c2d2720bac5b8b4567", StringComparison.OrdinalIgnoreCase);
    internal static float RushHour(bool sprinting) => sprinting ? 1.25f : 1f;
    internal static bool FreightBrake(bool wasSprinting, float previousSeconds, bool sprinting) => wasSprinting && !sprinting && previousSeconds >= 5f;
    internal static float Pilgrimage(bool sprinting, float lockoutSeconds) => !sprinting && lockoutSeconds <= 0f ? 1.25f : 1f;
    internal static float LegCapacity(string key) => key == "BP01-3" ? 1.4f : 1f;
    internal static float Ergonomics(string key, bool daytime, bool milestone) => key == "BP24-1" && daytime ? 1.25f : key == "BP31-2" && milestone ? 1.2f : 1f;
    internal static float SprintCost(string key) => key == "BP44-3" ? 2f : 1f;
    internal static float IncomingDamage(string key) => key is "BP07-2" or "BP37-1" ? .85f : 1f;
    internal static float JumpImpulse(string key) => key == "BP07-2" ? (float)Math.Sqrt(.75f) : 1f;
}
