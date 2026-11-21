using InventoryTraversal = EFT.InventoryLogic.ItemExtensions;
using System;
using EFT.InventoryLogic;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

internal static class GrenadeInventoryPath
{
    private static bool? NativeTraversal;
    internal static void Reset() => NativeTraversal = null;
    internal static void Prepare()
    {
        if (NativeTraversal.HasValue) return;
        try
        {
            foreach (var method in Harmony.GetAllPatchedMethods())
            {
                var type = method.DeclaringType;
                while (type?.DeclaringType is { } parent) type = parent;
                if (type is null || type != typeof(InventoryTraversal) &&
                    !typeof(Item).IsAssignableFrom(type) && !typeof(IContainer).IsAssignableFrom(type)) continue;
                if (Harmony.GetPatchInfo(method) is not { } patches) continue;
                foreach (var owner in patches.Owners)
                    if (owner != Plugin.PluginGuid) { NativeTraversal = false; return; }
            }
            NativeTraversal = true;
        }
        catch { NativeTraversal = false; }
    }
    internal static bool Contains(CompoundItem equipment, Item source)
    {
        Prepare();
        return NativeTraversal == true && ProvesPath(equipment, source) ||
            BackpackNative.ContainsId(equipment.GetAllItems(), source.Id);
    }
    // A parent pointer alone is insufficient. Check both forward links at
    // every level, using exactly the containers/items traversed by GetAllItems.
    // Unusual, stale, cyclic or deep paths retain the full native search.
    private static bool ProvesPath(CompoundItem equipment, Item source)
    {
        var item = source;
        for (var depth = 0; depth < 32; depth++)
        {
            if (ReferenceEquals(item, equipment)) return true;
            if (item.GetType().Assembly != typeof(Item).Assembly || item.CurrentAddress is not { } address ||
                address.GetType().Assembly != typeof(Item).Assembly) return false;
            if (address.Container is not { } container ||
                container.GetType().Assembly != typeof(Item).Assembly ||
                container.ParentItem is not CompoundItem parent ||
                parent.GetType().Assembly != typeof(Item).Assembly || ReferenceEquals(parent, item)) return false;
            var found = false;
            foreach (var child in container.Items) if (ReferenceEquals(child, item)) { found = true; break; }
            if (!found) return false;
            found = false;
            foreach (var candidate in parent.Containers) if (ReferenceEquals(candidate, container)) { found = true; break; }
            if (!found) return false;
            item = parent;
        }
        return false;
    }
}
