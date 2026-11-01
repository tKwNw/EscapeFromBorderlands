using EscapeFromBorderlands.Client;
using EFT;
using EFT.InventoryLogic;
namespace UnityEngine {
 public static class Mathf {public static float Clamp01(float x)=>Math.Clamp(x,0,1);public static float Clamp(float x,float lo,float hi)=>Math.Clamp(x,lo,hi);}
 public static class Time {public static float time=100;public static int frameCount=1;}
 public struct Vector2 {public float x,y;public float sqrMagnitude=>x*x+y*y;}
 public struct Vector3 {public float x,y,z;public float magnitude=>(float)Math.Sqrt(x*x+y*y+z*z);public float sqrMagnitude=>x*x+y*y+z*z;public Vector3 normalized=>this;}
}
namespace Comfort.Common {public delegate void Callback(ReloadResult result);public sealed class ReloadResult {public bool Succeed;}public static class Singleton<T> where T:class {public static T Instance=null!;public static bool Instantiated=>Instance!=null;}}
namespace EFT {
 public enum Pose {Stand,Duck,Prone}
 public class Health {public bool IsAlive=true;}
 public class Stamina {public float NormalValue=1f;}
 public class Physics {public Stamina Stamina=new();}
 public class Motion {public bool IsInPronePose;}
 public class Player {
  public string ProfileId=Guid.NewGuid().ToString("N");public bool HasAk74,Bleeding;public float HealthFraction=1;public List<Weapon> Primaries=new();public Health HealthController=new();public Hand? HandsController;public Physics Physical=new();public Pose Pose;public UnityEngine.Vector3 Velocity;public UnityEngine.Vector2 InputDirection;public bool IsInPronePose=>MovementContext.IsInPronePose;public Motion MovementContext=new();
  public class Hand {public Item? Item;public bool IsAiming;}
  public class FirearmController:Hand {
   public new Weapon Item {get=>(Weapon)base.Item!;set=>base.Item=value;}public bool IsTriggerPressed,Blindfire,CanReload=true,ThrowOnReload;public Weapon Weapon=>Item;
   public float ObservedReloadSpeed=1,AnimatorSpeed=1;public Comfort.Common.Callback? Pending;
   [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] public bool CanStartReload()=>CanReload;
   [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] public void SetAnimatorAndProceduralValues()=>AnimatorSpeed=LegendaryRuntime.GetReloadSpeedMultiplier(Item);
   [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] public void ReloadBarrels(AmmoPack ammoPack,ItemAddress? placeToPutContainedAmmoMagazine,Comfort.Common.Callback callback){if(Blindfire)return;if(!CanStartReload()||ammoPack.AmmoCount<=0){callback(new(){Succeed=false});return;}ObservedReloadSpeed=LegendaryRuntime.GetReloadSpeedMultiplier(Item);if(ThrowOnReload)throw new InvalidOperationException("native reload failed");Pending=callback;}
  }
 }
 public class Clock {public DateTime Date=new(2026,10,17,12,0,0);public DateTime Calculate()=>Date;}
 public class GameWorld {public Clock GameDateTime=new();public readonly List<Player> Players=new();public Player? GetAlivePlayerByProfileID(string id)=>Players.FirstOrDefault(p=>p.ProfileId==id&&p.HealthController.IsAlive);}
}
namespace EFT.InventoryLogic {
 public class Item {public string Id=Guid.NewGuid().ToString("N"),StringTemplateId="";}
 public class ItemAddress {}
 public class AmmoTemplate {public int PenetrationPower=20,ProjectileCount=1;public float InitialSpeed=800;}
 public class Magazine {public string Id=Guid.NewGuid().ToString("N");public int Count=30,MaxCount=30;}
 public class Weapon:Item {public Magazine? Magazine=new();public int ChamberAmmoCount;public object[] Chambers=[];public bool Folded;public string WeapClass="assaultRifle";public int GetCurrentMagazineCount()=>Magazine?.Count??0;public string Mode="single";public readonly List<Item> Mods=new();public AmmoTemplate? CurrentAmmoTemplate=new();public Magazine? GetCurrentMagazine()=>Magazine;public int GetMaxMagazineCount()=>Magazine?.MaxCount??0;}
}
namespace EFT.InventoryLogic {public class AmmoPack {public Ammo? Ammo;public int AmmoCount=>Ammo==null?0:2;public Ammo? GetAmmoToReload(int index)=>Ammo;}}
public class Ammo:Item {public AmmoTemplate AmmoTemplate=new();public float InitialSpeed=>AmmoTemplate.InitialSpeed;}
public class Silencer:Item {}
public class Shot {public object? Weapon,HittedBallisticCollider;public int FireIndex;public EBulletState BulletState;public List<object> Fragments=new();public enum EBulletState {Normal,RicochetHit,FragmentationHit}}
namespace EFT.Ballistics {public class BodyPartCollider {public string PlayerProfileID="target";public object BodyPartType="Chest";}public class ArmorPlateCollider:BodyPartCollider {}}
namespace EscapeFromBorderlands.Client {
 internal static class Plugin {internal static readonly Log LogSource=new();internal sealed class Log {internal void LogInfo(string value){}}}
 internal static class LegendaryFireModePatch {internal static bool IsHeld(Player.FirearmController f)=>f.IsTriggerPressed;internal static string LogicalMode(Weapon w)=>w.Mode;}
 internal static class AdvancedLegendaryActions {internal static void DeploySmoke(Player? p,float seconds,string name="",string key=""){}internal static void DeferMissResolution(string id,int index){}internal static void TransferMatchingRound(Player? p,Weapon w,string ammo){} }
 internal class Relief {public bool Existing=true;public float TimeLeft=600;}
 internal class AdvancedMeleeState {public bool FirstAttack=true,Charged,StationaryRecoveryActive,PreviousAttackConnected=true;public float SprintSince=-1,StationarySince=100,SlashSpeed,ChainPenetration;public HashSet<string> MarkedEnemies=new();}
 internal static partial class MeleeRuntime {
  internal class HeldState {public Player Player=null!;public Item Item=null!;public RarityResolver.ShotRoll Roll=null!;public MeleeEffectDefinition Effect=>Roll.Definition.MeleeEffect!;public AdvancedMeleeState Advanced=new();public float Buildup;public int AttackNumber;}
  internal static readonly Dictionary<Player,HeldState> HeldItems=new();internal static HeldState? Held(Player p)=>HeldItems.GetValueOrDefault(p);
  internal static float HealthFraction(Player p)=>p.HealthFraction;internal static bool HasAk74(Player p)=>p.HasAk74;
 }
 internal static partial class AdvancedMeleeRuntime {
  private static readonly Dictionary<Player,Relief> KillPainkillers=new();private static readonly Dictionary<Player,VariantDefinition> KillPainkillerSources=new();
  internal static bool Crouched(Player p)=>p.Pose==Pose.Duck;internal static IEnumerable<Weapon> Primaries(Player p)=>p.Primaries;internal static bool Bleeding(Player p)=>p.Bleeding;
  internal static float NearbySpeedBonus(MeleeRuntime.HeldState held)=>NearBonus;internal static float NearBonus;internal static bool ExtraReady;internal static bool ExtraJumpReady(Player p)=>ExtraReady;
  internal static void AddRelief(Player p,VariantDefinition d,float seconds){KillPainkillers[p]=new(){TimeLeft=seconds};KillPainkillerSources[p]=d;}
  internal static void ClearAudit(){KillPainkillers.Clear();KillPainkillerSources.Clear();NearBonus=0;ExtraReady=false;}
 }
 internal static class MasterKeyRuntime {internal static bool Ready=true;internal static bool Available(Player p)=>Ready;}
}
