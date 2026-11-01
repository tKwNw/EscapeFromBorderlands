using EFT; using EFT.InventoryLogic;
namespace EscapeFromBorderlands.Client {
internal static class BackpackWeightScope { internal static Player? Player = null; }
internal static class BackpackRuntime {
 internal sealed class Record { public bool Active(string s)=>false; public System.Collections.Generic.Dictionary<string,float> Until=new(); }
 internal sealed class State { public string Key=""; public Record Record=new(); }
 internal static State? Get(Player? p)=>null;
 internal static Player? Actor(object value)=>null;
 internal static void Traversal(Player p,string a){}
 internal static float Weight(Player p,float f)=>f;
 internal static float TraversalFactor(Player p,string action)=>1f;
 internal static float MotionFactor(Player p)=>1;
}
internal static class BackpackNative { internal static Player? ItemOwner(Item item)=>null; }
}