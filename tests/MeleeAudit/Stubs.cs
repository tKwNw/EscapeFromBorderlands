using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public static class Time { public static float time; }
    public class Component { public object? Parent; public T? GetComponentInParent<T>() where T:class => this as T ?? Parent as T; }
    public class GameObject:Component {public Component transform=>this;}
    public struct Ray {}
    public struct RaycastHit {public float distance {get;set;}}
    public static class Random {public static float value=.5f;}
    public struct Vector2 { public float x,y; public Vector2(float a,float b){x=a;y=b;} public float sqrMagnitude=>x*x+y*y; }
    public struct Vector3
    {
        public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;}
        public static Vector3 up=>new(0,1,0);public float magnitude=>MathF.Sqrt(sqrMagnitude);public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;public static Vector3 operator*(Vector3 a,float k)=>new(a.x*k,a.y*k,a.z*k); public static Vector3 forward=>new(0,0,1); public float sqrMagnitude=>x*x+y*y+z*z;
        public Vector3 normalized {get {var n=MathF.Sqrt(sqrMagnitude);return n>0?new(x/n,y/n,z/n):default;}}
        public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
    }
    public struct Quaternion
    {
        public float Yaw;
        public static Vector3 operator*(Quaternion a,Vector3 b)=>new(MathF.Cos(a.Yaw)*b.x+MathF.Sin(a.Yaw)*b.z,b.y,-MathF.Sin(a.Yaw)*b.x+MathF.Cos(a.Yaw)*b.z);
    }
}
namespace Comfort.Common { public static class Singleton<T> where T:class { public static T Instance=null!; public static bool Instantiated=>Instance!=null; } }
public enum EDamageType { Melee,Bullet,Fall,Undefined,Explosion,GrenadeFragment,ThermobaricExplosion }
public enum AISoundType { step,gun }
public class GlobalEventDispatcher { public void PlaySound(EFT.IPlayer person,UnityEngine.Vector3 position,float power,AISoundType type){} }
public struct DamageInfo
{
    public EDamageType DamageType;public EFT.InventoryLogic.Item? Weapon;public IPlayerOwner? Player;
    public float Damage,PenetrationPower;public int? BlockedBy,DeflectedBy;public object? HittedBallisticCollider;public bool BleedBlock;
}
public interface IPlayerOwner { EFT.IPlayer iPlayer {get;} }
public class Owner(EFT.Player player):IPlayerOwner { public EFT.IPlayer iPlayer=>player; }
public class BodyPartCollider { public string PlayerProfileID="";public EFT.EBodyPart BodyPartType; }
public class IKnifeComponentTemplate
{
    public int KnifeHitSlashDam=20,KnifeHitStabDam=30;public float KnifeHitRadius=.6f;
}
public class Compute<T>(Func<T> compute){public virtual T Value=>compute();public void SetDirty(){}public static implicit operator T(Compute<T> v)=>v.Value;}
public class Deferred<T>(Func<T> compute):Compute<T>(compute) {public override T Value=>base.Value;public static implicit operator T(Deferred<T> v)=>v.Value;}
public class Stamina
{
    public float Current=50,Multiplier=1,TotalCapacity=100;public int Events;
    public List<Physical.Consumption> Consumptions=new();
    public void InvokeChangedAction()=>Events++;
    public void UpdateStamina(float value){Current=value;InvokeChangedAction();}
    public float Consume(Physical.Consumption consumption,bool fromDamage=false){Current=Math.Max(0,Current-consumption.Delta.Value*Multiplier);return 0;}
    public void Process(float dt){Current=Math.Max(0,Current-Consumptions.Sum(x=>x.Delta.Value)*dt*Multiplier);}
}
public class Physical
{
    public EFT.Player _player=null!; public bool CanJump=true; public int WeightUpdates; public void OnWeightUpdated()=>WeightUpdates++;
    public Dictionary<EConsumptionType,Consumption> Consumptions=new() { [EConsumptionType.Jump]=new(EConsumptionType.Jump) };
    public enum EConsumptionType { Sprint,Jump,Walk,Aim,Melee,VaultLegs,ClimbLegs,VaultHands,ClimbHands }
    public class Consumption(EConsumptionType t){public EConsumptionType ConsumptionType=t;public Compute<float> Delta=new(()=>10);}
    public Stamina Stamina=new(),HandsStamina=new(),Oxygen=new();
}
public class JumpPlayerState
{
    public enum EJumpState {PushingFromTheGround,Jump,Bumbped}
    public EFT.MovementContext MovementContext=null!;public float _skilledBoost=1,_stateTime,_liftDelay,_potentialH,_timeDescending; public bool _hasLastHeight; public int _framesWithoutMotion; public EJumpState _jumpState=EJumpState.Jump; public UnityEngine.Vector3 _liftForce; public void Enter(){}
}
public class HasItemRequirement
{
    public string Id="";public bool Met(EFT.Player player,EFT.Interactive.ExfiltrationPoint point)=>false;
}
public static class DamageHelper { public static DamageInfo MedKitUse=>new(); }
public static class LocalizationExtensions { public static string Localized(string value)=>value; }
public static class ShotSharedMethods {public static float Chance;public static ArmorResistance RealResistance(float durability,float maximum,int armorClass,float penetration)=>new();}
public class ArmorResistance {public float GetPenetrationChance(float penetration)=>ShotSharedMethods.Chance;}
namespace EFT.Ballistics { public class BallisticCollider{} }
public class InteractiveProxy:UnityEngine.Component {public EFT.Interactive.Door? Link;}
public class EFTHardSettings {public static EFTHardSettings Instance=new();public float BEHIND_CAST=.1f;}
namespace EFT.Interactive {
public class ExfiltrationPoint{} public enum EDoorState {Shut,Locked,Open}
public class Door {public bool CanBeBreached=true,CanInteractWithBreach=true,Operatable=true,IsBroken,NoInteractionsAllowed,Refuse,ThrowBefore,ThrowAfter;public EDoorState DoorState;public int Breaches,OpenInteractions;public Action? Callback;public EFT.Player? User;public void SetUser(EFT.Player p)=>User=p;public void Interact(EInteractionType type){OpenInteractions++;if(ThrowBefore)throw new Exception("before");Callback?.Invoke();if(!Refuse)DoorState=EDoorState.Open;if(ThrowAfter)throw new Exception("after");}public void KickOpen(UnityEngine.Vector3 p,bool confirmed){Breaches++;IsBroken=true;}}
}
namespace EFT
{
    public enum EBodyPart {Common,Head,Chest,Stomach,LeftArm,RightArm,LeftLeg,RightLeg}
    public enum EInteractionType {Open,Unlock}
    public interface IPlayer {string ProfileId{get;} }
    public class Player:IPlayer
    {
public float CalculateMovementVolumeDefaultMultiplier() => 1;

