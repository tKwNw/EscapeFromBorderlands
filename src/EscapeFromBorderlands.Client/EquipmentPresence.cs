using System;
using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

[Flags]
internal enum GearKind { None = 0, Backpack = 1, Rig = 2, Earpiece = 4, Headwear = 8, Melee = 16,
    Gear = Backpack | Rig | Earpiece | Headwear, Audio = Earpiece | Headwear }

// Cache absence as well as presence. Native inventory events refresh immediately;
// the existing 20 Hz roster reconciles mods that bypass those events. Only worn
// slots and hands are inspected, never the contents of a player's inventory.
internal static class EquipmentPresence
{
    private sealed class ItemObservation
    {
        private Item? Item;
        internal Item? Current => Item;
        private string? Template;
        private VariantDefinition? Definition;
        internal VariantDefinition? Read(Item? item)
        {
            var template = item?.StringTemplateId;
            if (!ReferenceEquals(Item, item) || Template != template)
            {
                Item = item; Template = template;
                RarityResolver.TryGetDefinition(item, out Definition!);
            }
            return Definition;
        }
    }
    private sealed class Entry
    {
        internal readonly ItemObservation Bag = new(), Rig = new(), Ear = new(), Hat = new(), Knife = new(), Hands = new();
        internal GearKind Kind;
        internal float Until;
    }
    private static readonly Dictionary<Player, Entry> Entries = new();
    [ThreadStatic] private static Player? ReadingPlayer;
    [ThreadStatic] private static Entry? ReadingEntry;
    [ThreadStatic] private static int ReadingEpoch;
    private static int EntryEpoch;

    // A movement composition can ask about the same actor's gear repeatedly.
    // Share the live entry only inside that synchronous call. Event observation
    // updates it in place; removal/reset invalidates it even during reentrancy.
    internal readonly struct ReadScope : IDisposable
    {
        private readonly Player? PreviousPlayer;
        private readonly Entry? PreviousEntry;
        private readonly int PreviousEpoch;
        private readonly bool Active;
        internal ReadScope(Player player)
        {
            PreviousPlayer = ReadingPlayer; PreviousEntry = ReadingEntry; PreviousEpoch = ReadingEpoch;
            Active = true;
            ReadingPlayer = player; ReadingEntry = null; ReadingEpoch = EntryEpoch;
        }
        public void Dispose()
        { if (Active) { ReadingPlayer = PreviousPlayer; ReadingEntry = PreviousEntry; ReadingEpoch = PreviousEpoch; } }
    }
    private static Entry? Find(Player player)
    {
        if (ReferenceEquals(ReadingPlayer, player) && ReadingEpoch == EntryEpoch && ReadingEntry is not null) return ReadingEntry;
        Entries.TryGetValue(player, out var entry);
        if (ReferenceEquals(ReadingPlayer, player)) { ReadingEntry = entry; ReadingEpoch = EntryEpoch; }
        return entry;
    }
    private static readonly List<Player> Removed = new();
    private static readonly HashSet<Player> Bags = new(), Rigs = new(), Ears = new();
    private static readonly HashSet<Player> HeldMelee = new();
    internal static bool AnyHeldMelee => HeldMelee.Count != 0;
    internal static void AddHeldMelee(RaidPlayerWorkset work) => work.Add(HeldMelee);
    internal static int Revision { get; private set; }
    internal static void AddWearers(GearKind kind, RaidPlayerWorkset work)
    {
        if ((kind & GearKind.Backpack) != 0) work.Add(Bags);
        if ((kind & GearKind.Rig) != 0) work.Add(Rigs);
        if ((kind & GearKind.Earpiece) != 0) work.Add(Ears);
    }
    internal static bool IsIndexedWearer(Player player, GearKind kind) =>
        (kind & GearKind.Backpack) != 0 && Bags.Contains(player) ||
        (kind & GearKind.Rig) != 0 && Rigs.Contains(player) ||
        (kind & GearKind.Earpiece) != 0 && Ears.Contains(player);
    private static void Track(Player player, GearKind old, GearKind kind)
    {
        if (old == kind) return;
        if ((kind & GearKind.Backpack) != 0) Bags.Add(player); else Bags.Remove(player);
        if ((kind & GearKind.Rig) != 0) Rigs.Add(player); else Rigs.Remove(player);
        if ((kind & GearKind.Earpiece) != 0) Ears.Add(player); else Ears.Remove(player);
        Revision++;
    }
    private static int GearCount, AudioCount, MeleeCount, RigCount, EarpieceCount;
    private static float NextScan;
    internal static int AudioRevision { get; private set; }
    internal static bool AnyGear => GearCount != 0;
    internal static bool AnyAudio => AudioCount != 0;
    internal static bool AnyMelee => MeleeCount != 0;
    internal static bool AnyRig => RigCount != 0;
    internal static bool AnyEarpiece => EarpieceCount != 0;

