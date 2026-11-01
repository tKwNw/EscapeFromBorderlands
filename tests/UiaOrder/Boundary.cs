// Controlled EFT/UIA-shaped domain for real Harmony installation. The optional
// query transpiler models the inspected UIA 2.1.6 IL; no game code executes.
using System.Runtime.CompilerServices;
using EFT.InventoryLogic;
namespace BepInEx.Bootstrap {public static class Chainloader {public static readonly Dictionary<string,object> PluginInfos=[];}}
namespace EFT.InventoryLogic
{
 public enum EquipmentSlot {Backpack}
 public class Item {public string Id=Guid.NewGuid().ToString("N"),Key="";public List<Item> Children=[];public IEnumerable<Item> GetAllItems()=>new[]{this}.Concat(Children.SelectMany(i=>i.GetAllItems()));}
 public class ThrowWeap:Item {public bool Spent;}
 public class Magazine:Item {}
 public class Slot {public Item? ContainedItem;}
 public class InventoryEquipment {public Slot Bag=new();public Slot GetSlot(EquipmentSlot slot)=>Bag;}
 public class Inventory {public InventoryEquipment Equipment=new();}
 public class InventoryController
 {
  public Inventory Inventory=new();public List<Item> Reachable=[];public int NativeCalls;
  [MethodImpl(MethodImplOptions.NoInlining)] public void GetReachableItemsOfTypeNonAlloc<T>(IList<T> items,Predicate<T>? predicate) where T:Item
  {NativeCalls++;foreach(var item in Reachable.OfType<T>())if(predicate is null||predicate(item))items.Add(item);}
 }
}
namespace EFT
{
 public class Player {}
 public class PlayerInputTranslator
 {
  [MethodImpl(MethodImplOptions.NoInlining)]public List<ThrowWeap> vmethod_1(InventoryController controller){var list=new List<ThrowWeap>();controller.GetReachableItemsOfTypeNonAlloc(list,null);return list;}
 }
 public class FirearmHandsInputTranslator
 {
  [MethodImpl(MethodImplOptions.NoInlining)]public List<Magazine> ReloadExternalMagazine(InventoryController controller){var list=new List<Magazine>();controller.GetReachableItemsOfTypeNonAlloc(list,null);return list;}
 }
}
public class BotReload
{
 [MethodImpl(MethodImplOptions.NoInlining)]public List<Magazine> GetMagazineForReload(InventoryController controller){var list=new List<Magazine>();controller.GetReachableItemsOfTypeNonAlloc(list,null);return list;}
 [MethodImpl(MethodImplOptions.NoInlining)]public List<Magazine> AddAmmoToMagazines(InventoryController controller){var list=new List<Magazine>();controller.GetReachableItemsOfTypeNonAlloc(list,null);return list;}
}
namespace EscapeFromBorderlands.Client
{
 internal static class Plugin {internal static Log LogSource=new();internal class Log {public void LogWarning(string value){}public void LogError(string value){}}}
 internal static class RarityResolver {internal record Effect(string Key);internal record Definition(Effect? BackpackEffect);internal static bool TryGetDefinition(Item item,out Definition definition){definition=new(item.Key==""?null:new(item.Key));return item.Key!="";}}
 internal static class BackpackRuntime {internal static EFT.Player? Actor(InventoryController inventory)=>null;}
 internal static class BackpackGrenadeAction {internal static bool TryThrow(EFT.Player player)=>false;}
 internal static class ReusableGrenadeRuntime {internal static bool Usable(Item item)=>item is not ThrowWeap grenade||!grenade.Spent;internal static void Select(InventoryController inventory,IList<ThrowWeap> items){for(int i=items.Count-1;i>=0;i--)if(!Usable(items[i]))items.RemoveAt(i);}}
}
namespace UseItemsAnywhere.Patches
{
 internal static class InventorySlotQueries
 {
  internal static readonly List<Item> Grenades=[],Reload=[];internal static string Last="";
  internal static void CollectGrenades<T>(InventoryController controller,IList<T> items,Predicate<T>? predicate) where T:Item {Last="grenades";foreach(var item in Grenades.OfType<T>())if(predicate is null||predicate(item))items.Add(item);}
  internal static void CollectReloadItems<T>(InventoryController controller,IList<T> items,Predicate<T>? predicate) where T:Item {Last="reload";foreach(var item in Reload.OfType<T>())if(predicate is null||predicate(item))items.Add(item);}
  internal static void CollectNativeItems<T>(InventoryController controller,IList<T> items,Predicate<T>? predicate) where T:Item {Last="bot";controller.GetReachableItemsOfTypeNonAlloc(items,predicate);}
 }
}
namespace UseItemsAnywhere.QuickUseWheel
{
 internal static class QuickUseWheelInventory {[MethodImpl(MethodImplOptions.NoInlining)]internal static bool HasResource(Item item)=>item is not null;}
}
