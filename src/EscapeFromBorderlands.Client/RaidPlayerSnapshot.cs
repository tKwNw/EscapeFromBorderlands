using System;
using System.Collections.Generic;
using EFT;

namespace EscapeFromBorderlands.Client;

// The gear tick methods run on Unity's main thread. Lazily capture once for
// this update and release player references when the update is finished.
internal sealed class RaidPlayerSnapshot
{
    private readonly List<Player> _players = new();
    private readonly HashSet<Player> _members = new();
    private readonly HashSet<string> _profiles = new(StringComparer.Ordinal);
    private GameWorld? _world;

    public List<Player>.Enumerator GetEnumerator() => _players.GetEnumerator();
    internal bool Contains(Player player) => _members.Contains(player);
    internal bool ContainsProfile(string id) => _profiles.Contains(id);

    internal void Capture(GameWorld world)
    {
        if (ReferenceEquals(_world, world)) return;
        Clear();
        _world = world;
        foreach (var entry in world.RegisteredPlayers)
        {
            if (entry is not Player player || !PlayerRefresh.CanObserve(player) || !_members.Add(player)) continue;
            _players.Add(player);
            _profiles.Add(player.ProfileId);
        }
    }

    internal void Clear()
    {
        if (_world is null) return;
        _world = null;
        _players.Clear();
        _members.Clear();
        _profiles.Clear();
    }
}
