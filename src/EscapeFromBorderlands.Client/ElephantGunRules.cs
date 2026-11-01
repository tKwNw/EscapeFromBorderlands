using System.Collections.Generic;
using EFT.InventoryLogic;

namespace EscapeFromBorderlands.Client;

internal static class ElephantGunRules
{
    internal sealed class Reload
    {
        internal AmmoTemplate? Ammo;
    }

    private static readonly Dictionary<Weapon, Reload> Reloads = new();

    // An unloaded gun is not a slug. Use the actual cartridge from the firing
    // scope, never the other barrel or a cached last-fired ammunition type.
    internal static bool IsSlug(AmmoTemplate? ammo) => ammo is not null && ammo.ProjectileCount == 1;

    internal static Reload? BeginReload(Weapon weapon, AmmoTemplate? incomingAmmo)
    {
        if (!RarityResolver.TryGetDefinition(weapon, out var definition) || definition.SpecialId != "elephant_gun") return null;
        var reload = new Reload { Ammo = incomingAmmo };
        Reloads[weapon] = reload;
        return reload;
    }

    internal static bool EndReload(Weapon weapon, Reload? reload)
    {
        if (reload is null || !Reloads.TryGetValue(weapon, out var current) || !ReferenceEquals(current, reload)) return false;
        return Reloads.Remove(weapon);
    }

    internal static float ReloadSpeed(Weapon weapon, float preview) =>
        Reloads.TryGetValue(weapon, out var reload) ? IsSlug(reload.Ammo) ? 0.40f : 1f : preview;

    internal static void Reset() => Reloads.Clear();
}
