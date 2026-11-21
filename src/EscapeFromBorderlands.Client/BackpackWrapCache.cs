using System;

namespace EscapeFromBorderlands.Client;

// Each view owns its last result. A bounded numeric-only cache also reuses the
// solver across fresh views. Inputs and results never alias between views.
internal sealed class BackpackWrapCache
{
    private const int SharedCapacity = 32, MaximumPockets = 64;
    [ThreadStatic] private static BackpackWrapCache?[]? Shared;
    [ThreadStatic] private static int Next;
    private BackpackGridLayout.Placement[] Original = Array.Empty<BackpackGridLayout.Placement>();
    private float[] Widths = Array.Empty<float>(), Heights = Array.Empty<float>();
    private float Limit, Gap;
    private int OriginalCount;
    private bool Sidecar;
    private BackpackGridLayout.Wrapped? Result;
    internal BackpackGridLayout.Wrapped Read(BackpackGridLayout.Placement[] original, float[] widths, float[] heights,
        float limit, float gap, int originalCount, bool sidecar)
    {
        if (Matches(original, widths, heights, limit, gap, originalCount, sidecar)) return Result!;
        BackpackGridLayout.Wrapped? result = null;
        if (original.Length <= MaximumPockets && Shared is not null)
            foreach (var entry in Shared)
                if (entry is not null && entry.Matches(original, widths, heights, limit, gap, originalCount, sidecar))
                { result = Copy(entry.Result!); break; }
        var fresh = result is null;
        result ??= BackpackGridLayout.Wrap(original, widths, heights, limit, gap, originalCount, sidecar);
        Capture(original, widths, heights, limit, gap, originalCount, sidecar, result);
        if (fresh && original.Length <= MaximumPockets)
        {
            Shared ??= new BackpackWrapCache?[SharedCapacity];
            var entry = new BackpackWrapCache();
            entry.Capture(original, widths, heights, limit, gap, originalCount, sidecar, Copy(result));
            Shared[Next] = entry; Next = (Next + 1) % SharedCapacity;
        }
        return result;
    }
    internal static void ResetShared() { Shared = null; Next = 0; }
    private static BackpackGridLayout.Wrapped Copy(BackpackGridLayout.Wrapped result) => new()
    { Positions = (BackpackGridLayout.Placement[])result.Positions.Clone(), Scales = (float[])result.Scales.Clone(), Width = result.Width, Height = result.Height };
    private bool Matches(BackpackGridLayout.Placement[] original, float[] widths, float[] heights, float limit, float gap, int originalCount, bool sidecar)
    {
        var same = Result is not null && Original.Length == original.Length && widths.Length == original.Length &&
            heights.Length == original.Length && Limit == limit && Gap == gap && OriginalCount == originalCount && Sidecar == sidecar;
        if (same)
            for (var i = 0; i < original.Length; i++)
                if (Original[i].X != original[i].X || Original[i].Y != original[i].Y || Widths[i] != widths[i] || Heights[i] != heights[i])
                { same = false; break; }
        return same;
    }
    private void Capture(BackpackGridLayout.Placement[] original, float[] widths, float[] heights,
        float limit, float gap, int originalCount, bool sidecar, BackpackGridLayout.Wrapped result)
    {
        if (Original.Length != original.Length)
        { Original = new BackpackGridLayout.Placement[original.Length]; Widths = new float[original.Length]; Heights = new float[original.Length]; }
        Array.Copy(original, Original, original.Length); Array.Copy(widths, Widths, widths.Length); Array.Copy(heights, Heights, heights.Length);
        Limit = limit; Gap = gap; OriginalCount = originalCount; Sidecar = sidecar;
        Result = result;
    }
}
