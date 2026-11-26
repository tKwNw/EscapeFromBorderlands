using System;

namespace EscapeFromBorderlands.Client;

// View-local results plus a bounded numeric-only cache for fresh views.
// Compare every geometry input; never retain items, views or Unity objects.
internal sealed class BackpackLayoutPlan
{
    private const int SharedCapacity = 32, MaximumPockets = 64;
    [ThreadStatic] private static BackpackLayoutPlan?[]? Shared;
    [ThreadStatic] private static int Next;
    private readonly float[] _widths, _heights;
    private BackpackGridLayout.Frame[] _source = Array.Empty<BackpackGridLayout.Frame>();
    private bool _dirty = true, _sidecar;
    internal BackpackGridLayout.Placement[] Positions { get; private set; } = Array.Empty<BackpackGridLayout.Placement>();
    internal float Width
    {
        get; private set;
    }
    internal float Height
    {
        get; private set;
    }
    internal int Count => _widths.Length;
    internal BackpackLayoutPlan(int count)
    {
        _widths = new float[count];
        _heights = new float[count];
    }
    internal void SetSize(int index, float width, float height)
    {
        if (_widths[index] == width && _heights[index] == height)
            return;
        _widths[index] = width;
        _heights[index] = height;
        _dirty = true;
    }
    internal bool Resolve(BackpackGridLayout.Frame[] source, bool sidecar)
    {
        var changed = _source.Length != source.Length;
        for (var i = 0; !changed && i < source.Length; i++)
            changed = _source[i].X != source[i].X || _source[i].Y != source[i].Y ||
                _source[i].Width != source[i].Width || _source[i].Height != source[i].Height;
        if (!_dirty && !changed && _sidecar == sidecar)
            return false;
        if (changed)
            _source = (BackpackGridLayout.Frame[])source.Clone();
        if (Count <= MaximumPockets && Shared is not null)
            foreach (var entry in Shared)
                if (entry is not null && entry.Matches(source, _widths, _heights, sidecar))
                {
                    if (Positions.Length != Count)
                        Positions = new BackpackGridLayout.Placement[Count];
                    Array.Copy(entry.Positions, Positions, Count);
                    Width = entry.Width;
                    Height = entry.Height;
                    _sidecar = sidecar;
                    _dirty = false;
                    return true;
                }
        Positions = BackpackGridLayout.Preserve(source, _widths, _heights, 4f, sidecar);
        Width = Height = 0f;
        for (var i = 0; i < Count; i++)
        {
            Width = Math.Max(Width, Positions[i].X + _widths[i]);
            Height = Math.Max(Height, Positions[i].Y + _heights[i]);
        }
        _sidecar = sidecar;
        _dirty = false;
        if (Count <= MaximumPockets)
        {
            Shared ??= new BackpackLayoutPlan?[SharedCapacity];
            var entry = new BackpackLayoutPlan(Count)
            {
                _source = (BackpackGridLayout.Frame[])source.Clone(),
                Positions = (BackpackGridLayout.Placement[])Positions.Clone(),
                Width = Width,
                Height = Height,
                _sidecar = sidecar,
                _dirty = false
            };
            Array.Copy(_widths, entry._widths, Count);
            Array.Copy(_heights, entry._heights, Count);
            Shared[Next] = entry;
            Next = (Next + 1) % SharedCapacity;
        }
        return true;
    }
    internal static void ResetShared()
    {
        Shared = null;
        Next = 0;
    }
    private bool Matches(BackpackGridLayout.Frame[] source, float[] widths, float[] heights, bool sidecar)
    {
        if (_sidecar != sidecar || Count != widths.Length || _source.Length != source.Length)
            return false;
        for (var i = 0; i < source.Length; i++)
            if (_source[i].X != source[i].X || _source[i].Y != source[i].Y ||
                _source[i].Width != source[i].Width || _source[i].Height != source[i].Height)
                return false;
        for (var i = 0; i < Count; i++)
            if (_widths[i] != widths[i] || _heights[i] != heights[i])
                return false;
        return true;
    }
}