    internal static bool Has(Player? player, GearKind kind) => (Get(player) & kind) != 0;
    internal static GearKind Kinds(Player player) => Get(player);
    internal static Item? Worn(Player player, GearKind kind)
    {
        _ = Get(player);
        var entry = Find(player); if (entry is null) return null;
        return kind switch { GearKind.Backpack => entry.Bag.Current, GearKind.Rig => entry.Rig.Current,
            GearKind.Earpiece => entry.Ear.Current, _ => null };
    }
    private static GearKind Get(Player? player)
    {
        if (ReferenceEquals(player, null) || PlayerRefresh.Resetting) return GearKind.None;
        var entry = Find(player);
        if (entry is not null && Time.unscaledTime < entry.Until) return entry.Kind;
        return Observe(player, entry);
    }
    private static GearKind Observe(Player player, Entry? entry)
    {
        if (!PlayerRefresh.CanObserve(player)) { Forget(player); return GearKind.None; }
        if (entry is null) { Entries[player] = entry = new Entry(); EntryEpoch++; }
        var gear = player.InventoryController.Inventory.Equipment;
        var kind = GearKind.None;
        if (entry.Bag.Read(gear.GetSlot(EquipmentSlot.Backpack)?.ContainedItem)?.BackpackEffect is not null) kind |= GearKind.Backpack;
        if (entry.Rig.Read(gear.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem)?.ChestRigEffect is not null) kind |= GearKind.Rig;
        if (entry.Ear.Read(gear.GetSlot(EquipmentSlot.Earpiece)?.ContainedItem)?.IsEarpiece == true) kind |= GearKind.Earpiece;
        if (entry.Hat.Read(gear.GetSlot(EquipmentSlot.Headwear)?.ContainedItem) is { IsHeadwear: true, IsMysteryFleaOffer: false }) kind |= GearKind.Headwear;
        var heldMelee = entry.Hands.Read(player.HandsController?.Item)?.MeleeEffect is not null;
        if (heldMelee ? HeldMelee.Add(player) : HeldMelee.Remove(player)) Revision++;
        if (entry.Knife.Read(gear.GetSlot(EquipmentSlot.Scabbard)?.ContainedItem)?.MeleeEffect is not null || heldMelee) kind |= GearKind.Melee;
        if (ActorCapabilities.IsHuman(player) && (entry.Kind & GearKind.Audio) != (kind & GearKind.Audio)) AudioRevision++;
        Track(player, entry.Kind, kind);
        Count(entry.Kind, -1, player); entry.Kind = kind; Count(kind, 1, player);
        // Ordinary bot loadouts rarely change. Native inventory events still
        // observe immediately; affected actors retain the original 20 Hz rate.
        entry.Until = Time.unscaledTime + (player.IsAI && kind == GearKind.None ? .25f : .05f);
        return kind;
    }
    private static void Count(GearKind kind, int delta, Player player)
    {
        if ((kind & GearKind.Gear) != 0) GearCount += delta;
        if (ActorCapabilities.IsHuman(player) && (kind & GearKind.Audio) != 0) AudioCount += delta;
        if ((kind & GearKind.Melee) != 0) MeleeCount += delta;
        if ((kind & GearKind.Rig) != 0) RigCount += delta;
        if ((kind & GearKind.Earpiece) != 0) EarpieceCount += delta;
    }
    internal static void Changed(Player player)
    {
        Entries.TryGetValue(player, out var entry);
        Observe(player, entry);
    }
    internal static void Forget(Player player)
    {
        if (HeldMelee.Remove(player)) Revision++;
        if (Entries.TryGetValue(player, out var entry))
        {
            if (ActorCapabilities.IsHuman(player) && (entry.Kind & GearKind.Audio) != 0) AudioRevision++;
            Track(player, entry.Kind, GearKind.None);
            Count(entry.Kind, -1, player); Entries.Remove(player);
            EntryEpoch++;
        }
    }
    internal static void Maintain(GameWorld world, RaidPlayerSnapshot snapshot)
    {
        if (Time.unscaledTime < NextScan) return;
        NextScan = Time.unscaledTime + .05f;
        snapshot.Capture(world);
        foreach (var player in snapshot) _ = Get(player);
        try
        {
            foreach (var player in Entries.Keys) if (!snapshot.Contains(player)) Removed.Add(player);
            foreach (var player in Removed) Forget(player);
        }
        finally { Removed.Clear(); }
    }
    internal static void Reset() { Entries.Clear(); EntryEpoch++; ReadingEntry = null; Removed.Clear(); Bags.Clear(); Rigs.Clear(); Ears.Clear(); HeldMelee.Clear(); Revision++; GearCount = AudioCount = MeleeCount = RigCount = EarpieceCount = AudioRevision = 0; NextScan = 0f; }
}

[HarmonyPatch(typeof(Player), nameof(Player.OnItemAddedOrRemoved))]
internal static class EquipmentPresenceChangePatch
{
    // Observe both sides: other postfixes and callbacks may query gear or alter
    // it again. Finalizer runs even when a third-party callback throws.
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Player __instance) => EquipmentPresence.Changed(__instance);
    [HarmonyPriority(Priority.Last)]
    private static Exception? Finalizer(Player __instance, Exception? __exception)
    { EquipmentPresence.Changed(__instance); return __exception; }
}
