namespace EscapeFromBorderlands.Client;

// These historical melee scenarios equip no earpiece. Headset effects and their
// native bindings are covered by Latest/earpieces and the native audits.
internal static class EarpieceRuntime
{
    internal static float MeleeSpeed(EFT.Player? player) => 1f;
    internal static float Movement(EFT.Player player) => 1f;
}
