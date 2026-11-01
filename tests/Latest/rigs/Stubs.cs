// Controlled engine doubles: production rig, inventory, medical, resolver and
// server storage code are linked unchanged by RigTests.csproj.
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
namespace UnityEngine {
 public static class Time {public static float time;}
 public struct Vector2 {public float x,y;public Vector2(float a,float b){x=a;y=b;}public float magnitude=>(float)Math.Sqrt(sqrMagnitude);public float sqrMagnitude=>x*x+y*y;}
 public struct Vector3 {
 public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}
 public static Vector3 zero=>new();public static Vector3 up=>new(0,1,0);public static Vector3 right=>new(1,0,0);
 public static Vector3 operator *(Vector3 v,float f)=>new(v.x*f,v.y*f,v.z*f);public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
 public float magnitude=>(float)Math.Sqrt(sqrMagnitude);public float sqrMagnitude=>x*x+y*y+z*z;public Vector3 normalized=>magnitude>0?this*(1/magnitude):zero;
 public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;public static Vector3 Cross(Vector3 a,Vector3 b)=>new(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
 }
 public static class Random {public static float Range(float a,float b)=>(a+b)/2;public static int Range(int a,int b)=>(a+b)/2;}
}
namespace Comfort.Common {public static class Singleton<T> where T:class {public static T Instance=null!;public static bool Instantiated=>Instance is not null;}}
public partial class Shot {public Item? Weapon;public int FireIndex;public float Damage=100,PenetrationPower=30,InitialSpeed=1000,Speed=1000,BulletMassGram=10,BulletDiameterMilimeters=9,BallisticCoefficient=1;public UnityEngine.Vector3 StartVelocity=new(1000,0,0),StartPosition,_currentVelocity;public Trajectory TrajectoryInfo=new();public PlayerBridge? Player;}
public class LightComponent {public bool IsActive;}
public class FoodDrinkComponent {}
public class MedKitComponent {public float HpResource,HpResourceRate;}
public class HealthEffectsComponent {public Dictionary<EDamageEffectType, object> DamageEffects=new();}
public class StashGridClass {public string ID="";public CompoundItem ParentItem=null!;}
public class MovementContext {public bool IsSprintEnabled,Vaulting,IsInPronePose;public int PhysicalCondition;public bool PlayerAnimatorGetIsVaulting()=>Vaulting;}
public enum EPhysicalCondition {LeftArmDamaged}
public class Resource {public float Current=100,TotalCapacity=100;public void UpdateStamina(float n)=>Current=n;}
public class Physical {public Resource Stamina=new(),HandsStamina=new();public bool HoldingBreath;public int Refreshes;public void OnWeightUpdated()=>Refreshes++;}
public static class DamageHelper {public static readonly object MedKitUse=new();}
namespace EFT {
 public enum EBodyPart {Common,Head,Chest,Stomach,LeftArm,RightArm,LeftLeg,RightLeg}
 public enum EDamageType {Bullet,Explosion,GrenadeFragment,Melee,Fall,LightBleeding,HeavyBleeding,Other}
 public class GameWorld {public List<Player> RegisteredPlayers=new();public EFT.Ballistics.BallisticsCalculator SharedBallisticsCalculator=new();public readonly List<System.Collections.IEnumerator> Coroutines=new();public void StartCoroutine(System.Collections.IEnumerator e)=>Coroutines.Add(e);}
 public class Player {
  public string ProfileId=Guid.NewGuid().ToString();public ActiveHealthController ActiveHealthController;public ActiveHealthController HealthController=>ActiveHealthController;
  public Player(){ActiveHealthController=new(){Player=this};}
  public InventoryController InventoryController=new();public Physical Physical=new();public MovementContext MovementContext=new();public Pwa ProceduralWeaponAnimation=new();public Search? SearchController;public UnityEngine.Vector2 InputDirection;public UnityEngine.Vector3 Position;public ItemHandsController? HandsController;
  public bool Crouched,WoodsFlag,AimingLoaded;public Vision NightVisionObserver=new Vision();public int Refreshes;public void UpdateSpeedLimitByHealth()=>Refreshes++;public void UpdateArmsCondition()=>Refreshes++;
  public class ItemHandsController {public Item? Item;public bool IsAiming;public Player? Player;}
  public class BaseGrenadeHandsController:ItemHandsController {}
  public class BaseKnifeController:ItemHandsController {}
  public class FirearmController:ItemHandsController {public new Weapon Item {get=>(Weapon)base.Item!;set=>base.Item=value;}public TestAnimator FirearmsAnimator=new();public Weapon Weapon=>(Weapon)Item!;public bool Reloading,IsTriggerPressed;public void QuickReloadMag(){}public void ReloadMag(){}public int ReloadRefreshes;public void SetAnimatorAndProceduralValues()=>ReloadRefreshes++;public bool IsInReloadOperation()=>Reloading;public int Updates;public void RecalculateErgonomic()=>Updates++;public void UpdateHipInaccuracy()=>Updates++;}
 }
 public class TestAnimator {public float speed=1;public void SetAnimationSpeed(float value)=>speed=value;}
 public class Pwa {public Optic? CurrentScope;public void UpdateWeaponVariables(){}public void PhysicalConditionUpdated(EPhysicalCondition x,int full){}}
 public class Optic {public bool IsOptic;}
 public class Vision {public Toggle Component=new Toggle();}
 public class Toggle {public TogglableComponent Togglable=new();public bool IsOn {get=>Togglable.On;set=>Togglable.On=value;}}
 public class TogglableComponent {public bool On;}
 public class SearchOperation {public Item Item=new();}
 public class Search {public List<SearchOperation> SearchOperations=new();}
}
namespace EFT.HealthSystem {
 public enum EDamageEffectType {LightBleeding,HeavyBleeding,DestroyedPart}
 public class BodyHealth {public float Current=100,Maximum=100;}
 public class Effect {public EBodyPart BodyPart;public bool Existing=true;public void ForceRemove()=>Existing=false;}
 public class LightBleeding:Effect {}public class HeavyBleeding:Effect {}
 public class ActiveHealthController {
  public Resource Hydration=new();public Player Player=null!;public bool IsAlive=true;public Dictionary<EBodyPart,BodyHealth> Parts=Enum.GetValues<EBodyPart>().ToDictionary(p=>p,p=>new BodyHealth());public HashSet<EBodyPart> Destroyed=new(),Fractured=new();public List<Effect> Effects=new();public List<(EBodyPart,float)> Surgeries=new();
  public bool IsBodyPartDestroyed(EBodyPart part)=>Destroyed.Contains(part);public bool IsBodyPartBroken(EBodyPart part)=>Fractured.Contains(part);public BodyHealth GetBodyPartHealth(EBodyPart part,bool rounded)=>Parts[part];public IEnumerable<Effect> GetAllEffects()=>Effects;
  public void ChangeHealth(EBodyPart part,float value,object damage){var h=Parts[part];h.Current=Math.Min(h.Maximum,Math.Max(0,h.Current+value));}
  public void RestoreBodyPart(EBodyPart part,float penalty){Destroyed.Remove(part);Parts[part].Current=1;Parts[part].Maximum=(float)Math.Ceiling(Parts[part].Maximum*penalty);Surgeries.Add((part,penalty));}
 }
}
namespace EFT.InventoryLogic {
 public enum EquipmentSlot {TacticalVest,ArmorVest,Backpack,FirstPrimaryWeapon,SecondPrimaryWeapon,Holster,FaceCover}
 public class RawTemplate {public string ParentId="";}
 public class Item {public RawTemplate Template=new();public bool GridFull,CompartmentsFull,IsCorpse;public string Id=Guid.NewGuid().ToString("N")[..24];public string StringTemplateId="";public float Weight=1;public int StackObjectsCount=1;public List<Item> Children=new();public Dictionary<Type,object> Components=new();public object? CurrentAddress;public Player? Owner;public bool IsBarter;public IEnumerable<Item> GetAllItems()=>new[]{this}.Concat(Children.SelectMany(c=>c.GetAllItems()));public void RaiseRefreshEvent(){}}
 public class CompoundItem:Item {public StashGridClass[] Grids=Array.Empty<StashGridClass>();}
 public class Silencer:Item {}
 public class AmmoTemplate {public int ProjectileCount=1;public string Caliber="Caliber9x19PARA";}
 public class Ammo:Item {public AmmoTemplate AmmoTemplate=new();public bool IsUsed;}
 public class WeaponTemplate {public float Ergonomics=40;}
 public class Weapon:CompoundItem {public string WeapClass="assaultRifle",Mode="single",ReloadMode="ExternalMagazine";public Magazine? Magazine;public new WeaponTemplate Template=new();public int Chambers;public List<Item> Mods=>Children;public Magazine? GetCurrentMagazine()=>Magazine;}
 public class CartridgeStack {public List<Item> Items=new();}
 public class Magazine:CompoundItem {public int Count=30,MaxCount=30;public CartridgeStack Cartridges=new();public string? Compatible;public Operation ApplyWithoutRestrictions(InventoryController inventory,Ammo ammo,int count,bool simulate)=>new(){Failed=ammo.StackObjectsCount<=0||Count>=MaxCount||Compatible is not null&&Compatible!=ammo.StringTemplateId,Apply=()=>{Count++;ammo.StackObjectsCount--;if(!Cartridges.Items.OfType<Ammo>().Any())Cartridges.Items.Add(new Ammo{StringTemplateId=ammo.StringTemplateId});}};}
 public class Slot {public Item? ContainedItem;}
 public class Equipment {private Dictionary<EquipmentSlot,Slot> Slots=Enum.GetValues<EquipmentSlot>().ToDictionary(s=>s,s=>new Slot());public Slot GetSlot(EquipmentSlot slot)=>Slots[slot];public IEnumerable<Item> GetAllItems()=>Slots.Values.Where(s=>s.ContainedItem!=null).SelectMany(s=>s.ContainedItem!.GetAllItems()).Distinct();}
 public class FloatValue {public float Value=100;}
 public class Inventory {public Equipment Equipment=new();public FloatValue TotalWeight=new();}
 public class Operation {public bool Failed;public Action? Apply;}
 public class InventoryController {public Inventory Inventory=new();public Player? Player;public Task<Operation> TryRunNetworkTransaction(Operation o){if(!o.Failed)o.Apply?.Invoke();return Task.FromResult(o);}}
}
namespace EscapeFromBorderlands.Client {
 internal class MeleeEffectDefinition {public string Ability="";}
 internal static class Plugin {public static Log LogSource=new();public class Log {public void LogInfo(string s){}public void LogWarning(string s){}}}
 internal static class Native {
  internal static object? Get(object? o,string name)=>o?.GetType().GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(o)??o?.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(o);
  internal static T? Component<T>(Item item)where T:class=>item.Components.GetValueOrDefault(typeof(T)) as T;
  internal static float Number(object? o,string name)=>Convert.ToSingle(Get(o,name)??0);
  internal static object? Call(object o,string name)=>o.GetType().GetMethod(name)!.Invoke(o,null);
 }
 internal static partial class BackpackRuntime {
  internal sealed class Ledger {
   public string Traversal="";public float LastTraversal=-1000;public readonly HashSet<string> Categories=new(),Templates=new();public readonly Dictionary<string,float> Until=new(StringComparer.Ordinal);public readonly Dictionary<string,float> Distance=new(StringComparer.Ordinal);public readonly Dictionary<string,int> Checklist=new(StringComparer.Ordinal);
   public bool Ready(string key)=>!Until.TryGetValue("cd:"+key,out var t)||UnityEngine.Time.time>=t;public bool Active(string key)=>Until.TryGetValue(key,out var t)&&UnityEngine.Time.time<t;public void Arm(string key,float seconds)=>Until[key]=UnityEngine.Time.time+seconds;public void Cooldown(string key,float seconds)=>Arm("cd:"+key,seconds);
  }
  internal sealed class State {internal bool ColdCharge,JumpCharged,JumpSafe;internal Player Player=null!;internal Item Bag=null!;internal RarityResolver.ShotRoll Roll=null!;internal Ledger Record=new();internal string Key=>Roll.Definition.BackpackEffect!.Key;}
  private static readonly Dictionary<Player,State> BagStates=new();
  internal static State? Get(Player? p){if(p is null)return null;var bag=p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem;if(bag is null||RarityResolver.Resolve(bag) is not {} roll||roll.Definition.BackpackEffect is null)return null;if(!BagStates.TryGetValue(p,out var s)||s.Bag.Id!=bag.Id)BagStates[p]=s=new State{Player=p,Bag=bag,Roll=roll};return s;}
  internal static bool Woods(Player p)=>p.WoodsFlag;internal static bool MainGridFull(Item bag)=>bag.GridFull;internal static float TraversalFactor(Player p,string action){var s=Get(p);return s?.Key=="BP40-2"&&s.Record.Traversal.Length>0&&s.Record.Traversal!=action&&UnityEngine.Time.time-s.Record.LastTraversal<=5f?.8f:1f;}
  internal static bool Milestone(State s)=>s.Record.Distance.GetValueOrDefault(s.Key)>=1000;
  internal static bool AimingLoadedPrimary(Player p)=>p.AimingLoaded;
  internal static Dictionary<string,float> CarriedFactors=new();internal static float CarriedWeightFactor(Player p,Item i)=>CarriedFactors.GetValueOrDefault(i.Id,1);
  internal static bool Day=true;internal static bool Daytime()=>Day;internal static Player? Actor(object? o)=>o as Player??Native.Get(o,"Player")as Player;
  internal static bool BlackLeg(Player p)=>p.ActiveHealthController.Destroyed.Overlaps(new[]{EBodyPart.LeftLeg,EBodyPart.RightLeg});
  internal static void RestoreLeg(Player p,float f){var s=p.Physical.Stamina;s.Current=Math.Min(s.TotalCapacity,s.Current+s.TotalCapacity*f);}
 }
 internal static partial class BackpackNative {
  internal static bool InBag(Item item,Item bag)=>bag.GetAllItems().Contains(item);internal static bool Barter(Item i)=>i.IsBarter;internal static Player? WeaponOwner(Weapon w)=>w.Owner;
 }
 internal static class BackpackPresentation {internal static bool NightVisionOn(Player? p)=>p?.NightVisionObserver?.Component?.Togglable?.On==true;internal static bool FullCompartments(Item bag)=>bag.CompartmentsFull;}
 internal static class BackpackSearch {internal static bool Corpse(Item item)=>item.IsCorpse;}
 internal static class MeleeRuntime {internal static Player? Owner(Player.ItemHandsController controller)=>controller.Player;}
 internal static class AdvancedMeleeRuntime {internal static bool Crouched(Player p)=>p.Crouched;}
 internal static partial class LegendaryRuntime {internal static int RemainingAmmo(Weapon w)=>(w.Magazine?.Count??0)+w.Chambers;internal static int Capacity(Weapon w)=>(w.Magazine?.MaxCount??0)+w.Chambers;}
 internal static class LegendaryFireModePatch {internal static string LogicalMode(Weapon w)=>w.Mode;}

