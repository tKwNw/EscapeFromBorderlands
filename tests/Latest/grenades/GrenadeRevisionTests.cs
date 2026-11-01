using System.Reflection;
using System.Text.Json;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EscapeFromBorderlands.Client;
using UnityEngine;
using S = EscapeFromBorderlands.Server;

internal static partial class GrenadeFeatureTests
{
    private static Grenade[] Children(GameWorld world) => world.Grenades.Where(g => LegendaryGrenadeEffects.Try(g.WeaponSource, out var p) && p.SmokeChild).ToArray();
    private static void Revision(Action<string, Action> run, Action<bool, string> check, Action<float, float, string> near)
    {
        run("Every Trailblazer drop ignores all parent colliders while keeping scenery collisions", () =>
        {
            var item=Copy("GR65");var player=Scene(item);var parent=Throw(player,item);var world=Singleton<GameWorld>.Instance;
            parent.transform.position=new(16,4,0);parent.LateUpdate();var children=Children(world);
            var parentColliders=parent.GetComponentsInChildren(typeof(Collider),true).Cast<Collider>().ToArray();
            check(children.Length==4&&parentColliders.Length==2,"immediate and spaced drops with compound colliders");
            var scenery=new Collider();
            foreach(var child in children)
                foreach(var c in child.GetComponentsInChildren(typeof(Collider),true).Cast<Collider>())
                {
                    foreach(var p in parentColliders)check(Physics.GetIgnoreCollision(p,c),"all parent/child collision impulses disabled, including inactive colliders");
                    check(!Physics.GetIgnoreCollision(c,scenery),"child still lands on map geometry");
                }
            foreach(var p in parentColliders)check(!Physics.GetIgnoreCollision(p,scenery),"parent retains native map physics");
            check(ReusableGrenadeRuntime.Remaining(item)==2,"wall still costs only 1 charge");
        });
        run("Trailblazer collision isolation never applies to unrelated or ordinary grenades", () =>
        {
            var trail=Copy("GR65");var other=Copy("GR62");var player=Scene(trail,other);var parent=Throw(player,trail);var unrelated=Throw(player,other);
            foreach(var c in unrelated.GetComponentsInChildren(typeof(Collider),true).Cast<Collider>())
                foreach(var p in parent.GetComponentsInChildren(typeof(Collider),true).Cast<Collider>())check(!Physics.GetIgnoreCollision(p,c),"unrelated physical copies keep native interactions");
            var count=Physics.Ignored.Count;Throw(player,other);check(Physics.Ignored.Count==count,"ordinary smoke signature adds no exclusions");
        });
        run("Fog of War scales visible Local and Shape particles in all dimensions", () =>
        {
            var item=Copy("GR56");var player=Scene(item);RarityResolver.Resolve(item)!.GrenadeSmokeSizeMultiplier=1.2f;
            var smoke=(SmokeGrenade)Throw(player,item);LegendaryGrenadeEffects.Try(smoke.WeaponSource,out var payload);var emission=payload.Emission!;
            foreach(var system in emission.GetComponentsInChildren(typeof(ParticleSystem),true).Cast<ParticleSystem>())
            {
                check(system.main.scalingMode==ParticleSystemScalingMode.Hierarchy,"active and inactive systems inherit cloud scale");
                near(system.VisibleScale.x,2.4f,"visible width");near(system.VisibleScale.y,2.4f,"visible height");near(system.VisibleScale.z,2.4f,"visible depth");
            }
            near(emission._startFillSize*emission._fillSystem.VisibleScale.x,3.5f*2.4f,"native fill puff is scaled once");
            near(emission._startFillDistance,2.4f,"native world-space fill distance scales once");
        });
        run("Smoke reattachment and pool reuse restore particle modes without accumulating scale", () =>
        {
            var item=Copy("GR56");var player=Scene(item);var smoke=(SmokeGrenade)Throw(player,item);LegendaryGrenadeEffects.Try(smoke.WeaponSource,out var payload);var emission=payload.Emission!;
            var size=RarityResolver.Resolve(item)!.GrenadeSmokeSizeMultiplier*2;
            emission.AttachTo(smoke.transform,Vector3.zero);emission.AttachTo(smoke.transform,Vector3.zero);
            near(emission.transform.localScale.x,size,"repeat hand-to-projectile binding does not compound");
            LegendaryGrenadeEffects.RestoreEmission(emission);
            check(((ParticleSystem)emission._particleSystem!).Mode==ParticleSystemScalingMode.Local&&emission._fillSystem.Mode==ParticleSystemScalingMode.Shape,"original modes returned to pool");
            near(emission.transform.localScale.x,1,"original container scale");near(emission._startFillSize,3.5f,"original fill size");near(emission._startFillDistance,1,"original fill distance");
            emission.AttachTo(new Transform(),Vector3.zero);check(((ParticleSystem)emission._particleSystem!).Mode==ParticleSystemScalingMode.Local,"ordinary reuse stays native");
        });
        run("Trailblazer child smoke retains vanilla particles while other legendary smoke uses its size roll", () =>
        {
            var trail=Copy("GR65");var normal=Copy("GR62");var player=Scene(trail,normal);Throw(player,trail);var child=(SmokeGrenade)Children(Singleton<GameWorld>.Instance).Single();
            child.OnCollisionEnter(new Collision());child.InvokeBlowUpEvent();LegendaryGrenadeEffects.Try(child.WeaponSource,out var cp);
            check(((ParticleSystem)cp.Emission!._particleSystem!).Mode==ParticleSystemScalingMode.Local&&cp.Emission._fillSystem.Mode==ParticleSystemScalingMode.Shape,"child has native particle modes");
            near(cp.Emission.transform.localScale.x,1,"no parent size inheritance");
            RarityResolver.Resolve(normal)!.GrenadeSmokeSizeMultiplier=1.3f;var smoke=(SmokeGrenade)Throw(player,normal);LegendaryGrenadeEffects.Try(smoke.WeaponSource,out var p);
            near(((ParticleSystem)p.Emission!._particleSystem!).VisibleScale.x,1.3f,"shared smoke-size bonus scales particles too");
        });
        run("Trailblazer samples every 5 horizontal metres across frames and changing height", () =>
        {
            var path = new TrailblazerPath(new(1, 0, 0)); var drops = new List<Vector3>();
            path.Advance(new(13, 12, 0), drops.Add); path.Advance(new(26, 12, 0), drops.Add);
            check(drops.Count == 5, "all crossed boundaries emitted");
            for (var i=0;i<drops.Count;i++)near(drops[i].x,6+i*5,"even horizontal spacing");
            near(drops[0].y,5,"height interpolated on flight segment");
            path.Advance(new(26,100,0),drops.Add);check(drops.Count==5,"vertical motion does not add clouds");
            path.Advance(new(26,100,5),drops.Add);check(drops.Count==6,"turn continues path distance");near(drops[^1].z,5,"no duplicate boundary after exact spacing");
        });
        run("Trailblazer immediately drops an ordinary M18 and creates a wall until landing", () =>
        {
            var item=Copy("GR65");var p=Scene(item);var roll=RarityResolver.Resolve(item)!;roll.GrenadeSmokeSizeMultiplier=1.3f;roll.GrenadeSmokeDurationMultiplier=1.4f;
            var parent=(SmokeGrenade)Throw(p,item);var world=Singleton<GameWorld>.Instance;
            check(Children(world).Length==1,"immediate release drop");
            check(parent.TimerStarts==0,"parent smoke life waits for landing");
            parent.InvokeBlowUpEvent();check(!parent.Emitting,"native fuse cannot make a mid-air parent cloud");
            parent.transform.position=new(12,3,0);parent.LateUpdate();var children=Children(world);
            check(children.Length==3,"release, 5-metre and 10-metre drops");near(children[1].transform.position.x,5,"first spacing");near(children[2].transform.position.x,10,"second spacing");
            foreach(var c in children){check(c.WeaponSource.StringTemplateId=="617aa4dd8166f034d57de9c5"&&!ReusableGrenadeRuntime.IsReusable(c.WeaponSource),"ordinary M18 identity");near(((SmokeGrenade)c).EmitTime,90,"no parent duration roll");}
            parent.OnCollisionEnter(new Collision{contacts=[new(){normal=new(1,0,0)}]});
            LegendaryGrenadeEffects.Try(parent.WeaponSource,out var payload);check(!payload.Landed,"wall impact is not ground landing");
            parent.transform.position=new(16,0,0);parent.OnCollisionEnter(new Collision());
            check(Children(world).Length==4&&parent.Emitting,"last crossed boundary plus final parent cloud");
            check(payload.Emission!=null&&parent.TimerStarts==1,"final parent emission and full lifetime start on landing");
            parent.transform.position=new(30,0,0);parent.LateUpdate();parent.OnCollisionEnter(new Collision());
            check(Children(world).Length==4&&ReusableGrenadeRuntime.Remaining(item)==2,"no post-landing drops or extra charge costs");
        });
        run("Trailblazer short throws retain the immediate drop and final cloud", () =>
        {
            var item=Copy("GR65");var p=Scene(item);var parent=(SmokeGrenade)Throw(p,item);parent.transform.position=new(2,0,0);
            parent.OnCollisionEnter(new Collision());check(!parent.Emitting,"landing still respects native fuse minimum");parent.InvokeBlowUpEvent();
            check(parent.Emitting&&Children(Singleton<GameWorld>.Instance).Length==1,"short throw still covers start and end");
        });
        run("Dropped M18 grenades wait for landing, keep vanilla clouds and never recurse", () =>
        {
            var item=Copy("GR65");var p=Scene(item);var parent=Throw(p,item);var world=Singleton<GameWorld>.Instance;var child=(SmokeGrenade)Children(world).Single();
            child.InvokeBlowUpEvent();check(!child.Emitting&&child.TimerStarts==0,"no child cloud or lifetime timer in flight");
            child.OnCollisionEnter(new Collision());check(child.Emitting&&child.TimerStarts==1,"native smoke starts after landing");
            LegendaryGrenadeEffects.Try(child.WeaponSource,out var payload);check(payload.Emission!=null,"child has native smoke particles");
            near(payload.Emission!.transform.localScale.x,1,"no parent smoke-size roll");near(payload.Emission.Prewarm,0,"normal native growth");
            check(ReferenceEquals(ReusableGrenadeRuntime.Explosion(child.WeaponSource,child.WeaponSource),child.WeaponSource),"no legendary explosive wrapper");
            child.LateUpdate();child.InvokeBlowUpEvent();check(Children(world).Length==1&&ReusableGrenadeRuntime.Remaining(item)==2,"no recursive children or charge use");
            ReusableGrenadeRuntime.Reset();var count=world.Grenades.Count;parent.LateUpdate();check(world.Grenades.Count==count,"old projectile cannot spawn after session reset");
        });
        run("Smoke ownership binds at release even without the separate native factory postfix", () =>
        {
            var item=Copy("GR57");var p=Scene(item);GrenadeFactory.RunCreatedHook=false;var smoke=(SmokeGrenade)Throw(p,item);
            LegendaryGrenadeEffects.Try(smoke.WeaponSource,out var payload);check(payload.Grenade==smoke&&payload.Owner==p.ProfileId,"returned projectile bound directly");
            smoke.InvokeBlowUpEvent();near(GrenadeBuffs.Movement(p),1.2f,"ordinary loadout receives smoke movement bonus");
        });
        run("All 5 smoke bonuses follow actual emission instead of component enabled state", () =>
        {
            foreach(var key in new[]{"GR57","GR58","GR62","GR63","GR64"})
            {
                ReusableGrenadeRuntime.Reset();var item=Copy(key);var p=Scene(item);p.Crouched=true;var smoke=(SmokeGrenade)Throw(p,item);
                float Value()=>key switch{"GR57"=>GrenadeBuffs.Movement(p),"GR58"=>GrenadeBuffs.Recoil(p),"GR62"=>GrenadeBuffs.Packing(p),"GR63"=>GrenadeBuffs.Medicine(p),_=>GrenadeBuffs.Recovery(p,"legRecovery")};
                near(Value(),1,"not active before emission");smoke.InvokeBlowUpEvent();smoke.enabled=false;check(Value()!=1,"active cloud survives component disable: "+key);
                check(GrenadeBuffs.Statuses(p).Any(s=>s.Key=="grenade-buff:smoke:"+key),"active smoke status visible: "+key);
                p.Position=new(8.01f,0,0);near(Value(),1,"outside emitter radius");p.Position=new(8,0,0);check(Value()!=1,"inclusive 8-metre boundary");
                Time.time+=smoke.LifeTime;near(Value(),1,"native cloud lifetime expiry");check(!GrenadeBuffs.Statuses(p).Any(s=>s.Key.StartsWith("grenade-buff:smoke:")),"expired status removed");
            }
        });
        run("Still Waters refreshes weapon recoil caches when crouching and leaving smoke", () =>
        {
            var item=Copy("GR58");var p=Scene(item);Throw(p,item).InvokeBlowUpEvent();p.HandsController=new Player.FirearmController{Owner=p,Item=new Weapon()};
            GrenadeBuffs.Maintain();var before=p.ProceduralWeaponAnimation.Refreshes;
            p.Crouched=true;GrenadeBuffs.Maintain();check(p.ProceduralWeaponAnimation.Refreshes==before+1,"crouching refreshes live weapon variables");
            p.Position=new(9,0,0);GrenadeBuffs.Maintain();check(p.ProceduralWeaponAnimation.Refreshes==before+2,"leaving smoke restores cached recoil");
        });
        run("Matching smoke statuses are grouped and react to crouching and pool disposal", () =>
        {
            var a=Copy("GR58");var b=Copy("GR58");var p=Scene(a,b);var first=Throw(p,a);Throw(p,b).InvokeBlowUpEvent();first.InvokeBlowUpEvent();
            check(!GrenadeBuffs.Statuses(p).Any(),"standing has no Still Waters status");p.Crouched=true;
            check(GrenadeBuffs.Statuses(p).Count()==1,"overlapping matching smoke has 1 status");
            LegendaryGrenadeEffects.Try(first.WeaponSource,out var payload);LegendaryGrenadeEffects.RestoreEmission(payload.Emission!);
            near(GrenadeBuffs.Recoil(p),.4f,"another active cloud still supplies effect");p.Position=new(9,0,0);check(!GrenadeBuffs.Statuses(p).Any(),"leaving both clouds removes status");
        });
        run("Pop Smoke defers held particles and keeps its full area through growth without blocking cleanup", () =>
        {
            var item=Copy("GR59");var p=Scene(item);var hands=new Player.BaseGrenadeHandsController{Owner=p,Item=item};hands.Cook();check(hands.GrenadeEmission==null,"no early hand smoke");
            var smoke=(SmokeGrenade)Throw(p,item);LegendaryGrenadeEffects.Try(smoke.WeaponSource,out var payload);check(payload.Emission==null,"no cloud before detonation");smoke.InvokeBlowUpEvent();
            check(smoke.AreaTimes.Contains(.3f),"full peak area on emission event");float t=.01f;LegendaryGrenadeEffects.SmokeArea(smoke,ref t);near(t,.3f,"maintained peak during native growth");
            t=0;LegendaryGrenadeEffects.SmokeArea(smoke,ref t);near(t,0,"native final-area cleanup remains available");
            var emission=payload.Emission!;LegendaryGrenadeEffects.RestoreEmission(emission);near(emission._defaultStartFillDelay,25,"ordinary pooled fill delay restored");
        });
        run("Pop Smoke replaces only its obsolete growth roll and retains saved 0.21.1 bonuses", () =>
        {
            using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Grenades.Pop0211Rolls.json")!;
            using var snapshots=JsonDocument.Parse(stream);var replaced=0;var unchanged=0;
            foreach(var row in snapshots.RootElement.EnumerateArray())
            {
                var original=row.GetProperty("bonuses").EnumerateArray().Select(x=>x.GetString()!).ToArray();
                var roll=RarityResolver.Resolve(Copy("GR59",row.GetProperty("id").GetString()))!;var current=roll.AffixDescriptions.Skip(1).ToArray();
                check(current.Length==4&&current.Distinct().Count()==4&&!current.Any(x=>x.StartsWith("Rapid Screen")),"4 useful distinct bonuses");
                foreach(var text in original.Where(x=>!x.StartsWith("Rapid Screen")))check(current.Contains(text),"unaffected saved roll retained");
                if(original.Any(x=>x.StartsWith("Rapid Screen")))replaced++;else{unchanged++;check(original.SequenceEqual(current),"unaffected copies retain complete bonus order");}
                near(roll.GrenadeSmokeBuildTimeMultiplier,1,"instant signature has no obsolete growth multiplier");
            }
            check(replaced>0&&unchanged>0,"both upgrade branches covered");
        });
        run("Reviewed balance changes use 4x Big One radius, 25-metre flash and 15 Shagbag charges", () =>
        {
            var big=Copy("GR15");var bang=Copy("GR35");var shag=Copy("GR40");var p=Scene(big,bang,shag);
            var radius=RarityResolver.Resolve(big)!.GrenadeRadiusMultiplier;check(radius>=4&&radius<=4.8f,"+300% means 4x before optional random radius");
            var g=Throw(p,bang);near(ReusableGrenadeRuntime.Explosion(g.WeaponSource,g.WeaponSource).Blindness.y,25,"Big Bang visual flash radius");
            for(var i=0;i<15;i++){Throw(p,shag);check(ReusableGrenadeRuntime.Remaining(shag)==14-i,"independent 15-charge capacity");}
            check(!ReusableGrenadeRuntime.Usable(shag)&&p.InventoryController.Reachable.Contains(shag),"empty Shagbag retained and filtered");Time.time=280;check(ReusableGrenadeRuntime.Remaining(shag)==1,"normal 180-second recharge");
        });
        run("Previously breached closed doors reopen repeatedly while locked and non-operatable doors stay closed", () =>
        {
            var item=Copy("GR01");var p=Scene(item);RarityResolver.Resolve(item)!.GrenadeDoorBreach=true;var door=new EFT.Interactive.Door();
            Throw(p,item).InvokeBlowUpEvent();check(door.IsBroken&&door.DoorState==EFT.Interactive.EDoorState.Open,"initial native breach");
            for(var i=0;i<2;i++){door.DoorState=EFT.Interactive.EDoorState.Shut;Throw(p,item).InvokeBlowUpEvent();check(door.IsBroken&&door.DoorState==EFT.Interactive.EDoorState.Open,"reclosed broken door opens again");}
        });
        run("APBS 1-percent defaults preserve explicit rates and legacy settings during migration", () =>
        {
            var defaults=S.RarityConfiguration.Parse("{}",out _);foreach(var category in new[]{"guns","melee","backpacks","chestRigs","grenades"})near((float)defaults.Chance("apbs",category),1,"new default: "+category);
            var saved=S.RarityConfiguration.Parse("{\"apbsPmcLegendaryChancePercent\":0.25,\"spawnChances\":{\"apbs\":{\"guns\":0,\"grenades\":0.75}}}",out var migrated,"apbs");
            near((float)saved.Chance("apbs","guns"),0,"explicit disabled category");near((float)saved.Chance("apbs","melee"),.25f,"legacy configured rate");near((float)saved.Chance("apbs","grenades"),.75f,"custom grenade rate");near((float)saved.Chance("apbs","backpacks"),1,"missing category new default");check(migrated!=null,"missing categories written back");
        });
    }
}
