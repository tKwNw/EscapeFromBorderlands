using System;
using System.Collections.Generic;
using EFT;

namespace EscapeFromBorderlands.Client;

// The gear tick methods run on Unity's main thread. Lazily capture once for
// this update. Retain indexes between passes, but validate every live member,
// profile, human/bot flag and position before reusing them on the next pass.
internal sealed class RaidPlayerSnapshot
{
    private readonly List<Player> _players = new();
    private readonly HashSet<Player> _members = new();
    private readonly Dictionary<string, Player> _profiles = new(StringComparer.Ordinal);
    private bool _duplicateProfiles;
    private readonly Dictionary<Player, int> _order = new();
    private readonly List<Player> _humans = new();
    private GameWorld? _world;
    private bool _captured;
    private readonly List<(Player Player, string Profile, bool Human)> _candidates = new();
    private readonly List<string> _profileIds = new();
    private readonly HashSet<Player> _seen = new();

    public List<Player>.Enumerator GetEnumerator() => _players.GetEnumerator();
    internal bool Contains(Player player) => _members.Contains(player);
    internal bool ContainsProfile(string id) => _profiles.ContainsKey(id);
    internal void AddProfile(string id, Action<Player> add)
    {
        if (!_duplicateProfiles)
        {
            if (_profiles.TryGetValue(id, out var player))
                add(player);
            return;
        }
        // Preserve the original roster semantics even during duplicate-profile transitions.
        for (var i = 0; i < _players.Count; i++)
            if (_players[i].ProfileId == id)
                add(_players[i]);
    }
    internal int Order(Player player) => _order[player];
    internal IReadOnlyList<Player> Humans => _humans;

    internal void Capture(GameWorld world)
    {
        if (_captured && ReferenceEquals(_world, world))
            return;
        if (!ReferenceEquals(_world, world))
            Clear();
        try
        {
            var unchanged = ReferenceEquals(_world, world);
            var humanIndex = 0;
            foreach (var entry in world.RegisteredPlayers)
            {
                if (entry is not Player player || !PlayerRefresh.CanObserve(player) || !_seen.Add(player))
                    continue;
                var id = player.ProfileId;
                var human = !player.IsAI;
                var index = _candidates.Count;
                if (index >= _players.Count || !ReferenceEquals(_players[index], player) || _profileIds[index] != id)
                    unchanged = false;
                if (human)
                {
                    if (humanIndex >= _humans.Count || !ReferenceEquals(_humans[humanIndex], player))
                        unchanged = false;
                    humanIndex++;
                }
                _candidates.Add((player, id, human));
            }
            unchanged &= _candidates.Count == _players.Count && humanIndex == _humans.Count;
            if (!unchanged)
            {
                _players.Clear();
                _members.Clear();
                _profiles.Clear();
                _order.Clear();
                _humans.Clear();
                _profileIds.Clear();
                _duplicateProfiles = false;
                foreach (var candidate in _candidates)
                {
                    _order.Add(candidate.Player, _players.Count);
                    _players.Add(candidate.Player);
                    _members.Add(candidate.Player);
                    _profileIds.Add(candidate.Profile);
                    if (candidate.Human)
                        _humans.Add(candidate.Player);
                    if (!_profiles.TryAdd(candidate.Profile, candidate.Player))
                        _duplicateProfiles = true;
                }
            }
            _world = world;
            _captured = true;
        }
        catch
        {
            Clear();
            throw;
        }
        finally
        {
            _candidates.Clear();
            _seen.Clear();
        }
    }

    internal void EndPass() => _captured = false;

    internal void Clear()
    {
        _world = null;
        _captured = false;
        _players.Clear();
        _members.Clear();
        _profiles.Clear();
        _duplicateProfiles = false;
        _order.Clear();
        _humans.Clear();
        _profileIds.Clear();
        _candidates.Clear();
        _seen.Clear();
    }
}