 internal static class ChestRigLegSprintPatch {internal static bool Fractured(Player p)=>p.ActiveHealthController.Fractured.Count>0;}
 internal static class ChestRigReload {internal static void SwapHolstered(ChestRigRuntime.State s,Weapon pistol){}}
}
namespace EscapeFromBorderlands.Server {internal class MeleeEffectDefinition {}}
namespace EFT.InventoryLogic {public class Launcher:Item{}}
namespace EscapeFromBorderlands.Client {

 internal static class WeaponCombatState {}
}
public class PlayerBridge {public EFT.Player iPlayer=null!;public bool IsAI;}
public class Trajectory {public UnityEngine.Vector3 Velocity;public void Initialize(UnityEngine.Vector3 a,UnityEngine.Vector3 b,float mass,float diameter,float coefficient,bool ai){Velocity=b;}}
namespace EscapeFromBorderlands.Server {
 internal class RigDatabase {
  internal Dictionary<SPTarkov.Server.Core.Models.Common.MongoId,SPTarkov.Server.Core.Models.Eft.Common.Tables.TemplateItem> Items=new();
  internal Dictionary<SPTarkov.Server.Core.Models.Common.MongoId,SPTarkov.Server.Core.Models.Eft.Common.Tables.TemplateItem> GetItems()=>Items;
  internal RigLocales GetLocales()=>new();
 }
 internal class RigLocales {internal Dictionary<string,object> Global=new(){{"en",new()}};}
 internal class RigLocale {internal Dictionary<string,string> English=new();internal Dictionary<string,string> GetLocaleDb(string language)=>English;}
 internal class RigCloner {internal T Clone<T>(T input)=>System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(input,new System.Text.Json.JsonSerializerOptions{Converters={new SPTarkov.Server.Core.Utils.Json.Converters.StringToMongoIdConverter()}}),new System.Text.Json.JsonSerializerOptions{Converters={new SPTarkov.Server.Core.Utils.Json.Converters.StringToMongoIdConverter()}})!;}
}

namespace EFT.RocketLauncher {internal sealed class ReferenceNamespaceMarker{}}
namespace EFT.RocketLauncher.Explosion {internal sealed class ReferenceNamespaceMarker{}}
namespace EFT.Ballistics {internal sealed class ReferenceNamespaceMarker{}}
namespace EscapeFromBorderlands.Server {internal sealed class RigTemplateTable {private readonly RigDatabase database;internal RigTemplateTable(RigDatabase value){database=value;}internal Dictionary<SPTarkov.Server.Core.Models.Common.MongoId,SPTarkov.Server.Core.Models.Eft.Common.Tables.TemplateItem> Items=>database.Items;}}
