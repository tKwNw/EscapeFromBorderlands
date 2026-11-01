using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EscapeFromBorderlands.Client;
using UnityEngine;
int cases=0,assertions=0,failures=0;
void Check(bool value,string text){assertions++;if(!value)throw new Exception(text);}
void Run(string name,Action test){cases++;try{LegendaryRuntime.ResetSession();MeleeRuntime.HeldItems.Clear();AdvancedMeleeRuntime.ClearAudit();Time.time=100;Singleton<GameWorld>.Instance=new();test();Console.WriteLine("PASS "+name);}catch(Exception e){failures++;Console.WriteLine("FAIL "+name+": "+(e.InnerException??e));}}
var catalog=LegendaryCatalog.Load();var guns=catalog.Variants.Where(d=>d.BackpackEffect==null&&d.MeleeEffect==null&&d.ChestRigEffect==null).ToArray();
(Player P,Weapon W) Equip(VariantDefinition d,int ammo=30){var p=new Player();var w=new Weapon{StringTemplateId=d.TemplateId,Magazine=new(){Count=ammo,MaxCount=Math.Max(30,ammo)}};p.HandsController=new Player.FirearmController{Item=w};Singleton<GameWorld>.Instance.Players.Add(p);return(p,w);}
object State(Weapon w)=>typeof(LegendaryRuntime).GetMethod("GetState",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[w.Id])!;
void Set(Weapon w,string name,object value)=>State(w).GetType().GetProperty(name)!.SetValue(State(w),value);
int Number(Weapon w,string name)=>(int)State(w).GetType().GetProperty(name)!.GetValue(State(w))!;
EffectStatusModel.Status? Status(Player p,Weapon w)=>LegendaryRuntime.CurrentStatus(p,w);
void Fire(Player p,Weapon w){var before=LegendaryRuntime.RemainingAmmo(w);w.Magazine!.Count--;LegendaryRuntime.RegisterShot(w,new Ammo{AmmoTemplate=w.CurrentAmmoTemplate!},p.ProfileId,++Time.frameCount,guns.First(d=>d.TemplateId==w.StringTemplateId),before);}
Run("Last four bullets indicator follows live ammo boundaries, including empty",()=>{var d=guns.First(d=>d.EffectKind=="ordinary"&&d.TriggerIndex==4);var(p,w)=Equip(d,5);Check(Status(p,w)==null,"five rounds is inactive");for(int n=4;n>=1;n--){w.Magazine!.Count=n;Check(Status(p,w)?.Active==true,"last "+n+" is active");}w.Magazine!.Count=0;Check(Status(p,w)==null,"empty is hidden");w.Magazine.Count=30;Check(Status(p,w)==null,"refilled magazine is hidden");});
Run("HUD reads never advance shot counters or consume buffs",()=>{var d=guns.First(d=>d.EffectKind=="ordinary"&&d.TriggerIndex==6);var(p,w)=Equip(d);for(int i=0;i<100;i++)_=Status(p,w);Check(Number(w,"TotalShots")==0&&Number(w,"ShotsInMagazine")==0,"no phantom shots");});
Run("ADS-only status appears and disappears with the actual aiming gate",()=>{var d=guns.First(d=>d.CustomEffect?.Trigger=="while_ads");var(p,w)=Equip(d);Check(Status(p,w)==null,"hipfire hidden");p.HandsController!.IsAiming=true;Check(Status(p,w)?.Active==true,"ADS active");p.HandsController.IsAiming=false;Check(Status(p,w)==null,"ADS ended");});
Run("Magazine-wide chamber check status activates immediately and clears on reload",()=>{var d=guns.First(d=>d.CustomEffect?.Trigger=="chamber_buff");var(p,w)=Equip(d);Check(Status(p,w)==null,"not checked");LegendaryRuntime.NotifyChamberCheck(w);Check(Status(p,w)?.Active==true,"ready before first shot");Fire(p,w);Check(Status(p,w)?.Active==true,"remains after shot");LegendaryRuntime.NotifyReloadCompleted(w,false);Check(Status(p,w)==null,"reload clears");});
Run("Draw charge is spent once without keeping its permanent recoil penalty active in the HUD",()=>{var d=guns.First(d=>d.SpecialId=="zantetsuken");var(p,w)=Equip(d);Check(Status(p,w)?.Detail=="Next shot ready","draw arms");Fire(p,w);Check(Status(p,w)==null,"constant recoil is not empowered damage");p.HandsController=new Player.FirearmController{Item=new Weapon()};LegendaryRuntime.ObserveHeldWeapon(p);p.HandsController=new Player.FirearmController{Item=w};Check(Status(p,w)?.Active==true,"redraw rearms once");});
Run("Every-N shot uses the upcoming shot and resets its indicator after firing",()=>{var d=guns.First(d=>d.CustomEffect?.Trigger=="every_n_shot");var(p,w)=Equip(d);_=Status(p,w);Set(w,"TotalShots",d.CustomEffect!.Count-2);Check(Status(p,w)==null,"not next interval");Set(w,"TotalShots",d.CustomEffect.Count-1);Check(Status(p,w)?.Detail=="Next shot ready","upcoming shot qualified");Fire(p,w);Check(Status(p,w)==null,"not stale from last shot");});
Run("After-pause cooldown counts down then becomes active",()=>{var d=guns.First(d=>d.CustomEffect?.Trigger=="after_pause");var(p,w)=Equip(d);_=Status(p,w);Fire(p,w);Check(Status(p,w) is {Active:false,Ready:>0},"real cooldown");Time.time+=d.CustomEffect!.Seconds;Check(Status(p,w)?.Active==true,"ready exactly at deadline");});
Run("ADS charge timer stops displaying when aiming ends",()=>{var d=guns.First(d=>d.CustomEffect?.Trigger=="ads_seconds_next_shot");var(p,w)=Equip(d);p.HandsController!.IsAiming=true;Check(Status(p,w) is {Active:false,Ready:>0},"charging");p.HandsController.IsAiming=false;Check(Status(p,w)==null,"no inactive charge");});
Run("Uninterrupted held-trigger effect ends on release",()=>{var d=guns.First(d=>d.CustomEffect?.Trigger=="uninterrupted_at_least"&&d.CustomEffect.RequireTriggerHeld);var(p,w)=Equip(d);((Player.FirearmController)p.HandsController!).IsTriggerPressed=true;_=Status(p,w);Set(w,"TriggerHeldShots",d.CustomEffect!.Count);Set(w,"UninterruptedShots",d.CustomEffect.Count);Check(Status(p,w)?.Active==true,"qualified held trigger");((Player.FirearmController)p.HandsController!).IsTriggerPressed=false;LegendaryRuntime.NotifyTriggerReleased(w);Check(Status(p,w)==null,"release ends effect");});
Run("Binary status only exists in its real selected firing mode",()=>{var d=guns.First(d=>d.SpecialId=="wave015_binary_hunter");var(p,w)=Equip(d);Check(Status(p,w)==null,"semi has no binary effect");w.Mode="burst";Check(Status(p,w)?.Detail=="Binary Trigger active","binary selected");w.Mode="fullauto";Check(Status(p,w)==null,"switching clears");});
Run("Changing fire mode resets upcoming within-mode shot ordinal",()=>{var d=guns.First(d=>d.SpecialId=="wave015_hammer_pair");var(p,w)=Equip(d);w.Mode="burst";_=Status(p,w);Set(w,"LastShotFireMode","single");Set(w,"ShotsInCurrentFireMode",7);_=Status(p,w);Check(Number(w,"ShotsInCurrentFireMode")==7,"read does not mutate committed ordinal");Check(LegendaryRuntime.GetStatModifiers(w).HasAny,"native preview remains valid");});
Run("Holstering, death and another weapon cannot inherit held-gun statuses",()=>{var d=guns.First(d=>d.CustomEffect?.Trigger=="while_ads");var(p,w)=Equip(d);p.HandsController!.IsAiming=true;Check(Status(p,w)!=null,"held conditional effect");p.HandsController=new Player.FirearmController{Item=new Weapon()};Check(Status(p,w)==null,"stowed weapon");p.HandsController=new Player.FirearmController{Item=w};p.HealthController.IsAlive=false;Check(Status(p,w)==null,"dead wearer");});
Run("All gun catalog entries can be evaluated without creating inactive rows or shots",()=>{foreach(var d in guns){LegendaryRuntime.ResetSession();var(p,w)=Equip(d);foreach(var mode in new[]{"single","burst","fullauto"}){w.Mode=mode;foreach(var n in new[]{0,1,4,30}){w.Magazine!.Count=n;var status=Status(p,w);Check(status==null||status.Value.Visible,"only visible statuses");Check(Number(w,"TotalShots")==0,"no status-induced shots");}}Singleton<GameWorld>.Instance.Players.Clear();}Check(guns.Length>1000,"entire weapon catalog");});
MeleeRuntime.HeldState Melee(Player p,string ability){var d=catalog.Variants.First(d=>d.MeleeEffect?.Ability==ability);var held=new MeleeRuntime.HeldState{Player=p,Item=new(){StringTemplateId=d.TemplateId},Roll=RarityResolver.Resolve(new Item{StringTemplateId=d.TemplateId})!};MeleeRuntime.HeldItems[p]=held;return held;}
Run("Melee charge hides after it is spent and stays separate from persistent relief",()=>{var p=new Player();var held=Melee(p,"counter_charge");Check(!AdvancedMeleeRuntime.Statuses(p).Any(),"uncharged hidden");held.Advanced.Charged=true;Check(AdvancedMeleeRuntime.Statuses(p).Single().Detail=="Next attack ready","charge ready");held.Advanced.Charged=false;Check(!AdvancedMeleeRuntime.Statuses(p).Any(),"spent hidden");var relief=catalog.Variants.First(d=>d.MeleeEffect?.KillPainkillerSeconds>0);AdvancedMeleeRuntime.AddRelief(p,relief,123);MeleeRuntime.HeldItems.Clear();var status=AdvancedMeleeRuntime.Statuses(p).Single();Check(status.Seconds==123&&status.Active,"native persistent timer survives stow");});
Run("Emergency Access is hidden after the raid use is spent",()=>{var p=new Player();var d=catalog.Variants.First(d=>d.MeleeEffect?.UnlocksDoorOncePerRaid==true);MeleeRuntime.HeldItems[p]=new(){Player=p,Item=new(),Roll=RarityResolver.Resolve(new Item{StringTemplateId=d.TemplateId})!};MasterKeyRuntime.Ready=true;Check(AdvancedMeleeRuntime.Statuses(p).Any(),"raid use ready");MasterKeyRuntime.Ready=false;Check(!AdvancedMeleeRuntime.Statuses(p).Any(),"no inactive spent row");MasterKeyRuntime.Ready=true;});
Run("Air Fare only shows when an extra jump is actually available",()=>{var p=new Player();var d=catalog.Variants.First(d=>d.MeleeEffect?.ExtraJumpHeightMultiplier>0);MeleeRuntime.HeldItems[p]=new(){Player=p,Item=new(),Roll=RarityResolver.Resolve(new Item{StringTemplateId=d.TemplateId})!};AdvancedMeleeRuntime.ExtraReady=false;Check(!AdvancedMeleeRuntime.Statuses(p).Any(),"ground/spent hidden");AdvancedMeleeRuntime.ExtraReady=true;Check(AdvancedMeleeRuntime.Statuses(p).Single().Detail=="Extra jump ready","airborne usable");});
Run("Status filtering removes all inactive rows and merges active/cooldown components",()=>{var list=EffectStatusModel.Visible(new[]{new EffectStatusModel.Status("off","Off",false),new("smoke","Smoke",false,ready:300),new("smoke","Smoke",true,60),new("gun","Gun",true),new("melee","Melee",true)});Check(list.Count==3,"one row per effect plus other sources");Check(list[0].Active&&list[0].Seconds==60&&list[0].Ready==300,"merged active and cooldown");Check(list.All(s=>!s.Label.Contains("Inactive",StringComparison.OrdinalIgnoreCase)),"no inactive text");});
Run("Variable-height rows stack without overlaps at every F12 scale",()=>{foreach(var heights in new[]{Array.Empty<float>(),new[]{25f},new[]{25f,53f,81f,25f,109f}}){var offsets=EffectStatusLayout.Offsets(heights,out var total);foreach(var scale in new[]{.5f,1f,3f})for(int i=1;i<offsets.Length;i++)Check(offsets[i]*scale>=(offsets[i-1]+heights[i-1]+EffectStatusLayout.Gap)*scale,"wrapped rows stay apart");if(heights.Length>0)Check(total>=offsets[^1]+heights[^1],"root contains every row");else Check(total==0,"empty stack");}});

