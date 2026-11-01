using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Bootstrap;
using EFT;
using EFT.InventoryLogic;
using EscapeFromBorderlands.Client;
using HarmonyLib;
using UseItemsAnywhere.Patches;

int cases=0,assertions=0,failures=0;
void Check(bool ok,string message){assertions++;if(!ok)throw new Exception(message);}
void Run(string name,Action body){cases++;try{body();Console.WriteLine("PASS "+name);}catch(Exception error){failures++;Console.WriteLine("FAIL "+name+": "+error);}}
var targets=new[]{AccessTools.Method(typeof(PlayerInputTranslator),"vmethod_1"),AccessTools.Method(typeof(FirearmHandsInputTranslator),"ReloadExternalMagazine"),AccessTools.Method(typeof(BotReload),"GetMagazineForReload"),AccessTools.Method(typeof(BotReload),"AddAmmoToMagazines")};
var efb=new Harmony("efb.uia-order.test");var uia=new Harmony("com.cj.useFromAnywhere.inventory-queries");
void Clear(){efb.UnpatchSelf();uia.UnpatchSelf();Chainloader.PluginInfos.Clear();InventorySlotQueries.Grenades.Clear();InventorySlotQueries.Reload.Clear();}
void Efb()=>efb.CreateClassProcessor(typeof(BackpackConcreteInventorySearchPatch)).Patch();
void Uia(){foreach(var target in targets)uia.Patch(target,transpiler:new HarmonyMethod(typeof(UiaQueryRewrite),nameof(UiaQueryRewrite.Transpiler)));}
Run("standalone Harmony installs all four inventory targets",()=>{Clear();Efb();foreach(var target in targets)Check(Harmony.GetPatchInfo(target)!.Transpilers.Count==1,"one standalone transpiler");});
Run("standalone grenade and player/bot reload queries keep native results",()=>{
 var controller=new InventoryController();var grenade=new ThrowWeap();var magazine=new Magazine();controller.Reachable.AddRange([grenade,magazine]);
 Check(new PlayerInputTranslator().vmethod_1(controller).SequenceEqual([grenade]),"native grenade");
 Check(new FirearmHandsInputTranslator().ReloadExternalMagazine(controller).SequenceEqual([magazine]),"native player reload");
 Check(new BotReload().GetMagazineForReload(controller).SequenceEqual([magazine])&&new BotReload().AddAmmoToMagazines(controller).SequenceEqual([magazine]),"both native bot queries");
});
foreach(var reverse in new[]{false,true})
{
 Run((reverse?"EFB first":"UIA first")+": both transpilers install without a missing-query exception",()=>{
  Clear();Chainloader.PluginInfos["com.cj.useFromAnywhere"]=new();if(reverse){Efb();Uia();}else{Uia();Efb();}
  foreach(var target in targets)Check(Harmony.GetPatchInfo(target)!.Transpilers.Count==2,"both patches present");
 });
 Run((reverse?"EFB first":"UIA first")+": configured grenade candidates survive composition",()=>{
  var controller=new InventoryController();var usable=new ThrowWeap();InventorySlotQueries.Grenades.Clear();InventorySlotQueries.Grenades.AddRange([new ThrowWeap{Spent=true},usable]);
  Check(new PlayerInputTranslator().vmethod_1(controller).SequenceEqual([usable])&&controller.NativeCalls==0,"UIA slots and EFB availability both applied");
 });
 Run((reverse?"EFB first":"UIA first")+": player magazines use configured slots",()=>{
  var controller=new InventoryController();var magazine=new Magazine();InventorySlotQueries.Reload.Clear();InventorySlotQueries.Reload.Add(magazine);
  Check(new FirearmHandsInputTranslator().ReloadExternalMagazine(controller).SequenceEqual([magazine])&&controller.NativeCalls==0&&InventorySlotQueries.Last=="reload","UIA player route");
 });
 Run((reverse?"EFB first":"UIA first")+": both bot methods retain native magazine rules",()=>{
  var controller=new InventoryController();var magazine=new Magazine();controller.Reachable.Add(magazine);
  Check(new BotReload().GetMagazineForReload(controller).SequenceEqual([magazine])&&InventorySlotQueries.Last=="bot","bot reload route");
  Check(new BotReload().AddAmmoToMagazines(controller).SequenceEqual([magazine])&&InventorySlotQueries.Last=="bot"&&controller.NativeCalls==2,"bot refill route");
 });
}
Run("real Harmony wheel postfix rejects only spent grenade instances",()=>{
 efb.CreateClassProcessor(typeof(UseItemsAnywhereGrenadeWheelPatch)).Patch();
 Check(!UseItemsAnywhere.QuickUseWheel.QuickUseWheelInventory.HasResource(new ThrowWeap{Spent=true}),"spent filtered");
 Check(UseItemsAnywhere.QuickUseWheel.QuickUseWheelInventory.HasResource(new ThrowWeap()),"charged accepted");
 Check(UseItemsAnywhere.QuickUseWheel.QuickUseWheelInventory.HasResource(new Item()),"other item unaffected");
});
Clear();
Console.WriteLine($"UIA Harmony ordering: {cases}/{cases-failures} cases passed; {assertions} assertions; {failures} failures.");
return failures==0?0:1;

internal static class UiaQueryRewrite
{
 // Mirrors the relevant UIA 2.1.6 query substitution and one-call guard.
 internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)
 {
  int replacements=0;
  foreach(var instruction in instructions)
  {
   if((instruction.opcode==OpCodes.Call||instruction.opcode==OpCodes.Callvirt)&&instruction.operand is MethodInfo method&&method.IsGenericMethod&&method.DeclaringType==typeof(InventoryController)&&method.Name=="GetReachableItemsOfTypeNonAlloc")
   {
    var name=__originalMethod.DeclaringType==typeof(BotReload)?"CollectNativeItems":__originalMethod.DeclaringType==typeof(PlayerInputTranslator)?"CollectGrenades":"CollectReloadItems";
    instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(InventorySlotQueries),name).MakeGenericMethod(method.GetGenericArguments());replacements++;
   }
   yield return instruction;
  }
  if(replacements!=1)throw new Exception("Expected one UIA inventory query; found "+replacements);
 }
}