        public string ProfileId{get;set;}=Guid.NewGuid().ToString();public UnityEngine.Vector3 Position;public string GroupId=""; public float PoseLevel=1; public Profile Profile=new();public AIData AIData=new();
        public bool IsAI;public UnityEngine.Ray InteractionRay=>new();public UnityEngine.Vector2 InputDirection;public ItemHandsController? HandsController;
        public HealthSystem.ActiveHealthController ActiveHealthController;public HealthSystem.ActiveHealthController HealthController=>ActiveHealthController;
        public InventoryLogic.InventoryController InventoryController=new();public Physical Physical=new();public MovementContext MovementContext;
        public Player(){ActiveHealthController=new(this);MovementContext=new(this);Physical._player=this;}
        public void Jump(){}
        public struct KnifeRaycastHit { public UnityEngine.Component collider; }
        public enum EKickType {Slash,Stab}
        public class Animator {public float Speed=1;public void SetAnimationSpeed(float s)=>Speed=s;}
        public class ItemHandsController {protected Player _player;public InventoryLogic.Item Item;public bool Destroyed;public ItemHandsController(Player player,InventoryLogic.Item item){_player=player;Item=item;} }
        public class FirearmController:ItemHandsController
        {
            public Animator FirearmsAnimator=new();
            public FirearmController(Player player, InventoryLogic.Item item):base(player,item){}
            public void Spawn(float animationSpeed,Action callback){}
        }
        public class BaseKnifeController:ItemHandsController
        {
public void ProcessOverlap(KnifeRaycastHit hit) { }

