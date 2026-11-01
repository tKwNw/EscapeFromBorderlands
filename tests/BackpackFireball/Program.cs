using EFT.InventoryLogic.Operations;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.AssetsManager;
using EFT.InventoryLogic;
using EscapeFromBorderlands.Client;
using UnityEngine;

var assertions=0;var failed=0;var cases=0;
void Check(bool value,string message){assertions++;if(!value)throw new Exception(message);}
void Run(string name,Action test){cases++;try{test();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
(Player P,GameWorld W,BackpackRuntime.State S) Scene(bool equipped=true){
 BackpackGrenadeAction.Reset();BackpackRuntime.States.Clear();Time.time=100;
 var p=new Player();var world=new GameWorld{MainPlayer=p};var s=new BackpackRuntime.State{Player=p};
 Singleton<GameWorld>.Instance=world;Singleton<ObjectsFactory>.Instance=new();Singleton<ItemFactory>.Instance=new();Singleton<GlobalEventDispatcher>.Instance=new();
 if(equipped){BackpackRuntime.States[p]=s;p.InventoryController.Inventory.Equipment.Bag.ContainedItem=s.Bag;}
 p.InventoryController.Reachable.AddRange([new ThrowWeap{Id="carried1"},new ThrowWeap{Id="carried2"}]);p.InventoryController.Inventory.Equipment.TopPriorityGrenade=(ThrowWeap)p.InventoryController.Reachable[0];p.GrenadeView.Item=p.InventoryController.Reachable[0];return(p,world,s);
}
Player.QuickGrenadeThrowHandsController Begin(Player p){Check(BackpackGrenadeAction.TryThrow(p),"free action accepted");Check(p.HandsController is Player.QuickGrenadeThrowHandsController,"native quick hands created");return(Player.QuickGrenadeThrowHandsController)p.HandsController!;}
bool Release(Player.QuickGrenadeThrowHandsController h)=>BackpackGrenadeAction.Release(h,.35f,new(1,2,3),new(0,0,0,1),new(4,5,6),false);
void Tick(GameWorld w,float time){Time.time=time;typeof(BackpackGrenadeAction).GetMethod("Maintain",BindingFlags.NonPublic|BindingFlags.Static)?.Invoke(null,[w]);w.Tick();}
void Drop(Player p){BackpackRuntime.States.Remove(p);p.InventoryController.Inventory.Equipment.Bag.ContainedItem=null;}
void Usable(Player p){var list=new List<ThrowWeap>();Check(p.NativeGrenadeKey(list),"native grenade key passes guards and selects a carried grenade");Check(list.Count==2&&list.All(g=>g.Id.StartsWith("carried")),"only carried grenades selected");}
(bool Ready,PlantingKit? Kit) ReadyKit(Player p){var hands=new Player.BaseGrenadeHandsController{Owner=p};object?[] args=[hands,null,false];if(PatchHooks.Prefix("BackpackJigsawKitAvailabilityPatch",args)){var ready=hands.GetPlantingKit(out var kit);return(ready,kit);}return((bool)args[2]!,args[1] as PlantingKit);}
(bool Success,PlantTripwireOperation? Operation) Plant(Player p,PlantingKit? marker,bool geometry=true,bool nativeValidation=true){
 // Native geometry is resolved by the planner before the inventory transaction.
 if(!geometry)return(false,null);
 object?[] args=[p.InventoryController,marker];PatchHooks.Prefix("BackpackJigsawKitConsumptionPatch",args);var kit=args[1] as PlantingKit;
 // SimulatePlantTripwire must still find/discard the actual grenade and any kit.
 var grenade=p.InventoryController.Reachable.OfType<ThrowWeap>().FirstOrDefault();
 if(grenade==null||kit!=null&&!p.InventoryController.Reachable.Contains(kit))return(false,null);
 var operation=new PlantTripwireOperation{_player=p,_plantingKit=kit,CanExecute=nativeValidation,Grenades=p.InventoryController.Reachable.OfType<ThrowWeap>().Count(),Kits=p.InventoryController.Reachable.OfType<PlantingKit>().Count()};
 var result=operation.RunAndDispose();if(!result.Failed){p.InventoryController.Reachable.Remove(grenade);if(kit!=null)p.InventoryController.Reachable.Remove(kit);}return(!result.Failed,operation);
}

Run("Missing native draw event is completed at release",()=>{
 var(p,w,s)=Scene();var h=Begin(p);Check(p.ProcessStatus==Player.EProcessStatus.Scheduled,"draw begins as scheduled");Check(p.InventoryController.IsChangingWeapon,"BeginSetInHands activity starts");
 Check(Release(h),"free release handled");Check(p.ProcessStatus==Player.EProcessStatus.None,"native process completes despite missing animation draw event");Check(!p.InventoryController.IsChangingWeapon,"temporary set-in-hands activity completes");
 h.CurrentOperation.OnDropFinishedAction();Tick(w,101);Check(p.HandsController!.Item==p.Original,"original item restored");Usable(p);
});
Run("Missing finish event recovers during cooldown",()=>{
 var(p,w,s)=Scene();p.AutoDraw=true;var h=Begin(p);Release(h);Check(!s.Record.Ready(s.Key),"cooldown has begun");Tick(w,102);Check(p.HandsController!.Item==p.Original,"watchdog restores previous weapon");Usable(p);
});
Run("Missing draw AND finish recover after backpack is dropped",()=>{
 var(p,w,s)=Scene();var h=Begin(p);Release(h);Drop(p);Tick(w,102);Check(p.HandsController!.Item==p.Original,"recovery does not require the backpack");Usable(p);
});
Run("Ordinary native completion keeps grenade controls usable",()=>{
 var(p,w,s)=Scene();p.AutoDraw=true;var h=Begin(p);Release(h);h.CurrentOperation.OnDropFinishedAction();Tick(w,101);Check(p.RestoreCalls==1,"one native return-to-weapon process");Usable(p);Drop(p);Usable(p);
});
Run("Free throw does not consume inventory grenades",()=>{
 var(p,w,s)=Scene();var h=Begin(p);Check(Release(h),"first release handled");Check(Release(h),"duplicate release suppressed");Check(h.Created==1,"exactly one free grenade");Check(p.InventoryController.Reachable.Count==2,"real grenades retained");Check(h.Position==new Vector3(1,2,3)&&h.Force==new Vector3(4,5,6)&&h.Prewarm==.35f,"native trajectory and fuse retained");Check(p.Physical.Thrown==1&&Singleton<GlobalEventDispatcher>.Instance.Throws==1,"native physical and AI throw notifications retained");
});
Run("Carried grenade release never enters free-grenade handler",()=>{
 var(p,w,s)=Scene();var h=new Player.BaseGrenadeHandsController{Item=p.InventoryController.Reachable[0]};Check(!BackpackGrenadeAction.Release(h,0,new(),new(),new(),false),"ordinary grenade uses native inventory discard and throw");
});
Run("Late duplicate draw event does not steal restored input",()=>{
 var(p,w,s)=Scene();var h=Begin(p);Release(h);h.CurrentOperation.OnDropFinishedAction();var restored=p.HandsController;h.CurrentOperation.OnDrawCompleteAction();Check(p.DrawNotifications==1,"spawn/draw callback runs once");Check(p.HandsController==restored,"late animation cannot restore grenade hands");Tick(w,102);Usable(p);
});
Run("Late duplicate draw after normal draw is ignored",()=>{
 var(p,w,s)=Scene();p.AutoDraw=true;var h=Begin(p);Release(h);h.CurrentOperation.OnDropFinishedAction();h.CurrentOperation.OnDrawCompleteAction();Check(p.DrawNotifications==1,"already completed native draw is not completed again");Tick(w,102);Usable(p);
});
Run("Unrelated inventory activities are preserved",()=>{
 var(p,w,s)=Scene();var h=Begin(p);var reload=new SetInHandsEventArgs(p.Original!);var other=new OtherActivity(h.Item!);p.InventoryController.Activity.Add(reload);p.InventoryController.Activity.Add(other);Release(h);p.HoldRestoreDraw=true;Tick(w,102);
 Check(p.InventoryController.Activity.Contains(reload)&&p.InventoryController.Activity.Contains(other),"recovery only clears hand activities for the temporary grenade");Check(p.InventoryController.IsChangingWeapon,"real pending weapon transition remains locked");
});
Run("Cancelled pre-spawn quick use leaves normal grenade input",()=>{
 var(p,w,s)=Scene();p.FailBeforeSpawn=true;BeginRejected();void BeginRejected(){Check(BackpackGrenadeAction.TryThrow(p),"input accepted before native failure");Check(p.HandsController!.Item==p.Original,"no temporary hands left");}Usable(p);
});
Run("Native failure after spawning quick hands is recovered",()=>{
 var(p,w,s)=Scene();p.FailAfterSpawn=true;Check(BackpackGrenadeAction.TryThrow(p),"native failure handled");Tick(w,101);Check(p.HandsController!.Item==p.Original,"failed native operation restored");Usable(p);
});
Run("Exception after spawning quick hands is recovered",()=>{
 var(p,w,s)=Scene();p.ThrowDuringSpawn=true;Check(BackpackGrenadeAction.TryThrow(p),"input handled");Tick(w,101);Check(p.HandsController!.Item==p.Original,"partial transition recovered");Usable(p);
});
Run("No animation release recovers at bounded timeout without charging cooldown",()=>{
 var(p,w,s)=Scene();var h=Begin(p);Tick(w,116);Check(p.HandsController!.Item==p.Original,"abandoned animation cannot lock hands permanently");Check(s.Record.Ready(s.Key),"no cooldown for an unthrown grenade");Drop(p);Usable(p);
});
Run("A failed return-to-weapon operation can retry",()=>{
 var(p,w,s)=Scene();var h=Begin(p);Release(h);p.FailRestore=true;h.CurrentOperation.OnDropFinishedAction();Check(p.HandsController==h,"failure leaves only the temporary hands pending");p.FailRestore=false;Tick(w,103);Check(p.HandsController!.Item==p.Original,"later scoped recovery retries the native hand process");Usable(p);
});
Run("Temporary activity is cleaned after a successful hand return",()=>{
 var(p,w,s)=Scene();var h=Begin(p);Release(h);h.CurrentOperation.OnDropFinishedAction();var ownSet=new SetInHandsEventArgs(h.Item!);var ownRemove=new RemoveFromHandsEventArgs(h.Item!);p.InventoryController.Activity.Add(ownSet);p.InventoryController.Activity.Add(ownRemove);Tick(w,101);Check(!p.InventoryController.Activity.Contains(ownSet)&&!p.InventoryController.Activity.Contains(ownRemove),"only stale free-grenade activities removed");Usable(p);
});
Run("A removed previous weapon falls back to empty hands",()=>{
 var(p,w,s)=Scene();var h=Begin(p);p.Original!.CurrentAddress=null;Release(h);Drop(p);Tick(w,102);Check(p.EmptyCalls==1,"native empty hands used");Usable(p);
});
Run("Dead player is not re-equipped by recovery",()=>{
 var(p,w,s)=Scene();var h=Begin(p);p.HealthController.IsAlive=false;Tick(w,116);Check(p.RestoreCalls==0&&p.EmptyCalls==0,"no forced hand transition for dead player");Check(!p.InventoryController.IsChangingWeapon,"temporary activity cleaned");
});
Run("Asset failure falls back to ordinary carried grenades",()=>{
 var(p,w,s)=Scene();Singleton<ObjectsFactory>.Instance.FailSynchronously=true;Check(!BackpackGrenadeAction.TryThrow(p),"asset failure does not own input");Usable(p);
});
Run("Dropping bag while assets load cancels only the free throw",()=>{
 var(p,w,s)=Scene();var assets=new TaskCompletionSource();Singleton<ObjectsFactory>.Instance.Load=assets.Task;Check(BackpackGrenadeAction.TryThrow(p),"load starts");Drop(p);assets.SetResult();Tick(w,101);Check(p.HandsController!.Item==p.Original,"no free controller after bag removal");Usable(p);
});
Run("Cooldown remains shared when Fireball is re-equipped",()=>{
 var(p,w,s)=Scene();var h=Begin(p);Release(h);Drop(p);Tick(w,102);var replacement=new BackpackRuntime.State{Player=p,Record=s.Record};BackpackRuntime.States[p]=replacement;p.InventoryController.Inventory.Equipment.Bag.ContainedItem=replacement.Bag;
 Check(!BackpackGrenadeAction.TryThrow(p),"bag replacement does not bypass cooldown");Usable(p);Time.time=401;Check(BackpackGrenadeAction.TryThrow(p),"free grenade ready after 300 seconds");
});
Run("Regular backpacks and predicates retain native selection",()=>{
 var(p,w,s)=Scene(false);Usable(p);p.InventoryController.Reachable.OfType<ThrowWeap>().Last().Examined=false;var dest=new List<ThrowWeap>();BackpackInventoryAccess.Grenades(p.InventoryController,dest,g=>g.Examined);Check(dest.Count==1&&p.InventoryController.NativeQueries==2,"native examined predicate unchanged");
});
Run("Real grenade in hands is never replaced by Fireball",()=>{
 var(p,w,s)=Scene();var real=new Player.BaseGrenadeHandsController{Item=p.InventoryController.Reachable[0]};p.HandsController=real;Check(!BackpackGrenadeAction.TryThrow(p)&&p.HandsController==real,"existing real grenade controller untouched");
});
Run("New raid does not inherit temporary grenade or cooldown",()=>{
 var(p,w,s)=Scene();var h=Begin(p);Release(h);var next=Scene();UsableWithoutFree();void UsableWithoutFree(){Drop(next.P);Usable(next.P);}Check(!BackpackGrenadeAction.Release(h,0,new(),new(),new(),false),"old temporary IDs discarded at reset");
});

Run("Fireball preserves the chosen real grenade AND slot icon",()=>{
 var(p,w,s)=Scene();var chosen=(ThrowWeap)p.InventoryController.Reachable[1];p.GrenadeView.Notify(new(chosen));p.AutoDraw=true;var h=Begin(p);
 Check(p.InventoryController.Inventory.Equipment.TopPriorityGrenade==chosen,"temporary draw does not change the preferred real grenade");Check(p.GrenadeView.Item==chosen,"temporary draw does not replace the slot icon");
 Release(h);h.CurrentOperation.OnDropFinishedAction();Tick(w,102);Check(p.GrenadeView.Item==chosen,"slot remains correct after return");Usable(p);
});
Run("Fireball with no carried grenades leaves an empty grenade slot",()=>{
 var(p,w,s)=Scene();p.InventoryController.Reachable.Clear();p.InventoryController.Inventory.Equipment.TopPriorityGrenade=null;p.GrenadeView.Item=null;var h=Begin(p);Release(h);h.CurrentOperation.OnDropFinishedAction();Tick(w,102);
 Check(p.InventoryController.Inventory.Equipment.TopPriorityGrenade==null&&p.GrenadeView.Item==null,"no ghost item is added");Check(!p.NativeGrenadeKey(new List<ThrowWeap>()),"no fake carried grenade can be selected during cooldown");
});
Run("Real grenade selection still changes normally after Fireball",()=>{
 var(p,w,s)=Scene();var h=Begin(p);Release(h);h.CurrentOperation.OnDropFinishedAction();Tick(w,102);var real=(ThrowWeap)p.InventoryController.Reachable[1];p.GrenadeView.Notify(new(real));Check(p.InventoryController.Inventory.Equipment.TopPriorityGrenade==real&&p.GrenadeView.Item==real,"real inventory hand event reaches the native slot view");Usable(p);
});
Run("Grenades from other systems are not filtered by address or template",()=>{
 var(p,w,s)=Scene();var unrelated=new ThrowWeap{CurrentAddress=null};p.GrenadeView.Notify(new(unrelated));Check(p.GrenadeView.Item==unrelated,"only EFB-owned temporary IDs are filtered");
});
Run("Jigsaw places a real tripwire with no carried installation kit",()=>{
 var(p,w,s)=Scene();s.Bag.Key="BP33-2";var ready=ReadyKit(p);Check(ready.Ready&&ready.Kit!=null&&ready.Kit.CurrentAddress==null,"planner receives an out-of-inventory marker");
 var result=Plant(p,ready.Kit);Check(result.Success,"native operation accepts kitless Jigsaw placement");Check(result.Operation!.NativeExecutions==1&&result.Operation.CreatedTraps==1,"normal transaction completes and creates exactly one trap");Check(result.Operation._plantingKit==null,"synthetic marker is not passed to inventory discard");Check(p.InventoryController.Reachable.OfType<ThrowWeap>().Count()==1,"actual grenade consumed");Check(!p.InventoryController.Reachable.OfType<PlantingKit>().Any(),"no kit was inserted or duplicated");
});
Run("Jigsaw preserves a real installation kit when one is carried",()=>{
 var(p,w,s)=Scene();s.Bag.Key="BP33-2";var realKit=new PlantingKit();p.InventoryController.Reachable.Add(realKit);var ready=ReadyKit(p);var result=Plant(p,ready.Kit);
 Check(result.Success&&p.InventoryController.Reachable.Contains(realKit),"placement succeeds without consuming the real kit");Check(p.InventoryController.Reachable.OfType<ThrowWeap>().Count()==1,"grenade is still consumed");
});
Run("Jigsaw cannot bypass invalid placement geometry",()=>{
 var(p,w,s)=Scene();s.Bag.Key="BP33-2";var ready=ReadyKit(p);var result=Plant(p,ready.Kit,geometry:false);Check(!result.Success&&result.Operation==null,"invalid native placement never reaches the inventory operation");Check(p.InventoryController.Reachable.OfType<ThrowWeap>().Count()==2,"failed placement does not consume a grenade");
});
Run("Jigsaw cannot bypass native inventory validation",()=>{
 var(p,w,s)=Scene();s.Bag.Key="BP33-2";var result=Plant(p,ReadyKit(p).Kit,nativeValidation:false);Check(!result.Success,"native inventory failure is preserved");Check(result.Operation!.NativeExecutions==1&&result.Operation.CreatedTraps==0,"normal validation runs and no trap is created");Check(p.InventoryController.Reachable.OfType<ThrowWeap>().Count()==2,"failed native transaction consumes nothing");
});
Run("Jigsaw cannot create a trap without a real grenade",()=>{
 var(p,w,s)=Scene();s.Bag.Key="BP33-2";p.InventoryController.Reachable.Clear();var result=Plant(p,ReadyKit(p).Kit);Check(!result.Success&&result.Operation==null,"missing grenade fails normal simulation");
});
Run("Removing Jigsaw after planning removes the kitless permission",()=>{
 var(p,w,s)=Scene();s.Bag.Key="BP33-2";var ready=ReadyKit(p);Drop(p);var result=Plant(p,ready.Kit);Check(!result.Success,"planner marker cannot be consumed as a real kit once the bag is removed");Check(p.InventoryController.Reachable.OfType<ThrowWeap>().Count()==2,"cancelled placement retains the grenade");
});
Run("Normal tripwire placement still consumes the carried kit and grenade",()=>{
 var(p,w,s)=Scene(false);var kit=new PlantingKit();p.InventoryController.Reachable.Add(kit);var ready=ReadyKit(p);Check(ready.Ready&&ready.Kit==kit,"normal planner finds the real kit");var result=Plant(p,kit);Check(result.Success,"normal native placement succeeds");Check(!p.InventoryController.Reachable.Contains(kit)&&p.InventoryController.Reachable.OfType<ThrowWeap>().Count()==1,"both native consumables are used");
});
Run("Other backpacks do not grant kitless placement",()=>{
 var(p,w,s)=Scene();s.Bag.Key="BP33-1";Check(!ReadyKit(p).Ready,"unrelated backpack does not provide a kit");var result=Plant(p,null);Check(!result.Success&&result.Operation!.NativeExecutions==0,"human no-kit guard remains native");
});
Run("Native AI kitless placement remains unchanged",()=>{
 var(p,w,s)=Scene(false);p.IsAI=true;var result=Plant(p,null);Check(result.Success&&result.Operation!.NativeExecutions==1,"AI keeps the existing native null-kit path");
});
Run("Placement hook only targets the parameterless native execution method",()=>{
 var placementType=typeof(PatchHooks).Assembly.GetType("EscapeFromBorderlands.Client.BackpackJigsawPlacementPatch");Check(placementType!=null,"scoped placement hook exists");
 var method=(MethodInfo)placementType!.GetMethod("TargetMethod",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,null)!;Check(method.DeclaringType==typeof(PlantTripwireOperation)&&method.GetParameters().Length==0&&method.ReturnType==typeof(Task<IResult>),"unambiguous native Task<IResult> overload");
 var uiType=typeof(PatchHooks).Assembly.GetType("EscapeFromBorderlands.Client.BackpackFreeGrenadeSelectionPatch");Check(uiType!=null,"scoped UI hook exists");
 var ui=(MethodInfo)uiType!.GetMethod("TargetMethod",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,null)!;Check(ui.Name=="EFT.ISetInHandsHandler.OnSetInHands"&&ui.GetParameters().Single().ParameterType==typeof(SetInHandsEventArgs),"native UI hook receives the correct hand event");
});

Console.WriteLine($"Grenade and tripwire repairs: {cases-failed}/{cases} cases passed; {assertions} assertions; {failed} failures.");return failed==0?0:1;
