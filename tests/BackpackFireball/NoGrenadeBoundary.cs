namespace EscapeFromBorderlands.Client;
internal static class ReusableGrenadeRuntime {internal static void Select(EFT.InventoryLogic.InventoryController inventory,System.Collections.Generic.IList<global::ThrowWeap> items){}internal static bool Release(EFT.Player.BaseGrenadeHandsController controller,float prewarm,UnityEngine.Vector3 position,UnityEngine.Quaternion rotation,UnityEngine.Vector3 force,bool lowThrow)=>false;}

internal static class UseItemsAnywhereCompatibility {
 internal static bool IsCollector(System.Reflection.MethodInfo method)=>false;
 internal static void CollectGrenades(EFT.InventoryLogic.InventoryController i,System.Collections.Generic.IList<global::ThrowWeap> items,System.Predicate<global::ThrowWeap>? predicate)=>i.GetReachableItemsOfTypeNonAlloc(items,predicate);
 internal static void CollectMagazines(EFT.InventoryLogic.InventoryController i,System.Collections.Generic.IList<global::Magazine> items,System.Predicate<global::Magazine>? predicate,bool bot)=>i.GetReachableItemsOfTypeNonAlloc(items,predicate);
}
