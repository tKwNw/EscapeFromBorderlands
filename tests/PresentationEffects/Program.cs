using System.Reflection;
using System.Text.Json;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EscapeFromBorderlands.Client;
var cases=new List<object>();int passed=0,failed=0,assertions=0;
void Check(bool ok,string reason){assertions++;if(!ok)throw new Exception(reason);}
void Test(string name,Action action){try{BackpackRuntime.States.Clear();Singleton<GameWorld>.Instance=new();action();passed++;cases.Add(new{name,status="PASS"});Console.WriteLine("PASS "+name);}catch(Exception e){failed++;cases.Add(new{name,status="FAIL",error=e.GetBaseException().Message});Console.WriteLine("FAIL "+name+": "+e.GetBaseException().Message);}}
object Recoil(int version,Player.FirearmController controller)=>version==0?new EFT.Animations.NewRecoil.NewRecoilShotEffect(controller):new EFT.Animations.OldRecoilShotEffect(controller);
float Settle(object target){var method=typeof(BackpackRecoilSettlingPatch).GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic)!;var parameters=method.GetParameters();var values=parameters.Select(p=>p.Name=="__instance"?target:p.Name!.StartsWith("___")?target.GetType().GetField(p.Name[3..],BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(target):(object).5f).ToArray();method.Invoke(null,values);return(float)values[Array.FindIndex(parameters,p=>p.Name=="deltaTime")]!;}
BackpackRuntime.State Equip(Player player,int grids=6,bool full=true,string key="BP41-2"){var state=new BackpackRuntime.State{Key=key,Bag=new Item{Grids=Enumerable.Range(0,grids).Select(_=>(object)new Grid{Items=full?[new Item()]:[]}).ToArray()}};BackpackRuntime.States[player]=state;return state;}
for(int renderer=0;renderer<2;renderer++){
 var mode=renderer==0?"new recoil":"old recoil";
 Test(mode+": six occupied compartments activate exactly 30% faster settling",()=>{var player=new Player();Equip(player);Check(Math.Abs(Settle(Recoil(renderer,new(player)))-.65f)<.0001f,"qualifying backpack bonus missing");});
 Test(mode+": emptying any compartment ends the bonus",()=>{var player=new Player();var state=Equip(player);((Grid)state.Bag.Grids[3]).Items=[];Check(Settle(Recoil(renderer,new(player)))==.5f,"empty compartment still qualified");});
 Test(mode+": a five-compartment bag does not qualify",()=>{var player=new Player();Equip(player,5);Check(Settle(Recoil(renderer,new(player)))==.5f,"wrong compartment count qualified");});
 Test(mode+": another signature does not qualify",()=>{var player=new Player();Equip(player,key:"BP01-1");Check(Settle(Recoil(renderer,new(player)))==.5f,"wrong backpack effect qualified");});
 Test(mode+": another player's backpack cannot affect this controller",()=>{var player=new Player();var other=new Player();Equip(other);Singleton<GameWorld>.Instance!.MainPlayer=other;Check(Settle(Recoil(renderer,new(player)))==.5f,"effect leaked between players");});
 Test(mode+": taking off the backpack restores normal settling",()=>{var player=new Player();Equip(player);var controller=new Player.FirearmController(player);BackpackRuntime.States.Remove(player);Check(Settle(Recoil(renderer,controller))==.5f,"removed backpack effect persisted");});
}
foreach(var scenario in new[]{"on","off","missing","other bag"})Test("Night Freight bloom: "+scenario,()=>{
 var player=new Player();Equip(player,key:scenario=="other bag"?"BP01-1":"BP15-1");Singleton<GameWorld>.Instance!.MainPlayer=player;
 if(scenario!="missing")player.NightVisionObserver.Component=new NightVisionComponent{Togglable=new TogglableComponent{On=scenario!="off"}};
 var bloom=new UltimateBloom();var type=typeof(BackpackBloomPatch);object?[] values=[bloom,0f];type.GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,values);
 Check(Math.Abs(bloom.m_BloomIntensity-(scenario=="on"?2f:10f))<.0001f,"night-vision activation state was not respected");
 type.GetMethod("Finalizer",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[bloom,values[1],null]);Check(bloom.m_BloomIntensity==10,"native bloom value leaked after rendering");
});
Console.WriteLine($"Presentation effects: {passed}/{passed+failed} cases passed; {assertions} assertions; {failed} failures.");
if(args.Length>0)File.WriteAllText(args[0],JsonSerializer.Serialize(new{scope="Extracted production hook bodies and native-shaped owner/night-vision doubles; no rendered Unity frame",passed,failed,assertions,cases},new JsonSerializerOptions{WriteIndented=true}));
return failed==0?0:1;
