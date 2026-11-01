using System.Reflection;
using System.Reflection.Emit;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EscapeFromBorderlands.Client;
using HarmonyLib;
using S = EscapeFromBorderlands.Server;
using UnityEngine;

int cases = 0, assertions = 0, failures = 0;
void Check(bool value, string reason) { assertions++; if (!value) throw new Exception(reason); }
void Near(float a, float b, string reason) => Check(Math.Abs(a - b) < .0001f, reason + $": {a} != {b}");
void Run(string name, Action test)
{
    cases++;
    try { ReusableGrenadeRuntime.Reset(); RarityResolver.ResetSession(); test(); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + (e.InnerException ?? e)); }
}
var definition = LegendaryCatalog.Load().Variants.Single(d => d.RuntimeKey == "GR26");
ThrowWeap Copy(string id) => new() { Id = id, StringTemplateId = definition.TemplateId };
Player Scene(params ThrowWeap[] grenades)
{
    Time.time = 100; ReusableGrenadeRuntime.FactoryHookReady = true;
    BackpackGrenadeAction.Reset(); BackpackRuntime.States.Clear();
    Singleton<ItemFactory>.Instance = new(); Singleton<GlobalEventDispatcher>.Instance = new();
    var p = new Player(); Singleton<GameWorld>.Instance = new() { MainPlayer = p };
    p.InventoryController.Reachable.AddRange(grenades); return p;
}
ThrowWeap? Select(Player p)
{
    var list = new List<ThrowWeap>();
    BackpackInventoryAccess.Grenades(p.InventoryController, list, i => i.Examined);
    return p.InventoryController.Inventory.Equipment.TopPriorityGrenade ?? list.FirstOrDefault();
}
Player.BaseGrenadeHandsController Fire(Player p, ThrowWeap item, bool fail = false)
{
    var h = new Player.BaseGrenadeHandsController { Owner = p, Item = item, FailSpawn = fail };
    // Exercise the real release prefix. The native fallback below deliberately
    // consumes an ordinary item, so a missed/incorrect guard fails the tests.
    var normal = PatchHooks.Prefix("BackpackFreeGrenadeReleasePatch", h, .35f, new Vector3(1,2,3), new Quaternion(0,0,0,1), new Vector3(4,5,6), false);
    if (normal) { p.InventoryController.Reachable.Remove(item); h.CreateAndRegisterGrenade(new(1,2,3),new(0,0,0,1),new(4,5,6),.35f); }
    return h;
}
void Spend(Player p, ThrowWeap item, int count) { for (int i = 0; i < count; i++) Check(Fire(p, item).Created == 1, "successful throw"); }
string Bonuses(ThrowWeap item) => string.Join("|", RarityResolver.Resolve(item)!.AffixDescriptions);
IEnumerable<CodeInstruction> Transpile(params CodeInstruction[] code) => (IEnumerable<CodeInstruction>)typeof(ReusableGrenadeProjectilePatch)
    .GetMethod("Transpiler", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [code])!;

