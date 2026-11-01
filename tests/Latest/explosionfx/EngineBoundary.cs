// Explicit engine doubles. The production FX adapter and Harmony patches are
// linked unchanged. NativeContracts verifies the actual 4.1.6 call paths/IL.
using System.Runtime.CompilerServices;
using UnityEngine;
using Systems.Effects;
namespace UnityEngine {
 public static class Time {public static float time;}
 public struct Vector3(float x,float y,float z) {
  public float x=x,y=y,z=z;public static Vector3 zero=>default;public static Vector3 one=>new(1,1,1);public static Vector3 up=>new(0,1,0);
  public static Vector3 operator *(Vector3 a,float s)=>new(a.x*s,a.y*s,a.z*s);
  public static Vector3 Scale(Vector3 a,Vector3 b)=>new(a.x*b.x,a.y*b.y,a.z*b.z);
 }
 public struct Quaternion {public static Quaternion identity=>default;}
 public class Object {
  public bool Destroyed;public static int Created,DestroyedCount;public static List<GameObject> Clones=[];
  public static GameObject Instantiate(GameObject original){var clone=new GameObject();clone.transform.localScale=original.transform.localScale;foreach(var p in original.Components.OfType<ParticleSystem>()){var q=new ParticleSystem();var main=q.main;main.scalingMode=p.main.scalingMode;clone.Add(q);}Created++;Clones.Add(clone);return clone;}
  public static void Destroy(Object value){value.Destroyed=true;DestroyedCount++;}
 }
 public class Transform {public Transform? parent;public Vector3 localPosition;public Quaternion localRotation;
  public void SetParent(Transform? value,bool worldPositionStays){parent=value;}
  public bool IsChildOf(Transform other){for(var p=parent;p!=null;p=p.parent)if(ReferenceEquals(p,other))return true;return false;}
  public Vector3 ParentScale=Vector3.one;public Vector3 lossyScale=>Vector3.Scale(Vector3.Scale(localScale,ParentScale),parent?.lossyScale??Vector3.one);public Vector3 localScale=Vector3.one,position;public Quaternion rotation;public void SetPositionAndRotation(Vector3 p,Quaternion r){position=p;rotation=r;}}
 public class Component:Object {public GameObject gameObject=null!;public Transform transform=>gameObject.transform;}
 public class GameObject:Object {
  public static List<GameObject> All=[];public GameObject(string name=""){All.Add(this);}public Transform transform=new();public bool Active;public List<Component> Components=[];
  public void Add(Component c){Components.Add(c);c.gameObject=this;}
  public Component GetComponent(Type type)=>Components.Single(c=>c.GetType()==type);
  public Component[] GetComponentsInChildren(Type type,bool includeInactive)=>All.Where(g=>!g.Destroyed&&(ReferenceEquals(g,this)||g.transform.IsChildOf(transform))).SelectMany(g=>g.Components).Where(c=>c.GetType()==type).ToArray();
  public void SetActive(bool active)=>Active=active;
 }
 public enum ParticleSystemScalingMode {Local,Hierarchy}
 public class ParticleSystem:Component {
  public ParticleSystemScalingMode Mode=ParticleSystemScalingMode.Local;
  public struct Main(ParticleSystem particle) {public ParticleSystemScalingMode scalingMode {get=>particle.Mode;set=>particle.Mode=value;}}
  public Main main=>new(this);public bool Alive,Throw;public int Plays,Clears;
  public Vector3 WorldScaleAtPlay;public void Play(bool children){WorldScaleAtPlay=transform.lossyScale;Alive=true;Plays++;}public void Clear(bool children){Clears++;}public void Stop(bool children){Alive=false;}
  public bool IsAlive(bool children){if(Throw)throw new Exception("engine unavailable");return Alive;}
 }
}
namespace EFT.InventoryLogic {public class Item{public T? GetItemComponent<T>() where T:class=>null;}}
public interface IExplosiveItem {float MaxExplosionDistance{get;}Vector3 Blindness{get;}}
public class ThrowWeap:IExplosiveItem {
 public bool Legendary,SmokeChild,Fail;public string Category="frag";public float BaseRadius=10,FinalRadius=20,BaseFlash=10,FinalFlash=20;
 public float MaxExplosionDistance=>BaseRadius;public Vector3 Blindness=>new(0,BaseFlash,0);
}
namespace EFT.Ballistics {public class BallisticCollider{}}
namespace EFT {
 public enum EPointOfView {ThirdPerson,FirstPerson}
 public class GameWorld{}
 public class Grenade {
  public ThrowWeap WeaponSource=new();public Action? Nested;public bool Throw;public int DamageCalls;
  [MethodImpl(MethodImplOptions.NoInlining)]public void Explosion(){Fixture.Effects.EmitGrenade("native",default,Vector3.up,1);Nested?.Invoke();if(Throw)throw new Exception("native failure");DamageCalls++;}
 }
}
public abstract class Emitter {public abstract void Emit(Vector3 position,Vector3 velocity);}
public class ParticleSystemAdapter:Emitter {
 // Native sampled lifetime/speed/start-size buffers used by impact calibration.
 public float[] _lifeTimes=[2],_speeds=[2],_sizes=[2];public List<(Vector3 Velocity,float Size)> Emissions=[];
 public override void Emit(Vector3 position,Vector3 velocity)=>Emit(position,velocity,1);
 public void Emit(Vector3 position,Vector3 velocity,float sizeMult)=>Emissions.Add((velocity,_sizes[0]*sizeMult));
}
public class SparkEmitter:Emitter {public Vector3 Velocity;public override void Emit(Vector3 p,Vector3 v)=>Velocity=v;}
namespace EFT.Particles {
 public class BasicParticleSystemMediator {
  private ParticleSystem[] _particleSystems;private int _index;public int NativePlays;
  public BasicParticleSystemMediator(params ParticleSystem[] particles){_particleSystems=particles;}
  [MethodImpl(MethodImplOptions.NoInlining)]public void Emit(Vector3 position,Quaternion rotation){if(_particleSystems.Length>0){var p=_particleSystems[_index];p.transform.SetPositionAndRotation(position,rotation);p.Play(true);_index=(_index+1)%_particleSystems.Length;NativePlays++;}}
 }
}
namespace Systems.Effects {
 public class Effects {
  private Dictionary<string,Effect> dictionary_1=new();
  public Effects(){Template=new();}
  public Effect Template {get=>dictionary_1["native"];set {dictionary_1["native"]=value;dictionary_1["Grenade_new"]=value;}}
  public sealed record Entry(Effect Effect,Vector3 Position,Vector3 Normal,EFT.Ballistics.BallisticCollider? HitCollider,bool WithDecal,float Volume,bool IsKnife,bool IsHitPointVisible,bool IsGrenade,EFT.EPointOfView Pov);
  public List<Entry> Queue=[];public bool FailQueue;public Action? DuringEmit;
  [MethodImpl(MethodImplOptions.NoInlining)]public void EmitGrenade(string ename,Vector3 position,Vector3 normal,float volume){AddEffectEmit(Template,position,normal,null,true,volume,false,true,true,0);}
  [MethodImpl(MethodImplOptions.NoInlining)]public void AddEffectEmit(Effect effect,Vector3 position,Vector3 normal,EFT.Ballistics.BallisticCollider? hitCollider,bool withDecal,float volume,bool isKnife,bool isHitPointVisible,bool isGrenade,EFT.EPointOfView pov=0){if(FailQueue)throw new Exception("visual queue unavailable");Queue.Add(new(effect,position,normal,hitCollider,withDecal,volume,isKnife,isHitPointVisible,isGrenade,pov));}
  public void Drain()=>Update();
  [MethodImpl(MethodImplOptions.NoInlining)]public void Update(){foreach(var entry in Queue.ToArray())entry.Effect.Emit(entry.Position,entry.Normal,entry.HitCollider!,entry.WithDecal,entry.Volume,entry.IsKnife,entry.IsHitPointVisible,entry.IsGrenade,entry.Pov);Queue.Clear();}
  public class Effect {
   public float LightRange=3,WindRadius=4,ParticlesShift=.1f;public EFT.Particles.BasicParticleSystemMediator? BasicParticleSystemMediator;
   public ParticleSys[] Particles=[];public Action? DuringPlayback;public bool Throw;
   [MethodImpl(MethodImplOptions.NoInlining)]public void Emit(Vector3 position,Vector3 normal,EFT.Ballistics.BallisticCollider hitCollider,bool withDecal,float volume,bool isKnife,bool isHitPointVisible,bool isGrenade,EFT.EPointOfView pov){DuringPlayback?.Invoke();if(Throw)throw new Exception("native visual failure");BasicParticleSystemMediator?.Emit(position,Quaternion.identity);foreach(var p in Particles)p.Emit(position,normal,0);}
   public class ParticleSys {
    public Emitter Particle=new ParticleSystemAdapter();public int Branch;
    // Six separate native emission branches, to exercise the real transpiler.
    [MethodImpl(MethodImplOptions.NoInlining)]public void Emit(Vector3 position,Vector3 normal,float distance){switch(Branch){case 0:Particle.Emit(position,normal);break;case 1:Particle.Emit(position,normal);break;case 2:Particle.Emit(position,normal);break;case 3:Particle.Emit(position,normal);break;case 4:Particle.Emit(position,normal);break;default:Particle.Emit(position,normal);break;}}
   }
  }
 }
}
public static class Fixture {public static Effects Effects=new();}
namespace EscapeFromBorderlands.Client {
 internal static class Plugin{internal static Log LogSource=new();}internal class Log{public List<string> Warnings=[],Infos=[];public void LogWarning(string text)=>Warnings.Add(text);public void LogInfo(string text)=>Infos.Add(text);}
 internal static class LegendaryGrenadeEffects {
  internal class Definition{public string RuntimeKey="GR15",GrenadeCategory="";}internal class RollType{public Definition Definition=new();}
  internal class Payload{public bool SmokeChild;public RollType Roll=new();}
  internal static bool Try(ThrowWeap item,out Payload payload){if(item.Fail)throw new Exception("adapter boundary failure");payload=new(){SmokeChild=item.SmokeChild,Roll=new(){Definition=new(){GrenadeCategory=item.Category}}};return item.Legendary;}
  private class Explosive(ThrowWeap item):IExplosiveItem{public float MaxExplosionDistance=>item.FinalRadius;public Vector3 Blindness=>new(0,item.FinalFlash,0);}
  internal static IExplosiveItem Explosion(IExplosiveItem source,ThrowWeap item)=>new Explosive(item);
 }
}
