using System.Collections;
using System.Text.Json.Nodes;
using EscapeFromBorderlands.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

int cases=0,assertions=0,failures=0;
void Check(bool value,string why){assertions++;if(!value)throw new Exception(why);}
void Near(double value,double expected,string why)=>Check(Math.Abs(value-expected)<1e-8,$"{why}: {value} != {expected}");
void Run(string name,Action test){cases++;try{test();Console.WriteLine("PASS "+name);}catch(Exception e){failures++;Console.WriteLine("FAIL "+name+": "+(e.InnerException??e));}}
var catalog=LegendaryCatalog.Load();
var allIds=catalog.Variants.Select(v=>v.TemplateId).Concat(catalog.LegacyAliases.Select(v=>v.TemplateId)).ToHashSet();
string Category(VariantDefinition v)=>v.IsEarpiece?"earpieces":v.GrenadeChargesPerRaid>0?"grenades":v.MeleeEffect!=null?"melee":v.BackpackEffect!=null?"backpacks":v.ChestRigEffect!=null?"chestRigs":"guns";
var active=catalog.Variants.Where(v=>v.SpawnEnabled&&!v.IsMysteryFleaOffer).ToArray();
var selected=new[]{active.First(v=>Category(v)=="guns"&&v.WeaponClass=="assaultRifle"),active.First(v=>Category(v)=="melee"),active.First(v=>Category(v)=="backpacks"),active.First(v=>Category(v)=="chestRigs"&&v.ChestRigEffect!.SoftArmorClass==0),active.First(v=>v.IsEarpiece)};
EquipmentSlots Slot(VariantDefinition v)=>Category(v) switch{"earpieces"=>EquipmentSlots.Earpiece,"melee"=>EquipmentSlots.Scabbard,"backpacks"=>EquipmentSlots.Backpack,"chestRigs"=>EquipmentSlots.TacticalVest,_=>EquipmentSlots.FirstPrimaryWeapon};
var roles=new[]{"pmcusec","pmcbear","pmcbot","exusec","followerbully","followerbigpipe","sectantwarrior","sectantpriest","arenafighter","arenafighterevent","peacemaker","bossbully"};
var excluded=new[]{"assault","marksman","cursedassault","assaultgroup","crazyassaultevent","gifter","infectedassault","infectedpmc","infectedtagilla","shooterbtr","test","bosstest","followertest","unrecognizedRole"};
var other=new MongoId("fffffffffffffffffffffffe");
Dictionary<EquipmentSlots,Dictionary<MongoId,double>> Equipment()=>selected.ToDictionary(Slot,v=>new Dictionary<MongoId,double>{{new(v.SourceTemplateId),100},{other,200}});
void Configure(RarityConfiguration config,bool imports=true)
{
    FactionEquipmentSpawning.Configure(catalog.Variants,catalog.LegacyAliases.Select(v=>v.TemplateId),config);
    if(imports)foreach(var category in new[]{"guns","backpacks","chestRigs","earpieces"})FactionEquipmentSpawning.RememberImported(3,category,active.Where(v=>Category(v)==category).Select(v=>v.TemplateId));
}
double Share(Dictionary<MongoId,double> pool)=>pool.Where(p=>allIds.Contains(p.Key.ToString())).Sum(p=>p.Value)/pool.Values.Sum();
StandaloneFixture Standalone(params string[] botRoles)
{
    var fixture=new StandaloneFixture();fixture.Definitions(selected.Select(v=>(v,new[]{Slot(v)})));
    foreach(var role in botRoles)
    {
        var bot=new BotType{BotInventory=new(){Equipment=Equipment(),Mods=new()}};
        foreach(var v in selected.Where(v=>Category(v) is "guns" or "chestRigs"))bot.BotInventory.Mods[new(v.SourceTemplateId)]=new(){{"test_slot",new(){other}}};
        fixture.Bots[role]=bot;
    }
    return fixture;
}