            public EKickType LastKickType;public object? CurrentKnifeOperation;public Animator FirearmsAnimator=new();
            public BaseKnifeController(Player p,InventoryLogic.Item i):base(p,i){}
            public void method_6(Player.KnifeRaycastHit hit){}public void SetMeleeSpeed(float speed){}public void Drop(float animationSpeed,Action callback,bool fastDrop){}public void Destroy(){}
        }
        public class KnifeController:BaseKnifeController
        {
            public KnifeController(Player p,InventoryLogic.Item i):base(p,i){}
            public void Spawn(float animationSpeed,Action callback){}
            public class Kick(KnifeController c){public KnifeController Controller=c;public void Start(EKickType type){} }
            public class Remove(KnifeController c){public KnifeController Controller=c;public void Start(Action action,bool fastDrop){} }
            public class SpawnOperation(KnifeController c){public KnifeController Controller=c;public void WeaponAppeared(){} }
        }
    }
    public class GameWorld
    {
        public static UnityEngine.GameObject? InteractionTarget;public static float InteractionDistance;public static int InteractionQueries;public static UnityEngine.GameObject? FindInteractable(UnityEngine.Ray ray,out UnityEngine.RaycastHit hit){InteractionQueries++;hit=new(){distance=InteractionDistance};return InteractionTarget;}
        public GameDateTime GameDateTime=new();public List<Player> AllAlivePlayersList=new();public Player? GetAlivePlayerByProfileID(string id)=>AllAlivePlayersList.FirstOrDefault(p=>p.ProfileId==id&&p.HealthController.IsAlive);
        public void HackShot(DamageInfo info){}
    }
    public class Motor {public float SpeedLimit=2;}
    public class MovementContext
    {
public void SetVaultingSpeed() { }

