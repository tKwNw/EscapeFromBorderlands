// This suite exercises melee with no equipped EFB rig; composed rig effects are covered by Latest/rigs.
namespace EscapeFromBorderlands.Client;
internal static class ChestRigRuntime
{
    internal static float Movement(EFT.Player? player) => 1f;
    internal static bool Is(EFT.Player? player, string signature) => false;
}
