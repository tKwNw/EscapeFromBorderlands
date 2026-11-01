using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EscapeFromBorderlands.Client;
using UnityEngine;
using S=EscapeFromBorderlands.Server;

internal static partial class GrenadeFeatureTests
{
 private static void FlashPrototypes(Action<string,Action> run,Action<bool,string> check,Action<float,float,string> near)
 {
  run("Flash prototypes match approved bases, charges, recharge and burst parameters",()=>{
   check(Designs["GR66"].SourceShortName=="Zarya"&&Designs["GR66"].GrenadeChargesPerRaid==3&&Designs["GR66"].GrenadeRechargeSeconds==180,"impact double flash parameters");
   check(Designs["GR67"].SourceShortName=="M7290"&&Designs["GR67"].GrenadeChargesPerRaid==1&&Designs["GR67"].GrenadeRechargeSeconds==300,"9-banger parameters");
   check(GrenadeEffectRules.FlashPulseCount("GR66")==2&&GrenadeEffectRules.FlashPulseCount("GR67")==9,"pulse counts");near(GrenadeEffectRules.FlashPulseInterval("GR66"),1,"double interval");near(GrenadeEffectRules.FlashPulseInterval("GR67"),.25f,"9-banger interval");
   foreach(var key in new[]{"GR66","GR67"}){var d=Designs[key];check(d.SpawnEnabled&&d.GrenadeCategory=="flash"&&d.AffixCount==4,"same reusable flash category and rolls");}
  });
  run("Curtain Call uses a private native impact template and a 3-second backup fuse",()=>{
   var item=Copy("GR66");var p=Scene(item);var ordinary=ItemFactory.Template(Designs["GR66"].SourceTemplateId);var g=Throw(p,item);
   near(g.WeaponSource.MinTimeToContactExplode,.3f,"native impact arming field");near(g.WeaponSource.GetExplDelay,3,"backup fuse");near(ordinary.MinTimeToContactExplode,-1,"ordinary flash stays fuse-triggered");near(ordinary.ExplDelay,2,"ordinary Zarya fuse unchanged");
   g.Lifetime=.299f;g.ProcessContactExplodeCollision(1);g.LateUpdate();check(Grenade.Blasts.Count==0,"unarmed collision does not detonate");
   g.Lifetime=.3f;g.ProcessContactExplodeCollision(1);g.LateUpdate();check(Grenade.Blasts.Count==1,"armed native impact produces primary flash");
   Time.time=100.999f;Singleton<GameWorld>.Instance.Tick();check(Grenade.Blasts.Count==1,"repeat not early");Time.time=101;Singleton<GameWorld>.Instance.Tick();check(Grenade.Blasts.Count==2,"repeat after exactly 1 second");
   Time.time=120;Singleton<GameWorld>.Instance.Tick();check(Grenade.Blasts.Count==2&&ReusableGrenadeRuntime.Remaining(item)==2,"no recursive pulses or additional charge spend");
  });
  run("9-Banger produces exactly 9 native flashes over 2 seconds with 1 charge",()=>{
   var item=Copy("GR67");var p=Scene(item);var g=Throw(p,item);near(g.WeaponSource.GetExplDelay,1.5f,"native M7290 fuse retained");near(g.WeaponSource.MinTimeToContactExplode,-1,"normal fuse rather than impact");
   var at=new Vector3(7,8,9);g.transform.position=at;g.InvokeBlowUpEvent();check(Grenade.Blasts.Count==1&&ReusableGrenadeRuntime.Remaining(item)==0,"first pulse spends only charge");
   Time.time=100.249f;Singleton<GameWorld>.Instance.Tick();check(Grenade.Blasts.Count==1,"no early second pulse");
   for(int pulse=1;pulse<9;pulse++){Time.time=100+pulse*.25f;Singleton<GameWorld>.Instance.Tick();check(Grenade.Blasts.Count==pulse+1,"one pulse per interval");}
   check(Grenade.Blasts.All(b=>b.Position==at&&b.Owner==p.ProfileId),"fixed detonation point and original owner");check(Grenade.Blasts.Select(b=>b.Item.Id).Distinct().Count()==9,"independent native projectile identities");
   Time.time=110;Singleton<GameWorld>.Instance.Tick();check(Grenade.Blasts.Count==9&&ReusableGrenadeRuntime.Remaining(item)==0,"no recursive burst or additional spends");
  });
  run("Repeated flashes retain full per-copy radius and duration without damage or fragments",()=>{
   var item=Copy("GR66");var p=Scene(item);var roll=RarityResolver.Resolve(item)!;roll.GrenadeFlashRadiusMultiplier=1.8f;roll.GrenadeFlashDurationMultiplier=1.4f;var g=Throw(p,item);g.InvokeBlowUpEvent();
   roll.GrenadeFlashRadiusMultiplier=3;roll.GrenadeFlashDurationMultiplier=3;Time.time=101;Singleton<GameWorld>.Instance.Tick();
   var a=Grenade.Blasts[0].Effect;var b=Grenade.Blasts[1].Effect;near(a.Blindness.y,b.Blindness.y,"snapshot radius retained");near(a.Blindness.z,b.Blindness.z,"snapshot duration retained");
   foreach(var blast in Grenade.Blasts){near(blast.Effect.GetStrength,0,"no blast damage");check(blast.Effect.FragmentsCount==0,"no fragmentation");}
  });
  run("Flash pulses reevaluate cover and facing through the existing flash adapter",()=>{
   var item=Copy("GR67");var p=Scene(item);var enemy=new Player();Singleton<GameWorld>.Instance.Actors.Add(enemy);var g=Throw(p,item);g.InvokeBlowUpEvent();Flash(g,(enemy,2,true));check(enemy.ActiveHealthController.Flash>0,"visible first pulse blinds");
   Time.time=100.25f;Singleton<GameWorld>.Instance.Tick();var child=Singleton<GameWorld>.Instance.Grenades.Last();enemy.ActiveHealthController.Flash=0;Physics.Blocked=true;Flash(child,(enemy,2,true));check(enemy.ActiveHealthController.Flash==0,"cover stops subsequent pulse");
   Physics.Blocked=false;Flash(child,(enemy,2,false));check(enemy.ActiveHealthController.Flash==0,"facing away stops visual pulse");Flash(child,(enemy,2,true));check(enemy.ActiveHealthController.Flash>0,"uncovered facing target affected");
  });
  run("New flashbangs do not grant thrower immunity",()=>{
   foreach(var key in new[]{"GR66","GR67"}){var item=Copy(key);var p=Scene(item);var g=Throw(p,item);Flash(g,(p,2,true));check(p.ActiveHealthController.Flash>0,"owner can be flashed by "+key);}
  });
  run("Empty 9-Banger copies keep independent charge timers and regenerate after 300 seconds",()=>{
   var a=Copy("GR67");var b=Copy("GR67");var p=Scene(a,b);Throw(p,a);check(ReusableGrenadeRuntime.Remaining(a)==0&&ReusableGrenadeRuntime.Remaining(b)==1,"separate copies");
   Time.time=399.999f;check(ReusableGrenadeRuntime.Remaining(a)==0,"not early");Time.time=400;check(ReusableGrenadeRuntime.Remaining(a)==1,"1 charge restored at interval");
  });
  run("9-Banger recharge is isolated from ordinary grenade settings and freezes for the raid",()=>{
   LegendaryGrenadeSettings.Bind(new BepInEx.Configuration.ConfigFile());LegendaryGrenadeSettings.Reset();
   var field=typeof(LegendaryGrenadeSettings).GetField("NineBangerRecharge",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
   var setting=(BepInEx.Configuration.ConfigEntry<float>)field.GetValue(null)!;
   try{near(LegendaryGrenadeSettings.Interval(Designs["GR26"]),180,"standard recharge");near(LegendaryGrenadeSettings.Interval(Designs["GR67"]),300,"independent default");setting.Value=420;near(LegendaryGrenadeSettings.Interval(Designs["GR67"]),300,"current raid unchanged");LegendaryGrenadeSettings.Reset();near(LegendaryGrenadeSettings.Interval(Designs["GR67"]),420,"next raid setting");near(LegendaryGrenadeSettings.Interval(Designs["GR66"]),180,"double flash standard setting");setting.Value=float.NaN;LegendaryGrenadeSettings.Reset();near(LegendaryGrenadeSettings.Interval(Designs["GR67"]),300,"invalid uses 300");}
   finally{setting.Value=300;LegendaryGrenadeSettings.Reset();}
  });
  run("Flash burst stops on session reset and cannot leak into another world",()=>{
   var item=Copy("GR67");var p=Scene(item);Throw(p,item).InvokeBlowUpEvent();var old=Singleton<GameWorld>.Instance;Time.time=100.25f;old.Tick();check(Grenade.Blasts.Count==2,"burst started");
   ReusableGrenadeRuntime.Reset();Time.time=101;old.Tick();check(Grenade.Blasts.Count==2,"generation reset cancels remaining pulses");
   var other=Copy("GR66");Scene(other);Throw(Singleton<GameWorld>.Instance.MainPlayer,other).InvokeBlowUpEvent();var previous=Singleton<GameWorld>.Instance;Singleton<GameWorld>.Instance=new(){MainPlayer=p};Time.time=101;previous.Tick();check(Grenade.Blasts.Count==1,"world replacement cancels prior burst");
  });
  run("Duplicate detonation callbacks schedule only 1 flash sequence",()=>{
   var item=Copy("GR66");var p=Scene(item);var g=Throw(p,item);g.InvokeBlowUpEvent();LegendaryGrenadeEffects.Detonated(g,g.WeaponSource,g.transform.position);Time.time=101;Singleton<GameWorld>.Instance.Tick();check(Grenade.Blasts.Count==2,"idempotent sequence");
  });
  run("Flash pulse inspection rows match actual signatures without mutating shared templates",()=>{
   foreach(var key in new[]{"GR66","GR67"}){Scene();var item=StatCopy(key);var delay=item.GetExplDelay;ConstructorStats(item);near(Stat(item,LegendaryGrenadeStatId.FlashPulses).Base(),key=="GR66"?2:9,"count row");near(Stat(item,LegendaryGrenadeStatId.FlashPulseInterval).Base(),key=="GR66"?1:.25f,"interval row");near(item.GetExplDelay,delay,"shared fuse not modified");
    if(key=="GR66")check(Stat(item,EItemAttributeId.ExplosionDelay).StringValue().Contains("Impact")&&Stat(item,EItemAttributeId.ExplosionDelay).StringValue().Contains("backup"),"impact and backup made explicit");}
  });
  run("Both new flash designs use existing grenade spawn and flea settings",()=>{
   var catalog=S.LegendaryCatalog.Load();var defs=catalog.Variants.Where(v=>v.RuntimeKey is "GR66" or "GR67").ToArray();check(defs.Length==2,"both server definitions present");var policy=new S.GrenadeSpawnPolicy(defs);var config=new S.RarityConfiguration();
   foreach(var d in defs){var weights=policy.Pool(new Dictionary<string,double>{{d.SourceTemplateId,100}},config,"standalone","pmcusec",true);check(weights.ContainsKey(d.TemplateId),"native-source eligible spawn "+d.RuntimeKey);var scav=policy.Pool(new Dictionary<string,double>{{d.SourceTemplateId,100}},config,"standalone","assault",false);check(!scav.ContainsKey(d.TemplateId),"ordinary Scavs stay excluded");}
  });
 }
}