MeleeRuntime.HeldState EquipMelee(Player p,VariantDefinition d){var h=new MeleeRuntime.HeldState{Player=p,Item=new(){StringTemplateId=d.TemplateId},Roll=RarityResolver.Resolve(new Item{StringTemplateId=d.TemplateId})!};MeleeRuntime.HeldItems[p]=h;return h;}
Run("Actual melee health and AK-family eligibility gates hide unavailable bonuses",()=>{var p=new Player();var ak=catalog.Variants.First(d=>d.MeleeEffect?.RequiresAk74==true);EquipMelee(p,ak);Check(!AdvancedMeleeRuntime.Statuses(p).Any(),"no equipped AK");p.HasAk74=true;Check(AdvancedMeleeRuntime.Statuses(p).Any(),"AK requirement met");var low=catalog.Variants.First(d=>d.MeleeEffect?.MaximumHealthFraction<1);EquipMelee(p,low);p.HealthFraction=1;Check(!AdvancedMeleeRuntime.Statuses(p).Any(),"healthy effect hidden");p.HealthFraction=low.MeleeEffect!.MaximumHealthFraction;Check(AdvancedMeleeRuntime.Statuses(p).Any(),"at actual health threshold");});
Run("Actual night, sniper, empty-primary and bleeding gates drive melee statuses",()=>{var p=new Player();var night=catalog.Variants.First(d=>d.MeleeEffect?.RequiresNight==true);EquipMelee(p,night);Check(!AdvancedMeleeRuntime.Statuses(p).Any(),"day hidden");Singleton<GameWorld>.Instance.GameDateTime.Date=new(2026,10,17,22,0,0);Check(AdvancedMeleeRuntime.Statuses(p).Any(),"night active");var sniper=catalog.Variants.First(d=>d.MeleeEffect?.RequiresSniper==true);EquipMelee(p,sniper);Check(!AdvancedMeleeRuntime.Statuses(p).Any(),"no sniper");p.Primaries.Add(new(){WeapClass="sniperRifle"});Check(AdvancedMeleeRuntime.Statuses(p).Any(),"real sniper class");var empty=catalog.Variants.First(d=>d.MeleeEffect?.RequiresEmptyPrimaries==true);EquipMelee(p,empty);Check(!AdvancedMeleeRuntime.Statuses(p).Any(),"loaded primary");p.Primaries[0].Magazine!.Count=0;Check(AdvancedMeleeRuntime.Statuses(p).Any(),"empty equipped primary");p.Primaries.Clear();Check(!AdvancedMeleeRuntime.Statuses(p).Any(),"requires an equipped primary");var bleeding=catalog.Variants.First(d=>d.MeleeEffect?.RequiresBleeding==true);EquipMelee(p,bleeding);Check(!AdvancedMeleeRuntime.Statuses(p).Any(),"no bleed");p.Bleeding=true;Check(AdvancedMeleeRuntime.Statuses(p).Any(),"bleeding active");});
Run("Melee stationary recovery and stacked slash speed reflect committed state",()=>{var p=new Player();var stationary=catalog.Variants.First(d=>d.MeleeEffect?.StationaryHealPerSecond>0);var held=EquipMelee(p,stationary);held.Advanced.StationarySince=Time.time;Check(AdvancedMeleeRuntime.Statuses(p).Single().Detail=="Charging","real charge period");Time.time+=stationary.MeleeEffect!.StationarySeconds+.1f;held.Advanced.StationaryRecoveryActive=true;Check(AdvancedMeleeRuntime.Statuses(p).Single().Active,"recovery active");held.Advanced.StationaryRecoveryActive=false;p.InputDirection=new(){x=1};Check(!AdvancedMeleeRuntime.Statuses(p).Any(),"moving ends recovery");var slash=catalog.Variants.First(d=>d.MeleeEffect?.SlashSpeedPerAttack>0);held=EquipMelee(p,slash);Check(!AdvancedMeleeRuntime.Statuses(p).Any(),"no accumulated slash speed");held.Advanced.SlashSpeed=.4f;Check(AdvancedMeleeRuntime.Statuses(p).Single().Detail.Contains("40"),"actual accumulated bonus");});
Run("All melee signatures evaluate without inactive rows",()=>{foreach(var d in catalog.Variants.Where(d=>d.MeleeEffect!=null)){var p=new Player();EquipMelee(p,d);foreach(var status in AdvancedMeleeRuntime.Statuses(p)){Check(status.Visible,"visible only");Check(!status.Label.Contains("Inactive",StringComparison.OrdinalIgnoreCase),"no inactive labels");}MeleeRuntime.HeldItems.Clear();}});
Run("Expired native pain relief disappears after weapon switch",()=>{var p=new Player();var d=catalog.Variants.First(d=>d.MeleeEffect?.KillPainkillerSeconds>0);AdvancedMeleeRuntime.AddRelief(p,d,0);Check(!AdvancedMeleeRuntime.Statuses(p).Any(),"zero native lifetime hidden");});

