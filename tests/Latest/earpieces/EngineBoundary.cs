// Controlled engine boundaries. Production item rolls, runtime, audio adapters,
// timers, switching and Harmony prefix/finalizer bodies are linked unchanged.
// NativeContracts separately validates their real SPT 4.1.6 API and IL paths.
#pragma warning disable CS0649 // Native reflection writes the linked cache fields.
using EFT;
using EFT.Ballistics;
using EFT.ActiveHeadphones;
using EFT.InventoryLogic;
using EscapeFromBorderlands.Client;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace UnityEngine
{
    public static class Time { public static float time; }
    public class AudioClip {}
    public class AudioSource
    {
        public float volume { get; set; }
        public float StartedVolume; public int Starts;
        [MethodImpl(MethodImplOptions.NoInlining)] public void PlayScheduled(double time) { StartedVolume = volume; Starts++; }
    }
    public struct Vector2(float x, float y) { public float x=x,y=y; public float sqrMagnitude=>x*x+y*y; public float magnitude=>MathF.Sqrt(sqrMagnitude); }
    public struct Vector3(float x,float y,float z)
    {
        public float x=x,y=y,z=z;
        public static Vector3 operator *(Vector3 a,float v)=>new(a.x*v,a.y*v,a.z*v);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static float Distance(Vector3 a,Vector3 b){var d=a-b;return MathF.Sqrt(d.x*d.x+d.y*d.y+d.z*d.z);}
    }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T:class { public static T Instance=null!; public static bool Instantiated=>Instance is not null; }
}
public static class MonoBehaviourSingleton<T> where T:class { public static T Instance=null!; public static bool Instantiated=>Instance is not null; }
namespace EFT.InventoryLogic
{
    public enum EquipmentSlot { Earpiece,Pockets,TacticalVest,FirstPrimaryWeapon,SecondPrimaryWeapon,Holster,Backpack }
    public enum EItemAttributeId { EffectiveDistance,Loudness,ChangeMovementSpeed,BlindnessProtection,Ergonomics }
    public enum EItemAttributeDisplayType { Compact }
    public class Item
    {
        public List<ItemAttribute> Attributes=[];
        public string Id=Guid.NewGuid().ToString("N"),StringTemplateId="";
        public List<Item> Children=[]; public Dictionary<Type,object> Components=[];
        public IEnumerable<Item> GetAllItems(){yield return this;foreach(var child in Children)foreach(var i in child.GetAllItems())yield return i;}
        public T? GetItemComponent<T>() where T:class=>Components.TryGetValue(typeof(T),out var c)?(T)c:null;
    }
    public class Weapon:Item { public int FireRate=>600;public int SingleFireRate=>240;public float NativeAccuracy=1f,NativeRecoil;public float RecoilDelta=>NativeRecoil;public float ErgonomicsDelta=>0;public float CenterOfImpactDelta=>NativeAccuracy-1f;public float VelocityDelta=>0;public float TotalShotgunDispersion=>NativeAccuracy;public float GetTotalCenterOfImpact(bool b)=>NativeAccuracy;public Malfunction MalfState=new();public class Malfunction{public float OverheatFirerateMult=1f;} }
    public class Knife:Item {}
    public class ThrowWeap:Item {}
    public class Headphones:Item
    {
        public HeadphonesTemplate Template=new();
        public Headphones(){}
        [MethodImpl(MethodImplOptions.NoInlining)] public Headphones(string id,HeadphonesTemplate template){Id=id;Template=template;StringTemplateId=template.Id;}
    }
    public class MedKitComponent {}
    public class FoodDrinkComponent {}
    public class Slot { public Item? ContainedItem; }
    public class Equipment
    {
        private readonly Dictionary<EquipmentSlot,Slot> Slots=[];
        public Slot GetSlot(EquipmentSlot slot)=>Slots.TryGetValue(slot,out var s)?s:Slots[slot]=new Slot();
        public IEnumerable<Item> GetAllItems()=>Slots.Values.Where(s=>s.ContainedItem is not null).SelectMany(s=>s.ContainedItem!.GetAllItems());
    }
    public class Inventory { public Equipment Equipment=new(); }
}
namespace EFT
{
    public enum EDamageType { Bullet,GrenadeFragment,Explosion,Melee,Fall }
    public enum EBodyPart { Common,Head,Chest }
    public class GameClock { public DateTime Time=new(2026,10,30,12,0,0); public DateTime Calculate()=>Time; }
    public class GameWorld { public List<Player> RegisteredPlayers=[];public Player? MainPlayer;public GameClock GameDateTime=new(); public string LocationId="woods"; }
    public class MovementContext
    {
        public Player Player=null!;public bool IsSprintEnabled,IsInPronePose,IsGrounded=true;
        public Dictionary<Player.ESpeedLimit,float> Limits=[];
        public void RemoveStateSpeedLimit(Player.ESpeedLimit limit)=>Limits.Remove(limit);
        public void SetAimingSlowdown(bool aiming,float slow){if(aiming)Limits[Player.ESpeedLimit.Aiming]=slow;else Limits.Remove(Player.ESpeedLimit.Aiming);}
    }
    public class Physical
    {
        public Player Player=null!; public Stamina Stamina=new(),HandsStamina=new();
    }
    public class Stamina {public float Current=100,TotalCapacity=100;public void UpdateStamina(float amount)=>Current=amount;}
    public class Player
    {
        public enum ESpeedLimit { Aiming,HealthCondition,Terrain }
        public string ProfileId=Guid.NewGuid().ToString("N"),GroupId="";
        public Vector2 InputDirection;public Vector3 Position;public float PoseLevel=1f;
        public PlayerInventoryController InventoryController=new();public MovementContext MovementContext=new();
        public Physical Physical=new();public HealthSystem.ActiveHealthController ActiveHealthController=new();
        public HealthSystem.ActiveHealthController HealthController=>ActiveHealthController;
        public Animations.ProceduralWeaponAnimation ProceduralWeaponAnimation=new();public FirearmsAnimator HandsAnimator=new();
        public ItemHandsController? HandsController;
        public BetterSource? SpeechSource,NestedStepSoundSource,_gearSource;
        public LocalStep _specificStepAudioController=new();
        public BetterSource? SecondaryStep {get=>_specificStepAudioController._source;set=>_specificStepAudioController._source=value;}
        public class LocalStep {public BetterSource? _source;}
        public float SurfaceRange;
        public Player? LastAggressor;public DamageInfo LastDamageInfo;
        public Player(){InventoryController.Player=this;MovementContext.Player=this;Physical.Player=this;ActiveHealthController.Player=this;}
        public void OnDead(){}
        [MethodImpl(MethodImplOptions.NoInlining)] public bool CheckSurface(float range){SurfaceRange=range;return true;}
        [MethodImpl(MethodImplOptions.NoInlining)] public void PlayStepSound(){UpdateStepSourceRolloff();new SoundBank().Play(NestedStepSoundSource!);if(SecondaryStep is {} second)new SoundBank().Play(second);}
        public void DefaultPlay(){} public void PlayGearSound(SoundBank bank,float volume){} public void TryStartContusion(float time){}
        [MethodImpl(MethodImplOptions.NoInlining)] public void SetRolloff(BetterSource source,float rolloff)=>source.SetRolloff(rolloff);
        [MethodImpl(MethodImplOptions.NoInlining)] public void UpdateStepSourceRolloff(){SetRolloff(NestedStepSoundSource!,40);SecondaryStep?.SetRolloff(NestedStepSoundSource!.MaxDistance);}
        public class ValueBlender {public float Speed;}
        public class PlayerInventoryController {public Player Player=null!;public Inventory Inventory=new();}
        public class ItemHandsController {public Player _player=null!;public Player Player{get=>_player;set=>_player=value;}public Item Item=null!;public bool IsAiming;}
        public class FirearmController:ItemHandsController
        {
            public new Weapon Item {get=>(Weapon)base.Item;set=>base.Item=value;}
            public FirearmsAnimator FirearmsAnimator=new();public Buffs BuffInfo=new();public int Refreshes;private float float_3,float_4;public float AccuracyCache=>float_3;public float DispersionCache=>float_4;public FireOperation? CurrentOperation;
            public void RecalculateErgonomic(){}public void UpdateHipInaccuracy(){}
            public class FireOperation{public Weapon Weapon=null!;public float _singleFireCooldownTime;}
            public class AutomaticFireOperation:FireOperation{public float _shotsTime,float_7;}
            public void SetAnimatorAndProceduralValues()=>Refreshes++;
            public float GetWeaponDrawSpeedMultiplier()=>1f;public void Shot(){}
            public void Spawn(float speed,Action callback){}public void Drop(float speed,Action callback,bool fastDrop,Item? nextControllerItem=null){}
            public class RepairMalfunction {public Weapon Weapon=null!; public void OnMalfunctionOffEvent(){} }
        }
        public class BaseKnifeController:ItemHandsController {public void Drop(float speed,Action callback,bool fastDrop,Item? nextControllerItem=null){} }
        public class MedsController:ItemHandsController {}
    }
    public class Buffs {public float AimMovementSpeed;}
    public class FirearmsAnimator {public float UseTimeMultiplier=1;public void SetUseTimeMultiplier(float speed){var p=Comfort.Common.Singleton<GameWorld>.Instance.RegisteredPlayers.FirstOrDefault(p=>ReferenceEquals(p.HandsAnimator,this));if(p is not null)EarpieceMedicalAnimation.Record(this,p,speed);UseTimeMultiplier=speed*(p is null?1:EarpieceRuntime.MedicalSpeed(p));}public float Speed=1;public void SetAnimationSpeed(float speed)=>Speed=speed;}
}
namespace EFT.HealthSystem
{
    public class ActiveHealthController
    {
        public Player Player=null!;public bool IsAlive=true;public float Health=440,Maximum=440;
        public HealthValue GetBodyPartHealth(EBodyPart part,bool rounded)=>new(Health,Maximum);
        public void ApplyDamage(){}public void DoContusion(float applyTime,float strength){}
        public class MedEffect {public void Started(){}public void Residue(){} }
    }
    public record struct HealthValue(float Current,float Maximum);
}
namespace EFT.UI {
 public class ItemInfoWindowLabels {}
 public class ItemSpecificationPanel {
  public Item? _item; public int Seen,Compared;
  [MethodImpl(MethodImplOptions.NoInlining)]public void RecreateAttributeBars()=>Seen=_item?.Attributes.Count??0;
  [MethodImpl(MethodImplOptions.NoInlining)]public void Compare(Item compareItem)=>Compared=compareItem.Attributes.Count;
 }
 public class StaticIcons { [MethodImpl(MethodImplOptions.NoInlining)] public Enum GetAttributeIcon(Enum id){if(id is not EItemAttributeId)throw new Exception("Unexpected id type");return id;} }
}
namespace EFT.Animations
{
    public class ProceduralWeaponAnimation
    {
        public float _aimingSpeed=1,SwayFalloff=1;public Player.ValueBlender _aimSwayBlender=new();public int Refreshes;
        public ShotEffector Shootingg=new();public void UpdateWeaponVariables()=>Refreshes++;
    }
    public class RecoilProcessBase {public float ReturnSpeed=1,Intensity=1;public virtual void CalculateRecoil(float deltaTime){} }
    public class NewRotationRecoilProcess:RecoilProcessBase {public bool AutoFireOn;public float _autoFireSpeed=1;public override void CalculateRecoil(float deltaTime){} }
    public class BreathEffector {public Physical Physical=null!;public bool TremorOn;public float Intensity=1;public void Process(float deltaTime){} }
}
namespace EFT.Interactive
{
    public enum EInteractionType { Open,Close,Breach }
    public class InteractionResult {public EInteractionType InteractionType;}
    public class WorldInteractiveObject {}
    public class Door:WorldInteractiveObject {public bool IsBroken;}
}
public class ShotEffector {public RecoilEffect CurrentRecoilEffect=new();}
public class RecoilEffect
{
    public EFT.Animations.RecoilProcessBase HandPositionRecoilEffect=new(),CameraRotationRecoilEffect=new();
    public EFT.Animations.NewRotationRecoilProcess HandRotationRecoilEffect=new();
}
public class PlayerBridge {public Player? iPlayer;}
namespace EFT.Ballistics {public struct DamageInfo {public EDamageType DamageType;public Item? Weapon;public PlayerBridge? Player;}}
public class ItemAttribute(Enum id)
{
 public Enum Id=id;public string Name="";public bool LessIsGood;
 public Func<float> Base=()=>0;public Func<string> StringValue=()=>"",Tooltip=()=>"";
 public Func<EItemAttributeDisplayType> DisplayType=()=>EItemAttributeDisplayType.Compact;
}
namespace EFT.InventoryLogic {public class HeadphonesTemplate {public string Id="";public float RolloffMultiplier=1.2f,AmbientVolume=-10,DryVolume=-7,GunsCompressorSendLevel=-3;}}
public enum AISoundType {step,shoot}
public class GlobalEventDispatcher {public void PlaySound(object person,AISoundType type,float power){} }
public class BreachDoorState {public void ExecuteDoorInteraction(EFT.Interactive.WorldInteractiveObject interactive,EFT.Interactive.InteractionResult interactionResult,Player user){} }
public class BetterAudio
{
    public int Refreshes;public HeadphonesTemplate CurrentHeadphonesTemplate=new();public ActiveHeadphonesDefaultConfigurator Controller=new();
    public void ApplyCurrentHeadphonesTemplate(){Refreshes++;Controller.Configure(CurrentHeadphonesTemplate);}
}
namespace EFT.ActiveHeadphones {public class BaseActiveHeadphonesMixerDependentComponent
{
    public Dictionary<string,float> Values=[];public Dictionary<string,bool> Fades=[];
    [MethodImpl(MethodImplOptions.NoInlining)] public void SetMixerFloat(string paramName,float value,bool doFade=true){Values[paramName]=value;Fades[paramName]=doFade;}
}
public class ActiveHeadphonesDefaultConfigurator:BaseActiveHeadphonesMixerDependentComponent
{
    [MethodImpl(MethodImplOptions.NoInlining)]public void Configure(HeadphonesTemplate template)
    {
        SetMixerFloat("GunsCompressorSendLevel",template.GunsCompressorSendLevel);SetMixerFloat("GunsVolume",template.DryVolume);
        SetMixerFloat("EnvironmentVolume",template.DryVolume);SetMixerFloat("OutEnvironmentVolume",template.DryVolume);SetMixerFloat("AmbientVolume",template.AmbientVolume);
    }
}
}
public class FirearmControllerSharedMethods {public static void ApplyLightAndSoundHealthEffects(){} }
public class BaseSoundPlayer
{
    public PlayerBridge playersBridge=new(); private BetterSource? _clipsSource;
    public BetterSource? ItemSource {get=>_clipsSource;set=>_clipsSource=value;}
    public float PlayedVolume;
    [MethodImpl(MethodImplOptions.NoInlining)] public void PlayClipInternal(AudioClip clip,int rolloff,float volume,int priority,bool stereo)
    {PlayedVolume=volume;if(_clipsSource is {} source){source.SetRolloff(rolloff);source.Play(volume);}}
    [MethodImpl(MethodImplOptions.NoInlining)]public void PlayClip(int rolloff)
    {PlayClipInternal(new AudioClip(),rolloff,1,0,false);}
}
public class WeaponSoundPlayer:BaseSoundPlayer
{
    public SuperBetterAudioQueue? _queue;public bool DeferSources; public Action? DuringFire;
    [MethodImpl(MethodImplOptions.NoInlining)]public void FireBullet(){_queue??=new SuperBetterAudioQueue();if(!DeferSources&&_queue.AudioSources.Length==0)_queue.AudioSources=[new SuperSource(),new SuperSource()];_queue.SetRolloff(80);_queue.Enqueue();DuringFire?.Invoke();}
    [MethodImpl(MethodImplOptions.NoInlining)]public void StopFiringLoop()=>_queue?.Enqueue();
}
public class SuperBetterAudioQueue
{
    public BetterSource[] AudioSources=[];
    public bool SimulateInlinedVolumeUpdate;
    [MethodImpl(MethodImplOptions.NoInlining)]public void Enqueue(){}
    [MethodImpl(MethodImplOptions.NoInlining)]public void SetRolloff(float distance){foreach(var source in AudioSources)source.SetRolloff(distance);}
    [MethodImpl(MethodImplOptions.NoInlining)]public void Update(){foreach(var source in AudioSources)new SuperAudioSample{SimulateInlinedVolumeUpdate=SimulateInlinedVolumeUpdate}.PlayOn((SuperSource)source);}
}
public class SuperAudioSample
{
    public bool SimulateInlinedVolumeUpdate;
    [MethodImpl(MethodImplOptions.NoInlining)] public void PlayOn(SuperSource source)
    {
        // Mirror native SuperAudioSample -> SuperSource scheduling: raw channel writes,
        // raw cache capture, output update, then physical audio starts.
        source.source1.volume=.7f; source.source2.volume=.3f;
        source.SimulateInlinedVolumeUpdate=SimulateInlinedVolumeUpdate;
        source.PlayScheduled(0);
    }
}
namespace EFT
{
    public class SoundBank
    {
        [MethodImpl(MethodImplOptions.NoInlining)]public float PickClipsByDistance(float distance)=>distance;
        public float PlayedVolume;
        [MethodImpl(MethodImplOptions.NoInlining)]public void Play(BetterSource source,int environment=0,float distance=0,float volume=1,float blendParameter=0,bool forceStereo=false,bool oneShot=true)
        {PlayedVolume=volume;source.Play(volume,oneShot);}
    }
}
public abstract class BetterSource
{
    public UnityEngine.AudioSource source1 = new(){volume=.7f};
    public float PreOcclusionVolume=.7f,OcclusionVolumeFactor=.8f,FadeFactor=.9f;
    public float Output { get=>source1.volume; set=>source1.volume=value; }
    public float MaxDistance{get;set;}=50;
    public abstract void SetRolloff(float distance);
    [MethodImpl(MethodImplOptions.NoInlining)] public virtual void UpdateSourceVolume(float speed=1f)=>Output=Output+(PreOcclusionVolume*OcclusionVolumeFactor*FadeFactor-Output)*speed;
    [MethodImpl(MethodImplOptions.NoInlining)] public void Release(){}
    [MethodImpl(MethodImplOptions.NoInlining)] public virtual void Play(float volume=1,bool oneShot=true){PreOcclusionVolume=.7f*volume;UpdateSourceVolume();}
    [MethodImpl(MethodImplOptions.NoInlining)] public virtual void PlayScheduled(double time=0){}
}
public class SuperSource:BetterSource
{
    public UnityEngine.AudioSource source2 = new(){volume=.3f};
    private float _secondPreEffectsVolume=.3f;public float Output2 { get=>source2.volume; set=>source2.volume=value; }
    [MethodImpl(MethodImplOptions.NoInlining)]public override void Play(float volume=1,bool oneShot=true){base.Play(volume,oneShot);_secondPreEffectsVolume=.3f*volume;UpdateSourceVolume();}
    public bool SimulateInlinedVolumeUpdate;
    public float SecondCache=>_secondPreEffectsVolume;
    public void NativeScheduledOutput(){Output=PreOcclusionVolume*OcclusionVolumeFactor*FadeFactor;Output2=_secondPreEffectsVolume*OcclusionVolumeFactor*FadeFactor;}
    [MethodImpl(MethodImplOptions.NoInlining)] public override void PlayScheduled(double time=0)
    {
        base.PlayScheduled(time);
        PreOcclusionVolume=source1.volume;_secondPreEffectsVolume=source2.volume;
        if(SimulateInlinedVolumeUpdate)NativeScheduledOutput();else UpdateSourceVolume();
        source1.PlayScheduled(time);source2.PlayScheduled(time);
    }
    [MethodImpl(MethodImplOptions.NoInlining)] public override void SetRolloff(float distance)=>MaxDistance=distance;
    [MethodImpl(MethodImplOptions.NoInlining)] public override void UpdateSourceVolume(float speed=1f){base.UpdateSourceVolume(speed);Output2=Output2+(_secondPreEffectsVolume*OcclusionVolumeFactor*FadeFactor-Output2)*speed;}
}
public class SimpleSource:BetterSource {
 public float OneShotGain=1;public float EffectiveOutput=>Output*OneShotGain;
 [MethodImpl(MethodImplOptions.NoInlining)]public override void Play(float volume=1,bool oneShot=true){OneShotGain=oneShot?volume:1;PreOcclusionVolume=.7f*(oneShot?1:volume);UpdateSourceVolume();}
 [MethodImpl(MethodImplOptions.NoInlining)]public override void SetRolloff(float distance)=>MaxDistance=distance;}
