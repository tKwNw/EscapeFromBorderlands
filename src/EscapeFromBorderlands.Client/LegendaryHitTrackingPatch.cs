using System;
using System.Reflection;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using EFT.RocketLauncher;
using EFT.RocketLauncher.Explosion;
using HarmonyLib;
using Systems.Effects;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

[HarmonyPatch(typeof(Shot), nameof(Shot.Tick))]
internal static class LegendaryHitTrackingPatch
{
    private static void Postfix(Shot __instance)
    {
        if (!__instance.HasAchievedTarget || __instance.ShotProcessed)
        {
            return;
        }

        if (LegendaryRuntime.TryGetApplication(__instance, out var application))
        {
            if (__instance.Parent is null)
            {
                ApplyLocationDamage(__instance, application);
                ApplyPenetrationRetention(__instance, application);
                TriggerExplosion(__instance, application);
            }
            foreach (var fragment in __instance.Fragments)
                if (!LegendaryRuntime.TryGetApplication(fragment, out _)) LegendaryRuntime.Associate(fragment, application, bonusProjectile: true);
        }

        ChestRigBallistics.Impact(__instance);
        LegendaryRuntime.RecordImpact(__instance);
        LegendaryRuntime.Release(__instance);
    }

    private static void ApplyLocationDamage(Shot shot, EffectApplication application)
    {
        if (application.HeadDamageMultiplier == 1f && application.BodyDamageMultiplier == 1f &&
            application.ConditionalDamageMultiplier == 1f && application.LimbDamageMultiplier == 1f) return;
        if (shot.HittedBallisticCollider is not BodyPartCollider body) return;

        var bodyPart = body.BodyPartType.ToString();
        shot.Damage *= bodyPart.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0
            ? application.HeadDamageMultiplier : application.BodyDamageMultiplier;
        if (application.ConditionalDamageMultiplier != 1f &&
            (string.IsNullOrEmpty(application.ConditionalTargetProfileId) ||
                string.Equals(application.ConditionalTargetProfileId, body.PlayerProfileID, StringComparison.Ordinal)) &&
            (string.IsNullOrEmpty(application.ConditionalBodyPart) ||
                string.Equals(application.ConditionalBodyPart, bodyPart, StringComparison.Ordinal)))
            shot.Damage *= application.ConditionalDamageMultiplier;
        if (application.LimbDamageMultiplier != 1f &&
            (bodyPart.IndexOf("Arm", StringComparison.OrdinalIgnoreCase) >= 0 ||
             bodyPart.IndexOf("Leg", StringComparison.OrdinalIgnoreCase) >= 0))
            shot.Damage *= application.LimbDamageMultiplier;
    }

    private static void ApplyPenetrationRetention(Shot shot, EffectApplication application)
    {
        if (shot.Fragments.Count == 0)
        {
            return;
        }

        if (application.RicochetRetention > 0f && shot.BulletState == Shot.EBulletState.RicochetHit)
        {
            foreach (var fragment in shot.Fragments)
            {
                fragment.Damage = Math.Max(fragment.Damage, shot.Damage * application.RicochetRetention);
                fragment.PenetrationPower = Math.Max(fragment.PenetrationPower, shot.PenetrationPower * application.RicochetRetention);
                LegendaryRuntime.Associate(fragment, application, bonusProjectile: true);
            }
        }

        if (application.SurfaceRetentionBonus > 0f && shot.HittedBallisticCollider is not BodyPartCollider)
        {
            foreach (var fragment in shot.Fragments)
            {
                fragment.Damage *= 1f + application.SurfaceRetentionBonus;
                fragment.PenetrationPower *= 1f + application.SurfaceRetentionBonus * 0.5f;
                LegendaryRuntime.Associate(fragment, application, bonusProjectile: true);
            }
        }
    }

    private static void TriggerExplosion(Shot shot, EffectApplication application)
    {
        if (application.ExplosionDamageFactor <= 0f || application.BonusProjectile || shot.Parent is not null)
        {
            return;
        }

        using var timing = GrenadeSpikeTrace.MeasureWeapon(application.Definition, "impact explosion");
        ProjectileImpactExplosion.Trigger(shot,application.ExplosionDamageFactor,application.ExplosionRadius);
    }
}

[HarmonyPatch]
internal static class LegendaryMissTrackingPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(
        typeof(BallisticsCalculator),
        nameof(BallisticsCalculator.RemovePredicate),
        new[] { typeof(Shot) });

    private static void Postfix(Shot __0, bool __result)
    {
        if (__result && !__0.HasAchievedTarget)
        {
            ChestRigRuntime.BulletFinished(__0);
            LegendaryRuntime.RecordMiss(__0);
            LegendaryRuntime.Release(__0);
        }
    }
}

