using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using QuestCondition = SPTarkov.Server.Core.Models.Eft.Common.Tables.QuestCondition;
using EFT.HandBook;
using QuestController = SPTarkov.Server.Core.Controllers.QuestController;
using Quest = SPTarkov.Server.Core.Models.Eft.Common.Tables.Quest;
using TaskConditionCounter = SPTarkov.Server.Core.Models.Eft.Common.Tables.TaskConditionCounter;
using HarmonyLib;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Helpers.Quest;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Utils.Json;
using C = EscapeFromBorderlands.Client;
using S = EscapeFromBorderlands.Server;
using ClientItem = EFT.InventoryLogic.Item;
using ServerItem = SPTarkov.Server.Core.Models.Eft.Common.Tables.Item;

int cases=0, assertions=0, failures=0;
void Check(bool value,string why){assertions++;if(!value)throw new Exception(why);}
void Run(string name,Action test){cases++;try{test();Console.WriteLine("PASS "+name);}catch(Exception e){failures++;Console.WriteLine("FAIL "+name+": "+(e.InnerException??e));}}
var catalog=S.LegendaryCatalog.Load();
var all=catalog.Variants.Select(v=>(Id:v.TemplateId,Source:v.SourceTemplateId)).Concat(catalog.LegacyAliases.Select(v=>(Id:v.TemplateId,Source:v.SourceTemplateId))).ToArray();
var mosin=catalog.Variants.First(v=>v.SourceFullName.Contains("Mosin")&&v.SpawnEnabled);
var otherMosin=catalog.Variants.First(v=>v.SourceTemplateId==mosin.SourceTemplateId&&v.TemplateId!=mosin.TemplateId);
var bag=catalog.Variants.First(v=>v.BackpackEffect!=null&&v.SpawnEnabled);
var rig=catalog.Variants.First(v=>v.ChestRigEffect!=null&&v.SpawnEnabled);
const string Wrong="ffffffffffffffffffffffff";
var identity=C.LegendaryQuestItems.Identity;
var harmony=new Harmony("efb.quest.validation.client");
Run("Unpatched native checks reproduce the legendary Mosin rejection",()=>{
 var condition=new ConditionKills{target="Savage",weapon=[mosin.SourceTemplateId],value=1,compareMethod=ECompareMethod.MoreOrEqual};var checker=new ConditionHitProgressChecker(condition);
 Check(!checker.Test(new HitConditionCheck{TargetSide="Savage",Weapon=mosin.TemplateId,Value=1}),"original native check rejects legendary ID");
 Check(checker.Test(new HitConditionCheck{TargetSide="Savage",Weapon=mosin.SourceTemplateId,Value=1}),"original native check accepts vanilla ID");
});
Run("Native kill and use hooks install and execute without Unity",()=>{
 var hooks=new[]{typeof(C.LegendaryQuestHitPatch),typeof(C.LegendaryQuestUsePatch)};
 foreach(var type in hooks)harmony.CreateClassProcessor(type).Patch();
 Check(hooks.Length==2,"both engine-independent native hooks installed");
});
Run("Every actual server quest hook installs without duplicate patches on repeated setup",()=>{
 S.LegendaryQuestItems.Install(catalog.Variants,catalog.LegacyAliases);
 var method=AccessTools.Method(typeof(QuestController),nameof(QuestController.HandoverQuest));var count=Harmony.GetPatchInfo(method)!.Prefixes.Count;
 S.LegendaryQuestItems.Install(catalog.Variants,catalog.LegacyAliases);
 Check(Harmony.GetPatchInfo(method)!.Prefixes.Count==count&&count==1,"single installed handover protection prefix");
});
ConditionKills Condition(params string[] weapons)=>new(){target="Savage",weapon=weapons,value=1,compareMethod=ECompareMethod.MoreOrEqual};
HitConditionCheck Hit(string tpl)=>new(){TargetSide="Savage",Weapon=tpl,Value=1,WeaponMods=[],TargetEquipment=[],Distance=80,CurrentHour=12,BodyPart=EBodyPart.Head,ScavRole="assault"};
bool Kill(ConditionHit condition,HitConditionCheck hit)=>new ConditionHitProgressChecker(condition).Test(hit);
var handbook=DispatchProxy.Create<IBaseHandbook,HandbookProxy>();Singleton<IBaseHandbook>.Instance=handbook;

