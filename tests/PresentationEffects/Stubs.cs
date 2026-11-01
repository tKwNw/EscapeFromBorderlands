using EFT.InventoryLogic;
using System.Collections;
using System.Reflection;
namespace UnityEngine { public struct Vector3 {} }
public class UltimateBloom { public float m_BloomIntensity=10; }
namespace Comfort.Common { public static class Singleton<T> where T:class { public static T? Instance; } }
namespace EFT.InventoryLogic {
 public class Item { public object[] Grids=[]; }
 public class Grid { public object[] Items=[]; }
 public class NightVisionComponent { public TogglableComponent Togglable=new(); }
 public class TogglableComponent { public bool On; }
}
namespace EFT {
 public class Player {
  public class SlotObserver<T> { public T? Component; }
  public SlotObserver<InventoryLogic.NightVisionComponent> NightVisionObserver=new();
  public class FirearmController { private readonly Player _player; public FirearmController(Player p){_player=p;} }
 }
 public class GameWorld { public Player? MainPlayer; }
}
namespace EFT.Animations.NewRecoil { public class NewRecoilShotEffect { private readonly EFT.Player.FirearmController _firearmController; public NewRecoilShotEffect(EFT.Player.FirearmController owner){_firearmController=owner;} public void FixedUpdate(float deltaTime){} } }
namespace EFT.Animations { public class OldRecoilShotEffect { private readonly EFT.Player.FirearmController _firearmController; public OldRecoilShotEffect(EFT.Player.FirearmController owner){_firearmController=owner;} public void FixedUpdate(float deltaTime){} } }
namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch:Attribute { public HarmonyPatch(){} public HarmonyPatch(Type t,string n){} }
 public static class AccessTools {
  public static MethodInfo Method(Type t,string n)=>t.GetMethod(n,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!;
 }
}
namespace EscapeFromBorderlands.Client {
 internal static class Native {
  internal static object? Get(object? instance,string name){ if(instance==null)return null;var type=instance.GetType();return type.GetProperty(name)?.GetValue(instance)??type.GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(instance); }
  internal static IEnumerable<object> Items(object? value)=>value is IEnumerable items?items.Cast<object>():[];
 }
 internal static class BackpackRuntime {
  internal class State { public string Key="";public Item Bag=new(); }
  internal static readonly Dictionary<EFT.Player,State> States=new();
  internal static EFT.Player? Actor(object? value)=>value as EFT.Player??Native.Get(value,"_player") as EFT.Player;
  internal static State? Get(EFT.Player? player)=>player!=null?States.GetValueOrDefault(player):null;
  internal static bool Is(EFT.Player? player,string key)=>Get(player)?.Key==key;
 }
}