[HarmonyPatch(typeof(Shot), nameof(Shot.Release))]
internal static class LegendaryProjectilePoolCleanupPatch
{
    private static void Prefix(Shot shot)
    {
        ChestRigRuntime.BulletFinished(shot);
        if (!LegendaryRuntime.TryGetApplication(shot, out _)) return;
        if (!shot.HasAchievedTarget) LegendaryRuntime.RecordMiss(shot);
        LegendaryRuntime.Release(shot);
    }
}

[HarmonyPatch]
internal static class LegendaryFleshHitPatch
{
    private static MethodBase[] TargetMethods() =>
    [
        AccessTools.Method(typeof(BodyPartCollider), nameof(BodyPartCollider.ApplyHit)),
        AccessTools.Method(typeof(ArmorPlateCollider), nameof(ArmorPlateCollider.ApplyHit)),
    ];

    private static void Prefix(BodyPartCollider __instance, ref DamageInfo damageInfo)
    {
        if (damageInfo.Weapon is not Weapon weapon || !damageInfo.IsForwardHit)
        {
            return;
        }

        var reachesFlesh = damageInfo.Damage > 0f && __instance is not ArmorPlateCollider &&
            !damageInfo.BlockedBy.HasValue && !damageInfo.DeflectedBy.HasValue;
        LegendaryRuntime.ApplyFleshHitEffects(
            weapon,
            damageInfo.FireIndex,
            reachesFlesh,
            __instance.PlayerProfileID ?? string.Empty,
            __instance.BodyPartType.ToString(),
            ref damageInfo.HeavyBleedingDelta);
    }
}

[HarmonyPatch(typeof(RocketProjectile), nameof(RocketProjectile.Initialize))]
internal static class LegendaryRocketRadiusPatch
{
    private static void Postfix(Ammo ammo, Weapon weapon, ExplosionModel ____explosionModel)
    {
        if (!RarityResolver.TryGetDefinition(weapon, out var definition) ||
            definition.SpecialId != "bunker_buster" ||
            ammo is not Rocket rocket ||
            ____explosionModel?._explosionSettings?.PhysicalSettings is not EFT.RocketLauncher.Explosion.PhysicalSettings liveSettings)
        {
            return;
        }

        // RocketProjectile.Initialize copies the ammo template settings into a
        // projectile-local explosion controller. Change that copy so vanilla
        // rockets and later RShG shots retain their original 4.5 m radius.
        liveSettings.ExplosionRadius = rocket.ExplosionRadius * 0.50f;
    }
}

[HarmonyPatch]
internal static class LegendaryRocketDirectHitPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(RocketProjectile), nameof(RocketProjectile.ProceedDamage));

    private static bool Prefix(
        RocketProjectile __instance,
        BodyPartCollider bodyPart,
        IPlayer player,
        Shot shot,
        IPlayer ____owner,
        Rocket ____ammo,
        Weapon ____weapon,
        Shot ____rocketShot)
    {
        if (!RarityResolver.TryGetDefinition(____weapon, out var definition) ||
            definition.SpecialId != "bunker_buster")
        {
            return true;
        }

        if (____owner is null || player is null)
        {
            return false;
        }

        var roll = RarityResolver.Resolve(____weapon);
        var damageMultiplier = (roll?.DamageMultiplier ?? 1f) * 1.50f;
        var penetrationMultiplier = (roll?.PenetrationMultiplier ?? 1f) * 1.50f;
        var damageInfo = new DamageInfo
        {
            DamageType = EDamageType.Bullet,
            Damage = __instance.CalculateDmgByVelocityAndBodyPart(bodyPart, shot) * damageMultiplier,
            ArmorDamage = ____ammo.ArmorDamage,
            PenetrationPower = (int)Math.Round(____ammo.PenetrationPower * penetrationMultiplier),
            Direction = ____rocketShot.CurrentVelocity.normalized,
            HitNormal = shot.HitNormal,
            HitPoint = shot.HitPoint,
            HittedBallisticCollider = bodyPart,
            Player = Singleton<GameWorld>.Instance.GetEverExistedBridgeByProfileID(____owner.ProfileId),
            Weapon = ____ammo,
            LightBleedingDelta = -10f,
            HeavyBleedingDelta = -10f,
        };
        var target = Singleton<GameWorld>.Instance.GetAlivePlayerBridgeByProfileID(player.ProfileId);
        if (target is not null)
        {
            var armor = bodyPart as ArmorPlateCollider;
            var armorType = armor is null ? (EArmorPlateCollider)0 : armor.ArmorPlateColliderType;
            target.ApplyShot(damageInfo, bodyPart.BodyPartType, bodyPart.BodyPartColliderType, armorType, ShotId.EMPTY_SHOT_ID);
        }
        return false;
    }
}

