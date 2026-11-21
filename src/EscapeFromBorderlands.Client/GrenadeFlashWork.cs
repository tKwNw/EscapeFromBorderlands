namespace EscapeFromBorderlands.Client;

internal static class GrenadeFlashWork
{
    // Occlusion cannot change a target for which every effect is already zero.
    // Do not discard bot sound-only flashes, owner immunity, or contusion.
    internal static bool HasEffect(float visual, float sound, float contusion, bool stun, bool burn, bool activeBot) =>
        visual > 0f && (stun || burn) || activeBot && (sound > 0f || visual > 0f) || contusion > 2f;
}
