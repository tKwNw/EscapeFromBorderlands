using Shot = EftBulletClass;
using Ammo = AmmoItemClass;
using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

[HarmonyPatch(typeof(Player.FirearmController),"method_58")]
internal static class ChestRigCartridgePatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Player.FirearmController __instance,Weapon weapon,Ammo ammo,out ChestRigRuntime.ShotScope? __state)
    {
        __state = null;
        var rig = ChestRigRuntime.Get(BackpackRuntime.Actor(__instance));
        if (rig is null) return;
        __state=ChestRigRuntime.Begin(weapon,ammo,rig,WeaponCombatState.PreparedRemaining(__instance,weapon,ammo));
        if(__state is not null){WeaponCombatState.Refresh(__instance);WeaponCombatState.RefreshPreparedSingleInterval(__instance,weapon);}
    }
    private static Exception? Finalizer(ChestRigRuntime.ShotScope? __state,Exception? __exception){ChestRigRuntime.End(__state);return __exception;}
}
internal static class ChestRigBallistics
{
    internal static void Apply(Shot shot,Ammo ammo,string playerId,ref EffectApplication application)
    {
        if(ChestRigCombat.FragmentFactor(ammo) is { } fragmentFactor)
        {shot.Damage*=fragmentFactor;return;}
        var scope=ChestRigRuntime.CurrentShot;
        if(scope is null)
        {
            if(shot.Weapon is LauncherItemClass&&ChestRigRuntime.Get(Singleton<GameWorld>.Instance?.RegisteredPlayers.OfType<Player>().FirstOrDefault(p=>p.ProfileId==playerId))?.Key=="AR21-2")
            {shot.Damage*=1.3f;shot.InitialSpeed*=1.4f;shot.Speed*=1.4f;shot.StartVelocity*=1.4f;shot.Vector3_1=shot.StartVelocity;shot.TrajectoryInfo.Initialize(shot.StartPosition,shot.StartVelocity,shot.BulletMassGram,shot.BulletDiameterMilimeters,shot.BallisticCoefficient,shot.Player?.IsAI==true);}
            return;
        }
        ChestRigRuntime.Associate(shot);
        shot.Damage*=scope.Bonuses.Damage;shot.PenetrationPower*=scope.Bonuses.Penetration;
        if(!scope.Bonuses.DoubleExplosive)return;
        if(application==EffectApplication.Empty||!application.Active)application=new EffectApplication(scope.State.Roll.Definition){Active=true};
        application.ProjectileCount=Math.Max(1,application.ProjectileCount)*2;
        application.ProjectileDamageMultiplier=application.ProjectileCount>2?.7f:1f;
        if(application.ProjectileCount==2){application.ProjectilePattern="paired_horizontal";application.ProjectileSpreadDegrees=.2f;}
    }
    internal static void Impact(Shot shot)
    {
        if(!ChestRigRuntime.TryShot(shot,out var scope))return;
        foreach(var fragment in shot.Fragments)ChestRigRuntime.AssociateSibling(fragment,shot);
        if(scope.Bonuses.DoubleExplosive&&shot.Parent is null&&shot.Ammo is Ammo ammo&&Singleton<GameWorld>.Instance is { } world)
        {
            using var timing = GrenadeSpikeTrace.MeasureWeapon(scope.State.Roll.Definition, "rig impact explosion");
            ProjectileImpactExplosion.Trigger(shot,.25f,.5f);
        }
        ChestRigRuntime.BulletFinished(shot);
    }
}
[HarmonyPatch]
internal static class ChestRigFleshHitPatch
{
    internal sealed class Hit
    {internal Player Target=null!;internal EBodyPart Part;internal float Before;internal ChestRigRuntime.ShotScope Scope=null!;}
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()=>new[]{AccessTools.Method(typeof(BodyPartCollider),nameof(BodyPartCollider.ApplyHit)),AccessTools.Method(typeof(ArmorPlateCollider),nameof(ArmorPlateCollider.ApplyHit))};
    private static void Prefix(BodyPartCollider __instance,ref DamageInfoStruct damageInfo,out Hit? __state)
    {
        __state=null;
        if(damageInfo.DamageType!=EDamageType.Bullet||damageInfo.Weapon is not Weapon weapon||damageInfo.Damage<=0)return;
        if(__instance is ArmorPlateCollider||damageInfo.BlockedBy.HasValue||damageInfo.DeflectedBy.HasValue)return;
        var shot=ChestRigRuntime.FindScope(weapon,damageInfo.FireIndex);
        if(shot is null || Singleton<GameWorld>.Instance is not { } world) return;
        var profile = __instance.PlayerProfileID;
        Player? target = null;
        foreach (var candidate in world.RegisteredPlayers)
            if (candidate is Player player && player.ProfileId == profile) { target = player; break; }
        if(target is null)return;
        var part=__instance.BodyPartType;
        __state=new Hit{Target=target,Part=part,Before=target.ActiveHealthController.GetBodyPartHealth(part,false).Current,Scope=shot};
    }
    private static void Postfix(Hit? __state)
    {
        if(__state is not { } h)return;
        if(h.Target.ActiveHealthController.GetBodyPartHealth(h.Part,false).Current<h.Before)
            ChestRigRuntime.FleshHit(h.Scope,h.Target,h.Part,!h.Target.HealthController.IsAlive);
    }
}
