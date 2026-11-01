using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.ActiveHeadphones;
using EFT.Animations;
using EFT.InventoryLogic;
using EscapeFromBorderlands.Client;
using UnityEngine;
using HarmonyLib;

int cases=0,assertions=0,failed=0;
void Check(bool ok,string why){assertions++;if(!ok)throw new Exception(why);}
void Near(double value,double expected,string why)=>Check(Math.Abs(value-expected)<.0002,$"{why}: {value} != {expected}");
void Run(string name,Action test){var filter=Environment.GetEnvironmentVariable("EFB_EARPIECE_CASE_FILTER");if(!string.IsNullOrEmpty(filter)&&!name.Contains(filter,StringComparison.Ordinal))return;cases++;try{test();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+(e.InnerException??e));}}
object? Call(Type type,string method,params object?[] args)=>type.GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!.Invoke(null,args);
var audioHarmony=new Harmony("efb.validation.earpiece.audio");
foreach(var name in new[]{"EarpieceSourceRangePatch","EarpieceSourceVolumePatch","EarpieceSourceReleasePatch","EarpieceGunScopePatch","EarpieceQueueSourcePatch","EarpieceQueuePlaybackPatch","EarpieceGunPlaybackOutputPatch","EarpieceScheduledGunSourcePatch","EarpieceGunMixerPatch","EarpieceItemAudioScopePatch","EarpieceAmbientPatch","EarpieceSourcePlaybackPatch","EarpieceClipDistancePatch","EarpieceActorSourcePatch","EarpieceSecondaryStepRangePatch","EarpieceStepScopePatch","EarpieceStepAudibilityPatch","EarpieceLocalStepInputPatch","EarpieceLocalItemInputPatch","LegendaryEarpieceAttributePatch","LegendaryEarpieceInspectStatsPatch","LegendaryEarpieceCompareStatsPatch","LegendaryEarpieceStatIconPatch","BackpackInventorySoundScopePatch","BackpackInventoryVolumePatch"})
 if(typeof(EarpieceAudio).Assembly.GetType("EscapeFromBorderlands.Client."+name) is { } type)audioHarmony.CreateClassProcessor(type).Patch();
