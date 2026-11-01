using System.Reflection;
using System.Text.Json.Nodes;
using EscapeFromBorderlands.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;

internal static class SharedConfigTests
{
    private const string Old = """{"autoDetectApbs":true,"spawnChances":{"standalone":{"guns":7,"melee":8,"grenades":9},"apbs":{"guns":0,"melee":3,"grenades":4},"bosses":{"guns":17,"grenades":0}},"factionSpawnChances":{"standalone":{"rogues":{"guns":6,"grenades":5}},"apbs":{"rogues":{"guns":0,"grenades":2}}}}""";
    private static readonly string[] Categories=["guns","melee","backpacks","chestRigs","grenades"];
    internal static void Run(Action<string,Action> run,Action<bool,string> check,Action<double,double,string> near)
    {
        run("Shared default config exposes only PMC/boss rates and flat faction categories",()=>
        {
            var json=RarityConfiguration.DefaultJson();var n=JsonNode.Parse(json)!;
            check(n["spawnChances"]!["pmcs"]!=null&&n["spawnChances"]!["bosses"]!=null,"shared rate blocks");
            check(n["spawnChances"]!["standalone"]==null&&n["spawnChances"]!["apbs"]==null,"no duplicate backend settings");
            check(n["factionSpawnChances"]!["standalone"]==null&&n["factionSpawnChances"]!["apbs"]==null&&n["factionSpawnChances"]!["shared"]==null,"flat faction config");
            foreach(var key in new[]{"standalonePmcLegendaryChancePercent","apbsPmcLegendaryChancePercent","pmcLegendaryWeaponChancePercent","bossLegendaryChancePercent"})check(n[key]==null,"obsolete scalar absent: "+key);
            foreach(var group in BotSpawnGroups.Expanded)foreach(var cat in Categories)near(n["factionSpawnChances"]![group]![cat]!.GetValue<double>(),1,"shared default");
        });
        run("One saved set drives both native and APBS rates for every category and faction",()=>
        {
            var c=RarityConfiguration.Parse("""{"spawnChances":{"pmcs":{"guns":4,"melee":0,"backpacks":3,"chestRigs":2,"grenades":7},"bosses":{"guns":19}},"factionSpawnChances":{"raiders":{"guns":11,"grenades":0},"rogues":{"guns":5},"pmcUsec":{"guns":0}}}""",out _);
            foreach(var role in new[]{"pmcusec","pmcbear","bossbully","pmcbot","exusec","followerbully","sectantwarrior","arenafighterevent","peacemaker","assault","marksman"})
                foreach(var cat in Categories)near(c.BotFraction("apbs",role,cat),c.BotFraction("standalone",role,cat),"identical backend rate: "+role+" "+cat);
            near(c.BotFraction("apbs","pmcusec","guns"),0,"PMC side override");near(c.BotFraction("standalone","pmcbear","guns"),.04,"shared PMC rate");near(c.BotFraction("apbs","bossbully","guns"),.19,"boss independent");
        });
        run("Legacy APBS migration keeps active custom values and explicit zeroes",()=>
        {
            var c=RarityConfiguration.Parse(Old,out var saved,"apbs");
            near(c.Chance("standalone","guns"),0,"active gun zero");near(c.Chance("apbs","melee"),3,"active melee setting");near(c.Chance("standalone","grenades"),4,"shared grenade setting");
            near(c.BotFraction("standalone","exusec","guns"),0,"active faction zero");near(c.BotFraction("apbs","exusec","grenades"),.02,"active faction rate");near(c.Chance("bosses","guns"),17,"boss unchanged");
            var n=JsonNode.Parse(saved!)!;check(n["spawnChances"]!["apbs"]==null&&n["spawnChances"]!["standalone"]==null,"legacy PMC options removed");check(n["factionSpawnChances"]!["rogues"]!["grenades"]!.GetValue<double>()==2,"faction flattened");
        });
        run("Legacy standalone migration selects native rates rather than APBS values",()=>
        {
            var c=RarityConfiguration.Parse(Old,out _,"standalone");near(c.Chance("apbs","guns"),7,"native gun choice");near(c.Chance("standalone","grenades"),9,"native grenade choice");near(c.BotFraction("apbs","exusec","guns"),.06,"native faction choice");near(c.BotFraction("standalone","exusec","grenades"),.05,"native faction grenade choice");
        });
        run("Initial config load defers ambiguous legacy writes until backend detection",()=>
        {
            var c=RarityConfiguration.Parse(Old,out var saved);check(saved==null&&c.LegacySpawnSettingsPending,"pending rates leave original bytes untouched");near(c.Chance("standalone","guns"),7,"legacy standalone readable during startup");near(c.Chance("apbs","guns"),0,"legacy APBS readable during startup");
        });
        run("Shared settings are stable across future backend changes and repeated loads",()=>
        {
            RarityConfiguration.Parse(Old,out var saved,"apbs");var a=RarityConfiguration.Parse(saved!,out var rewrite,"standalone");check(rewrite==null&&!a.LegacySpawnSettingsPending,"no second migration on backend switch");near(a.Chance("standalone","grenades"),4,"saved shared value retained");
            var b=RarityConfiguration.Parse(saved!,out rewrite,"apbs");check(rewrite==null,"idempotent APBS load");near(a.FactionChance("standalone","rogues","grenades"),b.FactionChance("apbs","rogues","grenades"),"same faction config");
        });
        run("Explicit new values win over legacy options while missing fields use active saved values",()=>
        {
            var c=RarityConfiguration.Parse("""{"spawnChances":{"pmcs":{"guns":12},"apbs":{"guns":1,"melee":0,"grenades":3}},"factionSpawnChances":{"rogues":{"guns":9},"apbs":{"rogues":{"guns":2,"grenades":0}}}}""",out var saved,"apbs");
            near(c.Chance("standalone","guns"),12,"new explicit PMC value wins");near(c.Chance("apbs","melee"),0,"missing active zero retained");near(c.BotFraction("apbs","exusec","guns"),.09,"new flat faction value wins");near(c.BotFraction("standalone","exusec","grenades"),0,"partial faction falls back to active zero");check(JsonNode.Parse(saved!)!["factionSpawnChances"]!["apbs"]==null,"old wrapper removed");
        });
        run("Older scalar rate configs migrate to a single set without restoring inactive defaults",()=>
        {
            const string json="""{"pmcLegendaryWeaponChancePercent":2,"standalonePmcLegendaryChancePercent":8,"apbsPmcLegendaryChancePercent":0.25,"bossLegendaryChancePercent":13}""";
            var a=RarityConfiguration.Parse(json,out var saved,"apbs");near(a.Chance("standalone","guns"),.25,"old APBS scalar retained");near(a.Chance("apbs","melee"),.25,"melee migration");near(a.Chance("standalone","backpacks"),1,"later category default");near(a.Chance("bosses","guns"),13,"boss legacy scalar retained");
            var b=RarityConfiguration.Parse(json,out _,"standalone");near(b.Chance("apbs","guns"),2,"oldest explicit native scalar wins");check(JsonNode.Parse(saved!)!["apbsPmcLegendaryChancePercent"]==null,"old scalar option removed");
        });
        run("Shared migration retains pricing, flea policy, switches and unrelated user fields",()=>
        {
            var c=RarityConfiguration.Parse("""{"enabled":false,"addToPmcWeapons":false,"addToBossWeapons":false,"addToNonScavFactions":false,"autoDetectApbs":false,"allowFleaMarketTrading":true,"priceMultipliers":{"Legendary":6.5},"custom":{"mode":"keep"},"spawnChances":{"standalone":{"guns":0},"apbs":{"guns":5}}}""",out var saved,"standalone");
            check(!c.Enabled&&!c.AddToPmcWeapons&&!c.AddToBossWeapons&&!c.AddToNonScavFactions&&!c.AutoDetectApbs&&c.AllowFleaMarketTrading,"existing switches");near(c.PriceMultiplierFor("Legendary",10),6.5,"custom price");check(JsonNode.Parse(saved!)!["custom"]!["mode"]!.GetValue<string>()=="keep","unknown object preserved");
        });
        run("Actual file migration backs up original bytes once and commits shared config atomically",()=>
        {
            var folder=Path.Combine(Path.GetDirectoryName(typeof(RarityConfiguration).Assembly.Location)!,"config");Directory.CreateDirectory(folder);var path=Path.Combine(folder,"config.json");var prior=File.Exists(path)?File.ReadAllBytes(path):null;var backups=Directory.GetFiles(folder,"config.json.before-shared-spawns.bak*").ToHashSet();
            var original="// Original user config\n"+Old+"\n";
            try
            {
                File.WriteAllText(path,original);RarityConfiguration.Load();check(File.ReadAllText(path)==original,"early load never rewrites legacy bytes");
                var c=RarityConfiguration.Load("apbs");near(c.BotFraction("standalone","exusec","grenades"),.02,"active selection committed");var made=Directory.GetFiles(folder,"config.json.before-shared-spawns.bak*").Except(backups).ToArray();check(made.Length==1&&File.ReadAllText(made[0])==original,"exact original config backup");
                var committed=File.ReadAllText(path);RarityConfiguration.Load("standalone");check(File.ReadAllText(path)==committed,"backend switch preserves committed values");check(Directory.GetFiles(folder,"config.json.before-shared-spawns.bak*").Except(backups).Count()==1,"no duplicate backup after canonical load");check(!File.Exists(path+".efb-migration.tmp"),"atomic temporary file consumed");
            }
            finally
            {
                if(prior is null)File.Delete(path);else File.WriteAllBytes(path,prior);
                foreach(var made in Directory.GetFiles(folder,"config.json.before-shared-spawns.bak*").Except(backups))File.Delete(made);
            }
        });
        run("Final shared config reaches live APBS equipment without discarding import snapshots",()=>
        {
            var catalog=LegendaryCatalog.Load();var active=catalog.Variants.Where(v=>v.SpawnEnabled).ToArray();FactionEquipmentSpawning.Configure(active,catalog.LegacyAliases.Select(v=>v.TemplateId),new());FactionEquipmentSpawning.RememberImported(3,"guns",active.Where(v=>v.MeleeEffect==null&&v.BackpackEffect==null&&v.ChestRigEffect==null&&v.GrenadeChargesPerRaid==0).Select(v=>v.TemplateId));
            var c=RarityConfiguration.Parse("""{"factionSpawnChances":{"rogues":{"guns":3}}}""",out _);FactionEquipmentSpawning.SelectConfiguration(c);var gun=active.First(v=>v.WeaponClass=="assaultRifle");var h=new ApbsEquipmentFixture{Shared=new(){{EquipmentSlots.FirstPrimaryWeapon,new(){{new(gun.SourceTemplateId),100}}}}};var pool=h.GetEquipmentByBotRole("exusec",3)[EquipmentSlots.FirstPrimaryWeapon];var ids=active.Select(v=>v.TemplateId).ToHashSet();near(pool.Where(p=>ids.Contains(p.Key.ToString())).Sum(p=>p.Value)/pool.Values.Sum(),.03,"updated config and imported IDs both retained");
        });
        run("Native and APBS grenade policies use the same saved faction setting",()=>
        {
            var defs=LegendaryCatalog.Load().Variants.Where(v=>v.GrenadeChargesPerRaid>0).ToArray();var policy=new GrenadeSpawnPolicy(defs);var original=defs.Select(v=>v.SourceTemplateId).Distinct().ToDictionary(id=>id,_=>10d);var c=RarityConfiguration.Parse("""{"factionSpawnChances":{"rogues":{"grenades":8}}}""",out _);var a=policy.Pool(original,c,"standalone","exusec",false);var b=policy.Pool(original,c,"apbs","exusec",false);check(a.SequenceEqual(b),"identical weighted grenade pools");var ids=defs.Select(v=>v.TemplateId).ToHashSet();near(a.Where(p=>ids.Contains(p.Key)).Sum(p=>p.Value)/a.Values.Sum(),.08,"single saved percentage");
        });
        run("Partial PMC-side overrides continue inheriting later edits to shared categories",()=>
        {
            var c=RarityConfiguration.Parse("""{"spawnChances":{"pmcs":{"guns":5,"melee":3}},"factionSpawnChances":{"pmcUsec":{"guns":0}}}""",out var saved);
            check(JsonNode.Parse(saved!)!["factionSpawnChances"]!["pmcUsec"]!["melee"]==null,"missing overrides remain inherited");near(c.BotFraction("standalone","pmcusec","melee"),.03,"initial inherited value");
            var node=JsonNode.Parse(saved!)!;node["spawnChances"]!["pmcs"]!["melee"]=9;c=RarityConfiguration.Parse(node.ToJsonString(),out _);
            near(c.BotFraction("apbs","pmcusec","melee"),.09,"subsequent shared edit inherited");near(c.BotFraction("apbs","pmcusec","guns"),0,"explicit side zero remains");
        });
    }
}
