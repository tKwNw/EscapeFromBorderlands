using System;using System.Linq;using Comfort.Common;using EFT;using EFT.Ballistics;using EFT.InventoryLogic;using UnityEngine;
namespace EscapeFromBorderlands.Client;
internal static class ChestRigBallistics
{
    internal static void Apply(Shot shot,Ammo ammo,string playerId,ref EffectApplication application)
    {
        if(ChestRigCombat.FragmentFactor(ammo) is { } fragmentFactor)
        {shot.Damage*=fragmentFactor;return;}
        var scope=ChestRigRuntime.CurrentShot;
        if(scope is null)
        {
            if(shot.Weapon is Launcher&&ChestRigRuntime.Get(Singleton<GameWorld>.Instance?.RegisteredPlayers.OfType<Player>().FirstOrDefault(p=>p.ProfileId==playerId))?.Key=="AR21-2")
            {shot.Damage*=1.3f;shot.InitialSpeed*=1.4f;shot.Speed*=1.4f;shot.StartVelocity*=1.4f;shot._currentVelocity=shot.StartVelocity;shot.TrajectoryInfo.Initialize(shot.StartPosition,shot.StartVelocity,shot.BulletMassGram,shot.BulletDiameterMilimeters,shot.BallisticCoefficient,shot.Player?.IsAI==true);}
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
            ProjectileImpactExplosion.Trigger(shot,.25f,.5f);
        }
        ChestRigRuntime.BulletFinished(shot);
    }
}