        protected Player _player;public UnityEngine.Quaternion TransformRotation;public Motor CharacterController=new();public bool IsGrounded=true,IsInPronePose,IsSprintEnabled,CanJump=true;public object? CurrentState;public UnityEngine.Vector3 TransformPosition=>_player.Position; public float VaultingSpeed;public VaultAnimator PlayerAnimator=new();public void method_17(){}
        public MovementContext(Player player){_player=player;} public void DirectApplyMotion(UnityEngine.Vector3 motion,float deltaTime){}
    }
    public class KnifeCollider {public Action<Player.KnifeRaycastHit>? OnHit;public float MaxDistance=1;public void OnFire(){}public void ManualUpdate(){} }
}
namespace EFT.InventoryLogic
{
    public class ArmorComponent
    {
        public class ArmorItem {public int Id=2;}
        public class RepairableValue {public float Durability=50,TemplateDurability=50;}
        public ArmorItem Item=new();public RepairableValue Repairable=new();public int ArmorClass=6;
        public void ApplyDamage(ref DamageInfo info){}
    }
    public enum EquipmentSlot {FirstPrimaryWeapon,SecondPrimaryWeapon,Scabbard,Holster,Backpack}
    public enum EPlayerItems {Equipment}
    public enum EItemAttributeId {KnifeHitSlashDam,KnifeHitStabDam,KnifeHitRadius}
    public class ItemAttributeClass {public EItemAttributeId Id;public Func<float> Base=null!;public Func<string> StringValue=null!;}
    public class Item { public string Id="",StringTemplateId="";public List<ItemAttributeClass> Attributes=new(); }
    public class Slot {public Item? ContainedItem;}
    public class InventoryEquipment
    {
        public Dictionary<EquipmentSlot,Slot> Slots=new();public Slot GetSlot(EquipmentSlot slot){if(!Slots.ContainsKey(slot))Slots[slot]=new();return Slots[slot];}
    }
    public class Weapon:Item {public string WeapClass="assaultRifle";public int ChamberAmmoCount,MagazineCount;public int GetCurrentMagazineCount()=>MagazineCount;}
    public class Inventory
    {
        public float NativeWeight=100;public readonly Deferred<float> TotalWeight,TotalWeightEliteSkill;public Inventory(){TotalWeight=new(()=>NativeWeight);TotalWeightEliteSkill=new(()=>NativeWeight);}
        public InventoryEquipment Equipment=new();public List<Item> Carried=new();
        public IEnumerable<Item> GetPlayerItems(EPlayerItems kind)=>Carried.Concat(Equipment.Slots.Values.Select(v=>v.ContainedItem).OfType<Item>());
    }
    public class InventoryController {public Inventory Inventory=new();}
    public class KnifeComponent
    {
        public Item Item;public IKnifeComponentTemplate Template;public KnifeComponent(Item item,IKnifeComponentTemplate template)
        {Item=item;Template=template;foreach(var id in Enum.GetValues<EItemAttributeId>())item.Attributes.Add(new(){Id=id,Base=()=>0,StringValue=()=>"0"});}
    }
}
namespace EFT.HealthSystem
{
    public class Health {public float Current=100,Maximum=100;}
    public class ActiveHealthController
    {
        public EFT.Player Player;public bool IsAlive=true;public Dictionary<EFT.EBodyPart,Health> Parts=new();
        public List<Effect> Effects=new();public List<EFT.EBodyPart> Fractures=new();
        public ActiveHealthController(EFT.Player player){Player=player;foreach(var part in Enum.GetValues<EFT.EBodyPart>().Where(p=>p!=EFT.EBodyPart.Common))Parts[part]=new();}
        public Health GetBodyPartHealth(EFT.EBodyPart part,bool ignored=false)=>Parts[part];
        public bool IsBodyPartDestroyed(EFT.EBodyPart part)=>Parts[part].Current<=0;
        public void ChangeHealth(EFT.EBodyPart part,float amount,DamageInfo info){Parts[part].Current=Math.Clamp(Parts[part].Current+amount,0,Parts[part].Maximum);}
        public IEnumerable<Effect> GetAllEffects()=>Effects;
        public class Effect { public bool Existing=true; public float Seconds; public void ForceRemove()=>Existing=false;public void AddWorkTime(float? time,bool reset){Seconds=reset?time??0:Seconds+(time??0);} }
        public class Bleeding:Effect {}
        public class Tremor:Effect {}
        protected class PainKiller:Effect {public PainKiller(){} }
        public T AddEffect<T>(EFT.EBodyPart part,float? delay=null,float? workTime=null,float? residue=null,float? strength=null,Action<T>? init=null) where T:Effect,new(){var effect=new T{Seconds=workTime??0};Effects.Add(effect);return effect;}
        public void DoBleed(bool heavy,EFT.EBodyPart part)=>Effects.Add(new Bleeding());public void DoFracture(EFT.EBodyPart part)=>Fractures.Add(part);
        public float ApplyDamage(EFT.EBodyPart bodyPart,float damage,DamageInfo damageInfo){ChangeHealth(bodyPart,-damage,damageInfo);if(Parts[EFT.EBodyPart.Head].Current<=0)IsAlive=false;return damage;}
        public float HandleFall(float height)=>height;
    }
}
namespace EscapeFromBorderlands.Client
{
    public static class Plugin {public static Logger LogSource=new();}
    public class Logger {public void LogInfo(string text){}public void LogWarning(string text){} }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class,AllowMultiple=true)]
    public class HarmonyPatch:Attribute {public HarmonyPatch(params object[] arguments){} }
    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPriority(int value):Attribute {public int Value {get;}=value;}
    public static class Priority {public const int Last=0;}
    public static class AccessTools
    {
        public static System.Reflection.FieldInfo Field(Type type,string name)
        {
            while(type!=null){var field=type.GetField(name,System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.DeclaredOnly);if(field!=null)return field;type=type.BaseType!;}return null!;
        }
        public static System.Reflection.PropertyInfo Property(Type type,string name)=>type.GetProperty(name)!;
        public static System.Reflection.MethodInfo Method(Type type,string name,Type[]? args=null)=>args==null?type.GetMethod(name)!:type.GetMethod(name,args)!;
        public static System.Reflection.ConstructorInfo Constructor(Type type,Type[] args)=>type.GetConstructor(args)!;
    }
}

public class BotsGroup {public HashSet<string> Enemies=new();public bool IsEnemy(EFT.IPlayer player)=>Enemies.Contains(player.ProfileId);}
namespace EFT {
 public class GameDateTime {public DateTime Date=new(2026,10,8,12,0,0);public DateTime Calculate()=>Date;}
 public class Profile {public InfoData Info=new();public class InfoData {public int Side;} }
 public class AIData {public BotOwner? BotOwner;}
 public class BotOwner {public BotsGroup? BotsGroup;}
 public class VaultAnimator {public float Speed;public void SetVaultingSpeed(float s)=>Speed=s;}
}

namespace EFT.Ballistics { }
