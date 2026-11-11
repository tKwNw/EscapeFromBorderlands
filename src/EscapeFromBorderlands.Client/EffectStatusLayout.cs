using System;
using System.Collections.Generic;

namespace EscapeFromBorderlands.Client;

internal static class EffectStatusLayout
{
    internal const float Width = 520f, TextWidth = 488f, Gap = 5f;
    internal static float Advance(float height, bool hasNext) => Math.Max(25f, height) + (hasNext ? Gap : 0f);
    internal static float[] Offsets(IReadOnlyList<float> heights, out float total)
    {
        var offsets = new float[heights.Count]; total = 0f;
        for (var i = 0; i < heights.Count; i++)
        {
            offsets[i] = total;
            total += Advance(heights[i], i + 1 < heights.Count);
        }
        return offsets;
    }
}
