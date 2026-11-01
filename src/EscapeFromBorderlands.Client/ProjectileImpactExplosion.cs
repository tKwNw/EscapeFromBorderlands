using System;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.RocketLauncher;
using EFT.RocketLauncher.Explosion;
using EFT.InventoryLogic;
using Systems.Effects;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class ProjectileImpactExplosion
{
    internal static void Trigger(Shot shot,float damageFactor,float radius)
    {
        var world = Singleton<GameWorld>.Instance;
        if (world?.SharedBallisticsCalculator is null || shot.Ammo is not Ammo ammo)
        {
            return;
        }

        // Start just outside the impacted surface so a wall or armor collider
        // does not occlude an explosion created inside its own hit surface.
        var position=shot.HitPoint+shot.HitNormal.normalized*.02f;
        var payload = new LegendaryExplosivePayload(
            ammo,
            shot.Damage * damageFactor,
            radius);
        try
        {
            if (Singleton<Effects>.Instantiated)
            {
                try { LegendaryExplosionFx.Impact(Singleton<Effects>.Instance, position, shot.HitNormal, payload.MaxExplosionDistance, shot.IsForwardHit ? 1 : 0); }
                catch (Exception visualError) { Plugin.LogSource.LogWarning("Impact explosion visual failed: "+visualError.Message); }
            }
            var profileId = shot.Player?.iPlayer.ProfileId ?? shot.PlayerProfileID;
            if (Singleton<GlobalEventDispatcher>.Instantiated)
            {
                Grenade.Explosion(
                    null,
                    payload,
                    position,
                    profileId,
                    world.SharedBallisticsCalculator,
                    shot.Weapon,
                    Vector3.zero);
            }
            else
            {
                ExplosionSharedMethods.Explosion(
                    payload,
                    position,
                    profileId,
                    world.SharedBallisticsCalculator,
                    shot.Weapon,
                    () => Grenade.GetDamageInfo(shot.Weapon, profileId, position, false),
                    0f,
                    0f,
                    null,
                    false);
            }
        }
        catch (Exception exception)
        {
            Plugin.LogSource.LogWarning($"Legendary impact explosion failed safely: {exception.Message}");
        }
    }
}

internal sealed class LegendaryExplosivePayload : IExplosiveItem
{
    private readonly Ammo _ammo;
    public LegendaryExplosivePayload(Ammo ammo, float strength, float radius)
    {
        _ammo = ammo;
        GetStrength = Math.Max(1f, strength);
        MaxExplosionDistance = Math.Max(0.5f, radius);
        MinExplosionDistance = Math.Min(0.5f, MaxExplosionDistance * 0.25f);
        ArmorDistanceDistanceDamage = new Vector3(MinExplosionDistance, MaxExplosionDistance, GetStrength * 0.5f);
    }

    public Vector3 Blindness => Vector3.zero;
    public Vector3 Contusion => Vector3.zero;
    public Vector3 ArmorDistanceDistanceDamage { get; }
    public float MinExplosionDistance { get; }
    public float MaxExplosionDistance { get; }
    public int FragmentsCount => 0;
    public float GetStrength { get; }
    public bool IsDummy => false;
    public Ammo CreateFragment() => _ammo;
}
