using System;
using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

// The display cache never decides whether a grenade can be selected or thrown.
internal static class CarriedGrenadeDisplay
{
    private struct Entry
    {
        internal string Id, Key;
        internal VariantDefinition Definition;
        private int _lastRemaining, _lastSeconds;
        private bool _lastShow;
        private string? _detail;

        internal string Detail(int remaining, bool held, bool show, double seconds)
        {
            var displayed = (int)Math.Ceiling(seconds);
            if (_detail is not null && remaining == _lastRemaining && displayed == _lastSeconds && show == _lastShow) return _detail;
            _lastRemaining = remaining; _lastSeconds = displayed; _lastShow = show;
            var detail = GrenadeEffectRules.ChargeText(remaining, Definition.GrenadeChargesPerRaid, held, seconds);
            return _detail = show ? detail : "· " + detail;
        }
    }

    private static readonly List<Entry> Entries = new();
    private static readonly HashSet<string> Seen = new(StringComparer.Ordinal);
    private static readonly List<GrenadeChargeLedger.DisplayQuery> Queries = new();
    private static readonly List<(int Remaining, double Seconds)> Display = new();
    private static Player? Owner;
    private static InventoryEquipment? Equipment;
    private static bool Dirty;
    private static float ReconcileAt;

    internal static void InventoryChanged(Player player, Item? item = null)
    {
        if (!ReferenceEquals(Owner, player)) return;
        // An ordinary stack move cannot alter the grenade set. A container
        // can contain a reusable grenade even if the event reports only its root.
        if (item is null || item is CompoundItem || item is ThrowWeap &&
            RarityResolver.TryGetDefinition(item, out var d) && d.GrenadeChargesPerRaid > 0) Dirty = true;
    }

    internal static void Reset()
    { Entries.Clear(); Seen.Clear(); Queries.Clear(); Display.Clear(); Owner = null; Equipment = null; Dirty = false; ReconcileAt = 0f; }

    internal static void Append(Player player, List<EffectStatusModel.Status> statuses, ref RaidUpdateTrace trace)
    {
        var equipment = player.InventoryController.Inventory.Equipment;
        var now = Time.unscaledTime;
        if (Dirty || !ReferenceEquals(Owner, player) || !ReferenceEquals(Equipment, equipment) || now >= ReconcileAt)
            Rebuild(player, equipment, now);
        trace.Phase("HUD carried grenade index");
        if (Entries.Count == 0) return;
        Queries.Clear();
        foreach (var entry in Entries)
            Queries.Add(new GrenadeChargeLedger.DisplayQuery(entry.Id, entry.Definition.GrenadeChargesPerRaid,
                LegendaryGrenadeSettings.Interval(entry.Definition)));
        ReusableGrenadeRuntime.Charges.DisplayBatch(Queries, Display);
        var heldId = (player.HandsController as Player.BaseGrenadeHandsController)?.Item?.Id;
        for (var i = 0; i < Entries.Count; i++)
        {
            var entry = Entries[i];
            var definition = entry.Definition;
            var capacity = definition.GrenadeChargesPerRaid;
            var (remaining, seconds) = Display[i];
            var held = entry.Id == heldId && remaining > 0;
            var show = GrenadeEffectRules.ShowCharges(remaining, capacity, held);
            if (!show && seconds <= 0) continue;
            var detail = entry.Detail(remaining, held, show, seconds);
            Entries[i] = entry;
            statuses.Add(new EffectStatusModel.Status(entry.Key, definition.ShortName, true, detail: detail));
        }
        trace.Phase("HUD grenade charge reads and labels");
    }
    private static void Rebuild(Player player, InventoryEquipment equipment, float now)
    {
        Owner = player; Equipment = equipment; Dirty = false;
        ReconcileAt = now + 1f; // Catch inventory mutations that bypass native events.
        try
        {
            var count = 0;
            foreach (var item in equipment.GetAllItems())
                if (item is ThrowWeap && RarityResolver.TryGetDefinition(item, out var definition) &&
                    definition.GrenadeChargesPerRaid > 0 && Seen.Add(item.Id))
                {
                    if (count == Entries.Count) Entries.Add(new Entry { Id = item.Id, Key = "grenade:" + item.Id, Definition = definition });
                    else if (Entries[count].Id != item.Id || !ReferenceEquals(Entries[count].Definition, definition))
                        Entries[count] = new Entry { Id = item.Id, Key = "grenade:" + item.Id, Definition = definition };
                    count++;
                }
            if (count < Entries.Count) Entries.RemoveRange(count, Entries.Count - count);
        }
        catch { Dirty = true; throw; }
        finally { Seen.Clear(); }
    }
}
