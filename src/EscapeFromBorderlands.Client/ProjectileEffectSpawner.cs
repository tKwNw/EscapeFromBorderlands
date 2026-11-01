using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class ProjectileEffectSpawner
{
    private static readonly HashSet<Shot> DelayedProjectiles = new();
    private static int SessionGeneration;

    public static void ResetSession()
    {
        SessionGeneration++;
        foreach (var shot in DelayedProjectiles.ToArray()) ReleaseDelayed(shot);
    }

    public static void Spawn(BallisticsCalculator calculator, Shot primary, EffectApplication application)
    {
        var center = primary.Direction.normalized;
        if (application.CenterBonusOnShell)
        {
            var shellCenter = application.ShellCenterDirection.sqrMagnitude > 0f ? application.ShellCenterDirection.normalized : center;
            SpawnOne(calculator, primary, application, 1, shellCenter);
            return;
        }
        var axis = RotationAxis(center, application.ProjectilePattern);
        if (string.Equals(application.ProjectilePattern, "random_cone", StringComparison.OrdinalIgnoreCase))
        {
            for (var index = 1; index < application.ProjectileCount; index++)
            {
                SpawnOne(calculator, primary, application, index, RandomConeDirection(center, application.ProjectileSpreadDegrees));
            }
            return;
        }

        if (application.ProjectileCount % 2 == 0 && application.ProjectileSpreadDegrees > 0f)
        {
            // Even volleys need symmetric offsets too. Keep the original on
            // the left edge and place every sibling from the original axis.
            // Four projectiles now occupy -1, -1/3, +1/3, +1 of the spread.
            SetDirection(primary,(Quaternion.AngleAxis(-application.ProjectileSpreadDegrees,axis)*center).normalized);
            for(var index=1;index<application.ProjectileCount;index++)
            {
                var angle=application.ProjectileSpreadDegrees*(index/(float)(application.ProjectileCount-1)*2f-1f);
                SpawnOne(calculator,primary,application,index,(Quaternion.AngleAxis(angle,axis)*center).normalized);
            }
            return;
        }

        // The original projectile remains on the sight line for odd-count
        // volleys. Spawn symmetric pairs around it so a three-projectile fan
        // lands left/center/right instead of center/center/right.
        if (application.ProjectileCount > 1 && application.ProjectileCount % 2 == 1)
        {
            var pairCount = application.ProjectileCount / 2;
            var spawned = 1;
            for (var pair = pairCount; pair >= 1; pair--)
            {
                var angle = application.ProjectileSpreadDegrees * pair / pairCount;
                var negative = (Quaternion.AngleAxis(-angle, axis) * center).normalized;
                SpawnOne(calculator, primary, application, spawned++, negative);
            }
            for (var pair = 1; pair <= pairCount; pair++)
            {
                var angle = application.ProjectileSpreadDegrees * pair / pairCount;
                var positive = (Quaternion.AngleAxis(angle, axis) * center).normalized;
                SpawnOne(calculator, primary, application, spawned++, positive);
            }
            return;
        }

        for (var index = 1; index < application.ProjectileCount; index++)
        {
            SpawnOne(calculator, primary, application, index);
        }
    }

    private static void SpawnOne(
        BallisticsCalculator calculator,
        Shot primary,
        EffectApplication application,
        int index,
        Vector3? forcedDirection = null)
    {
        var direction = forcedDirection ?? SpreadDirection(primary.Direction, application.ProjectileSpreadDegrees, index, application.ProjectileCount);
        var velocityScale = Math.Max(0.05f, application.BonusProjectileVelocityScale);
        var projectile = Shot.Create(
            primary.Ammo,
            // Native impact IDs combine the cartridge ID and fragment index.
            // Reserve distinct even indices after the ammo's natural pellets,
            // leaving the following odd index for native fragmentation.
            Math.Max(primary.FragmentIndex + 2, 2 * Math.Max(1, (primary.Ammo as Ammo)?.AmmoTemplate.ProjectileCount ?? 1)) + 2 * (index - 1),
            UnityEngine.Random.Range(0, 512),
            primary.StartPosition,
            direction,
            primary.InitialSpeed * velocityScale,
            primary.Speed * velocityScale,
            primary.BulletMassGram,
            primary.BulletDiameterMilimeters,
            primary.Damage * application.BonusProjectileDamageScale,
            primary.PenetrationPower * application.BonusProjectilePenetrationScale,
            primary.PenetrationChance,
            primary.RicochetChance,
            primary.FragmentationChance,
            primary.DeviationChance,
            primary.MinFragmentsCount,
            primary.MaxFragmentsCount,
            primary._defaultHitBody,
            primary.Randoms,
            primary.BallisticCoefficient,
            primary.Player?.iPlayer.ProfileId ?? primary.PlayerProfileID,
            primary.Weapon,
            primary.FireIndex,
            null);
        projectile.ArmorDamage = primary.ArmorDamage;
        ChestRigRuntime.AssociateSibling(projectile, primary);
        LegendaryRuntime.Associate(projectile, application, bonusProjectile: true);
        if (application.SpecialId == "wave015_echo")
        {
            // At range distances both impacts otherwise arrive in one frame.
            // EFT suppresses a second visual within 2.5 cm in that frame.
            // Allocate now so recycled primary objects cannot change the echo,
            // and retain its volley until this delayed sibling has finished.
            DelayedProjectiles.Add(projectile);
            if (Singleton<GameWorld>.Instance is not { } world)
            {
                ReleaseDelayed(projectile);
                return;
            }
            try { world.StartCoroutine(ShootEcho(world, calculator, projectile, SessionGeneration)); }
            catch (Exception exception)
            {
                ReleaseDelayed(projectile);
                Plugin.LogSource.LogWarning($"Echo follow-up could not be scheduled: {exception.Message}");
            }
        }
        else calculator.Shoot(projectile);
    }

    private static IEnumerator ShootEcho(GameWorld world, BallisticsCalculator calculator, Shot projectile, int session)
    {
        try
        {
            yield return new WaitForSeconds(0.12f);
            if (session != SessionGeneration || Singleton<GameWorld>.Instance != world) yield break;
            try
            {
                calculator.Shoot(projectile);
                DelayedProjectiles.Remove(projectile);
            }
            catch (Exception exception)
            {
                Plugin.LogSource.LogWarning($"Echo follow-up could not be fired: {exception.Message}");
            }
        }
        finally
        {
            if (session == SessionGeneration) ReleaseDelayed(projectile);
        }
    }

    private static void ReleaseDelayed(Shot projectile)
    {
        if (!DelayedProjectiles.Remove(projectile)) return;
        LegendaryRuntime.Release(projectile);
        Shot.Release(projectile);
    }

    private static Vector3 RandomConeDirection(Vector3 center, float degrees)
    {
        if (degrees <= 0f)
        {
            return center.normalized;
        }

        var forward = center.normalized;
        var upAxis = Math.Abs(Vector3.Dot(forward, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
        var rightAxis = Vector3.Cross(upAxis, forward).normalized;
        var yaw = UnityEngine.Random.Range(-degrees, degrees);
        var pitchLimit = Mathf.Sqrt(Math.Max(0f, degrees * degrees - yaw * yaw));
        var pitch = UnityEngine.Random.Range(-pitchLimit, pitchLimit);
        return (Quaternion.AngleAxis(pitch, rightAxis) * Quaternion.AngleAxis(yaw, upAxis) * forward).normalized;
    }

    private static void SetDirection(Shot shot, Vector3 direction)
    {
        shot.Direction = direction;
        shot.StartVelocity = direction * shot.Speed;
        shot._currentVelocity = shot.StartVelocity;
        shot.TrajectoryInfo.Initialize(
            shot.StartPosition,
            shot.StartVelocity,
            shot.BulletMassGram,
            shot.BulletDiameterMilimeters,
            shot.BallisticCoefficient,
            shot.Player?.IsAI == true);
    }

    private static Vector3 SpreadDirection(Vector3 original, float totalSpread, int index, int count)
    {
        if (totalSpread <= 0f || count <= 1)
        {
            return original.normalized;
        }

        var normalized = original.normalized;
        var axis = RotationAxis(normalized, "horizontal");
        var t = count == 2 ? (index % 2 == 1 ? 1f : -1f) : index / (float)(count - 1) * 2f - 1f;
        return (Quaternion.AngleAxis(totalSpread * t, axis) * normalized).normalized;
    }

    private static Vector3 RotationAxis(Vector3 forward, string pattern)
    {
        if (string.Equals(pattern, "vertical", StringComparison.OrdinalIgnoreCase))
        {
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            return right.sqrMagnitude > 0.001f ? right : Vector3.right;
        }

        return Math.Abs(Vector3.Dot(forward, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
    }
}