Run("All 3910 current, storage and retired templates match only their own vanilla source",()=>{
 Check(all.Length==3910&&all.Select(v=>v.Id).Distinct().Count()==all.Length,"complete expanded catalog and aliases");
 foreach(var entry in all){
  Check(identity.Matches(entry.Id,entry.Source),"client base equivalence "+entry.Id);
  Check(S.LegendaryQuestItems.Identity.Matches(entry.Id,entry.Source),"server base equivalence "+entry.Id);
  Check(!identity.Matches(entry.Source,entry.Id),"vanilla cannot satisfy specific legendary");
  Check(!identity.Matches(entry.Id,Wrong),"wrong base rejected");
 }
});
Run("Unknown third-party items retain their original identity and native category behavior",()=>{
 Check(identity.Source(Wrong)==Wrong&&identity.Matches(Wrong,Wrong),"unregistered template unchanged");
 Check(!identity.Matches(Wrong,mosin.SourceTemplateId),"unregistered item is not treated as a Mosin");
 HandbookProxy.Children.Clear();HandbookProxy.Children.Add((Wrong,"eeeeeeeeeeeeeeeeeeeeeeeb"));
 var condition=Condition();condition.weaponCategories=["eeeeeeeeeeeeeeeeeeeeeeeb"];
 Check(Kill(condition,Hit(Wrong)),"other mod's existing category match still works");
});
Run("Real native Mosin kill checks accept legendary copies and reject wrong weapons and targets",()=>{
 var condition=Condition(mosin.SourceTemplateId);
 Check(Kill(condition,Hit(mosin.TemplateId)),"legendary Mosin accepted");
 Check(Kill(condition,Hit(mosin.SourceTemplateId)),"ordinary Mosin still accepted");
 Check(!Kill(condition,Hit(Wrong)),"wrong receiver rejected");
 var wrong=Hit(mosin.TemplateId);wrong.TargetSide="Usec";Check(!Kill(condition,wrong),"target side still enforced");
});
Run("Native body-part, distance, role, time and hit-count restrictions remain enforced",()=>{
 var condition=Condition(mosin.SourceTemplateId);condition.bodyPart=[EBodyPart.Head];var hit=Hit(mosin.TemplateId);
 Check(Kill(condition,hit),"valid headshot");hit.BodyPart=EBodyPart.Chest;Check(!Kill(condition,hit),"wrong body part rejected");hit.BodyPart=EBodyPart.Head;
 condition.distance=new ConditionComparer{value=50,compareMethod=ECompareMethod.MoreOrEqual};hit.Distance=20;Check(!Kill(condition,hit),"short kill rejected");hit.Distance=80;
 condition.savageRole=["bossBully"];Check(!Kill(condition,hit),"wrong role rejected");condition.savageRole=["assault"];
 condition.daytime=new ConditionTimeComparer{from=1,to=5};Check(!Kill(condition,hit),"wrong time rejected");condition.daytime=null;
 hit.Value=0;Check(!Kill(condition,hit),"hit count retained");hit.Value=1;Check(Kill(condition,hit),"valid restricted event accepted");
});
Run("Native weapon-category matching uses the legendary weapon's vanilla base",()=>{
 HandbookProxy.Children.Clear();HandbookProxy.Children.Add((mosin.SourceTemplateId,"eeeeeeeeeeeeeeeeeeeeeeea"));
 var condition=Condition();condition.weaponCategories=["eeeeeeeeeeeeeeeeeeeeeeea"];
 Check(Kill(condition,Hit(mosin.TemplateId)),"bolt-action category matches Mosin");Check(!Kill(condition,Hit(Wrong)),"other class rejected");
});
Run("Required and forbidden enemy equipment includes legendary base identities",()=>{
 var condition=Condition(mosin.SourceTemplateId);var hit=Hit(mosin.TemplateId);hit.TargetEquipment=[rig.TemplateId,bag.TemplateId];
 condition.enemyEquipmentInclusive=[[rig.SourceTemplateId,bag.SourceTemplateId]];Check(Kill(condition,hit),"all base equipment satisfied");
 condition.enemyEquipmentInclusive=[[rig.SourceTemplateId,Wrong]];Check(!Kill(condition,hit),"AND group requires every item");
 condition.enemyEquipmentInclusive=null;condition.enemyEquipmentExclusive=[[rig.SourceTemplateId]];Check(!Kill(condition,hit),"forbidden base rejects legendary copy");
});
Run("Required and forbidden attachments retain their native grouping rules",()=>{
 var condition=Condition(mosin.SourceTemplateId);var hit=Hit(mosin.TemplateId);hit.WeaponMods=["scope-a","suppressor-a"];
 condition.weaponModsInclusive=[["scope-a","suppressor-a"],["scope-b"]];Check(Kill(condition,hit),"valid AND group accepted");
 condition.weaponModsInclusive=[["scope-a","missing"]];Check(!Kill(condition,hit),"missing attachment rejected");
 condition.weaponModsInclusive=null;condition.weaponModsExclusive=[["suppressor-a"]];Check(!Kill(condition,hit),"forbidden attachment rejected");
});

