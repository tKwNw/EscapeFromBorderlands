using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EFT.InventoryLogic;
using UnityEngine;
namespace EscapeFromBorderlands.Client;
// Cache native traversal metadata and fixed sums, while validating every live
// slot/occupant and numeric input. Direct edits need no inventory notification.
// Legendary/gear postfixes still run on each getter; real shots bypass this.
internal static class WeaponAttachmentInputs
{
    private static ConditionalWeakTable<Weapon, Plan> Plans = new();
    // Live polling normally reads the same held weapon repeatedly. Keep one
    // plan reference to avoid repeated type/CWT lookup, never its live inputs.
    // A single immutable root/plan pair prevents mismatched observations; reset
    // drops this bounded strong reference along with the weak plan table.
    private static Plan? Recent;
    internal static void Reset()
    {
        Recent = null;
        Plans = new();
    }
    private sealed class Node
    {
        internal CompoundItem Owner = null!;
        internal IReadOnlyList<Slot> Slots = null!;
        internal Slot[]? ArraySlots;
        internal int Count;
    }
    // Read entries by reference, avoiding large struct copies on every poll
    // without adding one managed object per slot when rebuilding the plan.
    private struct Input
    {
        internal IReadOnlyList<Slot> Slots;
        internal Slot[]? ArraySlots;
        internal int Index;
        internal Slot Slot;
        internal Item? Item;
        internal Mod? Mod;
        internal bool Stock;
        internal float Recoil, Ergo;
        internal int Accuracy;
    }
    private sealed class Plan
    {
        internal readonly Weapon Weapon;
        internal Plan(Weapon weapon) => Weapon = weapon;
        internal readonly List<Node> Nodes = new();
        internal Input[] Inputs = Array.Empty<Input>();
        internal int InputCount;
        internal Slot[] Chambers = null!;
        internal bool Built, Ready, Folded;
        internal float BaseErgo;
        internal (float recoil, float ergo, float accuracy) Stats;

        private bool StructureValid(Weapon weapon)
        {
            if (!Built || !ReferenceEquals(Chambers, weapon.Chambers))
                return false;
            foreach (var node in Nodes)
                if (!ReferenceEquals(node.Slots, node.Owner.Slots) ||
                    (node.ArraySlots is { } slots ? slots.Length : node.Slots.Count) != node.Count)
                    return false;
            return true;
        }
        internal bool Build(Weapon weapon)
        {
            Built = Ready = false;
            Nodes.Clear();
            Array.Clear(Inputs, 0, InputCount);
            InputCount = 0;
            Chambers = weapon.Chambers;
            if (!Walk(weapon, 0))
                return false;
            for (var i = 0; i < Chambers.Length; i++)
                if (!Add(Chambers, i))
                    return false;
            Built = true;
            return true;
        }
        private bool Add(IReadOnlyList<Slot> slots, int index)
        {
            var slot = slots[index];
            if (!WeaponAttachmentTraversal.NativeType(slot.GetType()))
                return false;
            var item = slot.ContainedItem;
            var mod = item as Mod;
            if (mod is not null && !WeaponAttachmentTraversal.NativeType(mod.GetType()))
                return false;
            if (InputCount == Inputs.Length)
                Array.Resize(ref Inputs, Math.Max(4, Inputs.Length * 2));
            Inputs[InputCount++] = new Input { Slots = slots, ArraySlots = slots as Slot[], Index = index, Slot = slot, Item = item, Mod = mod, Stock = mod is Stock };
            return true;
        }
        private bool Walk(CompoundItem owner, int depth)
        {
            if (depth > 64)
                return false;
            IReadOnlyList<Slot> slots = owner.Slots;
            Nodes.Add(new Node { Owner = owner, Slots = slots, ArraySlots = slots as Slot[], Count = slots.Count });
            for (var i = 0; i < slots.Count; i++)
                if (!Add(slots, i))
                    return false;
            for (var i = 0; i < slots.Count; i++)
            {
                if (slots[i].ContainedItem is not ContainerCollection collection)
                    continue;
                if (collection is not CompoundItem child)
                    return false;
                var shape = WeaponAttachmentTraversal.InputShape(child);
                if (shape == 0)
                    continue;
                if (shape < 0 || !Walk(child, depth + 1))
                    return false;
            }
            return true;
        }
        internal bool TryRead(Weapon weapon, out (float recoil, float ergo, float accuracy) stats)
        {
            stats = default;
            if (!StructureValid(weapon))
                return false;
            var folded = weapon.Folded;
            var baseErgo = weapon.Template.Ergonomics;
            var changed = !Ready || folded != Folded || baseErgo != BaseErgo;
            for (var i = 0; i < InputCount; i++)
            {
                ref var input = ref Inputs[i];
                // Native accessors are verified before this plan is admitted.
                // Validate identity and numeric inputs in one traversal; never
                // reuse a sum after a partial read or a silent slot change.
                var currentSlot = input.ArraySlots is { } slots ? slots[input.Index] : input.Slots[input.Index];
                if (!ReferenceEquals(input.Slot, currentSlot) ||
                    !ReferenceEquals(input.Item, input.Slot.ContainedItem))
                {
                    Ready = false;
                    return false;
                }
                if (input.Mod is null)
                    continue;
                var template = input.Mod.Template;
                var recoil = template.Recoil;
                var ergo = template.Ergonomics;
                var accuracy = input.Mod.Accuracy;
                if (input.Recoil != recoil || input.Ergo != ergo || input.Accuracy != accuracy)
                {
                    changed = true;
                    input.Recoil = recoil;
                    input.Ergo = ergo;
                    input.Accuracy = accuracy;
                }
            }
            if (!changed)
            {
                stats = Stats;
                return true;
            }
            double r = 0, e = 0;
            var a = 0;
            Ready = false;
            for (var i = 0; i < InputCount; i++)
            {
                ref var input = ref Inputs[i];
                if (input.Mod is null)
                    continue;
                if (!folded || !input.Stock)
                    r += input.Recoil;
                e += input.Ergo;
                a = checked(a + input.Accuracy);
            }
            Stats = ((float)r / 100f, (float)e / Mathf.Max(1f, baseErgo), unchecked(-a) / 100f);
            Folded = folded;
            BaseErgo = baseErgo;
            Ready = true;
            stats = Stats;
            return true;
        }
    }
    internal static bool TryRead(Weapon weapon, out (float recoil, float ergo, float accuracy) stats)
    {
        stats = default;
        var recent = Recent;
        if (recent is not null && ReferenceEquals(recent.Weapon, weapon))
        {
            if (recent.TryRead(weapon, out stats))
                return true;
            return recent.Build(weapon) && recent.TryRead(weapon, out stats);
        }
        if (!WeaponAttachmentTraversal.NativeType(weapon.GetType()))
            return false;
        // An empty weapon has no traversal to cache. Retain the live base
        // denominator (including unusual floating-point inputs).
        if (((IReadOnlyList<Slot>)weapon.Slots).Count == 0 && weapon.Chambers.Length == 0)
        {
            stats = (0f, 0f / Mathf.Max(1f, weapon.Template.Ergonomics), 0f);
            return true;
        }
        var plan = Plans.GetValue(weapon, static item => new Plan(item));
        Recent = plan;
        if (plan.TryRead(weapon, out stats))
            return true;
        return plan.Build(weapon) && plan.TryRead(weapon, out stats);
    }
}