[HarmonyPatch(typeof(Player.FirearmController.UtilityOperation), nameof(Player.FirearmController.UtilityOperation.OnIdleStartEvent))]
internal static class LegendaryUtilityCompletionPatch
{
    private static void Prefix(Player.FirearmController.UtilityOperation __instance)
    {
        if (!string.Equals(__instance.State.ToString(), "Ready", StringComparison.Ordinal)) return;

        var actor = BackpackNative.WeaponOwner(__instance.Weapon);
        if (actor != null)
        {
            var bit = __instance._utilityOperationType == Player.FirearmController.UtilityOperation.EUtilityType.ExamineWeapon ? 4 :
                __instance._utilityOperationType == Player.FirearmController.UtilityOperation.EUtilityType.CheckChamber ? 1 :
                __instance._utilityOperationType == Player.FirearmController.UtilityOperation.EUtilityType.CheckMagazine ? 2 : 0;
            if (bit != 0) { BackpackRuntime.Check(actor, __instance.Weapon.Id, bit); ChestRigRuntime.Check(actor,__instance.Weapon,bit); EarpieceRuntime.Get(actor)?.Effect.Check(bit); Headwear.Checked(actor, bit); }
            if (ChestRigUtilitySpeedPatch.Speeds.TryGetValue(__instance,out var oldSpeed)) { __instance.FirearmsAnimator.SetAnimationSpeed(oldSpeed); ChestRigUtilitySpeedPatch.Speeds.Remove(__instance); }
        }
        switch (__instance._utilityOperationType)
        {
            case Player.FirearmController.UtilityOperation.EUtilityType.ExamineWeapon:
                LegendaryRuntime.NotifyInspection(__instance.Weapon);
                break;
            case Player.FirearmController.UtilityOperation.EUtilityType.CheckChamber:
                LegendaryRuntime.NotifyChamberCheck(__instance.Weapon);
                break;
            case Player.FirearmController.UtilityOperation.EUtilityType.CheckMagazine:
                LegendaryRuntime.NotifyMagazineCheck(__instance.Weapon);
                break;
        }
    }
}

[HarmonyPatch]
internal static class LegendaryReloadCompletionPatch
{
    private static MethodBase[] TargetMethods() =>
    [
        AccessTools.Method(typeof(Player.FirearmController), nameof(Player.FirearmController.ReloadMag),
            [typeof(Magazine), typeof(ItemAddress), typeof(Callback)]),
        AccessTools.Method(typeof(Player.FirearmController), nameof(Player.FirearmController.QuickReloadMag),
            [typeof(Magazine), typeof(Callback)]),
        AccessTools.Method(typeof(Player.FirearmController), nameof(Player.FirearmController.ReloadWithAmmo),
            [typeof(AmmoPack), typeof(Callback)]),
        AccessTools.Method(typeof(Player.FirearmController), nameof(Player.FirearmController.ReloadCylinderMagazine),
            [typeof(AmmoPack), typeof(Callback), typeof(bool)]),
        AccessTools.Method(typeof(Player.FirearmController), nameof(Player.FirearmController.ReloadBarrels),
            [typeof(AmmoPack), typeof(ItemAddress), typeof(Callback)]),
        AccessTools.Method(typeof(Player.FirearmController), nameof(Player.FirearmController.ReloadGrenadeLauncher),
            [typeof(AmmoPack), typeof(Callback)]),
    ];

    private static void Prefix(Player.FirearmController __instance, MethodBase __originalMethod, ref Callback callback, object[] __args)
    {
        var original = callback;
        var actor = BackpackRuntime.Actor(__instance);
        var rig = ChestRigRuntime.Get(actor);
        if (rig is not null) { rig.Reloading=true; rig.ReloadWeapon=__instance.Item.Id;
            if (__args.OfType<Magazine>().FirstOrDefault() is { } incoming) ChestRigInventory.ReloadMagazines[rig.Player.ProfileId]=incoming; }
        var weapon = __instance.Item;
        var emergency = string.Equals(__originalMethod.Name, nameof(Player.FirearmController.QuickReloadMag), StringComparison.Ordinal);
        if(rig is not null)rig.EmergencyReloading=emergency;
        // Reapply the native base speed even when all bonuses have expired.
        // Skipping the 1x case leaves an earlier boost cached on the animator
        // and incorrectly accelerates subsequent reloads.
        __instance.SetAnimatorAndProceduralValues();
        callback = result =>
        {
            if (result.Succeed) { LegendaryRuntime.NotifyReloadCompleted(weapon, emergency); if(actor is not null&&rig is not null&&ReferenceEquals(ChestRigRuntime.Get(actor),rig))ChestRigRuntime.ReloadCompleted(actor,weapon,emergency); }
            if(rig is not null){rig.Reloading=false;rig.EmergencyReloading=false;ChestRigInventory.ReloadMagazines.Remove(rig.Player.ProfileId);}
            original?.Invoke(result);
        };
    }
}

[HarmonyPatch(typeof(Player.FirearmController), nameof(Player.FirearmController.ManualUpdate))]
internal static class LegendaryContinuousStatePatch
{
    private static void Postfix(Player.FirearmController __instance, Player? ____player) =>
        LegendaryRuntime.UpdateLiveState(____player, __instance.Item);
}