public class SuperSourceDistant:BetterSource {[MethodImpl(MethodImplOptions.NoInlining)]public override void SetRolloff(float distance)=>MaxDistance=distance;private float _secondPreEffectsVolume=.2f;public float SecondCache=>_secondPreEffectsVolume;public float Output2;[MethodImpl(MethodImplOptions.NoInlining)]public override void UpdateSourceVolume(float speed=1f){base.UpdateSourceVolume(speed);Output2=Output2+(_secondPreEffectsVolume*OcclusionVolumeFactor*FadeFactor-Output2)*speed;} }
namespace EscapeFromBorderlands.Client
{
    internal static class Plugin {internal static Logger LogSource=new();}
    internal class Logger {public void LogWarning(object message){} public void LogInfo(string s){} }
    internal static class BackpackRuntime
    {
        internal static bool WeatherBag;
        internal static float InventoryNoise=1;internal static float Noise(Player p,string kind)=>kind=="inventory"?InventoryNoise:1;
        internal static bool Is(Player? p,string key)=>p is not null&&WeatherBag&&key=="BP21-3";
        internal static Player? Actor(object? obj)=>obj switch {Player p=>p,Player.ItemHandsController h=>h.Player,Physical p=>p.Player,MovementContext m=>m.Player,EFT.HealthSystem.ActiveHealthController h=>h.Player,_=>null};
        internal static void RestoreLeg(Player p,float fraction)=>p.Physical.Stamina.UpdateStamina(Math.Min(p.Physical.Stamina.TotalCapacity,p.Physical.Stamina.Current+p.Physical.Stamina.TotalCapacity*fraction));
    }
    internal static class BackpackNative
    {
        internal static Player? WeaponOwner(Weapon weapon)=>Comfort.Common.Singleton<GameWorld>.Instance?.RegisteredPlayers.FirstOrDefault(p=>p.HandsController?.Item==weapon);
    }
    internal static class AdvancedMeleeRuntime
    {
        internal static bool Crouched(Player p)=>!p.MovementContext.IsInPronePose&&p.PoseLevel<.5f;
        internal static bool Enemy(Player p,Player other)=>p!=other&&(p.GroupId.Length==0||p.GroupId!=other.GroupId);
    }
    internal static class ChestRigRuntime
    {
        internal static object? Get(Player? p)=>null;
        internal static void RestoreArms(Player p,float fraction)=>p.Physical.HandsStamina.UpdateStamina(Math.Min(p.Physical.HandsStamina.TotalCapacity,p.Physical.HandsStamina.Current+p.Physical.HandsStamina.TotalCapacity*fraction));
        internal static float Health(Player p)=>p.ActiveHealthController.Health/p.ActiveHealthController.Maximum;
    }
    internal static class ChestRigNative{internal static object? For(Weapon w)=>null;}
    internal static class GrenadeBuffs{internal static bool HasState(Player? p)=>false;}
    internal static class ReusableGrenadeRuntime
    {
        internal static readonly GrenadeChargeLedger Charges=new(()=>Time.time);
        internal static bool IsReusable(Item? i)=>i is ThrowWeap&&RarityResolver.TryGetDefinition(i,out var d)&&d.GrenadeChargesPerRaid>0;
        internal static int Remaining(Item i)=>3;
    }
    internal static class LegendaryGrenadeSettings {internal static float Interval(VariantDefinition d)=>d.GrenadeRechargeSeconds;}
}

public enum EModClass {Weapon}
namespace EFT.UI {
 public enum EInventorySoundType {Pickup}
 public class GUISounds {
  public float Observed;public bool Fail;
  [MethodImpl(MethodImplOptions.NoInlining)]public float GetCommonSoundsVolume()=>1;
  [MethodImpl(MethodImplOptions.NoInlining)]public void PlayItemSound(string soundGroup,EInventorySoundType soundType,bool single){Observed=GetCommonSoundsVolume();if(Fail)throw new Exception("audio interrupted");}
  [MethodImpl(MethodImplOptions.NoInlining)]public bool PlayItemSound(EModClass itemClass){Observed=GetCommonSoundsVolume();return true;}
 }
}
