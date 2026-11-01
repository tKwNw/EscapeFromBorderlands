// Controlled engine boundary. Tests run production spawning, applications,
// rig shot tracking, and explosion dispatch. Unity physics remains in-game.
using System.Collections;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;
namespace UnityEngine {
 public static class Mathf {public static float Sqrt(float f)=>(float)Math.Sqrt(f);}
 public sealed class WaitForSeconds {public float Seconds;public WaitForSeconds(float s)=>Seconds=s;}
 public struct Quaternion {
  private System.Numerics.Quaternion value;
  public static Quaternion AngleAxis(float degrees,Vector3 axis)=>new(){value=System.Numerics.Quaternion.CreateFromAxisAngle(new(axis.x,axis.y,axis.z),degrees*(float)Math.PI/180f)};
  public static Vector3 operator *(Quaternion q,Vector3 v){var n=System.Numerics.Vector3.Transform(new(v.x,v.y,v.z),q.value);return new(n.X,n.Y,n.Z);}
  public static Quaternion operator *(Quaternion a,Quaternion b)=>new(){value=a.value*b.value};
 }
}
public partial class Shot {
 public Item Ammo=null!;public Shot? Parent;public List<Shot> Fragments=new();public string PlayerProfileID="";public int FragmentIndex;public bool HasAchievedTarget,ShotProcessed,IsForwardHit=true;
 public float ArmorDamage=.5f,PenetrationChance=1,RicochetChance,FragmentationChance,DeviationChance=1;public int MinFragmentsCount,MaxFragmentsCount;
 public object? _defaultHitBody,Randoms;public Vector3 Direction=new(0,0,1),HitPoint=new(0,0,10),HitNormal=new(0,0,-1);
 public static Shot Create(Item ammo,int fragmentIndex,int seed,Vector3 origin,Vector3 direction,float initialSpeed,float speed,float mass,float diameter,float damage,float penetration,float penetrationChance,float ricochet,float fragmentation,float deviation,int minFragments,int maxFragments,object? collider,object? randoms,float coefficient,string player,Item? weapon,int fireIndex,Shot? parent){
  var shot=new Shot{Ammo=ammo,FragmentIndex=fragmentIndex,StartPosition=origin,Direction=direction,StartVelocity=direction*speed,_currentVelocity=direction*speed,InitialSpeed=initialSpeed,Speed=speed,BulletMassGram=mass,BulletDiameterMilimeters=diameter,Damage=damage,PenetrationPower=penetration,PenetrationChance=penetrationChance,RicochetChance=ricochet,FragmentationChance=fragmentation,DeviationChance=deviation,MinFragmentsCount=minFragments,MaxFragmentsCount=maxFragments,_defaultHitBody=collider,Randoms=randoms,BallisticCoefficient=coefficient,PlayerProfileID=player,Weapon=weapon,FireIndex=fireIndex,Parent=parent};
  shot.TrajectoryInfo.Initialize(origin,shot.StartVelocity,mass,diameter,coefficient,false);return shot;
 }
 public static void Release(Shot shot){ChestRigRuntimeBridge.Release(shot);}
}
internal static class ChestRigRuntimeBridge {internal static void Release(Shot s)=>EscapeFromBorderlands.Client.ChestRigRuntime.BulletFinished(s);}
namespace EFT.Ballistics {
 public class BallisticsCalculator {
  public readonly List<Shot> Shots=new();public void Shoot(Shot shot)=>Shots.Add(shot);
  public Shot CreateShot(Ammo ammo,Vector3 origin,Vector3 direction,int fireIndex,string player,Item weapon,float speedFactor=1,int fragmentIndex=0)=>Shot.Create(ammo,fragmentIndex,0,origin,direction,1000,1000,10,9,100,30,1,0,0,1,0,0,null,null,1,player,weapon,fireIndex,null);
 }
}
namespace EFT.Interactive {
 public enum EDoorState {Shut,Open,Locked,Interacting,Breaching}
 public sealed class Door {public EDoorState DoorState;public bool IsBroken;}
}
namespace EscapeFromBorderlands.Client {
 internal static partial class LegendaryRuntime {
  internal static void NotifyReloadCompleted(Weapon w,bool emergency){}
  internal static readonly Dictionary<Shot,EffectApplication> Applications=new();
  internal static EffectApplication RegisterShot(Weapon w,Ammo a,string player,int index,VariantDefinition d)=>EffectApplication.Empty;
  internal static void Associate(Shot shot,EffectApplication a,bool bonusProjectile=false){if(a.Definition is not null)Applications[shot]=bonusProjectile?a.AsBonusProjectile():a;}
  internal static void Release(Shot shot)=>Applications.Remove(shot);
 }
}
namespace EFT.InventoryLogic {
 public interface IExplosiveItem {
  Vector3 Blindness{get;}Vector3 Contusion{get;}Vector3 ArmorDistanceDistanceDamage{get;}float MinExplosionDistance{get;}float MaxExplosionDistance{get;}int FragmentsCount{get;}float GetStrength{get;}bool IsDummy{get;}Ammo CreateFragment();
 }
}
public sealed class GlobalEventDispatcher {}
public static class ProjectileTestEvents {
 public sealed record Explosion(IExplosiveItem Payload,Vector3 Position,bool Fallback);
 public static readonly List<Explosion> Explosions=new();public static int Visuals;public static bool FailVisual;
 public static void Reset(){Explosions.Clear();Visuals=0;FailVisual=false;Comfort.Common.Singleton<GlobalEventDispatcher>.Instance=null!;Comfort.Common.Singleton<Systems.Effects.Effects>.Instance=new();}
}
namespace Systems.Effects {
 public sealed class Effects {public void EmitGrenade(string name,Vector3 point,Vector3 normal,int f){ProjectileTestEvents.Visuals++;if(ProjectileTestEvents.FailVisual)throw new Exception("VFX failure");}}
}
public static class Grenade {
 public static object GetDamageInfo(Item weapon,string player,Vector3 position,bool isAI)=>new();
 public static void Explosion(object? instance,IExplosiveItem payload,Vector3 position,string player,EFT.Ballistics.BallisticsCalculator calculator,Item? weapon,Vector3 direction)=>ProjectileTestEvents.Explosions.Add(new(payload,position,false));
 public static object smethod_0(Item? weapon,string player,Vector3 p,bool b)=>new();
}
public static class ExplosionSharedMethods {
 public static void Explosion(IExplosiveItem payload,Vector3 position,string player,EFT.Ballistics.BallisticsCalculator calculator,Item? weapon,Func<object> damage,float multiplier,float angle,Vector3? direction,bool deadly)=>ProjectileTestEvents.Explosions.Add(new(payload,position,true));
}

public delegate void Callback(TestReloadResult result);
public sealed class TestReloadResult {public bool Succeed;}

// FX rendering is exercised with real Harmony in the separate explosionfx
// suite. This dispatch boundary keeps the real producer/damage-route tests.
namespace EscapeFromBorderlands.Client {
 internal static class LegendaryExplosionFx {
  internal static void Impact(Systems.Effects.Effects effects,Vector3 p,Vector3 normal,float radius,float volume)=>effects.EmitGrenade("Grenade_new",p,normal,(int)volume);
 }
}