Run("Kill normalization preserves shared events and specific legendary requirements",()=>{
 var condition=Condition(mosin.SourceTemplateId);var hit=Hit(mosin.TemplateId);hit.WeaponMods=[rig.TemplateId];hit.TargetEquipment=[bag.TemplateId];
 var mods=hit.WeaponMods;var equipment=hit.TargetEquipment;var copy=(HitConditionCheck)C.LegendaryQuestItems.Hit(condition,hit);
 Check(!ReferenceEquals(copy,hit)&&copy.Weapon==mosin.SourceTemplateId,"separate normalized event");
 Check(hit.Weapon==mosin.TemplateId&&ReferenceEquals(hit.WeaponMods,mods)&&ReferenceEquals(hit.TargetEquipment,equipment),"shared event unchanged");
 Check(mods.SequenceEqual([rig.TemplateId])&&equipment.SequenceEqual([bag.TemplateId]),"original lists unchanged");
 Check(Kill(Condition(mosin.TemplateId),hit),"specific requirement accepts itself");
 Check(!Kill(Condition(otherMosin.TemplateId),hit),"different legendary with same base rejected");
 Check(!Kill(Condition(mosin.TemplateId),Hit(mosin.SourceTemplateId)),"vanilla cannot substitute for explicit legendary");
 object value=7;Check(ReferenceEquals(C.LegendaryQuestItems.Hit(condition,value),value),"non-event native fallback unchanged");
});
Run("Melee and grenade kill objectives use their own vanilla bases",()=>{
 foreach(var v in new[]{catalog.Variants.First(v=>v.MeleeEffect!=null),catalog.Variants.First(v=>v.GrenadeChargesPerRaid>0)})Check(Kill(Condition(v.SourceTemplateId),Hit(v.TemplateId)),"native kill event recognizes "+v.WeaponClass);
});