Run("Two copies use A,A,A,B,B,B rather than sharing a pool", () =>
{
    var a = Copy("copy-a"); var b = Copy("copy-b"); var p = Scene(a,b); var order = new List<string>();
    for (int i = 0; i < 6; i++)
    {
        var chosen = Select(p)!; order.Add(chosen.Id); Fire(p, chosen);
        Check(ReusableGrenadeRuntime.Remaining(a) == Math.Max(0, 2-i), "first copy's own remaining charges");
        Check(ReusableGrenadeRuntime.Remaining(b) == (i < 3 ? 3 : 5-i), "second copy is untouched until the first is empty");
    }
    Check(order.SequenceEqual(new[] {a.Id,a.Id,a.Id,b.Id,b.Id,b.Id}), "drain one real item before switching");
    Check(Select(p) is null && p.InventoryController.Reachable.Count == 2, "spent permanent copies remain carried but unselectable");
});
Run("Both spent copies fall through to an ordinary grenade", () =>
{
    var a = Copy("a"); var b = Copy("b"); var ordinary = new ThrowWeap { Id="ordinary",StringTemplateId=definition.SourceTemplateId };
    var p = Scene(a,b,ordinary); Spend(p,a,3); Spend(p,b,3);
    p.InventoryController.Inventory.Equipment.TopPriorityGrenade = a;
    Check(Select(p) == ordinary, "stale preferred spent grenade is cleared"); Fire(p,ordinary);
    Check(!p.InventoryController.Reachable.Contains(ordinary) && p.InventoryController.Reachable.Count == 2, "ordinary native consumption retained");
});
Run("A preferred copy remains selected when inventory enumeration order changes", () =>
{
    var a=Copy("a"); var b=Copy("b"); var p=Scene(a,b); Check(Select(p)==a,"first copy chosen");
    Fire(p,a); p.InventoryController.Reachable.Reverse();
    Check(Select(p)==a,"same partially spent item stays selected");
});
Run("Manual selection of another copy changes the pin without merging counts", () =>
{
    var a=Copy("a");var b=Copy("b");var p=Scene(a,b);Fire(p,a);
    PatchHooks.Prefix("ReusableGrenadeSelectedPatch",p,b);
    Check(Select(p)==b,"explicit selection honored");Fire(p,b);
    Check(ReusableGrenadeRuntime.Remaining(a)==2&&ReusableGrenadeRuntime.Remaining(b)==2,"independent partial counts");
});
Run("Selecting a normal grenade explicitly is respected", () =>
{
    var a=Copy("a");var ordinary=new ThrowWeap{Id="normal",StringTemplateId=definition.SourceTemplateId};var p=Scene(a,ordinary);
    Fire(p,a);PatchHooks.Prefix("ReusableGrenadeSelectedPatch",p,ordinary);Check(Select(p)==ordinary,"manual normal selection");
});
Run("Unexamined copies do not bypass the native examined predicate", () =>
{
    var a=Copy("a");a.Examined=false;var b=Copy("b");var p=Scene(a,b);Check(Select(p)==b,"native predicate preserved");
});
Run("Merely selecting and cancelling a grenade spends no charge", () =>
{
    var a=Copy("a");var p=Scene(a);for(int i=0;i<100;i++)Check(Select(p)==a,"same copy can be redrawn");
    Check(ReusableGrenadeRuntime.Remaining(a)==3&&p.Physical.Thrown==0,"no successful release no charge");
});
Run("A failed projectile factory spends nothing and another draw can retry", () =>
{
    var a=Copy("a");var p=Scene(a);Check(Fire(p,a,true).Created==0,"factory failure");
    Check(ReusableGrenadeRuntime.Remaining(a)==3&&p.Physical.Thrown==0,"no charge or physical notification");
    Fire(p,a);Check(ReusableGrenadeRuntime.Remaining(a)==2,"fresh controller can successfully retry");
});
Run("Duplicate release events create exactly one projectile and spend one charge", () =>
{
    var a=Copy("a");var p=Scene(a);var h=Fire(p,a);
    for(int i=0;i<20;i++)Check(ReusableGrenadeRuntime.Release(h,.35f,new(),new(),new(),false),"repeat handled");
    Check(h.Created==1&&ReusableGrenadeRuntime.Remaining(a)==2,"exactly once");
    Check(p.Physical.Thrown==1&&p.Skills.ThrowAction.Count==1&&Singleton<GlobalEventDispatcher>.Instance.Throws==1,"native skill, stamina and AI notification once");
});
Run("A spent copy cannot create additional projectiles", () =>
{
    var a=Copy("a");var p=Scene(a);Spend(p,a,3);for(int i=0;i<20;i++)Check(Fire(p,a).Created==0,"empty copy suppressed");
    Check(ReusableGrenadeRuntime.Remaining(a)==0&&p.Physical.Thrown==3,"bounded at three");
});
Run("Drop and pickup preserve spent charges, including a new object with the same ID", () =>
{
    var a=Copy("a");var p=Scene(a);Spend(p,a,2);p.InventoryController.Reachable.Remove(a);a.CurrentAddress=null;
    Check(Select(p)==null,"dropped copy unavailable");
    var picked=Copy(a.Id);p.InventoryController.Reachable.Add(picked);
    Check(ReusableGrenadeRuntime.Remaining(picked)==1,"same persisted ID same charge count");
    Fire(p,picked);Check(ReusableGrenadeRuntime.Remaining(picked)==0,"last charge only");
});
Run("Dropping a fully empty copy cannot refill it", () =>
{
    var a=Copy("a");var p=Scene(a);Spend(p,a,3);p.InventoryController.Reachable.Clear();p.InventoryController.Reachable.Add(Copy(a.Id));
    Check(Select(p)==null,"spent ledger entry is retained");
});
Run("Moving a copy between carried containers retains its count and bonuses", () =>
{
    var a=Copy("a");var p=Scene(a);var before=Bonuses(a);Fire(p,a);
    p.InventoryController.Reachable.Remove(a);var container=new Item();container.Children.Add(a);p.InventoryController.Reachable.Add(container);
    Check(ReusableGrenadeRuntime.Remaining(a)==2&&Bonuses(a)==before,"item ID is independent of its address/container");
    Fire(p,a);Check(ReusableGrenadeRuntime.Remaining(a)==1,"contained owned copy can still be released");
});
Run("A transfer between players in the same raid keeps remaining charges", () =>
{
    var a=Copy("a");var owner=Scene(a);Fire(owner,a);var recipient=new Player();owner.InventoryController.Reachable.Clear();recipient.InventoryController.Reachable.Add(a);
    Check(ReusableGrenadeRuntime.Remaining(a)==2,"transfer cannot refill");Fire(recipient,a);
    Check(ReusableGrenadeRuntime.Remaining(a)==1,"recipient spends from the same item ledger");
});
Run("A controller cannot throw a copy after it has left that player's inventory", () =>
{
    var a=Copy("a");var p=Scene(a);p.InventoryController.Reachable.Clear();Check(Fire(p,a).Created==0,"stale controller refused");
    Check(ReusableGrenadeRuntime.Remaining(a)==3,"no charge for missing owned source");
});
Run("A dead player cannot spend a charge or spawn a grenade", () =>
{
    var a=Copy("a");var p=Scene(a);p.HealthController.IsAlive=false;Check(Fire(p,a).Created==0,"dead controller refused");Check(ReusableGrenadeRuntime.Remaining(a)==3,"no charge");
});
Run("An unavailable projectile hook cannot consume the real legendary item", () =>
{
    var a=Copy("a");var p=Scene(a);ReusableGrenadeRuntime.FactoryHookReady=false;
    Check(Fire(p,a).Created==0&&p.InventoryController.Reachable.Contains(a),"fail closed");Check(ReusableGrenadeRuntime.Remaining(a)==3,"no charge");
});
Run("A new raid refills charges while retaining the same four random bonuses", () =>
{
    var a=Copy("a");var b=Copy("b");var p=Scene(a,b);var x=Bonuses(a);var y=Bonuses(b);Spend(p,a,3);Fire(p,b);
    ReusableGrenadeRuntime.Reset();RarityResolver.ResetSession();
    Check(ReusableGrenadeRuntime.Remaining(a)==3&&ReusableGrenadeRuntime.Remaining(b)==3,"both refill independently");
    Check(Bonuses(a)==x&&Bonuses(b)==y,"persisted item identity preserves the rolls");
});
Run("Four grenade bonuses have deterministic five-percent rolls with per-copy variation", () =>
{
    var variations=new HashSet<string>();for(int i=0;i<500;i++)
    {
        var item=Copy("copy-"+i);var roll=RarityResolver.Resolve(item)!;var text=Bonuses(item);variations.Add(text);
        Check(roll.AffixDescriptions.Count==5&&roll.AffixDescriptions[0]==definition.EffectText,"signature plus four boons");
        Check(roll.GrenadeDamageMultiplier>=1.15f&&roll.GrenadeDamageMultiplier<=1.3f,"blast range");
        Check(roll.GrenadeRadiusMultiplier>=1.1f&&roll.GrenadeRadiusMultiplier<=1.2f,"radius range");
        Check(roll.GrenadeFragmentMultiplier>=1.2f&&roll.GrenadeFragmentMultiplier<=1.4f,"fragment range");
        Check(roll.GrenadeThrowDistanceMultiplier>=1.15f&&roll.GrenadeThrowDistanceMultiplier<=1.3f,"throw range");
        RarityResolver.ResetSession();Check(Bonuses(item)==text,"same ID same roll after cache reset");
    }
    Check(variations.Count>100,"distinct copies vary");
});
Run("Every projectile has a unique ID and carries the exact source copy's blast bonuses", () =>
{
    var a=Copy("a");var b=Copy("b");var p=Scene(a,b);var ids=new HashSet<string>();
    foreach(var item in new[]{a,a,a,b,b,b})
    {
        var roll=RarityResolver.Resolve(item)!;var h=Fire(p,item);var projectile=h.Projectiles.Single();
        Check(projectile.Original!=null&&projectile.Original.StringTemplateId==definition.SourceTemplateId,"native temporary grenade");
        Check(projectile.Original!.CurrentAddress==null&&projectile.Original.Id!=item.Id&&ids.Add(projectile.Original.Id),"separate projectile identity outside inventory");
        var explosion=ReusableGrenadeRuntime.Explosion(new TestExplosive(),projectile.Original);
        Near(explosion.GetStrength,100*roll.GrenadeDamageMultiplier,"source copy's damage");
        Near(explosion.MaxExplosionDistance,5*roll.GrenadeRadiusMultiplier,"source copy's radius");
        Check(explosion.FragmentsCount==(int)Math.Round(75*roll.GrenadeFragmentMultiplier,MidpointRounding.AwayFromZero),"source copy's fragment count");
        Check(projectile.ProfileId==p.ProfileId,"native thrower kill attribution");
    }
    Check(p.InventoryController.Reachable.Count==2,"no temporary grenade added to inventory");
});
Run("Projectile bonuses are a snapshot even if the source roll object changes", () =>
{
    var a=Copy("a");var p=Scene(a);var roll=RarityResolver.Resolve(a)!;var old=roll.GrenadeDamageMultiplier;var h=Fire(p,a);
    roll.GrenadeDamageMultiplier=10;Near(ReusableGrenadeRuntime.Explosion(new TestExplosive(),h.Projectiles.Single().Original!).GetStrength,100*old,"snapshot not mutable source");
});
Run("Removing the inventory copy after release does not remove its projectile bonuses", () =>
{
    var a=Copy("a");var p=Scene(a);var multiplier=RarityResolver.Resolve(a)!.GrenadeDamageMultiplier;var h=Fire(p,a);p.InventoryController.Reachable.Clear();a.CurrentAddress=null;
    Near(ReusableGrenadeRuntime.Explosion(new TestExplosive(),h.Projectiles[0].Original!).GetStrength,100*multiplier,"in-flight payload persists");
});
Run("Native position, force, rotation and fuse prewarm reach the projectile unchanged", () =>
{
    var a=Copy("a");var p=Scene(a);var h=Fire(p,a);
    Check(h.Position==new Vector3(1,2,3)&&h.Force==new Vector3(4,5,6)&&h.Rotation==new Quaternion(0,0,0,1)&&h.Prewarm==.35f,"native launch inputs");
});
Run("Explosion modifiers do not mutate shared vanilla data or other grenades", () =>
{
    var original=new TestExplosive();var modified=new ReusableGrenadeExplosive(original,1.25f,1.2f,1.4f);
    Near(modified.GetStrength,125,"blast damage");Near(modified.ArmorDistanceDistanceDamage.z,35,"armor blast damage");
    Near(modified.MinExplosionDistance,2.4f,"minimum radius");Near(modified.ArmorDistanceDistanceDamage.y,4.8f,"armor radius");
    Check(modified.FragmentsCount==105&&modified.CreateFragment()==original.Fragment,"real native fragments preserved");
    Near(original.GetStrength,100,"original strength unchanged");Check(original.FragmentsCount==75,"original count unchanged");
    Check(ReferenceEquals(original,ReusableGrenadeRuntime.Explosion(original,new Item{Id="ordinary"})),"unrelated grenade unchanged");
});
Run("Native underhand release spends one charge and keeps the underhand notification", () =>
{
    var a=Copy("underhand");var p=Scene(a);var h=new Player.BaseGrenadeHandsController{Owner=p,Item=a};
    Check(ReusableGrenadeRuntime.Release(h,.2f,new(1,2,3),new(0,0,0,1),new(2,1,0),true),"underhand handled");
    Check(h.Created==1&&p.Physical.LastLow&&ReusableGrenadeRuntime.Remaining(a)==2,"one native low throw");
});
Run("Throw distance applies this copy's boon at the actual native force adapter", () =>
{
    var a=Copy("distance");var p=Scene(a);var h=new Player.BaseGrenadeHandsController{Owner=p,Item=a};
    ChestRigCombat.RigBonus=false;BackpackRuntime.Records.Clear();object?[] args=[h,10f];PatchHooks.Prefix("BackpackGrenadeDistancePatch",args);
    Near((float)args[1]!,10*(float)Math.Sqrt(RarityResolver.Resolve(a)!.GrenadeThrowDistanceMultiplier),"grenade force multiplier");
});
Run("Throw distance composes with the existing rig and backpack effects", () =>
{
    var a=Copy("distance");var p=Scene(a);var h=new Player.BaseGrenadeHandsController{Owner=p,Item=a};ChestRigCombat.RigBonus=true;
    var record=new BackpackRuntime.Ledger();record.Until["throwDistance"]=Time.time+20;BackpackRuntime.Records[p.ProfileId]=record;
    object?[] args=[h,10f];PatchHooks.Prefix("BackpackGrenadeDistancePatch",args);
    Near((float)args[1]!,10*(float)Math.Sqrt(RarityResolver.Resolve(a)!.GrenadeThrowDistanceMultiplier*1.35f*1.5f),"all native force multipliers");
    ChestRigCombat.RigBonus=false;BackpackRuntime.Records.Clear();
});
Run("Nested explosion modifiers compose multiplicatively without changing attribution", () =>
{
    var source=new TestExplosive();var rigLike=new ReusableGrenadeExplosive(source,1.3f,1.5f,1);
    var grenade=new ReusableGrenadeExplosive(rigLike,1.25f,1.2f,1.4f);
    Near(grenade.GetStrength,162.5f,"stacked blast strength");Near(grenade.MaxExplosionDistance,9,"stacked radius");
    Check(grenade.FragmentsCount==105&&grenade.CreateFragment()==source.Fragment,"fragment identity unchanged");
});
Run("Held charges share a row with per-copy stowed recharge countdowns", () =>
{
    var a=Copy("a");var b=Copy("b");var p=Scene(a,b);Spend(p,a,2);Fire(p,b);
    var stowed=ReusableGrenadeRuntime.Statuses(p).ToArray();
    Check(stowed.Length==2&&stowed.Single(s=>s.Key=="grenade:a").Label=="Encore 1/3 charges · +1 charge in 3:00"&&stowed.Single(s=>s.Key=="grenade:b").Label=="Encore 2/3 charges · +1 charge in 3:00","independent stowed counts and cooldowns");
    p.HandsController=new Player.BaseGrenadeHandsController{Item=a,Owner=p};
    var statuses=EffectStatusModel.Visible(ReusableGrenadeRuntime.Statuses(p));
    Check(statuses.Count==2&&statuses.Single(s=>s.Key=="grenade:a").Label=="Encore 1/3 charges · +1 charge in 3:00","held row replaces that copy's stowed row");
    p.HandsController=new Player.BaseGrenadeHandsController{Item=b,Owner=p};
    Check(ReusableGrenadeRuntime.Statuses(p).Single(s=>s.Key=="grenade:b").Label=="Encore 2/3 charges · +1 charge in 3:00","other copy's own charges");
    Fire(p,a);p.HandsController=new Player.BaseGrenadeHandsController{Item=a,Owner=p};
    Check(ReusableGrenadeRuntime.Statuses(p).Single(s=>s.Key=="grenade:a").Label=="Encore · +1 charge in 3:00","empty copy has cooldown without 0/3");
    Check(p.InventoryController.Reachable.Contains(a),"empty copy stays in inventory");
});
Run("Every grenade design shows stowed counts only between empty and full", () =>
{
    foreach(var d in LegendaryCatalog.Load().Variants.Where(d=>d.GrenadeChargesPerRaid>0).GroupBy(d=>d.RuntimeKey).Select(g=>g.First()))
    {
        ReusableGrenadeRuntime.Reset();
        var item=new ThrowWeap{Id="charge-status-"+d.RuntimeKey,StringTemplateId=d.TemplateId};var p=Scene(item);
        var capacity=d.GrenadeChargesPerRaid;var interval=LegendaryGrenadeSettings.Interval(d);
        for(var remaining=capacity;remaining>=0;remaining--)
        {
            p.HandsController=new Player.AbstractHandsController{Item=p.Original};
            var stowed=ReusableGrenadeRuntime.Statuses(p).SingleOrDefault(s=>s.Key=="grenade:"+item.Id);
            var count=remaining+"/"+capacity+" charges";
            if(remaining==capacity)Check(stowed.Key==null,"full stowed copy hidden: "+d.ShortName);
            else
            {
                Check(stowed.Label.Contains("+1 charge in "),"real stowed recharge remains visible: "+d.ShortName);
                Check(stowed.Label.Contains(count)==(remaining>0),"only intermediate stowed count shown: "+count);
                Check(remaining==0||stowed.Label.StartsWith(d.ShortName+" "+count+" · "),"partial count has no extra separator");
            }
            p.HandsController=new Player.BaseGrenadeHandsController{Owner=p,Item=item};
            var held=ReusableGrenadeRuntime.Statuses(p).Single(s=>s.Key=="grenade:"+item.Id);
            Check(held.Label.Contains(count)==(remaining>0),"held full/partial counts retained; empty count hidden");
            Check(ReusableGrenadeRuntime.Remaining(item)==remaining,"display reads do not spend charges");
            if(remaining==0)break;
            var token="status-spend-"+remaining;
            Check(ReusableGrenadeRuntime.Charges.Reserve(item.Id,capacity,token,interval)&&ReusableGrenadeRuntime.Charges.Finish(item.Id,token,true),"successful production-ledger spend");
        }
    }
});
Run("Stowed count follows empty-to-full recharge without changing deadlines", () =>
{
    var a=Copy("recharging");var full=Copy("untouched");var p=Scene(a,full);Spend(p,a,3);
    string Label()=>ReusableGrenadeRuntime.Statuses(p).Single(s=>s.Key=="grenade:"+a.Id).Label;
    Check(Label()=="Encore · +1 charge in 3:00","empty shows only its existing cooldown");
    Time.time=279;for(var i=0;i<100;i++)Check(Label()=="Encore · +1 charge in 0:01","polling neither reveals 0/3 nor resets the deadline");
    Time.time=280;Check(Label()=="Encore 1/3 charges · +1 charge in 3:00","first restored charge appears at its deadline");
    Time.time=460;Check(Label()=="Encore 2/3 charges · +1 charge in 3:00","second restored charge appears at its deadline");
    Check(ReusableGrenadeRuntime.Remaining(full)==3,"other copy remains full and independent");
    Time.time=640;Check(!ReusableGrenadeRuntime.Statuses(p).Any(s=>s.Key.StartsWith("grenade:")),"both full stowed copies disappear");
    p.HandsController=new Player.BaseGrenadeHandsController{Owner=p,Item=a};
    Check(Label()=="Encore 3/3 charges","drawing a full copy still shows its held count");
});
Run("Hiding grenade statuses does not refill or stop charges", () =>
{
    var a=Copy("a");var p=Scene(a);Fire(p,a);
    Check(!StatusVisibility.Allows("grenade:a",true,true,true,true,false),"grenade toggle separate from guns");
    for(int i=0;i<100;i++)_ = ReusableGrenadeRuntime.Statuses(p).Where(s=>StatusVisibility.Allows(s.Key,true,true,true,true,false)).ToArray();
    Check(ReusableGrenadeRuntime.Remaining(a)==2,"presentation does not mutate charges");Fire(p,a);Check(ReusableGrenadeRuntime.Remaining(a)==1,"hidden gameplay continues");
});
Run("Inspect descriptions refresh the exact copy's counter without duplicate blocks", () =>
{
    var a=Copy("a");var b=Copy("b");var p=Scene(a,b);var initial=ItemDescriptionPresentation.Compose(a,definition.EffectText+"\n\n"+definition.Description);
    Check(initial.Contains("Charges this raid: 3/3"),"full copy counter");Fire(p,a);
    var updated=ItemDescriptionPresentation.Compose(a,initial);
    Check(updated.Contains("Charges this raid: 2/3")&&!updated.Contains("Charges this raid: 3/3"),"old count replaced");
    Check(updated.Split("Charges this raid:").Length==2&&updated.Split("LEGENDARY").Length==2,"one counter and one bonus block");
    Check(ItemDescriptionPresentation.Compose(a,updated)==updated,"idempotent composition");
    Check(ItemDescriptionPresentation.Compose(b,definition.Description).Contains("Charges this raid: 3/3"),"second copy not changed");
});
Run("Only the single native factory item read receives a proxy adapter", () =>
{
    ReusableGrenadeRuntime.FactoryHookReady=false;var getter=typeof(TestGetter).GetProperty("Item")!.GetMethod!;
    var input=new[]{new CodeInstruction(OpCodes.Nop,null),new CodeInstruction(OpCodes.Callvirt,getter),new CodeInstruction(OpCodes.Ret,null)};
    var output=Transpile(input).ToArray();Check(output.Length==4&&ReusableGrenadeRuntime.FactoryHookReady,"one inserted adapter");
    Check(output[2].operand is MethodInfo m&&m.Name==nameof(ReusableGrenadeRuntime.ProjectileItem),"correct method");
    Check(ReferenceEquals(output[1],input[1])&&ReferenceEquals(output[3],input[2]),"native instructions retained");
});
Run("Missing or duplicate native factory item reads reject the transpiler", () =>
{
    var getter=typeof(TestGetter).GetProperty("Item")!.GetMethod!;
    foreach(var n in new[]{0,2})
    {
        ReusableGrenadeRuntime.FactoryHookReady=false;var rejected=false;
        try{_ = Transpile(Enumerable.Range(0,n).Select(_=>new CodeInstruction(OpCodes.Callvirt,getter)).ToArray()).ToArray();}catch(InvalidOperationException){rejected=true;}
        Check(rejected&&!ReusableGrenadeRuntime.FactoryHookReady,"guard rejects changed native layout");
    }
});
Run("Reservations prevent two controllers spending the same charge concurrently", () =>
{
    var ledger=new GrenadeChargeLedger();var successes=0;Parallel.For(0,100,i=>{if(ledger.Reserve("same-copy",3,"token-"+i))Interlocked.Increment(ref successes);});
    Check(successes==1&&ledger.Remaining("same-copy",3)==3,"one reservation, charge only on success");
});
Run("Cancelled, duplicate, stale and foreign completions cannot spend charges", () =>
{
    var l=new GrenadeChargeLedger();Check(l.Reserve("a",3,"one"),"reserve");Check(!l.Finish("a","foreign",true),"foreign completion rejected");
    Check(l.Finish("a","one",false)&&l.Remaining("a",3)==3,"cancel free");Check(!l.Finish("a","one",true),"late duplicate ignored");
    Check(l.Reserve("a",3,"old-raid"),"reserve old raid");l.Reset();Check(!l.Finish("a","old-raid",true),"old raid completion ignored");
    Check(l.Remaining("a",3)==3,"new raid remains full");
});
Run("Ledger capacity cannot be raised by resolving the same ID with another capacity", () =>
{
    var l=new GrenadeChargeLedger();Check(l.Remaining("a",3)==3,"initial capacity");Check(l.Reserve("a",99,"x")&&l.Finish("a","x",true),"one throw");
    Check(l.Remaining("a",99)==2,"capacity is per item for this raid");
});
Run("Thousands of successful and failed reservations never overspend or share charges", () =>
{
    var l=new GrenadeChargeLedger();for(int copy=0;copy<300;copy++)
    {
        var id="copy-"+copy;for(int n=0;n<3;n++)
        {
            Check(l.Reserve(id,3,"cancel")&&l.Finish(id,"cancel",false),"cancelled release");
            Check(l.Remaining(id,3)==3-n,"cancel preserves count");
            Check(l.Reserve(id,3,"use")&&l.Finish(id,"use",true),"successful release");
            Check(!l.Finish(id,"use",true),"duplicate completion");
        }
        Check(l.Remaining(id,3)==0&&!l.Reserve(id,3,"extra"),"empty stays empty");
    }
});
Run("Client and server agree on the grenade identity, charges and prototype spawn policy", () =>
{
    var server=S.LegendaryCatalog.Load().Variants.Single(d=>d.RuntimeKey=="GR26");
    Check(server.TemplateId==definition.TemplateId&&server.GrenadeChargesPerRaid==3,"same catalog identity and capacity");
    Check(server.AffixCount==4&&server.Tier=="Legendary"&&server.BackgroundColor=="orange","legendary rules");
    Check(server.SpawnEnabled&&server.SpawnWeight==1,"grenade enabled for its own loot pool");
    Check(server.LocaleDescription.StartsWith("<color=#66D9EF>"+definition.EffectText),"server fallback shows actual signature");
});
Run("The retired Prapor offer is removed while unrelated stock is preserved", () =>
{
    var d=S.LegendaryCatalog.Load().Variants.Single(v=>v.RuntimeKey=="GR26");
    var offer=S.ReusableGrenadeTestOffer.OfferId(d.TemplateId);
    var other=new SPTarkov.Server.Core.Models.Eft.Common.Tables.Item { Id=new("aaaaaaaaaaaaaaaaaaaaaaaa"),Template=new("bbbbbbbbbbbbbbbbbbbbbbbb") };
    var a=new SPTarkov.Server.Core.Models.Eft.Common.Tables.TraderAssort {
        Items=[other,new(){Id=offer,Template=new(d.TemplateId)}],
        BarterScheme=new(){{offer,[]}},LoyalLevelItems=new(){{offer,1}} };
    S.ReusableGrenadeTestOffer.Remove(a,d);S.ReusableGrenadeTestOffer.Remove(a,d);
    Check(a.Items.Count==1&&ReferenceEquals(a.Items[0],other)&&a.BarterScheme.Count==0&&a.LoyalLevelItems.Count==0,"only prototype stock removed, repeat safe");
});
Run("Held-key quick selection omits empty copies while retaining normal grenades", () =>
{
    var empty=Copy("empty");var charged=Copy("charged");var normal=new ThrowWeap{Id="normal",StringTemplateId=definition.SourceTemplateId};
    var p=Scene(empty,charged,normal);Spend(p,empty,3);
    var list=InventoryExtension.GetThrowablePriorityGrenadesList(p.InventoryController);
    Check(list.SequenceEqual(new[]{charged,normal}),"empty removed from the held-key list");
    Check(p.InventoryController.Reachable.Count==3&&ReusableGrenadeRuntime.Remaining(charged)==3,"inventory and second copy preserved");
    Spend(p,charged,3);Check(InventoryExtension.GetThrowablePriorityGrenadesList(p.InventoryController).SequenceEqual(new[]{normal}),"normal still selectable when all legendaries are empty");
});
Run("Quick-selection filtering leaves cached source lists intact", () =>
{
    var a=Copy("empty");var b=Copy("full");var p=Scene(a,b);Spend(p,a,3);var cached=new List<ThrowWeap>{a,b};
    object?[] args=[cached];typeof(ReusableGrenadeQuickSelectPatch).GetMethod("Postfix",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,args);
    Check(cached.SequenceEqual(new[]{a,b})&&((List<ThrowWeap>)args[0]!).SequenceEqual(new[]{b}),"filtered copy without native-cache mutation");
});
Run("An exhausted last copy disappears from the wheel and returns next raid", () =>
{
    var a=Copy("a");var p=Scene(a);var bonuses=Bonuses(a);Spend(p,a,3);
    Check(InventoryExtension.GetThrowablePriorityGrenadesList(p.InventoryController).Count==0,"no empty wheel entry");
    ReusableGrenadeRuntime.Reset();Check(InventoryExtension.GetThrowablePriorityGrenadesList(p.InventoryController).Single()==a&&Bonuses(a)==bonuses,"new raid refills same permanent copy");
});
Run("A stale selector result cannot return an exhausted grenade", () =>
{
    var a=Copy("a");var p=Scene(a);Spend(p,a,3);object?[] args=[a];
    var hook=typeof(ReusableGrenadeSelectorResultPatch).GetMethod("Postfix",BindingFlags.Static|BindingFlags.NonPublic)!;hook.Invoke(null,args);
    Check(args[0]==null,"stale empty selection cancelled");
    var ordinary=new ThrowWeap{StringTemplateId=definition.SourceTemplateId};args=[ordinary];hook.Invoke(null,args);Check(ReferenceEquals(args[0],ordinary),"ordinary selection unchanged");
});
Run("Empty quick-slot draws return a completed failure without a pretend throw", () =>
{
    var a=Copy("a");var p=Scene(a);Spend(p,a,3);var called=0;Result<IHandsController>? result=null;
    Callback<IHandsController> done=r=>{called++;result=r;};
    Check(!PatchHooks.Prefix("ReusableGrenadeEmptyDrawPatch",a,done)&&called==1&&result!.Failed,"draw rejected before native animation");
    Check(p.InventoryController.Reachable.Contains(a)&&ReusableGrenadeRuntime.Remaining(a)==0,"retained empty item");
    var b=Copy("b");Check(PatchHooks.Prefix("ReusableGrenadeEmptyDrawPatch",b,done)&&called==1,"charged copy uses native draw");
    Check(PatchHooks.Prefix("ReusableGrenadeEmptyDrawPatch",new ThrowWeap(),done)&&called==1,"normal grenade uses native draw");
});
Run("Holstering retains the cooldown and hides only the charge counter", () =>
{
    var a=Copy("a");var p=Scene(a);Fire(p,a);p.HandsController=new Player.BaseGrenadeHandsController{Item=a,Owner=p};
    Check(ReusableGrenadeRuntime.Statuses(p).Single().Label=="Encore 2/3 charges · +1 charge in 3:00","held counter");
    p.HandsController=new Player.AbstractHandsController{Item=p.Original};Check(ReusableGrenadeRuntime.Statuses(p).Single().Label=="Encore 2/3 charges · +1 charge in 3:00","holstered partial count and countdown remain");
    Check(ReusableGrenadeRuntime.Remaining(a)==2,"holstering does not refill");
});
Run("Bot grenade-only inventory searches skip spent copies and retain all other items", () =>
{
    var a=Copy("a");var b=Copy("b");var normal=new ThrowWeap();var weapon=new Item();var p=Scene(a,b,normal);Spend(p,a,3);
    var items=new Item[]{a,weapon,b,normal};Check(ReusableGrenadeRuntime.UsableItems(items).SequenceEqual(new Item[]{weapon,b,normal}),"only empty copy skipped");
    Check(items.Length==4&&p.InventoryController.Reachable.Contains(a),"underlying inventory intact");
    var getter=typeof(BotInventorySource).GetMethod("GetPlayerItems")!;
    var code=new[]{new CodeInstruction(OpCodes.Call,getter),new CodeInstruction(OpCodes.Ret,null)};
    var output=((IEnumerable<CodeInstruction>)typeof(ReusableGrenadeBotInventoryPatch).GetMethod("Transpiler",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[code])!).ToArray();
    Check(output.Length==3&&output[1].operand is MethodInfo m&&m.Name=="UsableItems","actual bot transpiler inserts the narrow adapter");
});
Run("Grenade loot odds have independent standalone, APBS and boss percentages", () =>
{
    var d=S.LegendaryCatalog.Load().Variants.Single(v=>v.RuntimeKey=="GR26");var policy=new S.GrenadeSpawnPolicy(new[]{d});
    var c=new S.RarityConfiguration{SpawnChances=new(){["standalone"]=new(){Grenades=2,Guns=0},["apbs"]=new(){Grenades=.5,Guns=90},["bosses"]=new(){Grenades=12,Guns=0}}};
    var original=new Dictionary<string,double>{{d.SourceTemplateId,40},{"other-mod-grenade",60}};
    foreach(var (method,role,pmc,chance) in new[]{("standalone","pmcusec",true,.02),("apbs","pmcbear",true,.005),("standalone","bosskilla",false,.12)})
    {
        var pool=policy.Pool(original,c,method,role,pmc);Near((float)(pool[d.TemplateId]/pool.Values.Sum()),(float)chance,"aggregate grenade share");
        Check(pool[d.SourceTemplateId]==40&&pool["other-mod-grenade"]==60,"all original weights preserved");
    }
    Check(original.Count==2,"native/APBS input cache unchanged");
});
Run("Zero grenade chance removes imported legendaries without changing other pools", () =>
{
    var d=S.LegendaryCatalog.Load().Variants.Single(v=>v.RuntimeKey=="GR26");var policy=new S.GrenadeSpawnPolicy(new[]{d});
    var c=new S.RarityConfiguration{SpawnChances=new(){["standalone"]=new(){Grenades=0},["apbs"]=new(){Grenades=0},["bosses"]=new(){Grenades=0}}};
    var original=new Dictionary<string,double>{{d.SourceTemplateId,50},{d.TemplateId,999999},{"other",50}};
    foreach(var method in new[]{"standalone","apbs"})foreach(var boss in new[]{false,true})
    {
        var pool=policy.Pool(original,c,method,boss?"bossgluhar":"pmcusec",!boss);
        Check(!pool.ContainsKey(d.TemplateId)&&pool[d.SourceTemplateId]==50&&pool["other"]==50,"zero means zero, no extra registrations");
    }
});
Run("Filtered vanilla sources cannot produce legendary grenades", () =>
{
    var d=S.LegendaryCatalog.Load().Variants.Single(v=>v.RuntimeKey=="GR26");var policy=new S.GrenadeSpawnPolicy(new[]{d});
    var original=new Dictionary<string,double>{{"allowed-other-grenade",100},{d.TemplateId,500}};
    var c=new S.RarityConfiguration{SpawnChances=new(){["apbs"]=new(){Grenades=90}}};var pool=policy.Pool(original,c,"apbs","pmcusec",true);
    Check(pool.Count==1&&pool["allowed-other-grenade"]==100,"source-based eligibility preserves native/APBS source filtering");
});
Run("Scavs stay excluded while expanded non-PMC factions receive grenade registrations", () =>
{
    var d=S.LegendaryCatalog.Load().Variants.Single(v=>v.RuntimeKey=="GR26");var policy=new S.GrenadeSpawnPolicy(new[]{d});
    var original=new Dictionary<string,double>{{d.SourceTemplateId,100},{d.TemplateId,1}};
    foreach(var role in new[]{"assault","marksman","pmcbot","exusec","sectantwarrior","followergluharassault"})
        Check(policy.Pool(original,new(),"standalone",role,false).ContainsKey(d.TemplateId)==(role is not "assault" and not "marksman"),"expanded role eligibility: "+role);
});
Run("Spawn master switches and retired definitions disable grenade injections", () =>
{
    var d=S.LegendaryCatalog.Load().Variants.Single(v=>v.RuntimeKey=="GR26");var original=new Dictionary<string,double>{{d.SourceTemplateId,100}};
    var policy=new S.GrenadeSpawnPolicy(new[]{d});var c=new S.RarityConfiguration{AddToPmcWeapons=false,AddToBossWeapons=false};
    Check(!policy.Pool(original,c,"standalone","pmcusec",true).ContainsKey(d.TemplateId),"PMC master switch");
    Check(!policy.Pool(original,c,"apbs","bossknight",false).ContainsKey(d.TemplateId),"boss master switch");
    c=new(){Enabled=false};Check(!policy.Pool(original,c,"standalone","pmcusec",true).ContainsKey(d.TemplateId),"global switch");
    var retired=d.Copy();retired.SpawnEnabled=false;Check(!new S.GrenadeSpawnPolicy(new[]{retired}).Pool(original,new(),"standalone","pmcusec",true).ContainsKey(d.TemplateId),"inactive template");
});
Run("Adding more grenade signatures does not multiply the configured category share", () =>
{
    var d=S.LegendaryCatalog.Load().Variants.Single(v=>v.RuntimeKey=="GR26");var extra=d.Copy();extra.TemplateId="aaaaaaaaaaaaaaaaaaaaaaaa";
    var policy=new S.GrenadeSpawnPolicy(new[]{d,extra});var pool=policy.Pool(new Dictionary<string,double>{{d.SourceTemplateId,100}},new(),"standalone","pmcusec",true);
    Near((float)((pool[d.TemplateId]+pool[extra.TemplateId])/pool.Values.Sum()),.01f,"shared category budget");Near((float)pool[d.TemplateId],(float)pool[extra.TemplateId],"signature weights split the budget");
});
Run("Config migration adds independent grenade rates and preserves user settings", () =>
{
    const string json="""{"allowFleaMarketTrading":true,"priceMultipliers":{"Legendary":7.5},"customSetting":"preserve","enableReusableGrenadeTestOffer":true,"spawnChances":{"standalone":{"guns":0,"melee":3,"backpacks":4,"chestRigs":5},"apbs":{"guns":9,"melee":0,"backpacks":6,"chestRigs":7},"bosses":{"guns":8,"melee":9,"backpacks":10,"chestRigs":11}}}""";
    var c=S.RarityConfiguration.Parse(json,out var migrated,"apbs");Check(migrated!=null,"upgrade persisted");
    Near((float)c.Chance("standalone","grenades"),1,"standalone default");Near((float)c.Chance("apbs","grenades"),1,"APBS default");Near((float)c.Chance("bosses","grenades"),10,"boss default");
    Check(c.Chance("standalone","guns")==9&&c.Chance("apbs","melee")==0&&c.Chance("bosses","chestRigs")==11,"existing zeros and custom values kept");
    Check(c.AllowFleaMarketTrading&&c.PriceMultiplierFor("Legendary",10)==7.5,"existing flea and price config kept");
    var node=System.Text.Json.Nodes.JsonNode.Parse(migrated!)!;Check(node["customSetting"]!.GetValue<string>()=="preserve"&&node["enableReusableGrenadeTestOffer"]==null,"unrelated field kept, test switch removed");
    var again=S.RarityConfiguration.Parse(migrated!,out var second);Check(second==null&&again.Chance("standalone","grenades")==1,"idempotent migration");
});
Run("Existing explicit grenade zeros are preserved by migration", () =>
{
    var c=S.RarityConfiguration.Parse("""{"spawnChances":{"standalone":{"grenades":0},"apbs":{"grenades":0},"bosses":{"grenades":0}}}""",out _);
    foreach(var method in new[]{"standalone","apbs","bosses"})Check(c.Chance(method,"grenades")==0,"explicit zero "+method);
});
Run("Server registers one native grenade-pool postfix and leaves other loot untouched", () =>
{
    var d=S.LegendaryCatalog.Load().Variants.Single(v=>v.RuntimeKey=="GR26");
    var c=new S.RarityConfiguration();S.ReusableGrenadeSpawning.Install(new[]{d},c);S.ReusableGrenadeSpawning.Install(new[]{d},c);
    Check(Harmony.Registrations==1&&Harmony.Target!.Name=="GetLootFromCache","exact native target registered once");
    Check(Harmony.Postfix!.priority==Priority.Last,"source filters run before EFB injection");
    var original=new Dictionary<SPTarkov.Server.Core.Models.Common.MongoId,double>{{new(d.SourceTemplateId),100}};
    object?[] args={"pmcusec",true,"Pocket",original};Harmony.Postfix.Method.Invoke(null,args);
    Check(ReferenceEquals(args[3],original)&&original.Count==1,"non-grenade pool identity and contents intact");
});
Run("Actual server postfix converts native pools using standalone, APBS and boss settings", () =>
{
    var d=S.LegendaryCatalog.Load().Variants.Single(v=>v.RuntimeKey=="GR26");
    var c=new S.RarityConfiguration{SpawnChances=new(){["standalone"]=new(){Grenades=2},["apbs"]=new(){Grenades=.5},["bosses"]=new(){Grenades=12}}};
    S.ReusableGrenadeSpawning.Install(new[]{d},c);
    var original=new Dictionary<SPTarkov.Server.Core.Models.Common.MongoId,double>{{new(d.SourceTemplateId),100}};
    foreach(var (apbs,role,pmc,chance) in new[]{(false,"pmcusec",true,.02),(true,"pmcbear",true,.005),(true,"bosskilla",false,.12)})
    {
        S.ReusableGrenadeSpawning.SelectMode(c,apbs);object?[] args={role,pmc,"GrenadeItems",original};Harmony.Postfix!.Method.Invoke(null,args);
        var result=(Dictionary<SPTarkov.Server.Core.Models.Common.MongoId,double>)args[3]!;
        Near((float)(result[new(d.TemplateId)]/result.Values.Sum()),(float)chance,"native MongoId pool percentage");
        Check(!ReferenceEquals(result,original)&&original.Count==1,"native cache remains unchanged");
    }
    c.SpawnChances!["apbs"].Grenades=0;S.ReusableGrenadeSpawning.SelectMode(c,true);
    object?[] zeroArgs={"pmcusec",true,"GrenadeItems",original};Harmony.Postfix!.Method.Invoke(null,zeroArgs);
    Check(((Dictionary<SPTarkov.Server.Core.Models.Common.MongoId,double>)zeroArgs[3]!).Count==1,"zero propagates through actual postfix");
});
GrenadeFeatureTests.Run(Run, Check, Near);
QuestAdapterTests.Run(Run, Check);
DrawAudioTests.Run(Run, Check, Near);
UseItemsAnywhereTests.Run(Run, Check);
Console.WriteLine($"Reusable grenade results: {cases} cases, {assertions} assertions, {failures} failures.");
return failures==0?0:1;

public class TestGetter { public ThrowWeap Item => new(); }
public class TestExplosive : IExplosiveItem
{
    public Ammo Fragment = new();
    public Vector3 Blindness => new(0,0,0); public Vector3 Contusion => new(1.5f,4,18);
    public Vector3 ArmorDistanceDistanceDamage => new(1,4,28);
    public float MinExplosionDistance => 2; public float MaxExplosionDistance => 5;
    public int FragmentsCount => 75; public float GetStrength => 100; public bool IsDummy => false;
    public Ammo CreateFragment() => Fragment;
}
