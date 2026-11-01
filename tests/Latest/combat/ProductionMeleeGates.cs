using System;
using System.Linq;
using Comfort.Common;
using EFT;
namespace EscapeFromBorderlands.Client;
internal static partial class MeleeRuntime {
public static bool Enabled(Player player, MeleeEffectDefinition effect) =>
        MeleeRules.Enabled(effect, effect.MaximumHealthFraction < 1f ? HealthFraction(player) : 1f,
            !effect.RequiresAk74 || HasAk74(player)) && AdvancedMeleeRuntime.Enabled(player, effect);
}
internal static partial class AdvancedMeleeRuntime {
public static bool Enabled(Player player, MeleeEffectDefinition effect)
    {
        if (effect.RequiresSniper && !Primaries(player).Any(w => w.WeapClass == "sniperRifle")) return false;
        if (effect.RequiresEmptyPrimaries)
        {
            var weapons = Primaries(player).ToArray();
            if (weapons.Length == 0 || weapons.Any(w => w.ChamberAmmoCount + w.GetCurrentMagazineCount() > 0)) return false;
        }
        if (effect.RequiresBleeding && !Bleeding(player)) return false;
        if (effect.RequiresProne && !player.MovementContext.IsInPronePose) return false;
        if (effect.RequiresNight)
        {
            var clock = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.GameDateTime : null;
            if (clock is null) return false;
            var hour = clock.Calculate().Hour;
            if (hour >= 6 && hour < 20) return false;
        }
        return true;
    }
}
