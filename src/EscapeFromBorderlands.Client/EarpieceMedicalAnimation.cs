using System.Collections.Generic;
using System.Linq;
using EFT;

namespace EscapeFromBorderlands.Client;

// Re-evaluate conditional medical multipliers when their conditions or equipped
// gear change. Preserve the native animation input and all other EFB factors.
internal static class EarpieceMedicalAnimation
{
    private sealed class Entry
    { internal Player Player = null!; internal float Native, Factor; }
    private static readonly Dictionary<FirearmsAnimator, Entry> Entries = new();
    internal static void Reset() => Entries.Clear();
    internal static void Record(FirearmsAnimator animator, Player p, float native) =>
        Entries[animator] = new Entry { Player = p, Native = native, Factor = Factor(p, animator) };
    private static float Factor(Player p, FirearmsAnimator animator) => EarpieceRuntime.MedicalSpeed(p, ServiceWindow.MedicalController(p, animator)?.Item);
    internal static void Maintain()
    {
        if (Entries.Count == 0) return;
        foreach (var pair in Entries.ToArray())
        {
            var e = pair.Value;
            if (!PlayerRefresh.CanObserve(e.Player) || pair.Key == null || !ReferenceEquals(e.Player.HandsAnimator, pair.Key))
            { Entries.Remove(pair.Key!); continue; }
            if (ServiceWindow.MedicalController(e.Player, pair.Key) is null) continue;
            var factor = Factor(e.Player, pair.Key);
            if (e.Factor == factor) continue;
            e.Factor = factor;
            pair.Key.SetUseTimeMultiplier(e.Native);
        }
    }
}