Run("An empty held weapon retains its real time cooldown without an active-shot icon",()=>{var d=guns.First(d=>d.CustomEffect?.Trigger=="after_pause");var(p,w)=Equip(d,1);_=Status(p,w);Fire(p,w);Check(Status(p,w) is {Active:false,Ready:>0},"empty cooldown visible");Time.time+=d.CustomEffect!.Seconds;Check(Status(p,w)==null,"empty ready weapon does not claim active damage");});

Run("Elephant Gun is a separate MP-43-1C slug-only design with 4 random bonuses",()=>{
 var d=catalog.Variants.Single(d=>d.SpecialId=="elephant_gun");
 Check(d.SourceTemplateId=="5580223e4bdc2d1c128b457f"&&d.AffixCount==4,"correct native base and random bonus count");
 Check(d.EffectText.Contains("Slugs only")&&d.EffectText.Contains("600%")&&d.EffectText.Contains("300%")&&d.EffectText.Contains("60%"),"clear signature description");
 Check(catalog.Variants.Count(x=>x.TemplateId==d.TemplateId)==1,"unique persistent identity");
});
Run("Elephant Gun applies its full signature to every actual slug cartridge",()=>{
 var d=guns.Single(d=>d.SpecialId=="elephant_gun");var(p,w)=Equip(d,2);
 var slug=new Ammo{AmmoTemplate=new(){ProjectileCount=1}};
 var scope=LegendaryRuntime.BeginShot(w,slug,p.ProfileId,2)!;
 Check(scope.Application.DamageMultiplier==7f,"+600% means 7 times damage");
 Check(LegendaryRuntime.GetStatModifiers(w).RecoilDelta==3f,"+300% recoil delta");
 Check(Math.Abs(LegendaryRuntime.GetReloadSpeedMultiplier(w)-.4f)<.00001f,"40% normal reload speed");
 LegendaryRuntime.EndShot(scope);
});
Run("Buckshot and missing ammunition have no Elephant Gun signature",()=>{
 var d=guns.Single(d=>d.SpecialId=="elephant_gun");var(p,w)=Equip(d,2);
 foreach(var pellets in new[]{2,3,8,9,12,16,20}){
  var scope=LegendaryRuntime.BeginShot(w,new Ammo{AmmoTemplate=new(){ProjectileCount=pellets}},p.ProfileId,2)!;
  Check(scope.Application.DamageMultiplier==1f,"native pellet damage "+pellets);
  Check(!LegendaryRuntime.GetStatModifiers(w).HasAny,"native pellet recoil "+pellets);
  Check(LegendaryRuntime.GetReloadSpeedMultiplier(w)==1f,"native pellet reload "+pellets);
  LegendaryRuntime.EndShot(scope);
 }
 w.CurrentAmmoTemplate=null;Check(!ElephantGunRules.IsSlug(null),"empty is not slug");
 Check(!LegendaryRuntime.GetStatModifiers(w).HasAny&&LegendaryRuntime.GetReloadSpeedMultiplier(w)==1f,"unloaded preview does not invent bonuses");
});
Run("Mixed double barrels use the fired shell rather than the other chamber",()=>{
 var d=guns.Single(d=>d.SpecialId=="elephant_gun");var(p,w)=Equip(d,2);
 foreach(var pellets in new[]{1,8,8,1,1,8}){
  w.CurrentAmmoTemplate=new(){ProjectileCount=pellets==1?8:1};
  var scope=LegendaryRuntime.BeginShot(w,new Ammo{AmmoTemplate=new(){ProjectileCount=pellets}},p.ProfileId,2)!;
  Check(scope.Application.DamageMultiplier==(pellets==1?7:1),"actual damage follows barrel");
  Check(LegendaryRuntime.GetStatModifiers(w).RecoilDelta==(pellets==1?3:0),"actual recoil follows barrel");
  LegendaryRuntime.EndShot(scope);
  Check(LegendaryRuntime.GetStatModifiers(w).RecoilDelta==(pellets==1?0:3),"no leaked shot scope in next-shell preview");
 }
});
Run("Reloading an empty Elephant Gun uses incoming slugs or buckshot",()=>{
 var d=guns.Single(d=>d.SpecialId=="elephant_gun");var(p,w)=Equip(d,0);w.CurrentAmmoTemplate=null;
 foreach(var incoming in new[]{1,8,1,16}){
  var reload=ElephantGunRules.BeginReload(w,new(){ProjectileCount=incoming});
  Check(Math.Abs(LegendaryRuntime.GetReloadSpeedMultiplier(w)-(incoming==1?.4f:1f))<.00001f,"selected incoming ammo speed");
  Check(ElephantGunRules.EndReload(w,reload),"completion/cancel releases override");
  Check(LegendaryRuntime.GetReloadSpeedMultiplier(w)==1f,"empty normal after completion");
 }
});
Run("Incoming buckshot overrides a loaded slug and incoming slugs override loaded buckshot",()=>{
 var d=guns.Single(d=>d.SpecialId=="elephant_gun");var(p,w)=Equip(d,1);
 w.CurrentAmmoTemplate=new(){ProjectileCount=1};
 var buck=ElephantGunRules.BeginReload(w,new(){ProjectileCount=8});Check(LegendaryRuntime.GetReloadSpeedMultiplier(w)==1,"buckshot is not slowed by remaining slug");
 var slug=ElephantGunRules.BeginReload(w,new(){ProjectileCount=1});Check(!ElephantGunRules.EndReload(w,buck),"late previous callback cannot clear new reload");
 w.CurrentAmmoTemplate=new(){ProjectileCount=8};Check(Math.Abs(LegendaryRuntime.GetReloadSpeedMultiplier(w)-.4f)<.00001f,"incoming slug slowed despite remaining buckshot");
 Check(ElephantGunRules.EndReload(w,slug)&&LegendaryRuntime.GetReloadSpeedMultiplier(w)==1,"cleared back to buckshot");
});
Run("Native and other legendary shotguns do not enter Elephant Gun reload overrides",()=>{
 foreach(var d in guns.Where(d=>d.WeaponClass=="shotgun"&&d.SpecialId!="elephant_gun")){
  var(p,w)=Equip(d);Check(ElephantGunRules.BeginReload(w,new(){ProjectileCount=1})==null,"unrelated signature unchanged");
 }
 Check(ElephantGunRules.BeginReload(new Weapon(),new(){ProjectileCount=1})==null,"native weapon untouched");
});
Run("Elephant Gun reload state cannot survive a session reset or cross weapon copies",()=>{
 var d=guns.Single(d=>d.SpecialId=="elephant_gun");var(p,w)=Equip(d,0);w.CurrentAmmoTemplate=null;
 var(other,copy)=Equip(d,0);copy.CurrentAmmoTemplate=null;
 var reload=ElephantGunRules.BeginReload(w,new(){ProjectileCount=1});Check(LegendaryRuntime.GetReloadSpeedMultiplier(copy)==1,"separate receiver");
 LegendaryRuntime.ResetSession();Check(LegendaryRuntime.GetReloadSpeedMultiplier(w)==1&&!ElephantGunRules.EndReload(w,reload),"reset removes all override ownership");
});

