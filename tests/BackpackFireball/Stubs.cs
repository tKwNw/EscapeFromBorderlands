using EFT;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;

namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch:Attribute {public HarmonyPatch(){}public HarmonyPatch(Type t,string n){} }
 [AttributeUsage(AttributeTargets.Class|AttributeTargets.Method)] public class HarmonyPriority(int value):Attribute {}
 [AttributeUsage(AttributeTargets.Class)] public class HarmonyAfter(string owner):Attribute {}
 public static class Priority {public const int Last=0;}
 public class CodeInstruction {public OpCode opcode;public object? operand;}
 public static class AccessTools {
  public static MethodInfo Method(Type t,string n)=>t.GetMethod(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static)!;
  public static MethodInfo Method(Type t,string n,Type[] types)=>t.GetMethod(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static,null,types,null)!;
  public static MethodInfo DeclaredMethod(Type t,string n,Type[] types)=>t.GetMethod(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly,null,types,null)!;
  public static Type TypeByName(string name)=>Type.GetType(name)!;
 }
}
namespace UnityEngine {
 public static class Time {public static float time;}
 public class GameObject {public bool Active=true;public void SetActive(bool v)=>Active=v;}
 public class Transform {public GameObject gameObject=new();}
 public readonly record struct Quaternion(float x,float y,float z,float w);
 public readonly record struct Vector3(float x,float y,float z) {public static Vector3 up=>new(0,1,0);public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);}
}
namespace Comfort.Common {
 public static class Singleton<T> where T:class {public static T Instance=null!;public static bool Instantiated=>Instance!=null;}
 public delegate void Callback<T>(Result<T> result);
 public class Result<T> {public bool Failed;public T Value=default!;public string Error="";}
 public interface IResult {bool Failed{get;}string Error{get;}}
 public record OperationResult(bool Failed=false,string Error=""):IResult;
}
namespace EFT.AssetsManager {
 
