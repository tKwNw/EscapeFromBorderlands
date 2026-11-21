using System;
using System.Runtime.CompilerServices;
using Comfort.Common;
using EFT;

namespace EscapeFromBorderlands.Client;

internal enum ActorMember { Animation, HandsAnimator, HitReaction, MovementSkill, Physical }

// Native objects keep their owner across many callbacks. Validate the current
// binding on every hit; transfers, replaced animators and teardown cannot reuse
// a stale owner. Weak keys do not retain a completed raid.
internal static class ActorLookup
{
    private static ConditionalWeakTable<object, Player> Owners = new();
    internal static void Reset() => Owners = new();
    private static bool Matches(Player p, object key, ActorMember member) => member switch
    {
        ActorMember.Animation => ReferenceEquals(p.ProceduralWeaponAnimation, key),
        ActorMember.HandsAnimator => ReferenceEquals(p.HandsAnimator, key),
        ActorMember.HitReaction => ReferenceEquals(p.HitReaction, key),
        ActorMember.MovementSkill => ReferenceEquals(p.Skills?.Strength, key) || ReferenceEquals(p.Skills?.Endurance, key),
        ActorMember.Physical => ReferenceEquals(p.Physical, key),
        _ => false
    };
    internal static Player? Find(object? key, ActorMember member)
    {
        if (key is null || PlayerRefresh.ActiveWorld(Singleton<GameWorld>.Instance) is not { } world) return null;
        if (Owners.TryGetValue(key, out var known))
        {
            if (Matches(known, key, member) && PlayerRefresh.CanObserve(known) && PlayerRefresh.IsRegistered(world, known)) return known;
            Owners.Remove(key);
        }
        foreach (var candidate in world.RegisteredPlayers)
            if (candidate is Player player && Matches(player, key, member) && PlayerRefresh.CanObserve(player))
            { Owners.Add(key, player); return player; }
        return null;
    }
}
