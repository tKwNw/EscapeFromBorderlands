using System.Reflection;
using System.Reflection.Emit;
namespace Comfort.Common {public static class Singleton<T> where T:class {public static T? Instance{get;set;}}}
namespace EFT
{
 public enum ExitStatus{Survived,Runner,Killed,MissingInAction,Left,Transit}
 public enum GameStatus{Starting,Started,SoftStopping,Stopping,Stopped}
 public sealed class Health {public bool IsAlive=true;}
 public class Player {public string ProfileId="player";public string? WornKey;public Health HealthController=new();public EFT.Interactive.ExfiltrationPoint? ExfiltrationPoint;}
 public class GameWorld {public Player? MainPlayer;}
 public class AbstractGame:CommonAssets.Scripts.Game.EndByExitTrigerScenario.IGame
 {
  public GameStatus Status{get;set;}=GameStatus.Started;public float PastTime{get;set;}=100;
  public CommonAssets.Scripts.Game.EndByExitTrigerScenario? _endByExitTrigerScenario;
  public int UiRefreshes;public float Displayed;public int Stops;
  public void StopSession(string id,ExitStatus status,string name)=>Stops++;
  public void UpdateExfiltrationUi(EFT.Interactive.ExfiltrationPoint point,bool contains,bool initial=false)
  {
   UiRefreshes++;var duration=EscapeFromBorderlands.Client.BackpackVisitorExtraction.UiDuration(point.Settings.ExfiltrationTime,this,point);
   Displayed=point.Status==EFT.Interactive.EExfiltrationStatus.Countdown?duration-(PastTime-point.ExfiltrationStartTime):duration;
   EscapeFromBorderlands.Client.BackpackVisitorExtraction.ObserveUi(this,point,contains);
  }
 }
 public class LocalGame:AbstractGame {public void Stop(string profileId,ExitStatus exitStatus,string exitName,float delay=0){}}
}
namespace EFT.Interactive
{
 public enum EExfiltrationStatus{RegularMode,Countdown,UncompleteRequirements,NotPresent}
 public interface IExfiltrationRequirement {bool Met(EFT.Player p);}
 public sealed class Requirement(bool met):IExfiltrationRequirement {public bool Met(EFT.Player p)=>met;}
 public class ExitTriggerSettings {public string Name="exit";public float ExfiltrationTime=5;}
 public class ExfiltrationPoint
 {
  public ExitTriggerSettings Settings=new();public float ExfiltrationStartTime;
  public EExfiltrationStatus Status=EExfiltrationStatus.RegularMode;public readonly List<EFT.Player> Entered=new();
  public IExfiltrationRequirement[] Requirements=Array.Empty<IExfiltrationRequirement>();
  public IEnumerable<IExfiltrationRequirement> UnmetRequirements(EFT.Player p)=>Requirements.Where(r=>!r.Met(p));
 }
}
namespace CommonAssets.Scripts.Game
{
 public class EndByExitTrigerScenario
 {
  public interface IGame {EFT.GameStatus Status{get;}float PastTime{get;}void StopSession(string profileId,EFT.ExitStatus exitStatus,string exitName);}
  public class LocationEscape(EFT.Player player,EFT.Interactive.ExfiltrationPoint trigger,float start)
  {public readonly EFT.Player Player=player;public readonly EFT.Interactive.ExfiltrationPoint Trigger=trigger;public readonly float StartTime=start;}
  public IGame _game=null!;
  public readonly List<LocationEscape> _escapes=new();
  public readonly List<(EFT.Player,string,IEnumerable<EFT.Interactive.IExfiltrationRequirement>)> list_1=new();
  public readonly List<EFT.Interactive.ExfiltrationPoint> _monitoredEP=new();
  public void Update(){}
 }
}
namespace HarmonyLib
{
 [AttributeUsage(AttributeTargets.Class)]public sealed class HarmonyPatch(Type t,string method):Attribute{public readonly Type Target=t;public readonly string Method=method;}
 public static class AccessTools
 {
  public static FieldInfo Field(Type t,string name)=>t.GetField(name,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!;
  public static MethodInfo Method(Type t,string name)=>t.GetMethod(name,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!;
 }
 public sealed class CodeInstruction(OpCode code,object? value=null)
 {
  public OpCode opcode=code;public object? operand=value;public readonly List<Label> labels=new();public readonly List<object> blocks=new();
  public bool LoadsField(FieldInfo f)=>opcode==OpCodes.Ldfld&&Equals(operand,f);
 }
}
namespace EscapeFromBorderlands.Client
{
 internal static class BackpackRuntime {internal static bool Is(EFT.Player? p,string key)=>p?.HealthController?.IsAlive==true&&p.WornKey==key;}
 internal static class Native {internal static object? Get(object obj,string name)=>obj.GetType().GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(obj);}
}
