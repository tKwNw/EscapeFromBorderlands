using EFT;

namespace EscapeFromBorderlands.Client;

internal static class ActorCapabilities
{
    // These actor systems are deliberately human-only. Combat/target effects
    // remain shared; do not use this predicate as a general player-validity test.
    internal static bool IsHuman([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] Player? player) => player is { IsAI: false };
    // EFB stamina costs, capacity and recovery are human-only. Bots retain
    // their weapon, movement, health and other non-stamina Legendary effects.
    internal static bool UsesStamina([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] Player? player) => player is { IsAI: false };
    // Inspect the actual capability, not a faction or difficulty assumption.
    // A mod supplying an AI vault component opts back into the normal checks.
    internal static bool PollVaulting(Player player) => !player.IsAI || player.VaultingComponent is not null;
    // Ambient mixing belongs to the human audio listener, not bot perception.
    internal static bool AmbientListener(Player player) => !player.IsAI;
}
