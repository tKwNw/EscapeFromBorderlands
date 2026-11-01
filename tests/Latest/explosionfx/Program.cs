using EFT;using EFT.Particles;using EscapeFromBorderlands.Client;using HarmonyLib;using Systems.Effects;using UnityEngine;
int cases=0,assertions=0,failed=0;
void Check(bool value,string why){assertions++;if(!value)throw new Exception(why);}
void Near(float value,float expected,string why)=>Check(Math.Abs(value-expected)<.0002f,$"{why}: {value} != {expected}");
void Run(string name,Action test){var filter=Environment.GetEnvironmentVariable("EFB_EXPLOSION_FX_CASE_FILTER");if(!string.IsNullOrEmpty(filter)&&!name.Contains(filter,StringComparison.Ordinal))return;
#if HFX_PRESENT
 if(!name.StartsWith("HollywoodFX",StringComparison.Ordinal))return;
#else
 if(name.StartsWith("HollywoodFX",StringComparison.Ordinal))return;
#endif
 cases++;try{LegendaryExplosionFx.Reset();Plugin.LogSource.Warnings.Clear();Plugin.LogSource.Infos.Clear();Time.time=0;Fixture.Effects=new();test();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e);}}
var harmony=new Harmony("efb.validation.explosionfx");foreach(var type in new[]{typeof(LegendaryGrenadeFxScopePatch),typeof(LegendaryExplosionFxCapturePatch),typeof(LegendaryExplosionFxPlaybackPatch),typeof(LegendaryExplosionFxParticlePatch),typeof(LegendaryExplosionFxMediatorPatch),typeof(LegendaryHollywoodFxBlastPatch)})harmony.CreateClassProcessor(type).Patch();
if(typeof(LegendaryExplosionFx).Assembly.GetType("EscapeFromBorderlands.Client.LegendaryExplosionFxNativeQueuePatch") is {} queuePatch)harmony.CreateClassProcessor(queuePatch).Patch();
if(typeof(LegendaryExplosionFx).Assembly.GetType("EscapeFromBorderlands.Client.LegendaryExplosionFxQueuePlaybackPatch") is {} bridge)harmony.CreateClassProcessor(bridge).Patch();
Grenade Nade(float finalRadius=20,float baseRadius=10)=>new(){WeaponSource=new(){Legendary=true,FinalRadius=finalRadius,BaseRadius=baseRadius}};
ParticleSystem Particle(){var root=new GameObject();var p=new ParticleSystem();root.Add(p);return p;}
Run("queued grenade descriptors retain different radii without touching the shared effect",()=>{
 var effects=Fixture.Effects;var original=effects.Template;Nade(20).Explosion();Nade(5).Explosion();new Grenade().Explosion();
 Check(effects.Queue.Count==3,"one event each");var a=effects.Queue[0].Effect;var b=effects.Queue[1].Effect;
 Check(!ReferenceEquals(a,b)&&!ReferenceEquals(a,original)&&ReferenceEquals(effects.Queue[2].Effect,original),"per-event identities");Near(a.LightRange,6,"expanded light");Near(b.WindRadius,2,"contracted wind");Near(original.LightRange,3,"shared light unchanged");Near(original.ParticlesShift,.1f,"shared offset unchanged");Near(LegendaryExplosionFx.Factor,1,"no live scope after queuing");
 Near(LegendaryExplosionFx.Enter(a),0,"initial playback state");Near(LegendaryExplosionFx.Factor,2,"expanded descriptor");LegendaryExplosionFx.Enter(b);Near(LegendaryExplosionFx.Factor,.5f,"contracted descriptor");LegendaryExplosionFx.Enter(original);Near(LegendaryExplosionFx.Factor,1,"ordinary descriptor");
});
Run("all six native particle branches scale per-emission size and travel without changing buffers",()=>{
 var adapter=new ParticleSystemAdapter();Fixture.Effects.Template.Particles=Enumerable.Range(0,6).Select(i=>new Effects.Effect.ParticleSys{Particle=adapter,Branch=i}).ToArray();
 Nade(30).Explosion();new Grenade().Explosion();Fixture.Effects.Drain();Check(adapter.Emissions.Count==12,"exact particle count");for(int i=0;i<12;i++){Near(adapter.Emissions[i].Velocity.y,i<6?3:1,"travel factor");Near(adapter.Emissions[i].Size,i<6?6:2,"size factor");}
 Near(adapter._lifeTimes[0],2,"lifetime unchanged");Near(adapter._speeds[0],2,"authored speed unchanged");Near(adapter._sizes[0],2,"authored size unchanged");Near(LegendaryExplosionFx.Factor,1,"playback finalizer restored");
});
Run("secondary explosions use their own effective radius rather than their parent's scale",()=>{
 var parent=Nade(50);var child=Nade(12,8);parent.Nested=child.Explosion;parent.Explosion();Check(parent.DamageCalls==1&&child.DamageCalls==1,"damage count unchanged");
 var q=Fixture.Effects.Queue;LegendaryExplosionFx.Enter(q[0].Effect);Near(LegendaryExplosionFx.Factor,5,"parent scale");LegendaryExplosionFx.Enter(q[1].Effect);Near(LegendaryExplosionFx.Factor,1.5f,"child independent scale");
});
Run("smoke children, smoke signatures and ordinary grenades retain their native visual descriptors",()=>{
 foreach(var grenade in new[]{new Grenade(),Nade(),Nade()}){if(grenade.WeaponSource.Legendary){if(Fixture.Effects.Queue.Count==1)grenade.WeaponSource.SmokeChild=true;else grenade.WeaponSource.Category="smoke";}grenade.Explosion();}
 Check(Fixture.Effects.Queue.All(q=>ReferenceEquals(q.Effect,Fixture.Effects.Template)),"ordinary and smoke unscaled");
});
Run("flash grenades with zero blast radius scale from their final blindness radius",()=>{
 var grenade=Nade(0,0);grenade.WeaponSource.FinalFlash=30;grenade.WeaponSource.BaseFlash=10;grenade.Explosion();LegendaryExplosionFx.Enter(Fixture.Effects.Queue.Single().Effect);Near(LegendaryExplosionFx.Factor,3,"flash radius");
});
Run("impact FX use their absolute radius and authored particle envelope",()=>{
 Fixture.Effects.Template.Particles=[new(){Particle=new ParticleSystemAdapter()}]; // 2 s * 2 m/s + half 2 m size = 5 m.
 LegendaryExplosionFx.Impact(Fixture.Effects,new(1,2,3),Vector3.up,10,.25f);var q=Fixture.Effects.Queue.Single();LegendaryExplosionFx.Enter(q.Effect);Near(LegendaryExplosionFx.Factor,2,"absolute radius calibration");Near(q.Volume,.25f,"audio volume retained");Near(q.Position.x,1,"position retained");Near(q.Normal.y,1,"normal retained");
});
Run("missing particle samples and invalid radii have finite safe fallbacks",()=>{
 foreach(var (radius,reference) in new[]{(0f,10f),(10f,0f),(float.NaN,1f),(1f,float.NaN),(float.PositiveInfinity,1f),(float.MaxValue,float.Epsilon)})Near(LegendaryExplosionFx.Ratio(radius,reference),1,"invalid ratio");
 LegendaryExplosionFx.Impact(Fixture.Effects,default,Vector3.up,.5f,1);LegendaryExplosionFx.Enter(Fixture.Effects.Queue.Single().Effect);Near(LegendaryExplosionFx.Factor,.5f,"sampleless 1 metre fallback");
});
Run("non-grenade effects are not captured even inside a legendary scope",()=>{
 LegendaryExplosionFx.Current=new(20,10);var original=Fixture.Effects.Template;Fixture.Effects.AddEffectEmit(original,default,default,null,false,1,false,true,false,0);Check(ReferenceEquals(Fixture.Effects.Queue.Single().Effect,original),"non grenade unchanged");
});
Run("native and adapter failures restore nested scopes and do not block damage",()=>{
 LegendaryExplosionFx.Current=new(7,7);var grenade=Nade();grenade.Throw=true;try{grenade.Explosion();}catch{}Near(LegendaryExplosionFx.Current.Radius,7,"native failure restores scope");
 grenade=Nade();grenade.WeaponSource.Fail=true;grenade.Explosion();Check(grenade.DamageCalls==1,"cosmetic lookup failure leaves native damage");Check(Plugin.LogSource.Warnings.Count==1,"single diagnostic");
 Fixture.Effects.FailQueue=true;var safe=Nade();safe.Explosion();Check(safe.DamageCalls==1,"visual queue failure cannot cancel damage");Fixture.Effects.FailQueue=false;
 Fixture.Effects.Template.Throw=true;new Grenade().Explosion();LegendaryExplosionFx.PlayingScale=4;try{Fixture.Effects.Drain();}catch{}Near(LegendaryExplosionFx.Factor,4,"playback exception restores prior factor");
});
Run("scaled basic visuals clone their pool slot and leave simultaneously playing native particles unchanged",()=>{
 var world=new GameWorld();LegendaryExplosionFx.Maintain(world);var template=Particle();template.transform.localScale=new(2,2,2);template.Play(true);var mediator=new BasicParticleSystemMediator(template);Fixture.Effects.Template.BasicParticleSystemMediator=mediator;
 var before=UnityEngine.Object.Created;Nade(20).Explosion();Nade(30).Explosion();new Grenade().Explosion();Fixture.Effects.Drain();
 var clones=UnityEngine.Object.Clones.Skip(before).ToArray();Check(clones.Length==2,"separate concurrent instances");Near(clones[0].transform.localScale.x,4,"2x original scale");Near(clones[1].transform.localScale.x,6,"3x original scale");Near(template.transform.localScale.x,2,"native scale untouched");Check(mediator.NativePlays==1&&template.Plays==2,"ordinary event native path");Check(template.main.scalingMode==ParticleSystemScalingMode.Local,"native scaling mode unchanged");Check(clones.All(c=>((ParticleSystem)c.Components[0]).main.scalingMode==ParticleSystemScalingMode.Hierarchy),"private clones scale hierarchy");
});
Run("owned particle instances recycle only after completion and reset destroys only owned objects",()=>{
 var world=new GameWorld();LegendaryExplosionFx.Maintain(world);var template=Particle();Fixture.Effects.Template.BasicParticleSystemMediator=new(template);var before=UnityEngine.Object.Created;
 Nade(20).Explosion();Fixture.Effects.Drain();var clone=UnityEngine.Object.Clones.Last();var particle=(ParticleSystem)clone.Components[0];particle.Alive=false;Time.time=1;LegendaryExplosionFx.Maintain(world);Check(!clone.Active,"finished instance disabled");
 Nade(5).Explosion();Fixture.Effects.Drain();Check(UnityEngine.Object.Created==before+1,"reuses idle owned slot");Near(clone.transform.localScale.x,.5f,"new factor replaces old");LegendaryExplosionFx.Reset();Check(clone.Destroyed&&!template.gameObject.Destroyed,"reset only owned clone");
});
Run("FX maintenance errors fail safely and idle visuals expire",()=>{
 var world=new GameWorld();LegendaryExplosionFx.Maintain(world);var template=Particle();Fixture.Effects.Template.BasicParticleSystemMediator=new(template);Nade().Explosion();Fixture.Effects.Drain();var clone=UnityEngine.Object.Clones.Last();((ParticleSystem)clone.Components[0]).Throw=true;LegendaryExplosionFx.Maintain(world);Check(clone.Destroyed,"broken instance removed safely");Check(Plugin.LogSource.Warnings.Count==1,"diagnostic once");
 Nade().Explosion();Fixture.Effects.Drain();clone=UnityEngine.Object.Clones.Last();Time.time=31;LegendaryExplosionFx.Maintain(world);Check(!clone.Active,"maximum lifetime guard");Time.time=92;LegendaryExplosionFx.Maintain(world);Check(clone.Destroyed,"idle cleanup");
});
Run("decorative spark travel scales per event without changing shared emitter state",()=>{
 var spark=new SparkEmitter();Fixture.Effects.Template.Particles=[new(){Particle=spark}];Nade(20).Explosion();Fixture.Effects.Drain();Near(spark.Velocity.y,2,"scaled spark velocity");new Grenade().Explosion();Fixture.Effects.Drain();Near(spark.Velocity.y,1,"ordinary velocity restored");
});
Run("raid changes remove all private visual leases",()=>{
 var world=new GameWorld();LegendaryExplosionFx.Maintain(world);var template=Particle();Fixture.Effects.Template.BasicParticleSystemMediator=new(template);Nade().Explosion();Fixture.Effects.Drain();var clone=UnityEngine.Object.Clones.Last();LegendaryExplosionFx.Maintain(new GameWorld());Check(clone.Destroyed,"world transition cleanup");Near(LegendaryExplosionFx.Factor,1,"world transition scope cleanup");
});
Run("queued scaling survives bypassed capture and effect-prefix callbacks",()=>{
 var queue=AccessTools.Method(typeof(Effects),nameof(Effects.AddEffectEmit));var playback=AccessTools.Method(typeof(Effects.Effect),nameof(Effects.Effect.Emit));var basic=AccessTools.Method(typeof(BasicParticleSystemMediator),nameof(BasicParticleSystemMediator.Emit));
 harmony.Unpatch(queue,HarmonyPatchType.Prefix,harmony.Id);harmony.Unpatch(playback,HarmonyPatchType.Prefix,harmony.Id);harmony.Unpatch(playback,HarmonyPatchType.Finalizer,harmony.Id);harmony.Unpatch(basic,HarmonyPatchType.Prefix,harmony.Id);
 try {
  var adapter=new ParticleSystemAdapter();Fixture.Effects.Template.Particles=[new(){Particle=adapter}];Fixture.Effects.Template.BasicParticleSystemMediator=new(Particle());var grenade=Nade(40);grenade.Explosion();
  Check(!ReferenceEquals(Fixture.Effects.Queue.Single().Effect,Fixture.Effects.Template),"producer captured event before native callback");
  Check(Fixture.Effects.Queue.Single().WithDecal&&Fixture.Effects.Queue.Single().IsGrenade,"native grenade flags preserved");
  Fixture.Effects.Drain();Near(adapter.Emissions.Single().Size,8,"4x size without either callback");Near(adapter.Emissions.Single().Velocity.y,4,"4x travel without either callback");Near(UnityEngine.Object.Clones.Last().transform.localScale.x,4,"basic hierarchy scales without its prefix callback");Check(grenade.DamageCalls==1,"damage still exactly once");Near(LegendaryExplosionFx.Factor,1,"explicit playback scope restored");
  adapter.Emissions.Clear();LegendaryExplosionFx.Impact(Fixture.Effects,default,Vector3.up,10,1);Fixture.Effects.Drain();Near(adapter.Emissions.Single().Size,4,"impact also captured with callbacks bypassed");
 } finally {harmony.Unpatch(playback,HarmonyPatchType.Transpiler,harmony.Id);harmony.CreateClassProcessor(typeof(LegendaryExplosionFxCapturePatch)).Patch();harmony.CreateClassProcessor(typeof(LegendaryExplosionFxPlaybackPatch)).Patch();harmony.CreateClassProcessor(typeof(LegendaryExplosionFxMediatorPatch)).Patch();}
});
Run("basic particle clones preserve authored world scale including scaled parents",()=>{
 var world=new GameWorld();LegendaryExplosionFx.Maintain(world);var template=Particle();template.transform.localScale=new(1,2,3);template.transform.ParentScale=new(4,5,6);Fixture.Effects.Template.BasicParticleSystemMediator=new(template);
 Nade(40).Explosion();Fixture.Effects.Drain();var clone=UnityEngine.Object.Clones.Last();Near(clone.transform.localScale.x,16,"4x authored world width");Near(clone.transform.localScale.y,40,"4x authored world height");Near(clone.transform.localScale.z,72,"4x authored world depth");Near(template.transform.lossyScale.x,4,"shared original unchanged");
});
Run("queue and playback diagnostics identify legendary scale without repeated spam",()=>{
 var adapter=new ParticleSystemAdapter();Fixture.Effects.Template.Particles=[new(){Particle=adapter}];Nade(40).Explosion();Fixture.Effects.Drain();var first=Plugin.LogSource.Infos.Count;
 Check(Plugin.LogSource.Infos.Any(x=>x.Contains("GR15")&&x.Contains("queued 4.00x")),"queue shows key and scale");Check(Plugin.LogSource.Infos.Any(x=>x.Contains("GR15")&&x.Contains("playing 4.00x")),"playback shows key and scale");
 Nade(40).Explosion();Fixture.Effects.Drain();Check(Plugin.LogSource.Infos.Count==first,"same key/radius logged once per raid");Near(adapter.Emissions.Last().Size,8,"capture and prefix do not double-scale");
});
Run("native visual dispatch preserves renderer replacement hooks with empty native particles",()=>{
 var effect=Fixture.Effects.Template;effect.Particles=[];effect.BasicParticleSystemMediator=null;ExternalRenderer.Calls=0;
 var method=AccessTools.Method(typeof(Effects),nameof(Effects.EmitGrenade));harmony.Patch(method,prefix:new HarmonyMethod(typeof(ExternalRenderer),nameof(ExternalRenderer.Prefix)));
 try{var grenade=Nade(40);grenade.Explosion();Fixture.Effects.Drain();Check(ExternalRenderer.Calls==1,"replacement renderer runs exactly once");Check(grenade.DamageCalls==1,"damage dispatch stays once");Near(ExternalRenderer.Scale,4,"renderer sees legendary radius");}
 finally{harmony.Unpatch(method,AccessTools.Method(typeof(ExternalRenderer),nameof(ExternalRenderer.Prefix)));}
});
Run("optional HollywoodFX adapter skips cleanly when no renderer assembly is loaded",()=>{
 Check(!LegendaryHollywoodFx.Targets().Any(),"no required third-party target");Nade(40).Explosion();Check(Fixture.Effects.Queue.Count==1,"native event still queued");
});
#if HFX_PRESENT
Run("HollywoodFX replacement remains visible through native dispatch with wiped native particles",()=>{
 var main=Particle();var bundle=new HollywoodFX.Particles.EffectBundle([new(main)]);HollywoodRenderer.Blast=new(6,[bundle],new([]));HollywoodRenderer.Calls=0;
 Fixture.Effects.Template.Particles=[];Fixture.Effects.Template.BasicParticleSystemMediator=null;
 var method=AccessTools.Method(typeof(Effects),nameof(Effects.EmitGrenade));harmony.Patch(method,prefix:new HarmonyMethod(typeof(HollywoodRenderer),nameof(HollywoodRenderer.Prefix)));
 try {
  var grenade=Nade(40);grenade.Explosion();Fixture.Effects.Drain();HollywoodRenderer.Blast.CompleteDeferred();
  Check(HollywoodRenderer.Calls==1,"replacement dispatch occurs once");Near(main.WorldScaleAtPlay.x,4,"visible replacement keeps 4x scale after queued audio");Check(grenade.DamageCalls==1,"damage remains once");
  new Grenade().Explosion();Fixture.Effects.Drain();HollywoodRenderer.Blast.CompleteDeferred();Near(main.WorldScaleAtPlay.x,1,"ordinary grenade returns to renderer base size");
 } finally {harmony.Unpatch(method,AccessTools.Method(typeof(HollywoodRenderer),nameof(HollywoodRenderer.Prefix)));}
});
Run("HollywoodFX deferred particles keep per-blast scale after the native request ends",()=>{
 var main=Particle();var dust=Particle();var bundle=new HollywoodFX.Particles.EffectBundle([new(main)]);var extra=new HollywoodFX.Particles.EffectBundle([new(dust)]);
 var blast=new HollywoodFX.Explosion.ConfinedBlast(6,[bundle],extra);LegendaryExplosionFx.Current=new(32,8,"GR15");blast.Emit(default,Vector3.up);LegendaryExplosionFx.Current=default;
 blast.CompleteDeferred();Near(main.WorldScaleAtPlay.x,4,"delayed fireball scale");Near(dust.WorldScaleAtPlay.x,4,"delayed dust scale");Check(main.Mode==ParticleSystemScalingMode.Hierarchy,"parent scale applies to particles");
 Check(Plugin.LogSource.Infos.Any(x=>x.Contains("HollywoodFX renderer prepared at 4.00x")),"renderer-specific diagnostic");
});
Run("HollywoodFX normal pool reuse restores size and original particle modes",()=>{
 var main=Particle();var bundle=new HollywoodFX.Particles.EffectBundle([new(main)]);var blast=new HollywoodFX.Explosion.Blast([], [bundle],.65f);
 LegendaryExplosionFx.Current=new(32,8,"GR15");blast.Emit(default,Vector3.up);Near(main.WorldScaleAtPlay.x,2.6f,"existing HFX small-blast scale times legendary");
 LegendaryExplosionFx.Current=default;blast.Emit(default,Vector3.up);Near(main.WorldScaleAtPlay.x,.65f,"ordinary reuse returns to HFX base size");Check(main.Mode==ParticleSystemScalingMode.Local,"original mode restored");
 LegendaryExplosionFx.Reset();Check(main.transform.parent==null&&!main.gameObject.Destroyed,"reset detaches wrappers without deleting HFX particles");
});
Run("HollywoodFX separate blasts and absolute-radius impacts use independent scales",()=>{
 var a=Particle();var b=Particle();var first=new HollywoodFX.Explosion.ConfinedBlast(6,[new([new(a)])],new([]));var second=new HollywoodFX.Explosion.ConfinedBlast(6,[new([new(b)])],new([]));
 LegendaryExplosionFx.Current=new(32,8,"GR15");first.Emit(default,Vector3.up);LegendaryExplosionFx.Current=new(3,0,"impact");second.Emit(default,Vector3.up);LegendaryExplosionFx.Current=default;
 first.CompleteDeferred();second.CompleteDeferred();Near(a.WorldScaleAtPlay.x,4,"first blast retains scale");Near(b.WorldScaleAtPlay.x,.5f,"3m impact against HFX authored 6m radius");
});
Run("HollywoodFX nested emitter roots scale once and unsupported renderers keep running",()=>{
 var parent=Particle();var child=Particle();child.transform.parent=parent.transform;var bundle=new HollywoodFX.Particles.EffectBundle([new(parent),new(child)]);var blast=new HollywoodFX.Explosion.Blast([], [bundle],1);
 LegendaryExplosionFx.Current=new(32,8,"GR15");blast.Emit(default,Vector3.up);Near(parent.WorldScaleAtPlay.x,4,"root scales once");Near(child.WorldScaleAtPlay.x,4,"nested root not squared");
 var malformed=new HollywoodFX.Explosion.Blast([],[],1);malformed.Emit(default,Vector3.up);Check(Plugin.LogSource.Warnings.Any(x=>x.Contains("preserving its normal explosion")),"unsupported renderer warns and does not suppress Emit");
});
#endif
Console.WriteLine($"Explosion FX: {cases-failed}/{cases} cases passed; {assertions} assertions; {failed} failures.");Environment.ExitCode=failed==0?0:1;

public static class ExternalRenderer {
 public static int Calls;public static float Scale;
 public static void Prefix(){Calls++;var request=LegendaryExplosionFx.Current;Scale=LegendaryExplosionFx.Ratio(request.Radius,request.Reference);}
}

#if HFX_PRESENT
public static class HollywoodRenderer {
 public static HollywoodFX.Explosion.ConfinedBlast Blast=null!;public static int Calls;
 public static void Prefix(string ename,Vector3 position,Vector3 normal){Calls++;Blast.Emit(position,normal);}
}
#endif
