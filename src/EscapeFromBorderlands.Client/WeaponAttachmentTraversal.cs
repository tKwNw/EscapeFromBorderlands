using System;
using System.Collections.Generic;
using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

// Reproduce native FlattenWithoutArray order without constructing a LINQ tree
// for every occupied slot. Only type metadata survives a poll, never item data.
internal static class WeaponAttachmentTraversal
{
    private readonly struct Shape
    {
        internal readonly bool Native, DirectSlots, NoSlots;
        internal Shape(Type type)
        {
            Native = type.Assembly == typeof(Weapon).Assembly;
            DirectSlots = Native && typeof(CompoundItem).IsAssignableFrom(type) &&
                type.GetProperty("Containers")?.GetGetMethod(true)?.DeclaringType == typeof(CompoundItem);
            // The native magazine getter yields only its StackSlot, which the
            // native attachment selector excludes. Cylinder overrides do not qualify.
            NoSlots = Native && type.GetProperty("Containers")?.GetGetMethod(true)?.DeclaringType == typeof(MagazineItemClass);
        }
    }
    [ThreadStatic] private static Dictionary<Type, Shape>? Shapes;
    [ThreadStatic] private static List<Slot>? ExtraSlots;
    [ThreadStatic] private static Type? LastNativeType;
    [ThreadStatic] private static bool LastNativeResult;

    private static Shape GetShape(Type type)
    {
        var shapes = Shapes ??= new Dictionary<Type, Shape>();
        if (!shapes.TryGetValue(type, out var shape))
            shapes.Add(type, shape = new Shape(type));
        return shape;
    }
    internal static bool NativeType(Type type)
    {
        // Type/assembly identity cannot change during a raid. Retain metadata
        // only, while all slot, item and numeric inputs remain live.
        if (ReferenceEquals(type, LastNativeType))
            return LastNativeResult;
        var result = type.Assembly == typeof(Weapon).Assembly;
        LastNativeResult = result;
        LastNativeType = type;
        return result;
    }
    // One metadata lookup for the input walk, instead of testing native type,
    // magazine shape and direct-slot shape through three separate lookups.
    internal static int InputShape(CompoundItem item)
    {
        var shape = GetShape(item.GetType());
        return !shape.Native ? -1 : shape.NoSlots ? 0 : shape.DirectSlots ? 1 : -1;
    }
    internal static bool CanSnapshot(CompoundItem item) => GetShape(item.GetType()).DirectSlots;
    internal static bool HasNoChildSlots(CompoundItem item) => GetShape(item.GetType()).NoSlots;

    internal static bool Compatible()
    {
        foreach (var method in Harmony.GetAllPatchedMethods())
        {
            var type = method.DeclaringType;
            // Include compiler-generated selectors and the recursive flattener.
            while (type?.DeclaringType is { } parent)
                type = parent;
            var nativeContainerGetter = method.Name == "get_Containers" &&
                method.DeclaringType is { } declaring && typeof(GClass3248).IsAssignableFrom(declaring);
            if (type != typeof(CompoundItem) && type != typeof(Slot) && type != typeof(GClass1518) &&
                !(type == typeof(Weapon) && method.DeclaringType != type) && !nativeContainerGetter)
                continue;
            if (Harmony.GetPatchInfo(method) is not { } patches)
                continue;
            foreach (var owner in patches.Owners)
                if (owner != Plugin.PluginGuid)
                    return false;
        }
        return true;
    }

    internal static bool TryRead(Weapon weapon, List<Mod> result)
    {
        try
        {
            if (!Walk(weapon.Slots, result, 0))
                return false;
            // Native Weapon.AllSlots appends chambers after the recursive slots.
            foreach (var chamber in weapon.Chambers)
                if (!Add(chamber, result))
                    return false;
            return true;
        }
        finally
        {
            ExtraSlots?.Clear();
        }
    }

    private static bool Add(Slot slot, List<Mod> result)
    {
        if (!NativeType(slot.GetType()))
            return false;
        if (slot.ContainedItem is not Mod mod)
            return true;
        if (!NativeType(mod.GetType()))
            return false;
        result.Add(mod);
        return true;
    }

    private static bool Walk(IReadOnlyList<Slot> slots, List<Mod> result, int depth)
    {
        if (depth > 64)
            return false;
        var count = slots.Count;
        // Native order is all siblings, then each sibling's subtree in order.
        for (var i = 0; i < count; i++)
            if (!Add(slots[i], result))
                return false;
        for (var i = 0; i < count; i++)
            if (!Children(slots[i].ContainedItem, result, depth + 1))
                return false;
        return true;
    }

    private static bool Children(Item? item, List<Mod> result, int depth)
    {
        if (item is not GClass3248 collection)
            return true;
        var shape = GetShape(collection.GetType());
        if (!shape.Native || depth > 64)
            return false;
        if (shape.DirectSlots && collection is CompoundItem compound)
            return Walk(compound.Slots, result, depth);

        // Magazines, launchers and other native overrides may expose additional
        // slots. Keep their accessor, filtering exactly as the native selector.
        var extra = ExtraSlots ??= new List<Slot>(16);
        var start = extra.Count;
        foreach (var container in collection.Containers)
            if (container is Slot slot)
                extra.Add(slot);
        var end = extra.Count;
        for (var i = start; i < end; i++)
            if (!Add(extra[i], result))
                return false;
        for (var i = start; i < end; i++)
            if (!Children(extra[i].ContainedItem, result, depth + 1))
                return false;
        extra.RemoveRange(start, end - start);
        return true;
    }
}
