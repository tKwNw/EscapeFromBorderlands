using System;
using System.Collections.Generic;
using EFT;

namespace EscapeFromBorderlands.Client;

// Select affected actors while retaining the raid's original processing order.
// An inventory callback during a tick rebuilds the remaining selection, so a
// newly equipped later actor is still processed in this same pass.
internal sealed class RaidPlayerWorkset
{
    private List<Player> Players = new();
    private List<Player> Candidates = new();
    private readonly HashSet<Player> Seen = new();
    private readonly Action<RaidPlayerWorkset> Additional;
    private readonly Func<int>? AdditionalChanges;
    private readonly Comparison<Player> Order;
    private readonly Action<Player> AddPlayer;
    private RaidPlayerSnapshot? Snapshot;
    private GearKind Gear;
    private bool Humans;
    private bool HumansOnly;
    private int Revision;
    private int ExtraRevision;

    internal RaidPlayerWorkset(Action<RaidPlayerWorkset> additional, Func<int>? additionalChanges = null)
    { Additional = additional; AdditionalChanges = additionalChanges; Order = Compare; AddPlayer = Add; }
    private int Compare(Player a, Player b) => Snapshot!.Order(a).CompareTo(Snapshot.Order(b));
    internal RaidPlayerWorkset Select(RaidPlayerSnapshot snapshot, GearKind gear, bool humans = false, bool humansOnly = false)
    { Snapshot = snapshot; Gear = gear; Humans = humans; HumansOnly = humansOnly; Rebuild(); return this; }
    private void Rebuild()
    {
        Candidates.Clear(); Seen.Clear();
        if (HumansOnly)
        {
            // Human-only maintenance should scale with the human roster, not
            // the number of bots wearing Legendary bags or rigs.
            var humans = Snapshot!.Humans;
            for (var i = 0; i < humans.Count; i++)
                if (Humans || EquipmentPresence.IsIndexedWearer(humans[i], Gear)) Add(humans[i]);
        }
        else EquipmentPresence.AddWearers(Gear, this);
        if (Humans && !HumansOnly)
        {
            var humans = Snapshot!.Humans;
            for (var i = 0; i < humans.Count; i++) Add(humans[i]);
        }
        // All human actors already include every possible additional member.
        if (!(HumansOnly && Humans)) Additional(this);
        // Discover additional live state each pass (several providers have no
        // revision). Reuse the ordered selection when membership/order agree.
        // This also detects direct roster reorder without inventory events.
        var unchanged = Players.Count == Candidates.Count;
        var previousOrder = -1;
        if (unchanged)
            foreach (var player in Players)
            {
                if (!Seen.Contains(player)) { unchanged = false; break; }
                var order = Snapshot!.Order(player);
                if (order <= previousOrder) { unchanged = false; break; }
                previousOrder = order;
            }
        if (!unchanged)
        {
            Candidates.Sort(Order);
            var previous = Players; Players = Candidates; Candidates = previous;
        }
        Candidates.Clear();
        Revision = EquipmentPresence.Revision;
        ExtraRevision = AdditionalChanges?.Invoke() ?? 0;
    }
    internal void Add(Player player)
    { if ((!HumansOnly || ActorCapabilities.IsHuman(player)) && Snapshot!.Contains(player) && Seen.Add(player)) Candidates.Add(player); }
    internal void AddProfile(string id) => Snapshot!.AddProfile(id, AddPlayer);
    internal void Add(HashSet<Player> players)
    {
        if (!HumansOnly) { foreach (var p in players) Add(p); return; }
        var humans = Snapshot!.Humans;
        for (var i = 0; i < humans.Count; i++) if (players.Contains(humans[i])) Add(humans[i]);
    }
    internal void Add<T>(Dictionary<Player, T> states)
    {
        if (!HumansOnly) { foreach (var p in states.Keys) Add(p); return; }
        var humans = Snapshot!.Humans;
        for (var i = 0; i < humans.Count; i++) if (states.ContainsKey(humans[i])) Add(humans[i]);
    }
    internal void Clear() { Players.Clear(); Candidates.Clear(); Seen.Clear(); Snapshot = null; }
    private void EndPass() { Candidates.Clear(); Seen.Clear(); Snapshot = null; }
    public Enumerator GetEnumerator() => new(this);
    internal struct Enumerator : IDisposable
    {
        private readonly RaidPlayerWorkset Work;
        private int Index, LastOrder, Revision;
        public Player Current { get; private set; }
        internal Enumerator(RaidPlayerWorkset work)
        { Work = work; Index = 0; LastOrder = -1; Revision = work.Revision; Current = null!; }
        public bool MoveNext()
        {
            if (Revision != EquipmentPresence.Revision || Work.ExtraRevision != (Work.AdditionalChanges?.Invoke() ?? 0))
            { Work.Rebuild(); Revision = Work.Revision; Index = 0; }
            while (Index < Work.Players.Count)
            {
                var player = Work.Players[Index++];
                var order = Work.Snapshot!.Order(player);
                if (order <= LastOrder) continue;
                LastOrder = order; Current = player; return true;
            }
            return false;
        }
        public void Dispose() => Work.EndPass();
    }
}
