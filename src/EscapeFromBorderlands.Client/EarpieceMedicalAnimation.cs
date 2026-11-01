using System.Collections.Generic;
using System.Linq;
using EFT;

namespace EscapeFromBorderlands.Client;

// Re-evaluate the named use-time multiplier when stillness ends or the earpiece
// is removed. Preserve the native animation input and all other EFB factors.
internal static class EarpieceMedicalAnimation
{
    private sealed class Entry
    { internal Player Player = null!; internal float Native, Factor; }
    private static readonly Dictionary<FirearmsAnimator, Entry> Entries = new();
    internal static void Reset() => Entries.Clear();
    internal static void Record(FirearmsAnimator animator, Player p, float native) =>
        Entries[animator] = new Entry { Player = p, Native = native, Factor = EarpieceRuntime.MedicalSpeed(p) };
    internal static void Maintain()
    {
        foreach (var pair in Entries.ToArray())
        {
            var e = pair.Value;
            if (e.Player.HealthController?.IsAlive != true || !ReferenceEquals(e.Player.HandsAnimator, pair.Key))
            { Entries.Remove(pair.Key); continue; }
            if (e.Player.HandsController is not Player.MedsController) continue;
            var factor = EarpieceRuntime.MedicalSpeed(e.Player);
            if (e.Factor == factor) continue;
            e.Factor = factor;
            pair.Key.SetUseTimeMultiplier(e.Native);
        }
    }
}
