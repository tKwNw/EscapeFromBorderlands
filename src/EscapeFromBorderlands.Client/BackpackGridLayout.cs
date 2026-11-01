using System;
using System.Collections.Generic;

namespace EscapeFromBorderlands.Client;

internal static class BackpackGridLayout
{
    // 7 cell columns with space for the usual inter-pocket gaps. The visible
    // panel can impose a smaller budget, but a wide display never grows this.
    internal const float MaximumWidth = 457f;

    internal sealed class Wrapped
    {
        internal Placement[] Positions = Array.Empty<Placement>();
        internal float[] Scales = Array.Empty<float>();
        internal float Width, Height;
    }

    internal static Wrapped Wrap(Placement[] original, float[] widths, float[] heights, float limit, float gap, int originalCount = -1, bool sidecar = false)
    {
        if (original.Length != widths.Length || widths.Length != heights.Length || limit <= 0f ||
            float.IsNaN(limit) || float.IsInfinity(limit) || gap < 0f)
            throw new ArgumentException("Invalid compartment layout bounds.");
        limit = Math.Min(MaximumWidth, limit);
        var result = new Wrapped { Positions = (Placement[])original.Clone(), Scales = new float[widths.Length] };
        for (var i = 0; i < widths.Length; i++)
        {
            if (widths[i] <= 0 || heights[i] <= 0 || float.IsNaN(widths[i]) || float.IsNaN(heights[i]) ||
                float.IsInfinity(widths[i]) || float.IsInfinity(heights[i])) throw new ArgumentException("Invalid compartment dimensions.");
            result.Scales[i] = 1f;
            result.Width = Math.Max(result.Width, original[i].X + widths[i]);
            result.Height = Math.Max(result.Height, original[i].Y + heights[i]);
        }
        if (result.Width <= limit) return result;

        var count = originalCount < 0 ? original.Length : Math.Min(originalCount, original.Length);
        var groups = new List<Group>();
        // Native pockets sharing a horizontal span form a stack. Preserve that
        // complete stack rather than separating its lower pocket into a new row.
        for (var i = 0; i < original.Length; i++)
        {
            var group = new Group(); group.Items.Add(i); group.Include(original[i], widths[i], heights[i]);
            groups.Add(group);
        }
        for (var i = 0; i < groups.Count; i++)
        {
            if (groups[i].Items[0] >= count) continue;
            for (var j = i + 1; j < groups.Count; j++)
            {
                if (groups[j].Items[0] >= count || groups[i].Right <= groups[j].Left + .5f || groups[j].Right <= groups[i].Left + .5f) continue;
                foreach (var index in groups[j].Items)
                { groups[i].Items.Add(index); groups[i].Include(original[index], widths[index], heights[index]); }
                groups.RemoveAt(j); j = i;
            }
        }
        groups.Sort((a, b) => a.Top != b.Top ? a.Top.CompareTo(b.Top) : a.Left.CompareTo(b.Left));
        if (sidecar && count < original.Length)
        {
            // Keep the native body stack first and its restricted gun sleeve
            // beside it when possible. Small side pockets can sit below the body.
            var body = groups[0]; var area = -1f;
            foreach (var group in groups)
            {
                if (group.Items[0] >= count) continue;
                float value = 0f; foreach (var index in group.Items) value += widths[index] * heights[index];
                if (value <= area) continue; body = group; area = value;
            }
            groups.Remove(body); groups.Insert(0, body);
            var next = 1;
            for (var i = 1; i < groups.Count; i++)
            {
                if (groups[i].Items[0] < count) continue;
                var sleeve = groups[i]; groups.RemoveAt(i); groups.Insert(next++, sleeve);
            }
        }
        float x = 0f, y = 0f, rowHeight = 0f;
        var columns = new List<Column>();
        result.Width = result.Height = 0f;
        foreach (var group in groups)
        {
            var scale = Math.Min(1f, limit / (group.Right - group.Left));
            var width = (group.Right - group.Left) * scale; var height = (group.Bottom - group.Top) * scale;
            float px, py;
            if (sidecar)
            {
                Column? column = null;
                // Start neighbouring columns first; after reaching the limit,
                // append a fitting group to the shortest existing column.
                if (x + width <= limit + .001f)
                { column = new Column { X = x, Width = width }; columns.Add(column); x += width + gap; }
                else
                    foreach (var candidate in columns)
                        if (width <= candidate.Width + .001f && (column is null || candidate.Bottom < column.Bottom)) column = candidate;
                if (column is null)
                {
                    // An unusually wide later group gets a full row of its own.
                    float bottom = 0f; foreach (var candidate in columns) bottom = Math.Max(bottom, candidate.Bottom);
                    px = 0f; py = bottom; x = limit + gap;
                    foreach (var candidate in columns) candidate.Bottom = bottom + height + gap;
                }
                else { px = column.X; py = column.Bottom; column.Bottom += height + gap; }
            }
            else
            {
                if (x > 0f && x + width > limit + .001f) { x = 0f; y += rowHeight + gap; rowHeight = 0f; }
                px = x; py = y; x += width + gap; rowHeight = Math.Max(rowHeight, height);
            }
            foreach (var index in group.Items)
            {
                result.Positions[index] = new Placement(px + (original[index].X - group.Left) * scale,
                    py + (original[index].Y - group.Top) * scale);
                result.Scales[index] = scale;
            }
            result.Width = Math.Max(result.Width, px + width); result.Height = Math.Max(result.Height, py + height);
        }
        return result;
    }