var elephantHarmony=new HarmonyLib.Harmony("efb.validation.elephant.reload");
elephantHarmony.CreateClassProcessor(typeof(ElephantGunReloadPatch)).Patch();
Run("Actual reload Harmony hook chooses incoming ammunition before the native method",()=>{
 var d=guns.Single(d=>d.SpecialId=="elephant_gun");var(p,w)=Equip(d,0);w.CurrentAmmoTemplate=null;var controller=(Player.FirearmController)p.HandsController!;
 foreach(var pellets in new[]{1,8,1,16}){
  int callbacks=0;controller.ReloadBarrels(new(){Ammo=new(){AmmoTemplate=new(){ProjectileCount=pellets}}},null,result=>{callbacks++;});
  Check(Math.Abs(controller.ObservedReloadSpeed-(pellets==1?.4f:1))<.00001,"Harmony prefix precedes original reload");
  controller.Pending!(new(){Succeed=true});Check(callbacks==1&&LegendaryRuntime.GetReloadSpeedMultiplier(w)==1,"completion forwarded and scope removed");
  Check(controller.AnimatorSpeed==1,"cached animator penalty restored");
 }
});
Run("Actual reload Harmony finalizer removes failed slug reload state",()=>{
 var d=guns.Single(d=>d.SpecialId=="elephant_gun");var(p,w)=Equip(d,0);w.CurrentAmmoTemplate=null;var controller=(Player.FirearmController)p.HandsController!;
 controller.ThrowOnReload=true;bool threw=false;try{controller.ReloadBarrels(new(){Ammo=new()},null,_=>{});}catch(InvalidOperationException){threw=true;}
 Check(threw,"native failure propagates");Check(LegendaryRuntime.GetReloadSpeedMultiplier(w)==1&&controller.AnimatorSpeed==1,"finalizer restores override and animator");
 controller.ThrowOnReload=false;controller.ReloadBarrels(new(){Ammo=new()},null,_=>{});controller.Pending!(new(){Succeed=false});Check(LegendaryRuntime.GetReloadSpeedMultiplier(w)==1,"cancel/failure callback restores override");
});
Run("Rejected reload requests never leave a slug override installed",()=>{
 var d=guns.Single(d=>d.SpecialId=="elephant_gun");var(p,w)=Equip(d,0);w.CurrentAmmoTemplate=null;var controller=(Player.FirearmController)p.HandsController!;
 controller.Blindfire=true;controller.ReloadBarrels(new(){Ammo=new()},null,_=>{});Check(LegendaryRuntime.GetReloadSpeedMultiplier(w)==1,"blindfire does not arm a reload");
 controller.Blindfire=false;controller.CanReload=false;controller.ReloadBarrels(new(){Ammo=new()},null,_=>{});Check(LegendaryRuntime.GetReloadSpeedMultiplier(w)==1,"unavailable operation does not arm a reload");
 controller.CanReload=true;controller.ReloadBarrels(new(),null,_=>{});Check(LegendaryRuntime.GetReloadSpeedMultiplier(w)==1,"empty ammunition pack does not arm a reload");
});
elephantHarmony.UnpatchSelf();