Run("Every eligible 4.0.13 native role has a spawn group and Scavs/special roles are excluded",()=>
{
    foreach(var role in Enum.GetNames<WildSpawnType>())
    {
        var name=role.ToLowerInvariant();var blocked=excluded.Contains(name)||name.StartsWith("infected");
        Check((BotSpawnGroups.Group(role)==null)==blocked,"complete native role mapping: "+role);
    }
    foreach(var role in excluded)Check(BotSpawnGroups.Group(role)==null,"excluded: "+role);
    Check(BotSpawnGroups.Group("EXUSEC")=="rogues"&&BotSpawnGroups.Group("pmcBot")=="raiders","role aliases and case");
});
Run("Faction defaults are independent 1-percent categories while bosses remain 10 percent",()=>
{
    var config=RarityConfiguration.Parse("{}",out var migrated);
    foreach(var method in new[]{"standalone","apbs"})foreach(var role in roles)foreach(var category in new[]{"guns","melee","backpacks","chestRigs","grenades","earpieces"})Near(config.BotFraction(method,role,category),role=="bossbully"?.1:.01,"role default "+role);
    var node=JsonNode.Parse(migrated!)!;Check(node["factionSpawnChances"]!["followers"]!["grenades"]!.GetValue<double>()==1,"new settings persisted");
});
Run("Migration preserves old rates, zeroes, flea/pricing settings and unrelated config",()=>
{
    var config=RarityConfiguration.Parse("""{"addToPmcWeapons":false,"allowFleaMarketTrading":true,"priceMultipliers":{"Legendary":7},"customField":"keep","apbsPmcLegendaryChancePercent":0.25,"spawnChances":{"apbs":{"guns":0,"grenades":0.7},"bosses":{"guns":17}},"factionSpawnChances":{"apbs":{"rogues":{"guns":0,"grenades":4},"raiders":{"guns":3}}}}""",out var migrated,"apbs");
    Near(config.Chance("apbs","guns"),0,"explicit PMC zero");Near(config.Chance("apbs","melee"),.25,"legacy PMC rate");Near(config.Chance("bosses","guns"),17,"boss setting");
    Near(config.BotFraction("apbs","pmcusec","grenades"),0,"PMC toggle");Near(config.BotFraction("apbs","exusec","guns"),0,"Rogue zero");Near(config.BotFraction("apbs","exusec","grenades"),.04,"Rogue grenade rate");Near(config.BotFraction("apbs","pmcbot","guns"),.03,"Raider gun rate");Near(config.BotFraction("standalone","pmcbot","guns"),.03,"shared migrated method");
    Check(config.AllowFleaMarketTrading&&config.PriceMultiplierFor("Legendary",10)==7,"flea and price preserved");Check(JsonNode.Parse(migrated!)!["customField"]!.GetValue<string>()=="keep","unknown field preserved");
    var again=RarityConfiguration.Parse(migrated!,out var repeated);Check(repeated==null&&again.FactionChance("apbs","rogues","guns")==0,"migration idempotent");
});
Run("New faction switch does not alter PMC or boss switches",()=>
{
    var c=new RarityConfiguration{AddToNonScavFactions=false};foreach(var role in roles.Where(r=>r is not "pmcusec" and not "pmcbear" and not "bossbully"))Near(c.BotFraction("apbs",role,"guns"),0,"new groups disabled");
    Near(c.BotFraction("apbs","bossbully","guns"),.1,"boss unaffected");Near(c.BotFraction("apbs","pmcbear","guns"),.01,"PMC unaffected");
    c.AddToBossWeapons=false;c.AddToPmcWeapons=false;c.AddToNonScavFactions=true;Near(c.BotFraction("apbs","exusec","guns"),.01,"new group independent of PMC/boss toggles");c.Enabled=false;Near(c.BotFraction("apbs","exusec","guns"),0,"global disabled");
});
Run("Faction rates are case-insensitive, clamped, and optional PMC-side overrides inherit missing values",()=>
{
    var c=RarityConfiguration.Parse("""{"spawnChances":{"pmcs":{"guns":5}},"factionSpawnChances":{"RoGuEs":{"guns":120,"melee":-3},"pmcUsec":{"guns":0}}}""",out _);
    Near(c.BotFraction("apbs","exusec","guns"),.99,"clamped high");Near(c.BotFraction("apbs","exusec","melee"),0,"clamped low");Near(c.BotFraction("apbs","pmcusec","guns"),0,"USEC override");Near(c.BotFraction("apbs","pmcbear","guns"),.05,"BEAR inherits existing PMC setting");
    c.FactionSpawnChances!["shared"]["RoGuEs"].Guns=double.NaN;Near(c.BotFraction("apbs","exusec","guns"),.01,"invalid rate fallback");
});
Run("Standalone production adapter adds every category to every eligible group with natural mod pools",()=>
{
    var f=Standalone(roles);var c=new RarityConfiguration();Check(f.Apply(c)>0,"entries added");
    foreach(var role in roles)foreach(var v in selected)
    {
        var bot=f.Bots[role];var pool=bot.BotInventory.Equipment[Slot(v)];Near(Share(pool),role=="bossbully"?.1:.01,"combined share: "+role+" "+Category(v));Near(pool[other],200,"other mod unchanged");
        if(Category(v) is "guns" or "chestRigs")Check(!ReferenceEquals(bot.BotInventory.Mods[new(v.TemplateId)]["test_slot"],bot.BotInventory.Mods[new(v.SourceTemplateId)]["test_slot"]),"compatible mod pools independently copied");
    }
});
Run("Standalone strips stale EFB from excluded Scavs without changing their native weights",()=>
{
    var f=Standalone(excluded);foreach(var bot in f.Bots.Values)foreach(var v in selected)bot.BotInventory.Equipment[Slot(v)][new(v.TemplateId)]=500;
    f.Apply(new());foreach(var bot in f.Bots.Values)foreach(var pool in bot.BotInventory.Equipment.Values){Near(Share(pool),0,"no legendary Scav entries");Near(pool[other],200,"native weights preserved");}
});
Run("Standalone natural-source and firearm-mod requirements reject incompatible replacements",()=>
{
    var f=Standalone("pmcbot","exusec");var gun=selected[0];f.Bots["pmcbot"].BotInventory.Mods.Remove(new(gun.SourceTemplateId));f.Bots["exusec"].BotInventory.Equipment[Slot(gun)].Remove(new(gun.SourceTemplateId));
    f.Apply(new());foreach(var bot in f.Bots.Values)Near(Share(bot.BotInventory.Equipment[Slot(gun)]),0,"missing source/mod pool blocks gun");
});
Run("APBS-mode native fallback uses shared rates and never registers standalone PMCs",()=>
{
    var c=RarityConfiguration.Parse("""{"factionSpawnChances":{"raiders":{"guns":4}}}""",out _);var f=Standalone("pmcusec","pmcbot","bossbully","assault");f.Apply(c,"apbs",true);
    Near(Share(f.Bots["pmcusec"].BotInventory.Equipment[EquipmentSlots.FirstPrimaryWeapon]),0,"no duplicate PMC injection");Near(Share(f.Bots["pmcbot"].BotInventory.Equipment[EquipmentSlots.FirstPrimaryWeapon]),.04,"APBS fallback rate");Near(Share(f.Bots["bossbully"].BotInventory.Equipment[EquipmentSlots.FirstPrimaryWeapon]),.1,"boss fallback");Near(Share(f.Bots["assault"].BotInventory.Equipment[EquipmentSlots.FirstPrimaryWeapon]),0,"no Scavs");
    f.Apply(c,"apbs",true);Near(Share(f.Bots["pmcbot"].BotInventory.Equipment[EquipmentSlots.FirstPrimaryWeapon]),.04,"repeat normalization does not inflate weights");
});
Run("APBS role hook separates Rogues and Arena fighters sharing the same dataset",()=>
{
    var c=RarityConfiguration.Parse("""{"factionSpawnChances":{"rogues":{"guns":3},"arenaFighters":{"guns":7}}}""",out _);Configure(c);var helper=new ApbsEquipmentFixture{Shared=Equipment()};FactionEquipmentSpawning.InstallApbs(typeof(ApbsEquipmentFixture));FactionEquipmentSpawning.InstallApbs(typeof(ApbsEquipmentFixture));
    var rogue=helper.GetEquipmentByBotRole("exusec",3);var arena=helper.GetEquipmentByBotRole("arenafighterevent",3);Near(Share(rogue[EquipmentSlots.FirstPrimaryWeapon]),.03,"Rogue rate");Near(Share(arena[EquipmentSlots.FirstPrimaryWeapon]),.07,"Arena rate");Near(Share(helper.Shared[EquipmentSlots.FirstPrimaryWeapon]),0,"shared source dataset unchanged");
});
Run("APBS generic Default dataset can serve followers and Scavs without leaking EFB",()=>
{
    Configure(new());var helper=new ApbsEquipmentFixture{Shared=Equipment()};foreach(var role in roles.Where(r=>r is not "pmcusec" and not "pmcbear"))foreach(var pool in helper.GetEquipmentByBotRole(role,3).Values)Near(Share(pool),role=="bossbully"?.1:.01,"independent role/category");
    foreach(var role in excluded)foreach(var pool in helper.GetEquipmentByBotRole(role,3).Values)Near(Share(pool),0,"excluded role stays native");
});
Run("APBS 2.2 copied returns and 2.0 shared returns both receive isolated role dictionaries",()=>
{
    Configure(new());foreach(var cloned in new[]{false,true})
    {
        var h=new ApbsEquipmentFixture{Shared=Equipment(),CloneBeforeReturn=cloned};var result=h.GetEquipmentByBotRole("exusec",3);
        Check(!ReferenceEquals(result,h.Shared)&&!ReferenceEquals(result[EquipmentSlots.Backpack],h.Shared[EquipmentSlots.Backpack]),"outer and inner dictionaries owned");
        Near(Share(result[EquipmentSlots.Backpack]),.01,"returned role rate");Near(Share(h.Shared[EquipmentSlots.Backpack]),0,"original dataset intact");
    }
});
Run("APBS imported clone filters, vanilla source filters, and zero PMC settings stay independent",()=>
{
    var c=new RarityConfiguration{AddToPmcWeapons=false};Configure(c,false);var gun=selected[0];FactionEquipmentSpawning.RememberImported(3,"guns",new[]{gun.TemplateId});var helper=new ApbsEquipmentFixture{Shared=Equipment()};var result=helper.GetEquipmentByBotRole("pmcbot",3);
    Check(result[Slot(gun)].Keys.Where(k=>allIds.Contains(k.ToString())).Select(k=>k.ToString()).SequenceEqual(new[]{gun.TemplateId}),"only explicitly imported clone allowed");Near(Share(result[Slot(gun)]),.01,"Raider independent of disabled PMC");
    Check(result[EquipmentSlots.Backpack].Keys.All(k=>!allIds.Contains(k.ToString())),"unimported equipment not resurrected");helper.Shared[Slot(gun)].Remove(new(gun.SourceTemplateId));Near(Share(helper.GetEquipmentByBotRole("pmcbot",3)[Slot(gun)]),0,"filtered base cannot produce any clone");
});
Run("APBS tier-specific filtering and repeated queries remain stable",()=>
{
    Configure(new());var h=new ApbsEquipmentFixture{Shared=Equipment()};Near(Share(h.GetEquipmentByBotRole("exusec",2)[EquipmentSlots.FirstPrimaryWeapon]),0,"different tier cannot inherit allowed import IDs");
    var first=h.GetEquipmentByBotRole("exusec",3);for(var i=0;i<5;i++)Near(Share(h.GetEquipmentByBotRole("exusec",3)[EquipmentSlots.FirstPrimaryWeapon]),Share(first[EquipmentSlots.FirstPrimaryWeapon]),"repeat stable");
    FactionEquipmentSpawning.Apply(first,"exusec",3);Near(Share(first[EquipmentSlots.FirstPrimaryWeapon]),.01,"already-normalized copy cannot double-inject");
});
Run("APBS role queries remove aliases, wrong-category stock and disallowed equipment slots",()=>
{
    Configure(new());var h=new ApbsEquipmentFixture{Shared=Equipment()};var gun=selected[0];h.Shared[EquipmentSlots.FirstPrimaryWeapon][new(selected[2].TemplateId)]=99;h.Shared[EquipmentSlots.Pockets]=new(){{new(gun.TemplateId),99},{other,20}};
    if(catalog.LegacyAliases.Count>0)h.Shared[EquipmentSlots.FirstPrimaryWeapon][new(catalog.LegacyAliases[0].TemplateId)]=99;
    var result=h.GetEquipmentByBotRole("followerbully",3);Check(!result[EquipmentSlots.FirstPrimaryWeapon].ContainsKey(new(selected[2].TemplateId)),"no backpack in weapon pool");Check(result[EquipmentSlots.Pockets].Count==1&&result[EquipmentSlots.Pockets][other]==20,"unsupported slot purged only of EFB");
});
Run("APBS per-faction category zeroes strip old imports and preserve other categories",()=>
{
    var c=RarityConfiguration.Parse("""{"factionSpawnChances":{"followers":{"guns":0,"backpacks":4}}}""",out _);Configure(c);var h=new ApbsEquipmentFixture{Shared=Equipment()};h.Shared[EquipmentSlots.FirstPrimaryWeapon][new(selected[0].TemplateId)]=77;var result=h.GetEquipmentByBotRole("followerbigpipe",3);
    Near(Share(result[EquipmentSlots.FirstPrimaryWeapon]),0,"gun zero removes stale weight");Near(Share(result[EquipmentSlots.Backpack]),.04,"bags independent");Near(Share(result[EquipmentSlots.Scabbard]),.01,"melee independent");
});
Run("Grenade policy covers all groups and cannot admit Scavs through an isPmc flag",()=>
{
    var defs=active.Where(v=>v.GrenadeChargesPerRaid>0).ToArray();var policy=new GrenadeSpawnPolicy(defs);var originals=defs.Select(v=>v.SourceTemplateId).Distinct().ToDictionary(id=>id,_=>10d);var c=new RarityConfiguration();
    foreach(var role in roles.Concat(excluded))
    {
        var pool=policy.Pool(originals,c,"apbs",role,true);var share=pool.Where(p=>allIds.Contains(p.Key)).Sum(p=>p.Value)/pool.Values.Sum();Near(share,excluded.Contains(role)?0:role=="bossbully"?.1:.01,"role grenade share: "+role);
    }
    Check(originals.Keys.All(k=>!allIds.Contains(k)),"native cache input unchanged");
});
Run("Actual Harmony APBS loot-cache adapter applies faction grenade rates once",()=>
{
    var defs=active.Where(v=>v.GrenadeChargesPerRaid>0).ToArray();var c=RarityConfiguration.Parse("""{"factionSpawnChances":{"rogues":{"grenades":6}}}""",out _);
    ReusableGrenadeSpawning.Install(defs,c);ReusableGrenadeSpawning.SelectMode(c,true);ReusableGrenadeSpawning.InstallApbsCache(typeof(ApbsLootFixture));ReusableGrenadeSpawning.InstallApbsCache(typeof(ApbsLootFixture));
    var cache=new ApbsLootFixture{Shared=defs.Select(v=>v.SourceTemplateId).Distinct().ToDictionary(id=>new MongoId(id),_=>10d)};
    Near(Share(cache.GetLootFromCache("exusec",false,"GrenadeItems")),.06,"actual optional hook and argument names");Near(Share(cache.GetLootFromCache("assault",true,"GrenadeItems")),0,"Scav remains excluded");Near(Share(cache.Shared),0,"cached source unchanged");
    Check(cache.GetLootFromCache("exusec",false,"BackpackLoot").SequenceEqual(cache.Shared),"other loot types untouched");
});
Run("Grenade source exclusions and zero faction rates remove all stale clones",()=>
{
    var defs=active.Where(v=>v.GrenadeChargesPerRaid>0).ToArray();var source=defs.First().SourceTemplateId;var input=new Dictionary<string,double>{{source,100},{defs.First().TemplateId,400}};var cfg=RarityConfiguration.Parse("""{"factionSpawnChances":{"raiders":{"grenades":0}}}""",out _);var p=new GrenadeSpawnPolicy(defs);
    Check(p.Pool(input,cfg,"apbs","pmcbot",false).Keys.All(k=>!allIds.Contains(k)),"explicit zero strips all old EFB entries");
    input.Remove(source);Check(p.Pool(input,new(),"apbs","exusec",false).Count==0,"no filtered native source resurrected");
});
Run("Earpiece config migration preserves explicit rates and creates independent shared defaults",()=>{
 var cfg=RarityConfiguration.Parse("""{"spawnChances":{"pmcs":{"guns":0,"earpieces":0.7},"bosses":{"earpieces":3}},"factionSpawnChances":{"rogues":{"earpieces":0}},"custom":"keep"}""",out var migrated);
 foreach(var method in new[]{"standalone","apbs"}){Near(cfg.BotFraction(method,"pmcusec","earpieces"),.007,"same PMC rate for both backends");Near(cfg.BotFraction(method,"bossbully","earpieces"),.03,"explicit boss rate");Near(cfg.BotFraction(method,"exusec","earpieces"),0,"explicit Rogue zero");Near(cfg.BotFraction(method,"pmcbot","earpieces"),.01,"new independent faction default");}
 foreach(var role in excluded)Near(cfg.BotFraction("apbs",role,"earpieces"),0,"Scavs remain excluded");Check(System.Text.Json.Nodes.JsonNode.Parse(migrated!)!["custom"]!.GetValue<string>()=="keep","unrelated setting preserved");
 Check(RarityConfiguration.Category("Earpiece")=="earpieces","native slot category");
});
Run("Earpiece clones preserve one-sided helmet conflicts without changing base statistics",()=>{
 var def=active.First(v=>v.IsEarpiece);var src=new MongoId(def.SourceTemplateId);var clone=new MongoId(def.TemplateId);var helmet=new MongoId("fffffffffffffffffffffffd");
 var items=new Dictionary<MongoId,TemplateItem>{[src]=new(){Id=src,Properties=new(){Weight=.7,ConflictingItems=[helmet]}},[clone]=new(){Id=clone,Properties=new(){Weight=.7,ConflictingItems=[helmet]}},[helmet]=new(){Id=helmet,Properties=new(){ConflictingItems=[src]}}};
 EarpieceCompatibility.Apply(new[]{def},items);Check(items[helmet].Properties!.ConflictingItems!.Contains(clone),"helmet conflict extends to additive clone");Check(items[clone].Properties!.ConflictingItems!.Contains(helmet),"clone preserves original helmet restriction");Near(items[src].Properties!.Weight!.Value,.7,"native weight unchanged");Check(items[src].Properties!.ConflictingItems!.Count==1,"unrelated source conflict list unchanged");var count=items[helmet].Properties!.ConflictingItems!.Count;EarpieceCompatibility.Apply(new[]{def},items);Check(items[helmet].Properties!.ConflictingItems!.Count==count,"idempotent compatibility pass");
});
SharedConfigTests.Run(Run,Check,Near);
AirdropTests.Run(Run,Check,Near,catalog);
Console.WriteLine($"Faction spawn results: {cases} cases, {assertions} assertions, {failures} failures.");
return failures==0?0:1;