 public class ObjectsFactory {
  public enum PoolsCategory {Raid}public enum AssemblyType {Local}
  public Task Load=Task.CompletedTask;public bool FailSynchronously;
  public Task LoadBundlesAndCreatePools(PoolsCategory category,AssemblyType type,object[] prefabs,Diz.Jobs.JobYieldPriority priority,CancellationToken ct){if(FailSynchronously)throw new Exception("asset failure");return Load;}
 }
}
namespace EFT.InventoryLogic {
 public enum EquipmentSlot {Backpack}
 public class Item {public string Id=Guid.NewGuid().ToString("N")[..24];public string Key="";public object Prefab=new();public object? CurrentAddress=new();public List<Item> Children=[];public IEnumerable<Item> GetAllItems(){yield return this;foreach(var i in Children)yield return i;}}
 public class Slot {public Item? ContainedItem;}
 public class Equipment {public Slot Bag=new();public ThrowWeap? TopPriorityGrenade;public Slot GetSlot(EquipmentSlot s)=>Bag;}
 public class Inventory {public Equipment Equipment=new();}
 public class InventoryController {
  public EFT.Player Owner=null!;public Inventory Inventory=new();public List<Item> Reachable=[];public List<GEventArgs1> Activity=[];public int NativeQueries;
  public bool IsChangingWeapon=>Activity.Any(x=>x is SetInHandsEventArgs or RemoveFromHandsEventArgs);
  public IEnumerable<GEventArgs1> SelectEvents(Item item)=>Activity.Where(x=>x.Item==item);
  public void RemoveActiveEvent(GEventArgs1 e)=>Activity.Remove(e);
  public void GetReachableItemsOfTypeNonAlloc<T>(IList<T> dest,Predicate<T>? pred)where T:Item {NativeQueries++;foreach(var i in Reachable.OfType<T>())if(pred==null||pred(i))dest.Add(i);}
 }
}
public class GEventArgs1 {public EFT.InventoryLogic.Item Item;public GEventArgs1(EFT.InventoryLogic.Item item)=>Item=item;}
namespace EFT.InventoryLogic { public class SetInHandsEventArgs(EFT.InventoryLogic.Item item):GEventArgs1(item); }
namespace EFT.InventoryLogic { public class RemoveFromHandsEventArgs(EFT.InventoryLogic.Item item):GEventArgs1(item); }
public class OtherActivity(EFT.InventoryLogic.Item item):GEventArgs1(item);
public class ThrowWeap:EFT.InventoryLogic.Item {public bool Examined=true;}
namespace EFT.InventoryLogic { public class PlantingKit:EFT.InventoryLogic.Item {} }
public class Magazine:EFT.InventoryLogic.Item {}
public class ItemFactory {public int Created;public EFT.InventoryLogic.Item CreateItem(string id,string tpl,object? data){Created++;return tpl=="666b11055a706400b717cfa5"?new PlantingKit{Id=id,CurrentAddress=null}:new ThrowWeap{Id=id,CurrentAddress=null};}}
public interface GInterface206 {void SetOnUsedCallback(Comfort.Common.Callback<GInterface206> callback);}
public class GlobalEventDispatcher {public int Throws;public void ThrowGrenade(object g,UnityEngine.Vector3 pos,UnityEngine.Vector3 force,float power)=>Throws++;}
namespace EFT { public class PlayerInputTranslator {public EFT.Player Player_0=null!;public void vmethod_1(){} } }
namespace EFT { public class FirearmHandsInputTranslator {public void method_17(){} } }
public class BotReload {public void GetMagazineForReload(){}public void method_1(){} }
public static class PatchHooks {
 public static bool Prefix(string name,params object?[] args){var type=typeof(PatchHooks).Assembly.GetType("EscapeFromBorderlands.Client."+name);var result=type?.GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic)?.Invoke(null,args);return result is not bool flag||flag;}
}
namespace EFT.UI.DragAndDrop {
 public class FastAccessGrenadeItemView : EFT.ISetInHandsHandler {
  void EFT.ISetInHandsHandler.OnSetInHands(SetInHandsEventArgs args)=>OnSetInHands(args);
  public EFT.InventoryLogic.InventoryController InventoryController=null!;public EFT.InventoryLogic.Item? Item;public bool Selected;
  public void Notify(SetInHandsEventArgs args){if(PatchHooks.Prefix("BackpackFreeGrenadeSelectionPatch",args))OnSetInHands(args);}
  // Native successful hand event: the view stores BOTH the priority item and icon.
  public void OnSetInHands(SetInHandsEventArgs args){if(args.Item is ThrowWeap grenade){InventoryController.Inventory.Equipment.TopPriorityGrenade=grenade;Item=grenade;Selected=true;}}
 }
}
namespace EFT.InventoryLogic.Operations { public class PlantTripwireOperation {
 public object? _player;public PlantingKit? _plantingKit;public bool CanExecute=true;public int NativeExecutions,Grenades=1,Kits,CreatedTraps;
 public Comfort.Common.IResult ExecuteAndFinish(){NativeExecutions++;if(!CanExecute||Grenades<1)return new Comfort.Common.OperationResult(true,"native inventory validation failed");Grenades--;if(_plantingKit!=null)Kits--;return new Comfort.Common.OperationResult();}
 public Task<Comfort.Common.IResult> ExecuteInternal(){if(_player is EFT.Player p&&!p.IsAI&&_plantingKit==null)return Task.FromResult<Comfort.Common.IResult>(new Comfort.Common.OperationResult(true,"PlantingKit is not specified"));return Task.FromResult(ExecuteAndFinish());}
 public Comfort.Common.IResult RunAndDispose(){object?[] args=[this,null];var runNative=PatchHooks.Prefix("BackpackJigsawPlacementPatch",args);var result=(runNative?ExecuteInternal():(Task<Comfort.Common.IResult>)args[1]!).GetAwaiter().GetResult();if(!result.Failed)CreatedTraps++;return result;}
} }
namespace EFT {
 using Comfort.Common;using InventoryLogic;using UnityEngine;
 public class GameWorld {
  public Player MainPlayer=null!;public object? GrenadeFactory=new();public readonly List<IEnumerator> Routines=[];
  public void StartCoroutine(IEnumerator routine){if(routine.MoveNext())Routines.Add(routine);}
  public void Tick(){foreach(var r in Routines.ToArray())if(!r.MoveNext())Routines.Remove(r);}
 }
 public class Health {public bool IsAlive=true;}
 public class Physical {public int Thrown;public void OnThrow(bool low)=>Thrown++;}
 public class Player {
  public enum EProcessStatus {None,Scheduled,Internal}
  public class AbstractHandsController {public Item? Item;public bool Destroyed,Busy;public virtual bool IsInInteractionStrictCheck()=>Busy;}
  public class BaseGrenadeHandsController:AbstractHandsController {
   public Player? Owner;public bool GetPlantingKit(out PlantingKit kit){kit=Owner?.InventoryController.Reachable.OfType<PlantingKit>().FirstOrDefault()!;return kit!=null;}
   public int Created;public bool FailSpawn;public Transform[] ThrowingParts=[new()];public Vector3 Position,Force;public Quaternion Rotation;public float Prewarm;
   public object CreateAndRegisterGrenade(Vector3 pos,Quaternion rot,Vector3 force,float prewarm){if(FailSpawn)throw new Exception("spawn failure");Created++;Position=pos;Rotation=rot;Force=force;Prewarm=prewarm;return new();}
  }
  // This deliberately models native Process scheduling and its draw and drop
  // events separately; the old test double completed the process immediately.
  public class QuickGrenadeThrowHandsController:BaseGrenadeHandsController,GInterface206 {
   public sealed class Operation {
    public Action? _onControllerAppeared,_onControllerDestroyed;public Callback<GInterface206>? Callback_0;public bool Bool_0;public QuickGrenadeThrowHandsController Hands;
    public Operation(QuickGrenadeThrowHandsController hands)=>Hands=hands;
    public void OnDrawCompleteAction()=>_onControllerAppeared!();
    public void OnDropFinishedAction(){Bool_0=true;if(Hands.Destroyed)_onControllerDestroyed!();else Callback_0?.Invoke(new(){Value=Hands});}
    public void HideGrenade(Action hidden){if(Bool_0)hidden();else _onControllerDestroyed+=hidden;}
    public void FastForward(){if(!Bool_0)OnDropFinishedAction();}
   }
   public Operation CurrentOperation;public QuickGrenadeThrowHandsController()=>CurrentOperation=new(this);
   public void SetOnUsedCallback(Callback<GInterface206> cb)=>CurrentOperation.Callback_0=cb;
   public void FastForwardCurrentState()=>CurrentOperation.FastForward();
  }
  public class PlayerInventoryController:EFT.InventoryLogic.InventoryController {public void PlantTripwire(ThrowWeap grenade,PlantingKit? plantingKit,Vector3 from,Vector3 to){} }
  public Health HealthController=new();public Physical Physical=new();public AbstractHandsController? HandsController;public InventoryController InventoryController;
  public bool IsInBufferZone,CanManipulateWithHandsInBufferZone,IsAI,FailBeforeSpawn,FailAfterSpawn,FailRestore,ThrowDuringSpawn,AutoDraw,HoldRestoreDraw;
  public EProcessStatus ProcessStatus;public int DrawNotifications,RestoreCalls,EmptyCalls,NativeGrenadeEquips;public Item? Original;public Action? Queued,PendingRestoreDraw;
  public UI.DragAndDrop.FastAccessGrenadeItemView GrenadeView;
  public Player(){InventoryController=new PlayerInventoryController{Owner=this};GrenadeView=new(){InventoryController=InventoryController};Original=new();HandsController=new(){Item=Original};}
  public void SetInHandsForQuickUse(ThrowWeap item,Callback<GInterface206> complete){
   if(FailBeforeSpawn){complete(new(){Failed=true,Error="rejected"});return;}
   ProcessStatus=EProcessStatus.Scheduled;var activity=new SetInHandsEventArgs(item);InventoryController.Activity.Add(activity);
   var hands=new QuickGrenadeThrowHandsController{Item=item,Owner=this};HandsController=hands;
   hands.CurrentOperation._onControllerAppeared=()=>{
    InventoryController.Activity.Remove(activity);DrawNotifications++;GrenadeView.Notify(new SetInHandsEventArgs(item));
    // Native HandsChangedEvent installs PlayerOwner's callback before Complete.
    hands.SetOnUsedCallback(_=>RestoreNative(Original));
    complete(new(){Value=hands});ProcessStatus=EProcessStatus.None;var next=Queued;Queued=null;next?.Invoke();
   };
   if(ThrowDuringSpawn)throw new Exception("transition threw after spawn");
   if(FailAfterSpawn){complete(new(){Failed=true,Error="failed after creating controller"});return;}
   if(AutoDraw)hands.CurrentOperation.OnDrawCompleteAction();
  }
  private void RestoreNative(Item? previous){if(previous?.CurrentAddress!=null)SetInHands(previous,_=>{});else SetEmptyHands(_=>{});}
  public void SetInHands(Item item,Callback<AbstractHandsController> complete){RestoreCalls++;StartRestore(item,complete);}
  public void SetEmptyHands(Callback<AbstractHandsController> complete){EmptyCalls++;StartRestore(null,complete);}
  private void StartRestore(Item? item,Callback<AbstractHandsController> complete){
   if(ProcessStatus==EProcessStatus.Scheduled){Queued=()=>StartRestore(item,complete);return;}
   if(FailRestore){complete(new(){Failed=true,Error="restore rejected"});return;}
   ProcessStatus=EProcessStatus.Scheduled;
   var old=(QuickGrenadeThrowHandsController)HandsController!;old.Destroyed=true;
   var remove=new RemoveFromHandsEventArgs(old.Item!);InventoryController.Activity.Add(remove);
   old.CurrentOperation.HideGrenade(()=>{
    InventoryController.Activity.Remove(remove);HandsController=new(){Item=item};
    if(item!=null)InventoryController.Activity.Add(new SetInHandsEventArgs(item));
    Action draw=()=>{if(item!=null)InventoryController.Activity.RemoveAll(x=>x is SetInHandsEventArgs&&x.Item==item);complete(new(){Value=HandsController!});ProcessStatus=EProcessStatus.None;};
    if(HoldRestoreDraw)PendingRestoreDraw=draw;else draw();
   });
  }
  public bool NativeGrenadeKey(IList<ThrowWeap> list){
   // PlayerInputTranslator's guards run BEFORE EFB's concrete inventory-search adapter.
   if(HandsController is null||HandsController.Item?.CurrentAddress is null&&HandsController.Item!=null||InventoryController.IsChangingWeapon||ProcessStatus!=EProcessStatus.None)return false;
   list.Clear();EscapeFromBorderlands.Client.BackpackInventoryAccess.Grenades(InventoryController,list,g=>g.Examined);
   if(list.Count==0)return false;var selected=InventoryController.Inventory.Equipment.TopPriorityGrenade??list[0];if(!InventoryController.Reachable.Contains(selected))return false;NativeGrenadeEquips++;return true;
  }
 }
}
namespace EscapeFromBorderlands.Client {
 public static class Plugin {public static Logger LogSource=new();}
 public class Logger {public List<string> Messages=[];public void LogWarning(string s)=>Messages.Add(s);public void LogError(string s)=>Messages.Add(s);}
 internal static class RarityResolver {internal class Effect {public string Key="";}internal class Definition {public Effect? BackpackEffect;}internal static bool TryGetDefinition(EFT.InventoryLogic.Item item,out Definition d){d=new(){BackpackEffect=item.Key.StartsWith("BP")?new(){Key=item.Key}:null};return d.BackpackEffect!=null;}}
 internal static class BackpackRuntime {
  internal class Ledger {public Dictionary<string,float> Until=[];public bool Ready(string key)=>!Until.TryGetValue(key,out var t)||UnityEngine.Time.time>=t;public void Cooldown(string key,float seconds)=>Until[key]=UnityEngine.Time.time+seconds;}
  internal class State {public EFT.Player Player=null!;public EFT.InventoryLogic.Item Bag=new(){Key="BP07-1"};public Ledger Record=new();public string Key=>Bag.Key;}
  internal static Dictionary<EFT.Player,State> States=[];
  internal static State? Get(EFT.Player? p)=>p==null?null:States.GetValueOrDefault(p);
  internal static bool Is(EFT.Player? p,string key)=>Get(p)?.Key==key;
  internal static EFT.Player? Actor(object value)=>value is EFT.InventoryLogic.InventoryController inv?inv.Owner:value is EFT.Player.BaseGrenadeHandsController hands?hands.Owner:null;
 }
 internal static class Native {internal static object? Get(object v,string n)=>v.GetType().GetField(n)?.GetValue(v);internal static object? Call(object v,string n,params object?[] args)=>v.GetType().GetMethod(n)!.Invoke(v,args);}
}

namespace Diz.Jobs { public enum JobYieldPriority {Low} }

namespace EFT { public interface ISetInHandsHandler { void OnSetInHands(EFT.InventoryLogic.SetInHandsEventArgs args); } }