var catalog=LegendaryCatalog.Load();var ears=catalog.Variants.Where(v=>v.IsEarpiece).ToDictionary(v=>v.RuntimeKey);
Item New(string key,string? id=null)=>new Headphones{Id=id??Guid.NewGuid().ToString("N"),StringTemplateId=ears[key].TemplateId};
(Player Player,Player.FirearmController Firearm,EarpieceRuntime.State State) Scene(string key)
{
 EarpieceRuntime.Reset();WeaponCombatState.ResetSession();RarityResolver.ResetSession();ReusableGrenadeRuntime.Charges.Reset();BackpackRuntime.WeatherBag=false;BackpackRuntime.InventoryNoise=1;Time.time=1000;
 var p=new Player();var f=new Player.FirearmController{Player=p,Item=new Weapon()};p.HandsController=f;
 p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem=New(key);
 Singleton<GameWorld>.Instance=new(){MainPlayer=p,RegisteredPlayers=[p]};Singleton<BetterAudio>.Instance=null!;MonoBehaviourSingleton<BetterAudio>.Instance=new();
 return(p,f,EarpieceRuntime.Get(p)!);
}
void Tick()=>EarpieceRuntime.Maintain(Singleton<GameWorld>.Instance);
Player Victim(Player killer,bool firearm,float distance=5)
{
 var enemy=new Player{Position=new(distance,0,0),LastAggressor=killer,LastDamageInfo=new(){DamageType=firearm?EDamageType.Bullet:EDamageType.Melee,Weapon=firearm?killer.HandsController!.Item:new Knife()}};
 Singleton<GameWorld>.Instance.RegisteredPlayers.Add(enemy);return enemy;
}
void AudioUpdate(SuperSource source)
{
 source.UpdateSourceVolume();
}
Run("39 approved signatures cover exactly 11 standalone native bases",()=>{
 Check(ears.Count==39,"39 signatures");var expected=new[]{4,4,3,5,2,5,3,3,3,4,3};
 for(int i=1;i<=11;i++)Check(ears.Keys.Count(k=>k.StartsWith($"EP{i:00}-"))==expected[i-1],"selected per-base count");
 Check(ears.Values.Select(x=>x.SourceTemplateId).Distinct().Count()==11,"11 bases");Check(ears.Values.All(x=>x.AffixCount==4&&x.EffectKind=="earpiece"&&x.SpawnEnabled),"4 boons plus signature and spawnability");
 foreach(var key in new[]{"EP01-3","EP02-4","EP03-4","EP03-5","EP04-0","EP05-1","EP05-2","EP05-5","EP07-3","EP07-4","EP08-4","EP08-5","EP09-1","EP09-4","EP10-5","EP11-4","EP11-5"})Check(!ears.ContainsKey(key),"removed signature absent "+key);
 Check(ears["EP05-3"].ShortName=="Low-Profile"&&ears["EP06-1"].ShortName=="Matamata Turtle"&&ears["EP09-2"].ShortName=="Fast Clock","final names");
 Check(!ears["EP05-3"].EffectText.Contains("Arm Stamina")&&!ears["EP06-1"].EffectText.Contains("enemy bullet"),"replaced signatures discarded");
});
Run("3900 physical copies roll 4 distinct compatible bounded bonuses with stable seeds",()=>{
 var observed=new HashSet<string>();
 foreach(var d in ears.Values)for(int i=0;i<100;i++){
  var item=new Item{Id=$"earpiece-seed-{d.TemplateId}-{i}",StringTemplateId=d.TemplateId};var roll=RarityResolver.Resolve(item)!;
  var text=roll.AffixDescriptions.Skip(1).ToArray();Check(text.Length==4&&text.Distinct().Count()==4,"4 distinct descriptions");
  foreach(var line in text){observed.Add(line.Split(' ')[0]);Check(!line.Contains("Hard Stop"),"removed bonus absent");}
  Check(d.RuntimeKey!="EP09-2"||!text.Any(t=>t.StartsWith("Clear Head")),"no redundant immunity roll");
  var b=roll.Earpiece;
  foreach(var (value,lo,hi) in new[]{(b.Hearing,1.05f,1.15f),(b.Ambient,.75f,.9f),(b.SelfNoise,.75f,.9f),(b.Contusion,.7f,.85f),(b.AimCost,.8f,.9f),(b.Draw,1.1f,1.2f),(b.LegRecovery,1.1f,1.25f),(b.Aim,1.1f,1.2f)}){
   Check(value==1f||value>=lo-.0001f&&value<=hi+.0001f,"bounded roll");Near(Math.Round(value*20),value*20,"5% steps");
  }
  if(i==0){var original=roll.AffixDescriptions.ToArray();RarityResolver.ResetSession();Check(RarityResolver.Resolve(item)!.AffixDescriptions.SequenceEqual(original),"persistent item seed");}
 }
 Check(observed.Count==8,"all 8 allowed types reached");
});
Run("Checks require the correct completed action and share cooldowns between copies",()=>{
 var(p,f,s)=Scene("EP01-4");s.Effect.Check(1);Near(EarpieceRuntime.Aim(p),s.Roll.Earpiece.Aim,"chamber check does not grant magazine effect");
 s.Effect.Check(2);Near(s.Effect.Aim,1.4,"40% aiming speed");Near(s.Effect.Cooldown,15,"15 seconds");
 p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem=null;Check(EarpieceRuntime.Get(p)==null,"unequipped inactive");
 p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem=New("EP01-4");var next=EarpieceRuntime.Get(p)!;
 Check(!next.Effect.Active&&!next.Effect.Ready,"active buff ends; identical copy inherits cooldown");next.Effect.Check(2);Check(!next.Effect.Active,"cannot bypass cooldown");
 Time.time+=15;next.Effect.Check(2);Check(next.Effect.Active,"exact cooldown boundary");Time.time+=6;Near(next.Effect.Aim,1,"exact expiry");
 EarpieceRuntime.Reset();Check(EarpieceRuntime.Get(p)!.Effect.Ready,"new raid resets cooldowns");
});
Run("Cold Open charges exactly 3 cartridges and 1 damage snapshot covers all pellets",()=>{
 var(p,f,s)=Scene("EP01-5");Near(s.Effect.Fire(),1,"10 seconds required after equipping");Time.time+=10;
 for(int cartridge=0;cartridge<4;cartridge++){
  var shot=EarpieceRuntime.Begin(p,f.Item)!;Near(shot.Damage,cartridge<3?1.25:1,"charged cartridge");
  for(int pellet=0;pellet<12;pellet++)Near(EarpieceRuntime.CurrentShot!.Damage,shot.Damage,"pellets share snapshot");EarpieceRuntime.End(shot);Time.time+=.1f;
 }
 Check(s.Effect.Charges==0&&EarpieceRuntime.CurrentShot==null,"charges cannot accumulate and scope restored");Time.time+=10;Near(s.Effect.Fire(),1.25,"rearms after quiet interval");
});
Run("Dead Channel starts at 1 second; all WASD motion cancels it",()=>{
 var(p,f,s)=Scene("EP01-1");s.Roll.Earpiece.Ambient=.8f;Near(EarpieceAudio.Ambient,.8,"before threshold");Time.time+=1;Near(EarpieceAudio.Ambient,.2,"75% signature times 20% random reduction");
 p.InputDirection=new(1,0);Near(EarpieceAudio.Ambient,.8,"sideways movement cancels immediately");Tick();p.InputDirection=new();Time.time+=.5f;Near(EarpieceAudio.Ambient,.8,"timer resets");
});
Run("Hearing bonuses multiply, keep own sounds unchanged and select maps/footsteps",()=>{
 Near(EarpieceEffectRules.Hearing("EP05-3",1.15f,1.5f,false,"step","woods"),1.725,"Low-Profile plus maximum Far Signal");
 foreach(var map in new[]{"woods","shoreline","rezervbase"})Near(EarpieceEffectRules.Hearing("EP02-1",1.15f,1,false,"voice",map),1.725,"Moss Code +50% map-wide with independent Far Signal");
 Near(EarpieceEffectRules.Hearing("EP02-1",1.15f,1,false,"step","bigmap"),1.15,"other map retains random roll only");
 Near(EarpieceEffectRules.Hearing("EP11-1",1.15f,1,false,"step","woods"),1.4375,"6th Sense footstep range");
 Near(EarpieceEffectRules.Hearing("EP11-1",1.15f,1,false,"gun","woods"),1.15,"6th Sense leaves other audio range alone");
 Near(EarpieceEffectRules.Hearing("EP05-3",1.15f,1.5f,true,"step","woods"),1,"own range unchanged");
});
Run("Low-Profile accepts melee and grenade kills at 30 metres; kills refresh without stacking",()=>{
 var(p,f,s)=Scene("EP05-3");EarpieceRuntime.Killed(Victim(p,false,30));Near(s.Effect.Hearing,1.5,"melee qualifies at boundary");
 Time.time+=19;var enemy=Victim(p,false,8);enemy.LastDamageInfo=new(){DamageType=EDamageType.GrenadeFragment,Weapon=new ThrowWeap()};EarpieceRuntime.Killed(enemy);
 Near(s.Effect.Hearing,1.5,"grenade kill refreshes");Near(s.Effect.ActiveUntil-Time.time,20,"duration reset");Near(s.Effect.Cooldown,0,"no cooldown");
 Time.time+=20;Near(s.Effect.Hearing,1,"expires exactly");EarpieceRuntime.Killed(Victim(p,true,30.01f));Near(s.Effect.Hearing,1,"outside range excluded");
 var friendly=Victim(p,true);friendly.GroupId=p.GroupId="team";EarpieceRuntime.Killed(friendly);Near(s.Effect.Hearing,1,"friendly excluded");
});
Run("Shaggy Special counts grounded horizontal walking and sprint resets progress",()=>{
 var(p,f,s)=Scene("EP02-3");p.Physical.Stamina.Current=0;p.InputDirection=new(0,1);Tick();
 for(int i=0;i<200;i++){Time.time+=.1f;p.Position=new(i*.1f,0,0);Tick();}Time.time+=.1f;p.Position=new(20.1f,0,0);Tick();Near(p.Physical.Stamina.Current,20,"20% maximum restored");
 s.Effect.WalkDistance=19;p.MovementContext.IsSprintEnabled=true;Time.time+=.1f;Tick();Near(s.Effect.WalkDistance,0,"sprint resets");
 p.MovementContext.IsSprintEnabled=false;p.MovementContext.IsGrounded=false;Time.time+=.1f;p.Position=new(21,0,0);Tick();Near(s.Effect.WalkDistance,0,"falling adds no walking");
 p.MovementContext.IsGrounded=true;Time.time+=.1f;p.Position=new(100,0,0);Tick();Near(s.Effect.WalkDistance,0,"teleports and vehicles add no distance");
});
Run("Grounded and Quiet Hours preserve stamina and sway thresholds",()=>{
 var(p,f,s)=Scene("EP02-5");p.MovementContext.IsInPronePose=true;Tick();Time.time+=2.99f;Near(EarpieceRuntime.Sway(p),1,"3 seconds required");Time.time+=.01f;Near(EarpieceRuntime.Sway(p),.75,"25% sway reduction");Near(EarpieceRuntime.Value(p,"armRecovery"),1.5,"50% arms regen");
 p.MovementContext.IsInPronePose=false;Near(EarpieceRuntime.Sway(p),1,"leaving prone ends immediately");
 (p,f,s)=Scene("EP08-3");s.Roll.Earpiece.AimCost=.8f;
 foreach(var hour in new[]{20,23,0,5}){Singleton<GameWorld>.Instance.GameDateTime.Time=new(2026,10,30,hour,0,0);Near(EarpieceRuntime.Sway(p),.65,"night sway");Near(EarpieceRuntime.Value(p,"aimCost"),.52,"independent random aim-cost roll");}
 Singleton<GameWorld>.Instance.GameDateTime.Time=new(2026,10,30,6,0,0);Near(EarpieceRuntime.Sway(p),1,"06:00 ends night");Near(EarpieceRuntime.Value(p,"aimCost"),.8,"random boon remains");
});
Run("Budget Cuts restores once when Arm Stamina crosses to 0",()=>{
 var(p,f,s)=Scene("EP03-2");Tick();p.Physical.HandsStamina.Current=0;Time.time+=.1f;Tick();Near(p.Physical.HandsStamina.Current,50,"instant 50% maximum");
 p.Physical.HandsStamina.Current=0;Time.time+=.1f;Tick();Near(p.Physical.HandsStamina.Current,0,"cooldown prevents repeated exhaustion ticks");Time.time+=45;Tick();Near(p.Physical.HandsStamina.Current,0,"still at 0 does not re-trigger");
 p.Physical.HandsStamina.Current=1;Time.time+=.1f;Tick();p.Physical.HandsStamina.Current=0;Time.time+=.1f;Tick();Near(p.Physical.HandsStamina.Current,50,"new crossing after cooldown");
});
Run("Hostile firearm flesh damage triggers Loose Wire and post-hit Photo Finish only",()=>{
 var(p,f,s)=Scene("EP03-3");var enemy=new Player();var info=new DamageInfo{DamageType=EDamageType.Bullet,Weapon=new Weapon(),Player=new(){iPlayer=enemy}};
 p.ActiveHealthController.Health=250;EarpieceRuntime.Damaged(p,info);Near(EarpieceRuntime.Reload(p),1.35,"reload +35%");Near(s.Effect.Aim,1.25,"aim +25%");Time.time+=8;Near(EarpieceRuntime.Reload(p),1,"timed expiry");
 (p,f,s)=Scene("EP04-5");p.ActiveHealthController.Health=132;EarpieceRuntime.Damaged(p,info);Near(s.Effect.ActiveUntil-Time.time,6,"30% post-hit health");p.MovementContext.IsSprintEnabled=true;Near(EarpieceRuntime.Movement(p),1.3,"sprint only");p.MovementContext.IsSprintEnabled=false;Near(EarpieceRuntime.Movement(p),1,"walking unaffected");
 (p,f,s)=Scene("EP04-5");p.ActiveHealthController.Health=132;info.Weapon=new ThrowWeap();EarpieceRuntime.Damaged(p,info);Check(!s.Effect.Active,"Bullet-tagged grenade fragment excluded");
 info.Weapon=new Weapon();info.Player=new(){iPlayer=p};EarpieceRuntime.Damaged(p,info);Check(!s.Effect.Active,"self damage excluded");
});
Run("Sprint stop, sideways recoil, crouched recoil and ADS walking are distinct conditions",()=>{
 var(p,f,s)=Scene("EP04-1");s.Effect.Tick(true,true,false,false,0,100);Time.time++;s.Effect.Tick(true,false,false,false,0,100);Near(s.Effect.Aim,1.5,"actual sprint end triggers");Time.time+=5;Near(s.Effect.Aim,1,"5-second expiry");
 (p,f,s)=Scene("EP04-2");p.InputDirection=new(0,1);Near(EarpieceRuntime.Recoil(p),1,"forward motion normal");p.InputDirection=new(1,0);Near(EarpieceRuntime.Recoil(p),.75,"sideways recoil");
 (p,f,s)=Scene("EP08-1");p.PoseLevel=.2f;Near(EarpieceRuntime.Recoil(p),.6,"stationary crouch");p.InputDirection=new(0,1);Near(EarpieceRuntime.Recoil(p),1,"moving cancels");
 Near(EarpieceEffectRules.AimingWalkLimit(.33f),.665,"half the original ADS penalty");
});
Run("Garrote Wire and I Knocked apply the approved movement/damage values",()=>{
 var(p,f,s)=Scene("EP05-4");p.HandsController=new Player.BaseKnifeController{Player=p,Item=new Knife()};Near(EarpieceRuntime.Movement(p),1.2,"melee walk speed");Near(EarpieceRuntime.MeleeSpeed(p),1.25,"melee attack speed");p.HandsController=f;Near(EarpieceRuntime.Movement(p),1,"weapon switch ends movement bonus");
 (p,f,s)=Scene("EP07-1");s.Effect.Breached();Near(s.Effect.Damage,1.25,"25% damage");Near(EarpieceRuntime.Movement(p),1.3,"30% walking");Near(s.Effect.ActiveUntil-Time.time,10,"revised 10-second duration");Near(s.Effect.Cooldown,20,"20-second cooldown");
});
Run("Tempo Shift counts 5 cartridges; 2nd Verse only follows a repair event",()=>{
 var(p,f,s)=Scene("EP06-3");for(int i=0;i<4;i++){s.Effect.Fire();Time.time+=.2f;}Near(s.Effect.FireRate,1,"needs 5 shots");s.Effect.Fire();Near(s.Effect.FireRate,1.2,"20% fire rate");Near(s.Effect.Cooldown,12,"12 seconds");Time.time+=4;Near(s.Effect.FireRate,1,"4-second duration");
 (p,f,s)=Scene("EP06-5");s.Effect.Check(1);Check(!s.Effect.Active,"checking working firearm does not trigger");Call(typeof(EarpieceMalfunctionRepairedPatch),"Postfix",new Player.FirearmController.RepairMalfunction{Weapon=f.Item});Near(s.Effect.FireRate,1.4,"40% fire rate");Near(EarpieceRuntime.Recoil(p),.5,"50% recoil");Time.time+=10;Near(s.Effect.FireRate,1,"revised 10 seconds");
});
Run("Crossfade waits for completed melee-to-firearm draw and Band Hopper speeds both halves",()=>{
 var(p,f,s)=Scene("EP06-2");var knife=new Knife();EarpieceSwitching.Queue(p,knife,f.Item,true);var complete=false;
 object?[] draw=[f,1.2f,(Action)(()=>complete=true)];Call(typeof(EarpieceFirearmSwitchPatch),"Prefix",draw);Near(s.Effect.Damage,1,"draw not yet finished");((Action)draw[2]!)();Check(complete,"native callback");Near(s.Effect.Damage,1.3,"30% firearm damage after completion");
 (p,f,s)=Scene("EP10-3");var other=new Weapon();object?[] hide=[f,other,1f];Call(typeof(EarpieceFirearmHolsterPatch),"Prefix",hide);Near((float)hide[2]!,1.75,"holster speed");
 var incoming=new Player.FirearmController{Player=p,Item=other};p.HandsController=incoming;object?[] show=[incoming,1f,(Action)(()=>{})];Call(typeof(EarpieceFirearmSwitchPatch),"Prefix",show);Near((float)show[1]!,1.75,"draw speed");incoming.FirearmsAnimator.Speed=1.75f;((Action)show[2]!)();Near(incoming.FirearmsAnimator.Speed,1,"no persistent firing animation multiplier");
 EarpieceSwitching.Queue(p,other,f.Item,false);p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem=null;Check(EarpieceSwitching.Take(p,f.Item)==null,"unequipping cancels pending bonus");
});
Run("Feedback Loop selects 1 reachable physical copy and discards excess time reduction",()=>{
 var(p,f,s)=Scene("EP06-4");var d=catalog.Variants.First(v=>v.RuntimeKey=="GR26");
 ThrowWeap Grenade()=>new(){StringTemplateId=d.TemplateId};
 var pocket=Grenade();var rig=Grenade();var bag=Grenade();
 p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem=new Item{Children=[pocket]};p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem=new Item{Children=[rig]};p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem=new Item{Children=[bag]};
 void Spend(Item i,string token){Check(ReusableGrenadeRuntime.Charges.Reserve(i.Id,3,token),"reserve");Check(ReusableGrenadeRuntime.Charges.Finish(i.Id,token,true),"spend");}
 Spend(bag,"bag");Time.time+=10;Spend(pocket,"pocket");Time.time+=10;Spend(rig,"rig");EarpieceRuntime.Killed(Victim(p,true));
 Near(ReusableGrenadeRuntime.Charges.SecondsUntil(pocket.Id,3),155,"shortest reachable timer cut by 15");Near(ReusableGrenadeRuntime.Charges.SecondsUntil(rig.Id,3),180,"other carried copy unchanged");Near(ReusableGrenadeRuntime.Charges.SecondsUntil(bag.Id,3),160,"backpack excluded");
 var ledger=new GrenadeChargeLedger(()=>Time.time);ledger.Reserve("a",3,"1");ledger.Finish("a","1",true);ledger.Reserve("a",3,"2");ledger.Finish("a","2",true);Time.time+=175;ledger.ReduceRecharge("a",3,15);Near(ledger.Remaining("a",3),2,"only 1 charge restored");Near(ledger.SecondsUntil("a",3),180,"unused acceleration discarded");
});
Run("Food completion, medical protection and medical speed distinguish canceled food and actual meds",()=>{
 var(p,f,s)=Scene("EP07-5");var food=new Item();food.Components[typeof(FoodDrinkComponent)]=new FoodDrinkComponent();var use=new Use{HealthController=p.ActiveHealthController,MedItem=food};EarpieceFoodUse.Begin(use);use._interrupted=true;EarpieceFoodUse.End(use);Check(!s.Effect.Active,"cancelled food does not trigger");use._interrupted=false;EarpieceFoodUse.Begin(use);EarpieceFoodUse.End(use);Near(EarpieceRuntime.Movement(p),1.2,"20% walking");s.Roll.Earpiece.LegRecovery=1.25f;Near(EarpieceRuntime.Value(p,"legRecovery"),1.875,"50% signature times 25% random");
 (p,f,s)=Scene("EP09-5");var med=new Item();med.Components[typeof(MedKitComponent)]=new MedKitComponent();p.HandsController=new Player.MedsController{Player=p,Item=med};var info=new DamageInfo{DamageType=EDamageType.Bullet,Weapon=new Weapon()};Near(EarpieceRuntime.Incoming(p,info),.7,"firearm protection during meds");info.Weapon=new ThrowWeap();Near(EarpieceRuntime.Incoming(p,info),1,"fragments excluded");
 (p,f,s)=Scene("EP04-4");var useMed=new Item();useMed.Components[typeof(MedKitComponent)]=new MedKitComponent();p.HandsController=new Player.MedsController{Player=p,Item=useMed};Time.time+=3;Near(EarpieceRuntime.MedicalSpeed(p),1.5,"stationary medical use +50%");p.InputDirection=new(1,0);Near(EarpieceRuntime.MedicalSpeed(p),1,"motion ends speed");
});
Run("High Fidelity applies only after continuous ADS and Double Take cannot accumulate charges",()=>{
 var(p,f,s)=Scene("EP10-1");f.IsAiming=true;Tick();Time.time+=2;Near(EarpieceRuntime.Velocity(p),1.2,"20% projectile velocity");Near(EarpieceRuntime.Accuracy(p),.75,"25% dispersion reduction");f.IsAiming=false;Near(EarpieceRuntime.Velocity(p),1,"stopping ADS ends immediately");
 (p,f,s)=Scene("EP10-4");s.Effect.Check(2);Check(s.Effect.Charges==0,"magazine check excluded");s.Effect.Check(1);s.Effect.Check(1);Check(s.Effect.Charges==2,"2 maximum");Near(s.Effect.Fire(),1.25,"1st shot");Near(s.Effect.Fire(),1.25,"2nd shot");Near(s.Effect.Fire(),1,"3rd normal");
 (p,f,s)=Scene("EP10-2");EarpieceRuntime.Killed(Victim(p,false));Near(s.Effect.Reload,1,"melee does not trigger");EarpieceRuntime.Killed(Victim(p,true));Near(s.Effect.Reload,1.5,"firearm kill timed reload bonus");Time.time+=4;EarpieceRuntime.Killed(Victim(p,true));Near(s.Effect.ActiveUntil-Time.time,5,"kill refresh");Time.time+=5;Near(s.Effect.Reload,1,"expires at 5 seconds");
});
Run("Fast Clock and Tin Foil Crown scale only the grenade contusion adapter",()=>{
 foreach(var (key,expected) in new[]{("EP09-2",0f),("EP03-1",.25f)}){
  var(p,f,s)=Scene(key);s.Roll.Earpiece.Contusion=1;
  EarpieceFlashScope.Depth=0;object?[] ordinary=[p.ActiveHealthController,12f];Call(typeof(EarpieceContusionPatch),"Prefix",ordinary);Near((float)ordinary[1]!,12,"non-grenade condition unchanged");
  EarpieceFlashScope.Depth=1;object?[] grenade=[p.ActiveHealthController,12f];var proceed=(bool)Call(typeof(EarpieceContusionPatch),"Prefix",grenade)!;Near((float)grenade[1]!,12*expected,"grenade duration");Check(proceed==(expected>0),"immunity skips effect");EarpieceFlashScope.Depth=0;
 }
});
Run("Clear Reception changes recoil return parameters and restores them after processing",()=>{
 var(p,f,s)=Scene("EP11-3");p.Physical.HandsStamina.Current=80;var process=p.ProceduralWeaponAnimation.Shootingg.CurrentRecoilEffect.HandRotationRecoilEffect;process.ReturnSpeed=2;process._autoFireSpeed=3;process.AutoFireOn=true;process.Intensity=.9f;
 object?[] args=[process,null];Call(typeof(EarpieceRecoilRecoveryPatch),"Prefix",args);Near(process.ReturnSpeed,2.8,"return speed +40%");Near(process._autoFireSpeed,4.2,"cached automatic return speed");Near(process.Intensity,.9,"kick strength unchanged");Call(typeof(EarpieceRecoilRecoveryPatch),"Finalizer",process,args[1],null);Near(process.ReturnSpeed,2,"return restored");Near(process._autoFireSpeed,3,"cached value restored");
 p.Physical.HandsStamina.Current=79.9f;args=[process,null];Call(typeof(EarpieceRecoilRecoveryPatch),"Prefix",args);Near(process.ReturnSpeed,2,"below 80% excluded");Call(typeof(EarpieceRecoilRecoveryPatch),"Finalizer",process,args[1],null);
});
Run("Matamata Turtle preserves both gun queue channels locally without changing native caches",()=>{
 var(p,f,s)=Scene("EP06-1");var source=new SuperSource();var old=EarpieceAudio.Begin(p,"gun");EarpieceAudio.RegisterQueue(new(){AudioSources=[source]});EarpieceAudio.Current=old;
 AudioUpdate(source);Near(source.Output,.7*.8*.9*1f,"1st native channel at native volume");Near(source.Output2,.3*.8*.9*1f,"2nd native channel at native volume");Near(source.PreOcclusionVolume,.7,"native 1st cache restored");Near(source.SecondCache,.3,"native 2nd cache restored");
 var enemy=new Player();Near(EarpieceAudio.Volume(enemy,"gun"),1,"enemy gunfire also at native volume");Near(EarpieceRuntime.EmittedStep(p),1,"AI emitted-noise path unchanged");
 p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem=null;AudioUpdate(source);Near(source.Output,.7*.8*.9,"unequip restores original audio");
});
Run("Inside Voice, Deep Listening and Mute Point keep local and emitted noise separate",()=>{
 var(p,f,s)=Scene("EP01-2");s.Roll.Earpiece.SelfNoise=.75f;Near(EarpieceAudio.Volume(p,"step"),.375,"50% signature plus random local step reduction");Near(EarpieceAudio.Volume(p,"item"),.375,"local equipment reduction");Near(EarpieceRuntime.EmittedStep(p),1,"AI unchanged");Near(EarpieceAudio.Volume(new Player(),"step"),1,"others unchanged");
 (p,f,s)=Scene("EP11-2");p.MovementContext.IsInPronePose=true;Near(EarpieceAudio.Volume(new Player(),"step"),1.75,"others footsteps louder");Near(EarpieceAudio.Volume(p,"gun"),.5,"own gunfire halved");Near(EarpieceAudio.Volume(new Player(),"gun"),.5,"other gunfire halved");p.MovementContext.IsInPronePose=false;Near(EarpieceAudio.Volume(new Player(),"step"),1,"standing restores");
 (p,f,s)=Scene("EP08-2");p.PoseLevel=.2f;p.InputDirection=new(0,1);Near(EarpieceRuntime.EmittedStep(p),.6,"40% emitted crouch step reduction");object?[] sound=[p,AISoundType.step,10f];Call(typeof(EarpieceEmittedStepPatch),"Prefix",sound);Near((float)sound[2]!,6,"same AI sound power reduction");
});
Run("Expanded source ranges do not compound during repeated occlusion updates",()=>{
 var(p,f,s)=Scene("EP05-3");s.Roll.Earpiece.Hearing=1.15f;s.Effect.Killed(false,1);var enemy=new Player();var source=new SuperSource();EarpieceAudio.Register(source,enemy,"step");
 var distance=100f;EarpieceAudio.Rolloff(source,ref distance);source.SetRolloff(distance);Near(source.MaxDistance,172.5,"effective range expansion");
 for(int i=0;i<100;i++){distance=source.MaxDistance;EarpieceAudio.Rolloff(source,ref distance);source.SetRolloff(distance);Near(source.MaxDistance,172.5,"no repeated multiplication");}
 p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem=null;distance=source.MaxDistance;EarpieceAudio.Rolloff(source,ref distance);source.SetRolloff(distance);Near(source.MaxDistance,100,"unequip range restoration");
});
Run("Ambient profile filtering clones the native template and reset suppresses reconstruction",()=>{
 var(p,f,s)=Scene("EP01-1");s.Roll.Earpiece.Ambient=1;Time.time+=1;var template=new HeadphonesTemplate{AmbientVolume=-10};object?[] args=[template];Call(typeof(EarpieceAmbientPatch),"Prefix",args);var modified=(HeadphonesTemplate)args[0]!;Check(!ReferenceEquals(template,modified),"native template cloned");Near(template.AmbientVolume,-10,"base profile untouched");Near(modified.AmbientVolume,-10+20*Math.Log10(.25),"volume percentage converted to dB");
 EarpieceRuntime.Suspend();Check(EarpieceRuntime.Listener==null,"suspended throughout shared reset");EarpieceRuntime.Reset();Check(EarpieceRuntime.States.Count==0,"reset cannot reconstruct old worn states");
});
Run("Distant stereo sources preserve their independent channel cache",()=>{
 var(p,f,s)=Scene("EP06-1");var source=new SuperSourceDistant();EarpieceAudio.Register(source,p,"gun");source.UpdateSourceVolume();Near(source.Output,.7*.8*.9*1f,"distant 1st channel");Near(source.Output2,.2*.8*.9*1f,"distant independent 2nd channel");
 Near(source.PreOcclusionVolume,.7,"1st cache restored");Near(source.SecondCache,.2,"independent channel restored");
});
Run("Equipment audio expands its early cutoff while the final range scales exactly once",()=>{
 var(p,f,s)=Scene("EP05-3");s.Roll.Earpiece.Hearing=1.15f;s.Effect.Killed(false,1);var owner=new Player();var sound=new BaseSoundPlayer{playersBridge=new(){iPlayer=owner}};
 object?[] args=[sound,20,null];Call(typeof(EarpieceItemAudioScopePatch),"Prefix",args);try{
  Check((int)args[1]!>=34.5,"integer early cutoff includes extended range");var source=new SuperSource();var distance=(float)(int)args[1]!;EarpieceAudio.Rolloff(source,ref distance);Near(distance,34.5,"precise native range, not doubled");
 }finally{Call(typeof(EarpieceItemAudioScopePatch),"Finalizer",args[2],null);}
 Check(EarpieceAudio.Current.Owner==null,"audio owner scope restored");
});
Run("Pain and tremor filtering scales only the injury vector",()=>{
 var(p,f,s)=Scene("EP09-3");var breath=new BreathEffector{Physical=p.Physical,TremorOn=true};var input=new Vector3(4,6,8);var output=EarpieceSwayPatch.Injury(input,breath);Near(output.x,2,"injury x halved");Near(output.y,3,"injury y halved");Near(output.z,4,"injury z halved");Check(breath.TremorOn,"native injury remains active");
 p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem=null;output=EarpieceSwayPatch.Injury(input,breath);Near(output.y,6,"unequip restores native sway");
});
Run("Medical animation speed refreshes on movement and earpiece removal",()=>{
 var(p,f,s)=Scene("EP04-4");var med=new Item();med.Components[typeof(MedKitComponent)]=new MedKitComponent();p.HandsController=new Player.MedsController{Player=p,Item=med};Time.time+=3;p.HandsAnimator.SetUseTimeMultiplier(1.2f);Near(p.HandsAnimator.UseTimeMultiplier,1.8,"native input times stationary bonus");
 p.InputDirection=new(1,0);EarpieceMedicalAnimation.Maintain();Near(p.HandsAnimator.UseTimeMultiplier,1.2,"moving ends animation bonus");
 p.InputDirection=new();p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem=null;EarpieceMedicalAnimation.Maintain();Near(p.HandsAnimator.UseTimeMultiplier,1.2,"removal restores native input");
});
Run("Ordinary firearm caches restore after the last equipped legendary is removed",()=>{
 var(p,f,s)=Scene("EP10-1");f.Item.NativeAccuracy=.75f;f.Item.NativeRecoil=-.25f;WeaponCombatState.Refresh(f);Near(f.AccuracyCache,.75,"equipped accuracy cached");
 p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem=null;f.Item.NativeAccuracy=1f;f.Item.NativeRecoil=0;WeaponCombatState.Refresh(f);Near(f.AccuracyCache,1,"normal accuracy restored without another legendary item");Near(f.DispersionCache,1,"normal dispersion restored");
});
Run("Earpiece HUD visibility has its own setting and does not follow gun visibility",()=>{
 Check(StatusVisibility.Allows("earpiece:EP10-2",false,false,false,false,false,true),"earpiece visible with all other categories hidden");Check(!StatusVisibility.Allows("earpiece:EP10-2",true,true,true,true,true,false),"earpiece can be hidden independently");
});
Run("Ambient changes refresh the actual MonoBehaviour audio singleton and restore on unequip",()=>{
 var(p,f,s)=Scene("EP01-1");s.Roll.Earpiece.Ambient=1;Time.time+=1;EarpieceAudio.Maintain();
 Check(MonoBehaviourSingleton<BetterAudio>.Instance.Refreshes==1,"native audio controller refreshed despite empty Comfort singleton");
 for(int i=0;i<20;i++)EarpieceAudio.Maintain();Check(MonoBehaviourSingleton<BetterAudio>.Instance.Refreshes==1,"no redundant fades");
 p.InputDirection=new(0,1);EarpieceAudio.Maintain();Check(MonoBehaviourSingleton<BetterAudio>.Instance.Refreshes==2,"movement restores ambient");
 p.InputDirection=new();Time.time+=1;EarpieceAudio.Maintain();p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem=null;EarpieceAudio.Maintain();
 Check(MonoBehaviourSingleton<BetterAudio>.Instance.Refreshes==4,"unequip restores ambient");
});
Run("Ambient refresh is retried when the audio controller becomes available",()=>{
 var(p,f,s)=Scene("EP01-1");s.Roll.Earpiece.Ambient=1;Time.time+=1;MonoBehaviourSingleton<BetterAudio>.Instance=null!;EarpieceAudio.Maintain();
 MonoBehaviourSingleton<BetterAudio>.Instance=new();EarpieceAudio.Maintain();Check(MonoBehaviourSingleton<BetterAudio>.Instance.Refreshes==1,"unavailable controller did not acknowledge unapplied change");
});
Run("Matamata Turtle reaches deferred stereo playback with no manual registration or patch calls",()=>{
 var(p,f,s)=Scene("EP06-1");var sound=new WeaponSoundPlayer{playersBridge=new(){iPlayer=p},DeferSources=true};sound.FireBullet();
 Check(EarpieceAudio.Current.Owner==null,"firing scope has ended");var source=new SuperSource();sound._queue!.AudioSources=[source];sound._queue.Update();
 Near(source.Output,.7*.8*.9*1f,"deferred own 1st channel");Near(source.Output2,.3*.8*.9*1f,"deferred own 2nd channel");Near(source.PreOcclusionVolume,.7,"raw 1st cache retained");Near(source.SecondCache,.3,"raw 2nd cache retained");
 Check(EarpieceAudio.Current.Owner==null,"scheduled scope restored");sound.StopFiringLoop();sound._queue.Update();Near(source.Output,.7*.8*.9*1f,"tail remains native");
 var other=new WeaponSoundPlayer{playersBridge=new(){iPlayer=new Player()}};other.FireBullet();other._queue!.Update();Near(other._queue.AudioSources[0].Output,.7*.8*.9*1,"enemy gunfire also at native volume");
});
Run("Native channel interpolation applies a fresh factor without changing cached volumes",()=>{
 var(p,f,s)=Scene("EP06-1");var sound=new WeaponSoundPlayer{playersBridge=new(){iPlayer=p}};sound.FireBullet();sound._queue!.Update();var source=(SuperSource)sound._queue.AudioSources[0];
 for(int i=0;i<50;i++)source.UpdateSourceVolume(.25f);Near(source.Output,.7*.8*.9*1f,"no compounding during fade/occlusion updates");
 p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem=null;for(int i=0;i<50;i++)source.UpdateSourceVolume(.25f);
 Near(source.Output,.7*.8*.9,"loop returns to native volume");Near(source.Output2,.3*.8*.9,"both channels restored");Near(source.PreOcclusionVolume,.7,"cache never rewritten");
});
Run("First actor-owned source update extends hearing before the next maintenance tick",()=>{
 var(p,f,s)=Scene("EP11-1");s.Roll.Earpiece.Hearing=1;var enemy=new Player();var source=new SimpleSource();enemy.NestedStepSoundSource=source;enemy.SetRolloff(source,40);
 Near(source.MaxDistance,50,"1st enemy step range extends by 25%");Check(EarpieceAudio.Current.Owner==null,"actor scope restored");
 var own=new SimpleSource();p.NestedStepSoundSource=own;p.SetRolloff(own,40);Near(own.MaxDistance,40,"own range unaffected");
});
Run("Primary and secondary steps share 1 hearing expansion, including the early sampling gate",()=>{
 var(p,f,s)=Scene("EP11-1");s.Roll.Earpiece.Hearing=1;var enemy=new Player{NestedStepSoundSource=new SuperSource(),SecondaryStep=new SuperSource()};
 enemy.CheckSurface(40);Near(enemy.SurfaceRange,50,"surface sampling cutoff includes hearing range");enemy.PlayStepSound();
 Near(enemy.NestedStepSoundSource.MaxDistance,50,"primary 25% range");Near(enemy.SecondaryStep.MaxDistance,50,"secondary range not multiplied twice");
});
Run("Native clip distance selection follows hearing while own audio stays unchanged",()=>{
 var(p,f,s)=Scene("EP11-1");s.Roll.Earpiece.Hearing=1;var bank=new SoundBank();var enemy=new Player();var old=EarpieceAudio.Begin(enemy,"step");
 try{Near(bank.PickClipsByDistance(50),40,"clip-selection distance includes 25% farther hearing");}finally{EarpieceAudio.Current=old;}
 old=EarpieceAudio.Begin(enemy,"gun");try{Near(bank.PickClipsByDistance(50),50,"6th Sense does not expand gunfire");}finally{EarpieceAudio.Current=old;}
 old=EarpieceAudio.Begin(p,"step");try{Near(bank.PickClipsByDistance(50),50,"own audio unaffected");}finally{EarpieceAudio.Current=old;}
});
Run("Deep Listening updates actively playing enemy footsteps and gunfire when going prone",()=>{
 var(p,f,s)=Scene("EP11-2");var enemy=new Player();var step=new SuperSource();enemy.NestedStepSoundSource=step;enemy.PlayStepSound();
 var gun=new WeaponSoundPlayer{playersBridge=new(){iPlayer=enemy}};gun.FireBullet();gun._queue!.Update();p.MovementContext.IsInPronePose=true;step.UpdateSourceVolume();gun._queue.AudioSources[0].UpdateSourceVolume();
 Near(step.Output,.7*.8*.9*1.75,"other footsteps louder");Near(gun._queue.AudioSources[0].Output,.7*.8*.9*.5,"other gunfire quieter");
 p.MovementContext.IsInPronePose=false;step.UpdateSourceVolume();gun._queue.AudioSources[0].UpdateSourceVolume();Near(step.Output,.7*.8*.9,"standing restores step");Near(gun._queue.AudioSources[0].Output,.7*.8*.9,"standing restores gun");
});
Run("Pooled gun sources stay at native volume for Matamata Turtle and restore after the listener unequips",()=>{
 var(p,f,s)=Scene("EP06-1");var sound=new WeaponSoundPlayer{playersBridge=new(){iPlayer=p}};sound.FireBullet();sound._queue!.Update();var source=sound._queue.AudioSources[0];source.Release();
 var enemy=new WeaponSoundPlayer{playersBridge=new(){iPlayer=new Player()},_queue=new(){AudioSources=[source]}};enemy.FireBullet();enemy._queue.Update();Near(source.Output,.7*.8*.9*1,"other-character pooled source is also at native volume");p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem=null;EarpieceAudio.Maintain();Near(source.Output,.7*.8*.9,"pool restores normal output after unequip");
});
Run("Only the 9 unconditional earpiece signatures stay off the HUD",()=>{
 var permanent=new[]{"EP01-2","EP02-2","EP03-1","EP06-1","EP07-2","EP09-2","EP09-3","EP10-3","EP11-1"};
 foreach(var key in permanent){var(p,f,state)=Scene(key);p.PoseLevel=.2f;p.MovementContext.IsInPronePose=true;f.IsAiming=true;Tick();Time.time+=10;Tick();Check(!EarpieceRuntime.Statuses(p).Any(),"only unconditional modifier hidden: "+key);}
});
foreach(var key in new[]{"EP01-1","EP02-1","EP02-5","EP04-2","EP04-3","EP04-4","EP05-4","EP08-1","EP08-2","EP08-3","EP09-5","EP10-1","EP11-2","EP11-3"})
Run("Conditional headset HUD follows its actual gate and ends immediately: "+key,()=>{
 var(p,f,state)=Scene(key);p.PoseLevel=1;Singleton<GameWorld>.Instance.LocationId="bigmap";Singleton<GameWorld>.Instance.GameDateTime.Time=new(2026,11,1,12,0,0);p.Physical.HandsStamina.Current=79.99f;
 Check(!EarpieceRuntime.Statuses(p).Any(),"inactive gate hidden");
 switch(key){
 case "EP01-1":Time.time+=1;break;
 case "EP02-1":Singleton<GameWorld>.Instance.LocationId="woods";break;
 case "EP02-5":p.MovementContext.IsInPronePose=true;Tick();Time.time+=3;Tick();break;
 case "EP04-2":p.InputDirection=new(1,0);break;
 case "EP04-3":f.IsAiming=true;break;
 case "EP04-4":Time.time+=3;break;
 case "EP05-4":p.HandsController=new Player.BaseKnifeController{Player=p,Item=new Knife()};break;
 case "EP08-1":p.PoseLevel=.2f;break;
 case "EP08-2":p.PoseLevel=.2f;p.InputDirection=new(0,1);break;
 case "EP08-3":Singleton<GameWorld>.Instance.GameDateTime.Time=new(2026,11,1,20,0,0);break;
 case "EP09-5":var med=new Item();med.Components[typeof(MedKitComponent)]=new MedKitComponent();p.HandsController=new Player.MedsController{Player=p,Item=med};break;
 case "EP10-1":f.IsAiming=true;Tick();Time.time+=2;Tick();break;
 case "EP11-2":p.MovementContext.IsInPronePose=true;break;
 case "EP11-3":p.Physical.HandsStamina.Current=80;break;
 }
 var row=EarpieceRuntime.Statuses(p).Single();Check(row.Active&&row.Ready==0,"active untimed condition has 1 active row");Check(!row.Label.Contains("0s"),"no fictional zero-second timer");
 p.PoseLevel=1;p.MovementContext.IsInPronePose=false;f.IsAiming=false;p.HandsController=f;p.InputDirection=new(0,1);p.Physical.HandsStamina.Current=79.99f;Singleton<GameWorld>.Instance.LocationId="bigmap";Singleton<GameWorld>.Instance.GameDateTime.Time=new(2026,11,1,12,0,0);
 Check(!EarpieceRuntime.Statuses(p).Any(),"ending condition removes row without changing gameplay");
 p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem=null;Check(!EarpieceRuntime.Statuses(p).Any(),"unequip removes condition");
});
Run("Cold Open shows the 1st ready cartridge before firing without spending a charge",()=>{
 var(p,f,state)=Scene("EP01-5");Check(!EarpieceRuntime.Statuses(p).Any(),"not armed at equip");Time.time+=10;
 for(int i=0;i<100;i++){var row=EarpieceRuntime.Statuses(p).Single();Check(row.Active&&row.Detail=="3 shots ready","initial 3-shot readiness visible");Check(state.Effect.Charges==0,"HUD does not mutate charge state");}
 var shot=EarpieceRuntime.Begin(p,f.Item);EarpieceRuntime.End(shot);Check(EarpieceRuntime.Statuses(p).Single().Detail=="2 shots ready","only real cartridge consumes charge");Time.time+=10;Check(EarpieceRuntime.Statuses(p).Single().Detail=="3 shots ready","another full pause previews replenished package without mutating it");
});
Run("Instant walk recovery reports its trigger briefly without inventing a timed buff",()=>{
 var(p,f,state)=Scene("EP02-3");p.InputDirection=new(0,1);Tick();for(int i=0;i<20;i++){p.Position=new Vector3(p.Position.x+1,p.Position.y,p.Position.z);Time.time+=1;Tick();}
 var row=EarpieceRuntime.Statuses(p).Single();Check(row.Active&&row.Seconds==0&&row.Detail.StartsWith("Triggered"),"instant restoration uses explicit trigger receipt");Time.time+=2;Check(!EarpieceRuntime.Statuses(p).Any(),"receipt expires without repeating trigger");
});
Run("All 39 headset descriptions retain exactly 1 signature through panel refresh",()=>{
 foreach(var d in ears.Values){var item=New(d.RuntimeKey);var fallback="<color=#66D9EF>"+d.EffectText+"</color>";var text=ItemDescriptionPresentation.Compose(item,fallback+"\n\nUseful native description.");
 Check(text.Split(d.EffectText).Length-1==1,"signature once for "+d.RuntimeKey);Check(text.EndsWith("Useful native description."),"distinct native text retained");Check(text==ItemDescriptionPresentation.Compose(item,text),"panel refresh idempotent");Check(RarityResolver.Resolve(item)!.AffixDescriptions.Count==5,"signature plus 4 rolls retained");}
});
Run("Native scheduled gun output refresh survives a bypassed/inlined lower-level updater",()=>{
 var(p,f,s)=Scene("EP11-2");p.MovementContext.IsInPronePose=true;var gun=new WeaponSoundPlayer{playersBridge=new(){iPlayer=p}};gun.FireBullet();gun._queue!.SimulateInlinedVolumeUpdate=true;gun._queue.Update();
 var source=(SuperSource)gun._queue.AudioSources[0];Near(source.Output,.7*.8*.9*.5,"first channel reapplied at scheduled playback");Near(source.Output2,.3*.8*.9*.5,"second channel reapplied");
 Near(source.PreOcclusionVolume,.7,"raw first cache unchanged");Near(source.SecondCache,.3,"raw second cache unchanged");
});
Run("Matamata Turtle preserves own and other-character body, automatic and tail playback",()=>{
 var(p,f,s)=Scene("EP06-1");Check(ears["EP06-1"].EffectText.Contains("20%")&&ears["EP06-1"].EffectText.Contains("100%"),"live catalog matches approved amount");
 var gun=new WeaponSoundPlayer{playersBridge=new(){iPlayer=p}};gun.FireBullet();gun._queue!.SimulateInlinedVolumeUpdate=true;
 for(int cartridge=0;cartridge<25;cartridge++){gun._queue.Update();foreach(var channel in gun._queue.AudioSources.Cast<SuperSource>()){Near(channel.Output,.7*.8*.9*1f,"native scheduled body/automatic clip");Near(channel.Output2,.3*.8*.9*1f,"both stereo channels");}}
 gun.StopFiringLoop();gun._queue.Update();Near(gun._queue.AudioSources[0].Output,.7*.8*.9*1f,"tail also at native volume");
 var enemy=new WeaponSoundPlayer{playersBridge=new(){iPlayer=new Player()}};enemy.FireBullet();enemy._queue!.SimulateInlinedVolumeUpdate=true;enemy._queue.Update();Near(enemy._queue.AudioSources[0].Output,.7*.8*.9*1,"enemy gunfire also at native volume");
 p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem=null;EarpieceAudio.Maintain();Near(gun._queue.AudioSources[0].Output,.7*.8*.9,"active source restored after unequip");Check(!EarpieceRuntime.Statuses(p).Any(),"no permanent-effect row");
});
Run("I Knocked HUD follows successful breach, active duration, cooldown and unequip",()=>{
 var(p,f,s)=Scene("EP07-1");var door=new EFT.Interactive.Door();object?[] before=[door,null];Call(typeof(EarpieceBreachPatch),"Prefix",before);
 Call(typeof(EarpieceBreachPatch),"Postfix",door,new EFT.Interactive.InteractionResult{InteractionType=EFT.Interactive.EInteractionType.Breach},p,before[1]);Check(!EarpieceRuntime.Statuses(p).Any(),"failed breach has no row");
 door.IsBroken=true;Call(typeof(EarpieceBreachPatch),"Postfix",door,new EFT.Interactive.InteractionResult{InteractionType=EFT.Interactive.EInteractionType.Breach},p,before[1]);
 var row=EarpieceRuntime.Statuses(p).Single();Check(row.Active,"successful breach activates row");Near(row.Seconds,10,"active timer");Near(row.Ready,20,"cooldown timer");
 p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem=null;row=EarpieceRuntime.Statuses(p).Single();Check(!row.Active,"unequip ends active effect");Near(row.Ready,20,"cooldown persists while unworn");
 Time.time+=20;Check(!EarpieceRuntime.Statuses(p).Any(),"expired cooldown leaves no row");
});
Run("All triggered headset signatures expose activations, shot charges or cooldowns",()=>{
 var keys=new[]{"EP01-4","EP01-5","EP03-2","EP03-3","EP04-1","EP04-5","EP05-3","EP06-2","EP06-3","EP06-4","EP06-5","EP07-1","EP07-5","EP10-2","EP10-4"};
 foreach(var key in keys){var(p,f,s)=Scene(key);Check(!EarpieceRuntime.Statuses(p).Any(),"no untriggered row for "+key);
  switch(key){
   case "EP01-4":s.Effect.Check(2);break;
   case "EP01-5":Time.time+=10;var shot=EarpieceRuntime.Begin(p,f.Item);EarpieceRuntime.End(shot);break;
   case "EP03-2":Tick();p.Physical.HandsStamina.Current=0;Time.time+=.1f;Tick();break;
   case "EP03-3":case "EP04-5":p.ActiveHealthController.Health=132;EarpieceRuntime.Damaged(p,new(){DamageType=EDamageType.Bullet,Weapon=new Weapon(),Player=new(){iPlayer=new Player()}});break;
   case "EP04-1":p.InputDirection=new(0,1);p.MovementContext.IsSprintEnabled=true;Tick();p.MovementContext.IsSprintEnabled=false;Time.time+=.1f;Tick();break;
   case "EP05-3":case "EP06-4":case "EP10-2":EarpieceRuntime.Killed(Victim(p,true));break;
   case "EP06-2":s.Effect.Switched(true,true);break;
   case "EP06-3":for(int i=0;i<5;i++){s.Effect.Fire();Time.time+=.1f;}break;
   case "EP06-5":Call(typeof(EarpieceMalfunctionRepairedPatch),"Postfix",new Player.FirearmController.RepairMalfunction{Weapon=f.Item});break;
   case "EP07-1":s.Effect.Breached();break;
   case "EP07-5":var food=new Item();food.Components[typeof(FoodDrinkComponent)]=new FoodDrinkComponent();var use=new Use{HealthController=p.ActiveHealthController,MedItem=food};EarpieceFoodUse.Begin(use);EarpieceFoodUse.End(use);break;
   case "EP10-4":s.Effect.Check(1);break;
  }
  var rows=EarpieceRuntime.Statuses(p).ToArray();Check(rows.Length==1&&rows[0].Visible,"exactly 1 triggered row for "+key);Check(rows[0].Active||rows[0].Ready>0,"temporary activation/charges/cooldown only");
 }
});
Run("Every random bonus reaches its worn audio, stamina or aiming/draw adapter and restores",()=>{
 var(p,f,s)=Scene("EP02-1");Singleton<GameWorld>.Instance.LocationId="bigmap";var b=s.Roll.Earpiece;b.Hearing=1.15f;b.Ambient=.75f;b.SelfNoise=.75f;b.Contusion=.7f;b.AimCost=.8f;b.Draw=1.2f;b.LegRecovery=1.25f;b.Aim=1.2f;
 var enemy=new Player();var foot=new SimpleSource();enemy.NestedStepSoundSource=foot;enemy.SetRolloff(foot,40);Near(foot.MaxDistance,46,"Far Signal actual range target");Near(EarpieceAudio.Ambient,.75,"Noise Gate ambient");
 var own=new SuperSource();p.NestedStepSoundSource=own;p.PlayStepSound();Near(own.Output,.7*.8*.9*.75,"Self Muffle actual own source");
 EarpieceFlashScope.Depth=1;object?[] contusion=[p.ActiveHealthController,10f];Call(typeof(EarpieceContusionPatch),"Prefix",contusion);Near((float)contusion[1]!,7,"Clear Head actual grenade duration");EarpieceFlashScope.Depth=0;
 Near(EarpieceRuntime.Value(p,"aimCost"),.8,"Steady Aim consumption factor");Near(EarpieceRuntime.Value(p,"legRecovery"),1.25,"2nd Wind regeneration factor");
 object?[] draw=[f,1f];Call(typeof(EarpieceDrawPatch),"Postfix",draw);Near((float)draw[1]!,1.2,"Fast Hands native draw result");
 p.ProceduralWeaponAnimation._aimingSpeed=2;Call(typeof(EarpieceAimPatch),"Postfix",p.ProceduralWeaponAnimation);Near(p.ProceduralWeaponAnimation._aimingSpeed,2.4,"Quick Focus native aiming speed");
 p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem=null;EarpieceAudio.Maintain();Near(foot.MaxDistance,40,"range restored");Near(own.Output,.7*.8*.9,"own noise restored");Near(EarpieceAudio.Ambient,1,"ambient restored");Near(EarpieceRuntime.Value(p,"aimCost"),1,"aim cost restored");Near(EarpieceRuntime.Value(p,"legRecovery"),1,"regeneration restored");
 draw=[f,1f];Call(typeof(EarpieceDrawPatch),"Postfix",draw);Near((float)draw[1]!,1,"draw restored");p.ProceduralWeaponAnimation._aimingSpeed=2;Call(typeof(EarpieceAimPatch),"Postfix",p.ProceduralWeaponAnimation);Near(p.ProceduralWeaponAnimation._aimingSpeed,2,"aiming restored");
});
Run("High Fidelity composes native velocity and center-of-impact deltas after the ADS threshold",()=>{
 var(p,f,s)=Scene("EP10-1");f.IsAiming=true;Tick();Time.time+=1.99f;object?[] velocity=[f.Item,.1f];Call(typeof(EarpieceVelocityPatch),"Postfix",velocity);Near((float)velocity[1]!,.1,"no early velocity bonus");
 Time.time+=.01f;velocity=[f.Item,.1f];Call(typeof(EarpieceVelocityPatch),"Postfix",velocity);Near((float)velocity[1]!,.32,"existing 10% velocity times 20% signature");
 object?[] dispersion=[f.Item,.25f];Call(typeof(EarpieceAccuracyPatch),"Postfix",dispersion);Near((float)dispersion[1]!,-.0625,"existing spread delta times 25% reduction");
 f.IsAiming=false;dispersion=[f.Item,.25f];Call(typeof(EarpieceAccuracyPatch),"Postfix",dispersion);Near((float)dispersion[1]!,.25,"leaving ADS restores native delta");
});
Run("Rain Check reaches the production weather factor and composes with the backpack filter",()=>{
 var(p,f,s)=Scene("EP02-2");Near(BackpackAudio.WeatherFactor,.4,"rain and wind target reduced by 60%");BackpackRuntime.WeatherBag=true;Near(BackpackAudio.WeatherFactor,.1,"independent 75% backpack reduction multiplies");
 p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem=null;Near(BackpackAudio.WeatherFactor,.25,"removing headset retains only backpack");BackpackRuntime.WeatherBag=false;Near(BackpackAudio.WeatherFactor,1,"ordinary weather restored");Check(!EarpieceRuntime.Statuses(p).Any(),"weather passive has no HUD row");
});
Run("Matamata Turtle hearing doubles other-character step, gear and voice range and stacks with Far Signal",()=>{
 var(p,f,s)=Scene("EP06-1");s.Roll.Earpiece.Hearing=1.15f;s.Roll.Earpiece.SelfNoise=1;var enemy=new Player();
 Near(EarpieceAudio.Hearing(enemy,"step"),2.3,"100% signature times 15% roll");Near(EarpieceAudio.Hearing(enemy,"voice"),2.3,"voice range");Near(EarpieceAudio.Hearing(enemy,"item"),2.3,"equipment range");Near(EarpieceAudio.Hearing(p,"step"),1,"own range unchanged");
 var step=new SimpleSource();enemy.NestedStepSoundSource=step;enemy.SetRolloff(step,40);Near(step.MaxDistance,92,"native step range target");
 for(int i=0;i<100;i++){step.SetRolloff(step.MaxDistance);Near(step.MaxDistance,92,"large hearing multiplier does not compound");}
 var voice=new SimpleSource();enemy.SpeechSource=voice;enemy.SetRolloff(voice,50);Near(voice.MaxDistance,115,"native voice range target");
 var gear=new SimpleSource();enemy._gearSource=gear;enemy.SetRolloff(gear,20);Near(gear.MaxDistance,46,"native gear range target");
 step.Play();Near(step.Output,.7*.8*.9,"movement source remains audible");
 p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem=null;EarpieceAudio.Maintain();Near(step.MaxDistance,40,"step range restored");Near(voice.MaxDistance,50,"voice range restored");Near(gear.MaxDistance,20,"gear range restored");
});
Run("Matamata Turtle leaves shared reload and explosion mixer controls at native levels",()=>{
 var(p,f,s)=Scene("EP06-1");s.Roll.Earpiece.Ambient=1;s.Roll.Earpiece.SelfNoise=1;var audio=MonoBehaviourSingleton<BetterAudio>.Instance;var template=audio.CurrentHeadphonesTemplate;
 audio.ApplyCurrentHeadphonesTemplate();EarpieceAudio.Maintain();var levels=audio.Controller.Values;
 Near(levels["GunsVolume"],template.DryVolume,"shared weapon dry remains native");Near(levels["GunsCompressorSendLevel"],template.GunsCompressorSendLevel,"shared compressor send remains native");
 Near(levels["EnvironmentVolume"],template.DryVolume,"environment remains native");Near(levels["AmbientVolume"],template.AmbientVolume,"ambient remains native");
 // The reported routes share the weapon bus; no ownership assumption can make
 // a mixer-wide mute safe for unrelated clips on that bus.
 var sharedGain=Math.Pow(10,levels["GunsVolume"]/20);var nativeGain=Math.Pow(10,template.DryVolume/20);
 Near(.8*sharedGain,.8*nativeGain,"reload on shared dry bus remains audible");Near(.9*sharedGain,.9*nativeGain,"explosion on shared dry bus remains audible");
 Check(audio.Controller.Fades["GunsVolume"],"native fade policy preserved");Check(audio.Refreshes==1,"gun signature requires no extra profile refresh");Check(!EarpieceRuntime.Statuses(p).Any(),"no passive HUD row");
});
Run("Native headphone refresh changes shared levels normally while gunshots stay at native volume",()=>{
 var(p,f,s)=Scene("EP06-1");s.Roll.Earpiece.Ambient=1;var audio=MonoBehaviourSingleton<BetterAudio>.Instance;var template=audio.CurrentHeadphonesTemplate;
 var gun=new WeaponSoundPlayer{playersBridge=new(){iPlayer=p}};gun.FireBullet();gun._queue!.Update();Near(gun._queue.AudioSources[0].Output,.7*.8*.9*1,"native gun target before refresh");
 template.DryVolume=-15;template.GunsCompressorSendLevel=-4;audio.ApplyCurrentHeadphonesTemplate();EarpieceAudio.Maintain();
 Near(audio.Controller.Values["GunsVolume"],-15,"native shared dry update");Near(audio.Controller.Values["GunsCompressorSendLevel"],-4,"native shared compressed update");
 gun._queue.Update();Near(gun._queue.AudioSources[0].Output,.7*.8*.9*1,"native gun target after refresh");Near(template.DryVolume,-15,"shared template not overwritten");
});
Run("Removing Matamata Turtle restores source output without replacing current native settings",()=>{
 var(p,f,s)=Scene("EP06-1");s.Roll.Earpiece.Ambient=1;var audio=MonoBehaviourSingleton<BetterAudio>.Instance;var gun=new WeaponSoundPlayer{playersBridge=new(){iPlayer=p}};gun.FireBullet();gun._queue!.Update();EarpieceAudio.Maintain();Near(gun._queue.AudioSources[0].Output,.7*.8*.9*1,"gun source at native volume");
 audio.CurrentHeadphonesTemplate=new(){DryVolume=-12,GunsCompressorSendLevel=-6};audio.ApplyCurrentHeadphonesTemplate();p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem=null;EarpieceAudio.Maintain();
 Near(audio.Controller.Values["GunsVolume"],-12,"native dry profile retained");Near(audio.Controller.Values["GunsCompressorSendLevel"],-6,"native compressor send retained");Near(gun._queue.AudioSources[0].Output,.7*.8*.9,"existing source restored");
});
Run("Matamata Turtle does not require or modify mixer initialization during equip or reset",()=>{
 var(p,f,s)=Scene("EP06-1");s.Roll.Earpiece.Ambient=1;MonoBehaviourSingleton<BetterAudio>.Instance=null!;EarpieceAudio.Maintain();
 var gun=new WeaponSoundPlayer{playersBridge=new(){iPlayer=p}};gun.FireBullet();gun._queue!.Update();Near(gun._queue.AudioSources[0].Output,.7*.8*.9*1,"gun playback independent of headphone refresh");
 var audio=MonoBehaviourSingleton<BetterAudio>.Instance=new();audio.Controller.Configure(audio.CurrentHeadphonesTemplate);EarpieceAudio.Maintain();
 Check(audio.Refreshes==0,"gun playback does not request a broad mixer change");Near(audio.Controller.Values["GunsVolume"],audio.CurrentHeadphonesTemplate.DryVolume,"late native dry");
 EarpieceRuntime.Reset();Singleton<GameWorld>.Instance=null!;Near(audio.Controller.Values["GunsCompressorSendLevel"],audio.CurrentHeadphonesTemplate.GunsCompressorSendLevel,"reset leaves native send");Near(gun._queue.AudioSources[0].Output,.7*.8*.9,"reset restores gun source");
});
Run("Switching from Matamata Turtle preserves another headset's conditional gun volume",()=>{
 var(p,f,s)=Scene("EP06-1");s.Roll.Earpiece.Ambient=1;var audio=MonoBehaviourSingleton<BetterAudio>.Instance;audio.ApplyCurrentHeadphonesTemplate();var gun=new WeaponSoundPlayer{playersBridge=new(){iPlayer=p}};gun.FireBullet();gun._queue!.Update();
 p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem=New("EP11-2");p.MovementContext.IsInPronePose=true;EarpieceAudio.Maintain();
 Near(audio.Controller.Values["GunsVolume"],audio.CurrentHeadphonesTemplate.DryVolume,"native gun bus stays unchanged");Near(gun._queue.AudioSources[0].Output,.7*.8*.9*.5,"Deep Listening's 50% rule replaces native volume without compounding");
 Near(EarpieceAudio.Hearing(new Player(),"step"),EarpieceRuntime.Get(p)!.Roll.Earpiece.Hearing,"100% hearing ends on replacement");
});
Run("Both physical gun channels start at native volume even when the earlier native updater is bypassed",()=>{
 var(p,f,s)=Scene("EP06-1");s.Roll.Earpiece.SelfNoise=1;var gun=new WeaponSoundPlayer{playersBridge=new(){iPlayer=p}};gun.FireBullet();gun._queue!.SimulateInlinedVolumeUpdate=true;
 for(int i=0;i<20;i++){
  gun._queue.Update();foreach(var source in gun._queue.AudioSources.Cast<SuperSource>()){
   Near(source.source1.StartedVolume,.7*.8*.9*1,"1st physical start before queue postfix");Near(source.source2.StartedVolume,.3*.8*.9*1,"2nd physical start");Near(source.PreOcclusionVolume,.7,"raw 1st cache");Near(source.SecondCache,.3,"raw 2nd cache");
  }
 }
 gun.StopFiringLoop();gun._queue.Update();Near(((SuperSource)gun._queue.AudioSources[0]).source1.StartedVolume,.7*.8*.9*1,"tail physical start");
});
Run("Reload and equipment audio remain audible before and after actual gun queue playback",()=>{
 var(p,f,s)=Scene("EP06-1");s.Roll.Earpiece.SelfNoise=1;s.Roll.Earpiece.Ambient=1;var reload=new SuperSource();var item=new BaseSoundPlayer{playersBridge=new(){iPlayer=p},ItemSource=reload};
 item.PlayClip(20);Near(reload.Output,.7*.8*.9,"reload before shot");var gun=new WeaponSoundPlayer{playersBridge=new(){iPlayer=p}};gun.FireBullet();gun._queue!.Update();
 item.PlayClip(20);Near(reload.Output,.7*.8*.9,"reload after shot");Near(gun._queue.AudioSources[0].Output,.7*.8*.9*1,"gun remains native");
 Near(EarpieceAudio.Volume(p,"explosion"),1,"explosions have no signature volume reduction");Check(EarpieceAudio.Current.Owner==null,"item and gun scopes both end");
});
Run("A source released by a gun queue can play an explosion at native volume and range",()=>{
 var(p,f,s)=Scene("EP06-1");s.Roll.Earpiece.Hearing=1;var enemy=new Player();var gun=new WeaponSoundPlayer{playersBridge=new(){iPlayer=enemy}};gun.FireBullet();gun._queue!.Update();var source=gun._queue.AudioSources[0];
 Near(source.MaxDistance,160,"enemy gun hearing range");source.Release();source.Play();Near(source.Output,.7*.8*.9,"unattributed explosion is not a gunshot");Near(source.MaxDistance,80,"gun hearing expansion does not leak to reused source");
});
Run("Unrelated audio inside a firing callback is not classified as a gunshot",()=>{
 var(p,f,s)=Scene("EP06-1");s.Roll.Earpiece.SelfNoise=1;var casing=new SuperSource();var unowned=new SuperSource();var item=new BaseSoundPlayer{playersBridge=new(){iPlayer=p},ItemSource=casing};
 var gun=new WeaponSoundPlayer{playersBridge=new(){iPlayer=p},DuringFire=()=>{item.PlayClip(20);unowned.Play();}};gun.FireBullet();gun._queue!.Update();
 Near(casing.Output,.7*.8*.9,"nested equipment scope remains audible");Near(unowned.Output,.7*.8*.9,"unidentified non-queue sound is not a gunshot");Near(gun._queue.AudioSources[0].Output,.7*.8*.9*1,"only identified gun queue is native");
});
Run("Matamata Turtle slows walking and sprinting by 20% and restores on removal",()=>{
 var(p,f,s)=Scene("EP06-1");Near(EarpieceRuntime.Movement(p),.8,"walking");p.MovementContext.IsSprintEnabled=true;Near(EarpieceRuntime.Movement(p),.8,"sprinting");
 Near(EarpieceAudio.Volume(p,"gun"),1,"own gunfire native");Near(EarpieceAudio.Volume(new Player(),"gun"),1,"enemy gunfire native");
 Check(s.Roll.Definition.EffectText=="Hearing range is increased by 100%. Movement speed is reduced by 20%.","new signature only");
 p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem=null;Near(EarpieceRuntime.Movement(p),1,"removal");Check(!EarpieceRuntime.Statuses(p).Any(),"passive stays hidden");
});
Run("Self Muffle reaches both native step-bank inputs and stereo caches exactly once",()=>{
 var(p,f,s)=Scene("EP02-1");s.Roll.Earpiece.SelfNoise=.75f;p.NestedStepSoundSource=new SuperSource();p.SecondaryStep=new SuperSource();
 for(int i=0;i<20;i++){
  p.PlayStepSound();foreach(var step in new[]{p.NestedStepSoundSource,p.SecondaryStep}.Cast<SuperSource>()){
   Near(step.PreOcclusionVolume,.7*.75,"native first input");Near(step.SecondCache,.3*.75,"native second input");
   Near(step.Output,.7*.75*.8*.9,"first channel not squared");Near(step.Output2,.3*.75*.8*.9,"second channel not squared");
  }
 }
 Near(EarpieceRuntime.EmittedStep(p),1,"AI power unchanged");
 p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem=null;EarpieceAudio.Maintain();
 Near(p.NestedStepSoundSource.Output,.7*.8*.9,"active stereo restores after removal");p.PlayStepSound();Near(p.SecondaryStep.Output,.7*.8*.9,"next native input restored");
});
Run("First-person step controller is recognized without an outer Player scope",()=>{
 var(p,f,s)=Scene("EP01-2");s.Roll.Earpiece.SelfNoise=.75f;p.NestedStepSoundSource=new SuperSource();p.SecondaryStep=new SuperSource();
 var primary=new SoundBank();primary.Play(p.NestedStepSoundSource,volume:.8f);var secondary=new SoundBank();secondary.Play(p.SecondaryStep,volume:.8f);
 Near(primary.PlayedVolume,.8*.375,"primary signature times roll");Near(secondary.PlayedVolume,.8*.375,"secondary identity fallback");
 Near(p.SecondaryStep.Output,.7*.8*.375*.8*.9,"secondary output has 1 local factor");Check(EarpieceAudio.Current.Owner==null,"no synthetic player scope required");
});
Run("SimpleSource one-shot gain stays outside the native volume cache",()=>{
 var(p,f,s)=Scene("EP01-2");s.Roll.Earpiece.SelfNoise=.75f;var source=new SimpleSource();p.NestedStepSoundSource=source;var bank=new SoundBank();
 bank.Play(source,volume:.8f);Near(bank.PlayedVolume,.8*.375,"one-shot input");Near(source.PreOcclusionVolume,.7,"cache stays native");Near(source.OneShotGain,.8*.375,"native PlayOneShot gain");Near(source.EffectiveOutput,.7*.8*.9*.8*.375,"effective volume once");
 p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem=null;bank.Play(source,volume:.8f);Near(source.EffectiveOutput,.7*.8*.9*.8,"next step native after removal");
});
Run("SimpleSource looping input and pooled stereo release remove only the local baked factor",()=>{
 var(p,f,s)=Scene("EP01-2");s.Roll.Earpiece.SelfNoise=.75f;var loop=new SimpleSource();p.NestedStepSoundSource=loop;new SoundBank().Play(loop,volume:.8f,oneShot:false);
 Near(loop.PreOcclusionVolume,.7*.8*.375,"loop cache includes local input");loop.Release();Near(loop.PreOcclusionVolume,.7*.8,"release removes only local factor");Near(loop.Output,.7*.8*.8*.9,"other native gains retained");
 var stereo=new SuperSource();p.NestedStepSoundSource=stereo;new SoundBank().Play(stereo,volume:.8f);stereo.Release();Near(stereo.PreOcclusionVolume,.7*.8,"pooled first cache restored");Near(stereo.SecondCache,.3*.8,"pooled second cache restored");
});
Run("Local step input never changes other actors or unrelated sound banks",()=>{
 var(p,f,s)=Scene("EP01-2");s.Roll.Earpiece.SelfNoise=.75f;var enemy=new Player{NestedStepSoundSource=new SuperSource(),SecondaryStep=new SuperSource()};enemy.PlayStepSound();Near(enemy.NestedStepSoundSource.Output,.7*.8*.9,"enemy first");Near(enemy.SecondaryStep.Output,.7*.8*.9,"enemy second");
 var unrelated=new SuperSource();var bank=new SoundBank();bank.Play(unrelated,volume:.8f);Near(bank.PlayedVolume,.8,"unattributed bank unchanged");Near(unrelated.Output,.7*.8*.8*.9,"unrelated output");
});
Run("Own equipment input includes Inside Voice and Self Muffle and leaves other actors native",()=>{
 var(p,f,s)=Scene("EP01-2");s.Roll.Earpiece.SelfNoise=.75f;var source=new SuperSource();var sound=new BaseSoundPlayer{playersBridge=new(){iPlayer=p},ItemSource=source};
 sound.PlayClipInternal(new AudioClip(),20,.8f,0,false);Near(sound.PlayedVolume,.8*.375,"native equipment input");Near(source.Output,.7*.8*.375*.8*.9,"input and output do not square");
 var other=new BaseSoundPlayer{playersBridge=new(){iPlayer=new Player()},ItemSource=new SuperSource()};other.PlayClip(20);Near(other.PlayedVolume,1,"other equipment input native");
 p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem=null;sound.PlayClipInternal(new AudioClip(),20,.8f,0,false);Near(source.Output,.7*.8*.8*.9,"next own equipment playback restored");
});
Run("Every legendary headset gets per-copy stats with stable identity and unmodified native template",()=>{
 foreach(var d in ears.Values){
  var template=new HeadphonesTemplate{Id=d.TemplateId,RolloffMultiplier=1.2f,AmbientVolume=-10};
  var item=new Headphones("stats-"+d.TemplateId,template);var roll=RarityResolver.Resolve(item)!;var rows=item.Attributes;
  Check(rows.Count>=9,"constructor rows for "+d.RuntimeKey);Check(rows.Select(a=>a.Id).Distinct().Count()==rows.Count,"unique identities");
  ItemAttribute Row(LegendaryEarpieceStatId id)=>rows.Single(a=>a.Id.Equals(id));
  Near(Row(LegendaryEarpieceStatId.Hearing).Base(),1.2*LegendaryEarpieceStats.Hearing(roll),"native multiplier plus permanent copy bonuses");
  Near(Row(LegendaryEarpieceStatId.Ambient).Base(),Math.Pow(10,-10d/20d)*roll.Earpiece.Ambient,"final ambient volume fraction");
  Check(Row(LegendaryEarpieceStatId.Ambient).StringValue().EndsWith("%")&&!Row(LegendaryEarpieceStatId.Ambient).StringValue().Contains("dB"),"ambient uses player-facing percent");
  Near(Row(LegendaryEarpieceStatId.OwnStep).Base(),LegendaryEarpieceStats.Self(roll),"local step final");Near(Row(LegendaryEarpieceStatId.Contusion).Base(),EarpieceEffectRules.Contusion(d.RuntimeKey,roll.Earpiece.Contusion),"signature plus random contusion");
  var old=rows.ToArray();for(int i=0;i<10;i++)LegendaryEarpieceStats.Bind(item);Check(rows.SequenceEqual(old),"no duplicate rows on rebinding");
  Check(rows.All(a=>a.DisplayType()==EItemAttributeDisplayType.Compact&&a.StringValue().Length>0&&a.Tooltip().Length>0),"usable native compact fields");
  Near(template.RolloffMultiplier,1.2,"shared native hearing unchanged");Near(template.AmbientVolume,-10,"shared native ambient unchanged");
 }
});
Run("Headset inspection and comparison bind exact copies before native panel reads",()=>{
 var a=(Headphones)New("EP06-1","inspect-matamata");var b=(Headphones)New("EP01-2","compare-inside");
 var panel=new EFT.UI.ItemSpecificationPanel{_item=a};panel.RecreateAttributeBars();panel.Compare(b);Check(panel.Seen==10&&panel.Compared==9,"both panel paths see finalized rows");
 Near(a.Attributes.Single(x=>x.Id.Equals(LegendaryEarpieceStatId.Movement)).Base(),.8,"Matamata movement displayed");
 var icons=new EFT.UI.StaticIcons();foreach(var row in a.Attributes)Check(icons.GetAttributeIcon(row.Id) is EItemAttributeId,"supported native icon");
 var ordinary=new Headphones("ordinary",new HeadphonesTemplate{Id="ordinary-base"});panel._item=ordinary;panel.RecreateAttributeBars();Check(ordinary.Attributes.Count==0&&panel.Seen==0,"ordinary headphone untouched");
});
Run("Displayed permanent hearing excludes map triggers and includes Matamata and Inside Voice composition",()=>{
 var ambientItem=(Headphones)New("EP06-1");var ambientRoll=RarityResolver.Resolve(ambientItem)!;ambientRoll.Earpiece.Ambient=.8f;ambientItem.Template.AmbientVolume=-10;LegendaryEarpieceStats.Bind(ambientItem);var ambientRow=ambientItem.Attributes.Single(x=>x.Id.Equals(LegendaryEarpieceStatId.Ambient));Near(ambientRow.Base(),.2529822,"base plus boon as linear volume");Check(ambientRow.StringValue()=="25.3%"&&ambientRow.LessIsGood,"final percent and lower-is-quieter comparison");
 ambientItem.Template.AmbientVolume=0;ambientRoll.Earpiece.Ambient=1;Near(ambientRow.Base(),1,"neutral ambient level");Check(ambientRow.StringValue()=="100%","neutral level readable");
 var item=(Headphones)New("EP06-1");var roll=RarityResolver.Resolve(item)!;roll.Earpiece.Hearing=1.15f;LegendaryEarpieceStats.Bind(item);
 Near(item.Attributes.Single(x=>x.Id.Equals(LegendaryEarpieceStatId.Hearing)).Base(),1.2*2.3,"100% plus 15% hearing finalized");
 item=(Headphones)New("EP01-2");roll=RarityResolver.Resolve(item)!;roll.Earpiece.SelfNoise=.75f;LegendaryEarpieceStats.Bind(item);var steps=item.Attributes.Single(x=>x.Id.Equals(LegendaryEarpieceStatId.OwnStep));Near(steps.Base(),.375,"signature plus boon");Check(steps.StringValue()=="37.5%","readable final percent");
 item=(Headphones)New("EP05-3");roll=RarityResolver.Resolve(item)!;roll.Earpiece.Hearing=1.15f;LegendaryEarpieceStats.Bind(item);Near(item.Attributes.Single(x=>x.Id.Equals(LegendaryEarpieceStatId.Hearing)).Base(),1.2*1.15,"inactive kill trigger not shown permanently");
});

