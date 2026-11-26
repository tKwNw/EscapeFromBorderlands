using System;
using System.Collections.Generic;
using EFT;

namespace EscapeFromBorderlands.Client;

// Native registered actors retain their profile identity. Index the exact
// registered order, including dead actors and duplicate-profile transitions;
// the alive-player map alone would change the original first-match semantics.
internal static class CombatActorLookup
{
    private static GameWorld? World;
    private static List<IPlayer>? Roster;
    private static List<IPlayer>.Enumerator Version;
    private static int Count;
    private static readonly Dictionary<string, Player> First = new(StringComparer.Ordinal);
    private static bool Current(GameWorld world)
    {
        if (!ReferenceEquals(World, world) || !ReferenceEquals(Roster, world.RegisteredPlayers) || Roster.Count != Count)
            return false;
        // A completed List enumerator still validates the native version.
        // Count-changing updates take the branch above; same-count replacement
        // or reorder invalidates this sentinel without private-field reflection.
        try
        {
            return !Version.MoveNext();
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
    private static bool Rebuild(GameWorld world)
    {
        try
        {
            First.Clear();
            World = world;
            Roster = world.RegisteredPlayers;
            Count = Roster.Count;
            Version = Roster.GetEnumerator();
            while (Version.MoveNext())
                if (Version.Current is Player player && player.ProfileId is { } id)
                    First.TryAdd(id, player);
            return true;
        }
        catch (Exception)
        {
            // Indexing reads beyond the requested actor. A partial/foreign
            // later entry must not break a lookup the original scan could finish.
            Reset();
            return false;
        }
    }
    internal static Player? Find(GameWorld world, string? id)
    {
        if (!Current(world) && !Rebuild(world))
            return FindLive(world, id);
        if (id is not null && First.TryGetValue(id, out var player) && player.ProfileId == id)
            return player;
        // Do not cache absence. Foreign identity edits and partially registered
        // actors retain the original live search and repair the index.
        if (!Rebuild(world))
            return FindLive(world, id);
        if (id is not null)
            return First.TryGetValue(id, out player) ? player : null;
        return FindLive(world, id);
    }
    private static Player? FindLive(GameWorld world, string? id)
    {
        foreach (var candidate in world.RegisteredPlayers)
            if (candidate is Player p && p.ProfileId == id)
                return p;
        return null;
    }
    internal static void Reset()
    {
        First.Clear();
        Version.Dispose();
        Version = default;
        World = null;
        Roster = null;
        Count = 0;
    }
}