Run("Truly permanent firearm signatures do not add status rows",()=>{foreach(var d in guns.Where(LegendaryRuntime.PermanentStatus)){var(p,w)=Equip(d);p.HandsController!.IsAiming=true;Check(Status(p,w)==null,"unconditional signature hidden: "+d.ShortName);}});
Run("All ordinary prone and ADS families show only under their exact condition",()=>{foreach(var trigger in new[]{13,16,17,18}){var d=guns.First(d=>d.EffectKind=="ordinary"&&d.TriggerIndex==trigger);var(p,w)=Equip(d);p.HandsController!.IsAiming=trigger==13; p.Pose=trigger==18?Pose.Prone:trigger==17?Pose.Duck:Pose.Stand;p.MovementContext.IsInPronePose=trigger==18;Check(Status(p,w)?.Active==true,"active condition "+trigger);p.HandsController.IsAiming=trigger==16;p.Pose=Pose.Stand;p.MovementContext.IsInPronePose=false; if(trigger==13||trigger==16)Check(Status(p,w)==null,"aim gate ended");}});
Run("Requiem shows its final-round package without showing its permanent base bonus",()=>{var d=guns.First(d=>d.SpecialId=="requiem");var(p,w)=Equip(d,6);Check(Status(p,w)==null,"baseline damage bonus hidden");w.Magazine!.Count=1;Check(Status(p,w)?.Detail=="Final round ready","conditional package ready");w.Magazine.Count=0;Check(Status(p,w)==null,"empty ends readiness");});
Run("Kaban's Kebab only shows its periodic explosive cartridge",()=>{var d=guns.First(d=>d.SpecialId=="kabans_kabab");var(p,w)=Equip(d);_=Status(p,w);Check(Status(p,w)==null,"base rate/recoil hidden");Set(w,"ShotsInMagazine",9);Check(Status(p,w)?.Detail=="Next explosive shot ready","10th cartridge ready");Set(w,"ShotsInMagazine",10);Check(Status(p,w)==null,"old explosive cartridge is not reused");});
Run("Unconditional melee stats do not display, while prone damage protection does",()=>{foreach(var d in catalog.Variants.Where(d=>d.MeleeEffect!=null&&AdvancedMeleeRuntime.PermanentStatus(d.MeleeEffect))){var p=new Player();EquipMelee(p,d);Check(!AdvancedMeleeRuntime.Statuses(p).Any(),"flat held modifier hidden: "+d.ShortName);}var prone=catalog.Variants.First(d=>d.MeleeEffect?.RequiresProne==true);var q=new Player();EquipMelee(q,prone);Check(!AdvancedMeleeRuntime.Statuses(q).Any(),"standing protection absent");q.MovementContext.IsInPronePose=true;Check(AdvancedMeleeRuntime.Statuses(q).Single().Active,"prone protection visible");q.MovementContext.IsInPronePose=false;Check(!AdvancedMeleeRuntime.Statuses(q).Any(),"standing ends protection");});
Run("Instant target-hit and kill effects no longer pretend to be always-active buffs",()=>{foreach(var d in catalog.Variants.Where(d=>d.MeleeEffect is {} e&&(e.HealOnFleshHit>0||e.HealOnKill>0||e.HeavyBleedOnStab||e.HeadSlashDamageMultiplier!=1f)&&e.Ability.Length==0&&e.DamagePerFleshHit==0)){var p=new Player();EquipMelee(p,d);Check(!AdvancedMeleeRuntime.Statuses(p).Any(),"no untriggered proc buff: "+d.ShortName);}});
Run("Instant trigger receipts are per wearer, expire without polling refresh and preserve category filters",()=>{EffectStatusModel.ResetNotices();EffectStatusModel.Notify("owner","rig:TR17-1","Loose Change",100,"automatic packing");var row=EffectStatusModel.Notices("owner",100).Single();Check(row.Active&&row.Seconds==0&&row.Detail.StartsWith("Triggered"),"receipt is not a timed buff");Check(!EffectStatusModel.Notices("other",100).Any(),"other players cannot inherit notice");for(int n=0;n<100;n++)Check(EffectStatusModel.Notices("owner",101.999f).Count()==1,"reads do not change expiry");Check(!EffectStatusModel.Notices("owner",102).Any(),"exact expiry");Check(!StatusVisibility.Allows(row.Key,true,true,true,false),"existing rig toggle applies");EffectStatusModel.Notify("owner","gun:x","Proc",102,"hit");EffectStatusModel.ResetNotices();Check(!EffectStatusModel.Notices("owner",102).Any(),"session reset clears notices");});
Run("Charge and conditional cooldown labels never invent an Active 0s duration",()=>{foreach(var detail in new[]{"","2 shots ready","Triggered · restoration"}){var row=new EffectStatusModel.Status("earpiece:x","Example",true,ready:20,detail:detail);Check(!row.Label.Contains("Active 0s"),"no fake zero-duration timer");Check(row.Label.Contains("Cooldown 20s"),"real cooldown retained");}var timed=new EffectStatusModel.Status("x","Timed",true,5,20,"2 charges");Check(timed.Label.Contains("5s")&&timed.Label.Contains("20s")&&timed.Label.Contains("2 charges"),"active timer, charges and cooldown coexist");});

Console.WriteLine($"Combat status: {cases-failures}/{cases} cases passed; {assertions} assertions; {failures} failures.");return failures==0?0:1;
