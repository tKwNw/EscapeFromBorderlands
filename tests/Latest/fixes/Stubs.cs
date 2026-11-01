using System.Reflection;
using System.Reflection.Emit;
using EFT;
using EFT.InventoryLogic;
using EFT.HealthSystem;

namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class)]public sealed class HarmonyPatch:Attribute {public Type Type;public string Name;public HarmonyPatch(){Type=typeof(object);Name="";}public HarmonyPatch(Type t,string n){Type=t;Name=n;}}
 public static class AccessTools {public static FieldInfo Field(Type t,string n)=>t.GetField(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static)!;public static MethodInfo Method(Type t,string n)=>t.GetMethod(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance)!;}
 public class CodeInstruction {public OpCode opcode;public object? operand;public readonly List<string> labels=new();public CodeInstruction(OpCode op,object? arg=null){opcode=op;operand=arg;}public bool LoadsField(FieldInfo f)=>opcode==OpCodes.Ldfld&&Equals(operand,f);}
}
namespace UnityEngine {public static class Time {public static float time=100;}public struct Vector2 {public float x,y;public float sqrMagnitude=>x*x+y*y;public static Vector2 operator*(Vector2 a,float b)=>new(){x=a.x*b,y=a.y*b};}}
public class GClass848<T> {public T Value;public GClass848(T value){Value=value;}public static implicit operator T(GClass848<T> value)=>value.Value;public static implicit operator GClass848<T>(T value)=>new(value);}
public class PhysicalBase {public float WalkSpeedLimit=1;}
public class Physical:PhysicalBase {public UnityEngine.Vector2 BaseOverweightLimits,WalkOverweightLimits,SprintOverweightLimits,WalkSpeedOverweightLimits;public void OnWeightUpdated(){}public Player? Player_0;public Stamina Stamina=new();public enum EConsumptionType {Jump,VaultLegs,ClimbLegs,Sprint,Aim}public class Consumption {public EConsumptionType ConsumptionType;public GClass848<float> Delta=10f;}}
public class Stamina {
 public Player? Owner;public float Multiplier=1f,Current=100f,TotalCapacity=100f;public bool Fail,Reenter,ForceMode;public readonly List<Physical.Consumption> Consumptions=new();public Action? Nested;
 public void Process(float dt){if(!ForceMode)foreach(var c in Consumptions)Current=Math.Max(0,Current-c.Delta.Value*dt*Multiplier);}
 public float Consume(Physical.Consumption c,bool fromDamage=false){if(ForceMode)return 0;if(Reenter){Reenter=false;Nested?.Invoke();}if(Fail)throw new InvalidOperationException("native failure");var cost=c.Delta*Multiplier;Current=Math.Max(0,Current-cost);return cost;}
 public float Recovery(float seconds)=>2f*seconds*Multiplier;
}
public enum EBodyPart {Common,Head,Chest,Stomach,LeftArm,RightArm,LeftLeg,RightLeg}
public static class DamageHelper {public static readonly object MedKitUse=new();}
namespace EFT {
 public class Player {
  public string ProfileId=Guid.NewGuid().ToString("N");
  public Physical Physical=new();public ActiveHealthController ActiveHealthController;public UnityEngine.Vector2 InputDirection;public bool IsSprintEnabled,Crouched=true;public float LegRestored,BonusHealing,ExtraHealing,Painkiller,NoiseFactor=1f;
  public MeleeDefinition? Melee;public Player(){ActiveHealthController=new(this);Physical.Player_0=this;Physical.Stamina.Owner=this;}
 }
 public sealed class MeleeDefinition {public float JumpStaminaMultiplier=1f,VaultLegStaminaMultiplier=1f;}
}
namespace EFT.HealthSystem {
 public class Health {public float Current=50,Maximum=100;}
 public class Effect {public bool Existing=true;public EBodyPart BodyPart=EBodyPart.Chest;public void ForceRemove()=>Existing=false;}
 public sealed class HeavyBleeding:Effect {}public sealed class LightBleeding:Effect {}
 public class ActiveHealthController {
  public Health Energy=new(){Current=0},Hydration=new(){Current=0};public Player Player;public readonly Dictionary<EBodyPart,Health> Health=new();public readonly List<Effect> Effects=new();
  public ActiveHealthController(Player p){Player=p;foreach(var b in Enum.GetValues<EBodyPart>())Health[b]=new();}
  public Health GetBodyPartHealth(EBodyPart part,bool rounded)=>Health[part];public IEnumerable<Effect> GetAllEffects()=>Effects;public bool IsBodyPartDestroyed(EBodyPart part)=>Health[part].Current<=0;
  public void ChangeHealth(EBodyPart part,float amount,object reason){Health[part].Current+=amount;Player.ExtraHealing+=amount;}
  public void RestoreBodyPart(){}public void ChangeEnergy(){}public void ChangeHydration(){}
 }
}
namespace EFT.InventoryLogic {
 public class Template {public string ParentId="food";}
 public class Item {
  public string Id=Guid.NewGuid().ToString("N"),StringTemplateId="med";public Template Template=new();public readonly List<Item> Children=new();public readonly Dictionary<Type,object> Components=new();public int Refreshes;
  public IEnumerable<Item> GetAllItems()=>new[]{this}.Concat(Children.SelectMany(i=>i.GetAllItems()));public void RaiseRefreshEvent()=>Refreshes++;
 }
 public class MedKitComponent {public float HpResource=100,HpResourceRate=1;public Item Item=null!;}
 public class FoodDrinkComponent {public float HpPercent=10;}
 public class HealthEffectsComponent {}
}
namespace EFT.Interactive {public enum EInteractionType{Open,Breach}public class WorldInteractiveObject {}public class Door:WorldInteractiveObject {public bool IsBroken;}public class TreeInteractive {public float _lastVolume=1f;}}
public class InteractionResult {public EFT.Interactive.EInteractionType InteractionType;}
public class BreachDoorState {public void ExecuteDoorInteraction(EFT.Interactive.WorldInteractiveObject interactive,InteractionResult interactionResult,Action callback,Player user){}}
public class MedEffect {public ActiveHealthController HealthController=null!;public Item MedItem=null!;public EBodyPart BodyPart=EBodyPart.Chest;public bool _interrupted;}
public class PlayerOwner {public Player iPlayer=null!;}
namespace EscapeFromBorderlands.Client {
 internal static class Native {
  internal static object? Get(object? o,string n)=>o?.GetType().GetProperty(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(o)??o?.GetType().GetField(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(o);
  internal static void Set(object o,string n,object? v)=>o.GetType().GetField(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(o,v);
  internal static float Number(object o,string n)=>Convert.ToSingle(Get(o,n));internal static T? Component<T>(Item i)where T:class=>i.Components.TryGetValue(typeof(T),out var c)?(T)c:null;
  internal static void Call(object o,string n)=>o.GetType().GetMethod(n)!.Invoke(o,null);
 }
 internal static partial class BackpackRuntime {
  internal sealed class Ledger {
   internal readonly Dictionary<string,float> Until=new();internal float LastFood=-1000,LastDrink=-1000,LastTraversal=-1000;internal string Traversal="";
   internal bool Active(string key)=>Until.TryGetValue(key,out var t)&&UnityEngine.Time.time<t;internal bool Ready(string key)=>!Active("cd:"+key);internal void Arm(string key,float seconds)=>Until[key]=UnityEngine.Time.time+seconds;internal void Cooldown(string key,float seconds)=>Arm("cd:"+key,seconds);
  }
  internal class DefinitionFixture {internal string ShortName="Fixture";}
 internal class RollFixture {internal DefinitionFixture Definition=new();internal BackpackBoons Backpack=new();}
  internal class State {internal RollFixture Roll=new();internal Player Player=null!;internal Item Bag=new();internal string Key="BP40-2";internal Ledger Record=new();internal bool ColdCharge,SprintCostObserved;internal float ProneSeconds;}
  internal static readonly Dictionary<Player,State> States=new();
  internal static State? Get(Player? p)=>p!=null&&States.TryGetValue(p,out var s)?s:null;internal static bool Is(Player? p,string key)=>Get(p)?.Key==key;
  internal static Player? Actor(object? x)=>x is Player p?p:x is ActiveHealthController h?h.Player:x is Stamina st?st.Owner:x is Physical ph?ph.Player_0:null;
  
  internal static void RestoreLeg(Player p,float f)=>p.LegRestored+=f;internal static void Painkiller(Player p,float seconds)=>p.Painkiller=seconds;internal static float Noise(Player p,string kind)=>p.NoiseFactor;
 }
 internal static class MeleeRuntime {internal static Player? Owner(Stamina s)=>s.Owner;internal static MeleeDefinition? Effect(Player? p)=>p?.Melee;internal static void Heal(Player p,float amount)=>p.BonusHealing+=amount;}
 internal static class AdvancedMeleeRuntime {internal static bool Crouched(Player p)=>p.Crouched;}
}

namespace EscapeFromBorderlands.Client { internal static class ChestRigRuntime { internal static float Value(EFT.Player p,string kind)=>1f; internal static bool Is(EFT.Player p,string key)=>false; } }