Run("Real use-item checker matches base IDs and preserves explicit legendary targets",()=>{
 var condition=new ConditionUseItem{target=[mosin.SourceTemplateId]};var checker=new ConditionUseItemProgressChecker(condition);
 Check(checker.Test(mosin.TemplateId)&&checker.Test(mosin.SourceTemplateId)&&!checker.Test(Wrong),"native use base matching");
 condition.target=[mosin.TemplateId];Check(checker.Test(mosin.TemplateId)&&!checker.Test(otherMosin.TemplateId),"specific legendary remains specific");
});
Run("Native server FindItem queries handle static and repeatable quests",()=>{
 var helper=(QuestHelper)RuntimeHelpers.GetUninitializedObject(typeof(QuestHelper));
 foreach(var repeatable in new[]{false,true}){
  Quest quest=(Quest)RuntimeHelpers.GetUninitializedObject(repeatable?typeof(RepeatableQuest):typeof(Quest));quest.Id="aaaaaaaaaaaaaaaaaaaaaaaa";
  var condition=new QuestCondition{Id="bbbbbbbbbbbbbbbbbbbbbbbb",DynamicLocale=false,ConditionType="FindItem",Target=new ListOrT<string>([mosin.SourceTemplateId],null)};quest.Conditions=new(){AvailableForFinish=[condition]};
  Check(helper.GetFindItemConditionByQuestItem(mosin.TemplateId,[quest.Id],[quest]).Count==1,"native query accepts copy");
  Check(helper.GetFindItemConditionByQuestItem(Wrong,[quest.Id],[quest]).Count==0,"wrong source does not progress");
  Check(condition.Target.List!.SequenceEqual([mosin.SourceTemplateId]),"quest target list unchanged");
 }
});
Run("Real server selling counter counts matching quantities without rewriting inventory",()=>{
 var helper=(QuestHelper)RuntimeHelpers.GetUninitializedObject(typeof(QuestHelper));var id=new MongoId("cccccccccccccccccccccccc");
 var profile=new PmcData{Inventory=new(){Items=[new ServerItem{Id=id,Template=mosin.TemplateId}]}};
 var counter=new TaskConditionCounter{Value=0};var condition=new QuestCondition{Id="bbbbbbbbbbbbbbbbbbbbbbbb",DynamicLocale=false,ConditionType="SellItemToTrader",Target=new ListOrT<string>([mosin.SourceTemplateId],null)};
 var request=new ProcessSellTradeRequestData{Items=[new SoldItem{Id=id,Count=2}]};var method=AccessTools.Method(typeof(QuestHelper),"IncrementSoldToTraderCounter");
 method.Invoke(helper,[profile,counter,condition,request]);Check(counter.Value==2,"sold quantity counted once");
 condition.Target=new ListOrT<string>([Wrong],null);method.Invoke(helper,[profile,counter,condition,request]);Check(counter.Value==2,"wrong item does not increment");
 Check(profile.Inventory.Items.Single().Template==(MongoId)mosin.TemplateId,"profile template unchanged");
});
Run("Handover keeps all native instructions and sales retain the single identity substitution",()=>{
 foreach(var method in new[]{AccessTools.Method(typeof(QuestController),nameof(QuestController.HandoverQuest)),AccessTools.Method(typeof(QuestHelper),"IncrementSoldToTraderCounter")}){
  var before=PatchProcessor.GetOriginalInstructions(method).ToArray();var after=PatchProcessor.GetCurrentInstructions(method).ToArray();Check(before.Length==after.Length,"all native guards and inventory operations retained");
  string Operand(object? value)=>value switch{LocalBuilder local=>"local:"+local.LocalIndex+":"+local.LocalType,Label label=>"label:"+label.GetHashCode(),Label[] labels=>string.Join(",",labels.Select(l=>l.GetHashCode())),_=>value?.ToString()??""};
  var changes=before.Zip(after).Where(p=>p.First.opcode!=p.Second.opcode||Operand(p.First.operand)!=Operand(p.Second.operand)).ToArray();
  if(method.Name==nameof(QuestController.HandoverQuest))Check(changes.Length==0,"native handover body stays unchanged behind the protection prefix");
  else Check(changes.Length==1&&changes[0].Second.operand is MethodInfo target&&target.DeclaringType==typeof(S.LegendaryQuestItems),"only sale membership call replaced");
 }
});
Run("Alias augmentation retains groups and original IDs and is idempotent",()=>{
 var original=new List<string>{rig.TemplateId,"scope",rig.TemplateId,rig.SourceTemplateId};var output=identity.WithSources(original);
 Check(original.Count==4&&output.Count==4,"input untouched and no duplicate source");Check(output.Count(id=>id==rig.TemplateId)==2,"real identifiers retained");
 var first=identity.WithSources([bag.TemplateId,rig.TemplateId]);var second=identity.WithSources(first);
 Check(first.SequenceEqual(second)&&first.Contains(bag.SourceTemplateId)&&first.Contains(rig.SourceTemplateId),"repeated normalization stable");
});
QuestProtectionTests.Run(Run,Check,catalog);
Console.WriteLine($"Quest compatibility results: {cases} cases, {assertions} assertions, {failures} failures.");
return failures==0?0:1;

public class HandbookProxy:DispatchProxy
{
 public static readonly HashSet<(string Item,string Category)> Children=[];
 protected override object? Invoke(MethodInfo? method,object?[]? args){
  if(method!.Name=="IsChildOf")return Children.Contains((args![0]!.ToString()!,args[1]!.ToString()!));
  if(method.Name=="IsCategory")return Children.Any(v=>v.Category==args![0]!.ToString());
  return method.ReturnType==typeof(void)?null:method.ReturnType.IsValueType?Activator.CreateInstance(method.ReturnType):null;
 }
}

