using System;
using System.Runtime.CompilerServices;
using EFT;

namespace EscapeFromBorderlands.Client;

// Reuse only the callback, never its result or skill object. Every throw still
// enters ExecuteSkill and reads the actor's current Skills at invocation time.
internal static class GrenadeThrowSkill
{
    private static ConditionalWeakTable<Player, Action> Actions = new();
    internal static Action For(Player player) => Actions.GetValue(player, static p => () => p.Skills.ThrowAction.Complete());
    internal static void Reset() => Actions = new();
}