Run("Inventory sound scope installs on both native overloads and composes local volume",()=>{
 var(p,f,s)=Scene("EP01-2");s.Roll.Earpiece.SelfNoise=.75f;BackpackRuntime.InventoryNoise=.5f;BackpackAudio.InventoryFactor=.9f;
 var ui=new EFT.UI.GUISounds();ui.PlayItemSound("bag",EFT.UI.EInventorySoundType.Pickup,true);Near(ui.Observed,.1875,"inventory volume signatures multiply once");Near(BackpackAudio.InventoryFactor,.9,"scope restored after void overload");
 Check(ui.PlayItemSound(EModClass.Weapon),"native bool overload preserved");Near(ui.Observed,.1875,"bool overload volume");Near(BackpackAudio.InventoryFactor,.9,"scope restored after bool overload");
 ui.Fail=true;try{ui.PlayItemSound("bag",EFT.UI.EInventorySoundType.Pickup,true);}catch{}Near(BackpackAudio.InventoryFactor,.9,"exception finalizer restores scope");
});
Console.WriteLine($"Earpieces: {cases-failed}/{cases} cases passed; {assertions} assertions; {failed} failures.");Environment.ExitCode=failed==0?0:1;
class Use {public object HealthController=null!;public Item MedItem=null!;public bool _interrupted;}
