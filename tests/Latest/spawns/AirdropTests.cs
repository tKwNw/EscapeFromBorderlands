using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using EscapeFromBorderlands.Server;
using HarmonyLib;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Services;
using SPTarkov.Server.Core.Services.InRaid;

internal static class AirdropTests
{
    private static AirdropLootRequest NativeRequest=new();
    private static List<List<Item>> Captured=[];
    private static bool NativeConfig(ref AirdropLootRequest __result) { __result=NativeRequest;return false; }
    private static bool NativeFit(IEnumerable<List<Item>> crateLootPool,ref IEnumerable<List<Item>> __result)
    { Captured=crateLootPool.ToList();__result=Captured;return false; }

    internal static void Run(Action<string,Action> run,Action<bool,string> check,Action<double,double,string> near,LegendaryCatalog catalog)
    {
        var active=catalog.Variants.Where(v=>v.SpawnEnabled&&!v.IsMysteryFleaOffer&&v.SpawnWeight>0&&v.WeaponClass is not "grenadeLauncher" and not "specialWeapon").ToArray();
        var allIds=catalog.Variants.Select(v=>v.TemplateId).Concat(catalog.LegacyAliases.Select(v=>v.TemplateId)).ToHashSet();
        string Category(VariantDefinition d)=>d.GrenadeChargesPerRaid>0?"grenades":d.IsEarpiece?"earpieces":d.BackpackEffect!=null?"backpacks":d.ChestRigEffect!=null?"chestRigs":d.MeleeEffect!=null?"melee":"guns";
        var representatives=active.GroupBy(Category).Select(g=>g.First()).ToArray();
        AirdropLegendaryRolls Rolls()=>new(catalog.Variants,catalog.LegacyAliases,catalog.UnlootableMeleeTemplateIds.ToHashSet());
        List<Item> Group(string tpl)=>[new(){Id=new MongoId(),Template=new MongoId(tpl),Upd=new(){StackObjectsCount=1,SpawnedInSession=true}}];
        var unknown="fffffffffffffffffffffffe";

        run("Airdrops add exactly 1 shared percentage with a 1-percent default and idempotent config migration",()=>{
            var original=RarityConfiguration.DefaultJson();var node=JsonNode.Parse(original)!.AsObject();node.Remove("airdropLegendaryChancePercent");node["customSetting"]="preserve";
            var config=RarityConfiguration.Parse(node.ToJsonString(),out var migrated);near(config.AirdropFraction,.01,"default fraction");
            var saved=JsonNode.Parse(migrated!)!.AsObject();check(saved["airdropLegendaryChancePercent"]!.GetValue<double>()==1,"setting written for upgrades");check(saved["customSetting"]!.GetValue<string>()=="preserve","unrelated fields retained");
            check(saved.Count(p=>p.Key.Contains("airdrop",StringComparison.OrdinalIgnoreCase))==1,"no category/type/backend airdrop options");
            var again=RarityConfiguration.Parse(migrated!,out var repeated);check(repeated==null&&again.AirdropFraction==.01,"migration stable on next start");
        });
        run("Airdrop endpoints include 100 percent and sanitize only their own range",()=>{
            foreach(var (value,expected) in new[]{(-2d,0d),(0d,0d),(.25d,.0025d),(1d,.01d),(100d,1d),(125d,1d),(double.NaN,.01d),(double.PositiveInfinity,.01d)})
                near(new RarityConfiguration{AirdropLegendaryChancePercent=value}.AirdropFraction,expected,"airdrop clamp "+value);
            foreach(var value in new[]{-20d,0d,100d,120d}){
                var config=RarityConfiguration.Parse("{\"airdropLegendaryChancePercent\":"+value+"}",out var saved);
                near(JsonNode.Parse(saved!)!["airdropLegendaryChancePercent"]!.GetValue<double>(),Math.Clamp(value,0,100),"canonical persisted value");
                near(config.Chance("standalone","guns"),1,"bot rate preserved");}
        });
        run("Airdrop chance is independent of APBS, bot toggles, flea and saved bot zeroes",()=>{
            foreach(var backend in new[]{"standalone","apbs"}){
                var config=RarityConfiguration.Parse("""{"enabled":true,"addToPmcWeapons":false,"addToBossWeapons":false,"addToNonScavFactions":false,"allowFleaMarketTrading":false,"airdropLegendaryChancePercent":100,"apbsPmcLegendaryChancePercent":0,"standalonePmcLegendaryChancePercent":0,"priceMultipliers":{"Legendary":7}}""",out _,backend);
                near(config.AirdropFraction,1,"airdrop still enabled");near(config.BotFraction(backend,"pmcusec","guns"),0,"bot zero remains");near(config.PriceMultiplierFor("Legendary",10),7,"price preserved");
                var groups=representatives.Select(v=>Group(v.SourceTemplateId)).ToArray();Rolls().Roll(groups,config,()=>0).ToArray();check(groups.All(g=>allIds.Contains(g[0].Template.ToString())),"all categories convert independently of bot mode");}
        });
        run("Native airdrop pool filtering excludes all EFB stock and aliases without mutating global request data",()=>{
            var blacklist=new HashSet<MongoId>{new(unknown)};var request=new AirdropLootRequest{ItemBlacklist=blacklist,ItemTypeWhitelist=[new(unknown)]};var prepared=Rolls().Prepare(request);
            check(!ReferenceEquals(request,prepared)&&!ReferenceEquals(blacklist,prepared.ItemBlacklist),"request-local copies");
            check(blacklist.Count==1&&blacklist.Contains(new(unknown)),"global blacklist unchanged");check(allIds.All(id=>prepared.ItemBlacklist!.Contains(new(id))),"active, retired and mystery IDs blocked from native random/preset pools");check(prepared.ItemTypeWhitelist==request.ItemTypeWhitelist,"native type restrictions preserved");
        });
        run("Forced airdrop clone mappings become vanilla before the same roll and preserve amount ranges",()=>{
            var d=representatives.First();var alias=catalog.LegacyAliases.First(v=>v.SourceTemplateId!=d.SourceTemplateId);var forced=new Dictionary<MongoId,MinMax<int>>{{new(d.SourceTemplateId),new(){Min=1,Max=2}},{new(d.TemplateId),new(){Min=3,Max=4}},{new(alias.TemplateId),new(){Min=2,Max=3}},{new(unknown),new(){Min=1,Max=1}}};
            var request=new AirdropLootRequest{UseForcedLoot=true,ForcedLoot=forced};var prepared=Rolls().Prepare(request);
            check(prepared.UseForcedLoot==true&&prepared.ForcedLoot!.ContainsKey(new(alias.SourceTemplateId)),"forced path and alias base preserved");
            check(!prepared.ForcedLoot!.Keys.Any(k=>allIds.Contains(k.ToString())),"no uncontrolled forced EFB stock");
            check(prepared.ForcedLoot[new(d.SourceTemplateId)].Min==4&&prepared.ForcedLoot[new(d.SourceTemplateId)].Max==6,"colliding supported ranges summed");check(forced.Count==4&&forced[new(d.SourceTemplateId)].Min==1,"original mapping and ranges unchanged");check(prepared.ForcedLoot[new(unknown)].Min==1,"third-party forced loot retained");
        });
        run("Zero percent converts no supported airdrop item and consumes no randomness",()=>{
            var groups=representatives.Select(v=>Group(v.SourceTemplateId)).ToArray();var originals=groups.Select(g=>g[0].Template).ToArray();
            Rolls().Roll(groups,new(){AirdropLegendaryChancePercent=0},()=>throw new Exception("0 percent requested randomness")).ToArray();
            check(groups.Select(g=>g[0].Template).SequenceEqual(originals),"all native templates retained");
        });
        run("100 percent converts every supported source across all 6 categories",()=>{
            var sources=active.Where(v=>!catalog.UnlootableMeleeTemplateIds.Contains(v.SourceTemplateId)).GroupBy(v=>v.SourceTemplateId).ToArray();var groups=sources.Select(g=>Group(g.Key)).ToArray();
            var result=Rolls().Roll(groups,new(){AirdropLegendaryChancePercent=100},()=>.5).ToArray();
            check(representatives.Length==6,"catalog covers 6 categories");check(result.Length==groups.Length,"no loot roots added or removed");
            for(var i=0;i<groups.Length;i++)check(sources[i].Any(v=>v.TemplateId==groups[i][0].Template.ToString()),"converted to a current variant of the same vanilla base");
        });
        run("Airdrop chance boundary is per item and does not become a combined category probability",()=>{
            var d=representatives.First();var groups=new[]{Group(d.SourceTemplateId),Group(d.SourceTemplateId),Group(d.SourceTemplateId)};var values=new Queue<double>(new[]{.09999,0d,.1,.9});
            Rolls().Roll(groups,new(){AirdropLegendaryChancePercent=10},()=>values.Dequeue()).ToArray();
            check(allIds.Contains(groups[0][0].Template.ToString()),"below threshold converts");check(groups[1][0].Template.ToString()==d.SourceTemplateId&&groups[2][0].Template.ToString()==d.SourceTemplateId,"exact threshold and greater stay vanilla");check(values.Count==0,"one independent chance per root");
        });
        run("Airdrop variant choice honors signature weights",()=>{
            var a=new VariantDefinition{TemplateId="aaaaaaaaaaaaaaaaaaaaaaaa",SourceTemplateId=unknown,SpawnWeight=1};var b=new VariantDefinition{TemplateId="bbbbbbbbbbbbbbbbbbbbbbbb",SourceTemplateId=unknown,SpawnWeight=3};
            foreach(var (unit,expected) in new[]{(0d,a.TemplateId),(.249d,a.TemplateId),(.25d,b.TemplateId),(.999d,b.TemplateId),(1d,b.TemplateId)}){
                var group=Group(unknown);new AirdropLegendaryRolls([a,b],[]).Roll([group],new(){AirdropLegendaryChancePercent=100},()=>unit).ToArray();check(group[0].Template.ToString()==expected,"weighted bucket "+unit);}
        });
        run("Generated backpack variants retain current layout weights and the existing width cap",()=>{
            var d=active.First(v=>v.BackpackEffect!=null);var policy=Rolls();
            for(var i=0;i<120;i++){
                var group=Group(d.SourceTemplateId);policy.Roll([group],new(){AirdropLegendaryChancePercent=100},()=>i/120d).ToArray();var selected=active.Single(v=>v.TemplateId==group[0].Template.ToString());
                check(selected.StorageRollVersion==BackpackStorageVariants.CurrentLayoutVersion&&!selected.IsMysteryFleaOffer,"only active concrete layout");check(selected.StorageColumns<=BackpackStorageVariants.MaximumColumnRoll(selected),"new width-cap eligibility preserved");}
        });
        run("Third-party items, attachments and unsupported loot never roll as EFB",()=>{
            var group=Group(unknown);var original=group[0].Template;Rolls().Roll([group],new(){AirdropLegendaryChancePercent=100},()=>throw new Exception("unsupported item rolled")).ToArray();check(group[0].Template==original,"unknown item unchanged");
        });
        run("Airdrop conversion preserves root IDs, attachment graphs, state and Found in Raid flags",()=>{
            var d=representatives.First(v=>Category(v)=="guns");var group=Group(d.SourceTemplateId);var root=group[0];root.Location=new{ x=2,y=3,r=1 };var state=root.Upd;var location=root.Location;var child=new Item{Id=new MongoId(),Template=new(unknown),ParentId=root.Id.ToString(),SlotId="mod_scope",Upd=new(){StackObjectsCount=1}};group.Add(child);
            var id=root.Id;Rolls().Roll([group],new(){AirdropLegendaryChancePercent=100},()=>0).ToArray();check(root.Id==id&&ReferenceEquals(state,root.Upd)&&ReferenceEquals(location,root.Location),"root state retained");check(root.Upd!.SpawnedInSession==true&&ReferenceEquals(group[1],child)&&child.ParentId==id.ToString()&&child.Template.ToString()==unknown,"attachments and FIR retained");
        });
        run("Signature fire-mode restrictions repair incompatible preset state without mutating shared mode data",()=>{
            var variant=new VariantDefinition{TemplateId="aaaaaaaaaaaaaaaaaaaaaaaa",SourceTemplateId=unknown,FireModes=["single"]};var group=Group(unknown);var original=group[0].Upd!;original.FireMode=new(){FireMode="fullauto"};
            new AirdropLegendaryRolls([variant],[]).Roll([group],new(){AirdropLegendaryChancePercent=100},()=>0).ToArray();
            check(group[0].Upd!.FireMode!.FireMode=="single","restricted signature starts in a supported mode");check(original.FireMode.FireMode=="fullauto","original preset mode data untouched");check(group[0].Upd.SpawnedInSession==original.SpawnedInSession&&group[0].Upd.StackObjectsCount==original.StackObjectsCount,"other item state preserved");
        });
        run("Empty groups and unusual item stacks remain valid without quantity changes",()=>{
            var d=representatives.First(v=>v.GrenadeChargesPerRaid>0);var group=Group(d.SourceTemplateId);group[0].Upd!.StackObjectsCount=4;
            var result=Rolls().Roll([[],group],new(){AirdropLegendaryChancePercent=100},()=>throw new Exception("stack should stay native")).ToArray();check(result.Length==2&&result[0].Count==0,"empty input preserved");check(group[0].Template.ToString()==d.SourceTemplateId&&group[0].Upd!.StackObjectsCount==4,"unsupported stack is not converted into an invalid max-stack-1 item");
        });
        run("Retired, mystery, excluded-source and disabled variants are never selected",()=>{
            var a=new VariantDefinition{TemplateId="aaaaaaaaaaaaaaaaaaaaaaaa",SourceTemplateId=unknown,SpawnEnabled=false};var b=new VariantDefinition{TemplateId="bbbbbbbbbbbbbbbbbbbbbbbb",SourceTemplateId=unknown,IsMysteryFleaOffer=true};var c=new VariantDefinition{TemplateId="cccccccccccccccccccccccc",SourceTemplateId=unknown,SpawnWeight=0};var d=new VariantDefinition{TemplateId="dddddddddddddddddddddddd",SourceTemplateId=unknown,WeaponClass="specialWeapon"};
            var group=Group(unknown);new AirdropLegendaryRolls([a,b,c,d],[]).Roll([group],new(){AirdropLegendaryChancePercent=100},()=>throw new Exception("inactive source rolled")).ToArray();check(group[0].Template.ToString()==unknown,"inactive candidates excluded");a.SpawnEnabled=true;new AirdropLegendaryRolls([a],[],new HashSet<string>{unknown}).Roll([group],new(){AirdropLegendaryChancePercent=100},()=>throw new Exception("excluded source rolled")).ToArray();check(group[0].Template.ToString()==unknown,"unlootable melee source excluded");
        });
        run("Repeated fitting/enumeration never grants another roll to the same generated group",()=>{
            var d=representatives.First();var group=Group(d.SourceTemplateId);var policy=Rolls();var calls=0;
            var result=policy.Roll([group],new(){AirdropLegendaryChancePercent=1},()=>{calls++;return .5;});for(var i=0;i<20;i++){_=result.ToArray();_=policy.Roll([group],new(){AirdropLegendaryChancePercent=100},()=>throw new Exception("reroll" )).ToArray();}
            check(calls==1&&group[0].Template.ToString()==d.SourceTemplateId,"one attempt per generated group");
        });
        run("The master mod switch leaves all native airdrop loot untouched",()=>{
            var d=representatives.First();var group=Group(d.SourceTemplateId);Rolls().Roll([group],new(){Enabled=false,AirdropLegendaryChancePercent=100},()=>throw new Exception("disabled roll")).ToArray();check(group[0].Template.ToString()==d.SourceTemplateId,"disabled mod is native");
        });
        run("Real SPT airdrop hooks bind once and cover every native drop type before fitting",()=>{
            var config=new RarityConfiguration{AirdropLegendaryChancePercent=100};AirdropLegendarySpawning.Install(catalog.Variants,catalog.LegacyAliases,config);AirdropLegendarySpawning.Install(catalog.Variants,catalog.LegacyAliases,config);
            var getConfig=AccessTools.Method(typeof(AirdropService),"GetAirdropLootConfigByType");var fit=AccessTools.Method(typeof(AirdropService),"GetLootThatFitsContainer");var owner="com.tkw.escapefromborderlands.server.airdrops";
            check(Harmony.GetPatchInfo(getConfig)!.Postfixes.Count(p=>p.owner==owner)==1&&Harmony.GetPatchInfo(fit)!.Prefixes.Count(p=>p.owner==owner)==1,"one patch on each real target");
            var boundary=new Harmony("efb.validation.airdrop-native-boundaries");
            boundary.Patch(getConfig,prefix:new HarmonyMethod(typeof(AirdropTests),nameof(NativeConfig)));
            boundary.Patch(fit,prefix:new HarmonyMethod(typeof(AirdropTests),nameof(NativeFit)){priority=Priority.Last});
            try{
                var service=(AirdropService)RuntimeHelpers.GetUninitializedObject(typeof(AirdropService));
                foreach(var type in Enum.GetValues<SptAirdropTypeEnum>()){
                    NativeRequest=new(){ItemBlacklist=[]};var prepared=(AirdropLootRequest)getConfig.Invoke(service,[type])!;check(prepared.ItemBlacklist!.Count==allIds.Count,"all clone IDs filtered for "+type);check(NativeRequest.ItemBlacklist!.Count==0,"request-local filter for "+type);
                    var groups=representatives.Select(v=>Group(v.SourceTemplateId)).ToArray();var crate=new Item{Id=new MongoId(),Template=new(unknown)};
                    _=fit.Invoke(service,[crate,groups]);check(Captured.All(g=>allIds.Contains(g[0].Template.ToString())),"same shared roll reaches native fitting for "+type);
                }
            }finally{boundary.UnpatchSelf();}
        });
    }
}