    private sealed class Group
    {
        internal readonly List<int> Items = new();
        internal float Left = float.MaxValue, Top = float.MaxValue, Right, Bottom;
        internal void Include(Placement position, float width, float height)
        { Left = Math.Min(Left, position.X); Top = Math.Min(Top, position.Y); Right = Math.Max(Right, position.X + width); Bottom = Math.Max(Bottom, position.Y + height); }
    }
    private sealed class Column { internal float X, Width, Bottom; }

    internal readonly struct Placement
    {
        internal Placement(float x, float y) { X = x; Y = y; }
        internal float X { get; }
        internal float Y { get; }
    }
    internal readonly struct Frame
    {
        internal Frame(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
        internal float X { get; }
        internal float Y { get; }
        internal float Width { get; }
        internal float Height { get; }
    }

    internal static Placement[] Preserve(Frame[] original, float[] widths, float[] heights, float gap, bool sidecar)
    {
        if (original.Length == 0 || widths.Length != heights.Length || widths.Length < original.Length)
            throw new ArgumentException("Missing original backpack compartments.");
        var output = new Placement[widths.Length];
        for (var i = 0; i < original.Length; i++)
            output[i] = new Placement(original[i].X, original[i].Y);
        // Preserve the native left/right and above/below relationships for
        // every pocket. Flat Pack grows all pockets, so shifting neighbors
        // only by the first pocket's growth leaves later pockets overlapping.
        // Repeated relaxation follows these acyclic spatial relationships
        // even when native grid indices run in a different order.
        for (var pass = 0; pass < original.Length; pass++)
        {
            var changed = false;
            for (var i = 0; i < original.Length; i++)
                for (var j = 0; j < original.Length; j++)
                {
                    if (i == j) continue;
                    var a = original[i]; var b = original[j];
                    var x = output[j].X; var y = output[j].Y;
                    if (b.X >= a.X + a.Width - 1f)
                        x = Math.Max(x, output[i].X + widths[i] + Math.Max(gap, b.X - a.X - a.Width));
                    if (b.Y >= a.Y + a.Height - 1f)
                        y = Math.Max(y, output[i].Y + heights[i] + Math.Max(gap, b.Y - a.Y - a.Height));
                    if (x == output[j].X && y == output[j].Y) continue;
                    output[j] = new Placement(x, y); changed = true;
                }
            if (!changed) break;
        }
        float right = 0f, bottom = 0f;
        for (var i = 0; i < original.Length; i++)
        { right = Math.Max(right, output[i].X + widths[i]); bottom = Math.Max(bottom, output[i].Y + heights[i]); }
        if (sidecar)
        {
            for (var i = original.Length; i < widths.Length; i++)
            { output[i] = new Placement(right + gap, 0f); right += gap + widths[i]; }
        }
        else
        {
            var y = bottom + gap;
            for (var i = original.Length; i < widths.Length; i += 2)
            {
                output[i] = new Placement(0f, y);
                if (i + 1 < widths.Length) output[i + 1] = new Placement(widths[i] + gap, y);
                y += Math.Max(heights[i], i + 1 < heights.Length ? heights[i + 1] : 0f) + gap;
            }
        }
        return output;
    }
}
