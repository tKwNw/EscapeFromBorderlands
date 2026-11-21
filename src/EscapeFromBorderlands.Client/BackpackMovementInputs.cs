using System;
using EFT;
using EFT.InventoryLogic;

namespace EscapeFromBorderlands.Client;

internal static class BackpackMovementInputs
{
    internal static bool TryMainGridFull(Item bag, out bool full)
    {
        full = false;
        // Foreign layouts retain their reflective GetFreeSpace/FreeSpace contract.
        if (bag is not CompoundItem compound || bag.GetType().Assembly != typeof(Item).Assembly) return false;
        var grids = compound.Grids;
        if (grids is null || grids.Length == 0 || grids[0] is null) return true;
        var grid = grids[0];
        if (grid.GetType() != typeof(Grid)) return false;
        // Native grids in both supported versions have neither free-space API.
        // Preserve the original live size sum (including int overflow), without
        // reflective property boxing or Cast/OfType/Sum iterator allocations.
        var capacity = (float)grid.GridWidth * grid.GridHeight;
        var cells = 0;
        foreach (var item in grid.Items)
        {
            if (item is null) continue;
            var size = item.CalculateCellSize();
            cells = checked(cells + unchecked(size.X * size.Y));
        }
        full = capacity > 0f && cells >= capacity;
        return true;
    }

    internal static DateTime Date(GameDateTime clock) => clock.GetType() == typeof(GameDateTime)
        ? clock.Calculate() : (DateTime)(Native.Call(clock, "Calculate") ?? DateTime.MinValue);
}
