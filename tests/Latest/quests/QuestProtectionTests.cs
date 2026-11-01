using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Quests;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Utils;
using C = EscapeFromBorderlands.Client;
using S = EscapeFromBorderlands.Server;

internal static class QuestProtectionTests
{
    private const string A="aaaaaaaaaaaaaaaaaaaaaaaa", B="bbbbbbbbbbbbbbbbbbbbbbbb", D="dddddddddddddddddddddddd";
    private static PmcData Profile(params Item[] items)=>new(){Inventory=new(){Items=items.ToList()},Quests=[],TaskConditionCounters=[]};
    private static HandoverQuestRequestData Request(params string[] ids)=>new(){QuestId=A,ConditionId=B,Items=ids.Select(id=>new IdWithCount{Id=id,Count=1}).ToList()};
    private static Item Item(string id,string tpl,string? parent=null)=>new(){Id=id,Template=tpl,ParentId=parent};
    internal static void Run(Action<string,Action> run,Action<bool,string> check,S.LegendaryCatalog catalog)
    {
        var variants=catalog.Variants.Select(v=>(v.TemplateId,v.SourceTemplateId)).Concat(catalog.LegacyAliases.Select(v=>(v.TemplateId,v.SourceTemplateId))).ToArray();
        var mosin=catalog.Variants.First(v=>v.SourceFullName.Contains("Mosin")&&v.SpawnEnabled);
        run("Every expanded and retired EFB template is protected while vanilla sources are not",()=>{
            foreach(var (id,source) in variants)
            {
                check(C.LegendaryQuestItems.Identity.IsVariant(id)&&S.LegendaryQuestItems.Identity.IsVariant(id),"protected template "+id);
                check(!C.LegendaryQuestItems.Identity.IsVariant(source)&&!S.LegendaryQuestItems.Identity.IsVariant(source),"ordinary source remains consumable");
            }
        });
        run("Server hand-in guard recognizes direct copies across every legendary item category",()=>{
            foreach(var v in new[]{mosin,catalog.Variants.First(v=>v.MeleeEffect!=null),catalog.Variants.First(v=>v.BackpackEffect!=null),catalog.Variants.First(v=>v.ChestRigEffect!=null),catalog.Variants.First(v=>v.GrenadeChargesPerRaid>0)})
            {
                var profile=Profile(Item(A,v.TemplateId));
                check(S.LegendaryQuestItems.ProtectsHandover(profile,Request(A)),"selected EFB category protected");
                check(profile.Inventory!.Items!.Single().Template==(MongoId)v.TemplateId,"profile not rewritten");
            }
        });
        run("Selected vanilla containers protect deeply nested EFB copies without blocking unrelated containers",()=>{
            var profile=Profile(Item(A,mosin.SourceTemplateId),Item(B,mosin.SourceTemplateId,A),Item(D,mosin.TemplateId,B));
            check(S.LegendaryQuestItems.ProtectsHandover(profile,Request(A)),"outer container protects descendants");
            check(S.LegendaryQuestItems.ProtectsHandover(profile,Request(B)),"inner container protects descendants");
            check(S.LegendaryQuestItems.ProtectsHandover(profile,Request(D)),"copy itself protected");
            check(!S.LegendaryQuestItems.ProtectsHandover(profile,Request("eeeeeeeeeeeeeeeeeeeeeeee")),"unrelated request not blocked");
            check(profile.Inventory!.Items!.Count==3,"inspection does not remove children");
        });
        run("Ordinary hand-ins remain eligible and off-selection legendaries do not block the request",()=>{
            var profile=Profile(Item(A,mosin.SourceTemplateId),Item(B,mosin.TemplateId));
            check(!S.LegendaryQuestItems.ProtectsHandover(profile,Request(A)),"normal selected item remains eligible");
            check(!S.LegendaryQuestItems.ProtectsHandover(profile,Request()),"empty selection left to native validation");
            check(S.LegendaryQuestItems.ProtectsHandover(profile,Request(A,B)),"mixed request is protected as a whole");
            check(profile.Inventory!.Items!.Count==2,"no selection was consumed");
        });
        run("Actual native handover rejects a mixed request before any inventory or quest mutation",()=>{
            var session=new MongoId("eeeeeeeeeeeeeeeeeeeeeeee");
            var output=(ItemEventRouterResponse)RuntimeHelpers.GetUninitializedObject(typeof(ItemEventRouterResponse));output.Warnings=[];
            var holder=(EventOutputHolder)RuntimeHelpers.GetUninitializedObject(typeof(EventOutputHolder));
            AccessTools.Field(typeof(EventOutputHolder),"_outputStore").SetValue(holder,new Dictionary<MongoId,ItemEventRouterResponse>{{session,output}});
            var response=(HttpResponseUtil)RuntimeHelpers.GetUninitializedObject(typeof(HttpResponseUtil));
            var controller=(QuestController)RuntimeHelpers.GetUninitializedObject(typeof(QuestController));
            AccessTools.Field(typeof(QuestController),"<eventOutputHolder>P").SetValue(controller,holder);
            AccessTools.Field(typeof(QuestController),"<httpResponseUtil>P").SetValue(controller,response);
            var profile=Profile(Item(A,mosin.SourceTemplateId),Item(B,mosin.TemplateId));var request=Request(A,B);
            var before=profile.Inventory!.Items!.Select(i=>(i.Id,i.Template,i.ParentId)).ToArray();
            // All other controller dependencies are deliberately unset. Reaching
            // any native quest processing here would fail rather than silently pass.
            var result=controller.HandoverQuest(profile,request,session);
            check(ReferenceEquals(result,output)&&result.Warnings?.Count==1,"native error response returned");
            check(result.Warnings![0].ErrorMessage!.Contains("Legendary items cannot be handed in"),"clear protection message");
            check(profile.Inventory!.Items!.Select(i=>(i.Id,i.Template,i.ParentId)).SequenceEqual(before),"ordinary-first request leaves every item unchanged");
            check(profile.Quests!.Count==0&&profile.TaskConditionCounters!.Count==0,"quest state unchanged");
            check(request.Items!.Select(i=>i.Id.ToString()).SequenceEqual([A,B]),"original request order and selection retained");
            profile.Inventory!.Items!.Single(i=>i.Id==(MongoId)B).ParentId=A;
            request=Request(A);output.Warnings!.Clear();
            result=controller.HandoverQuest(profile,request,session);
            check(result.Warnings?.Count==1&&profile.Inventory!.Items!.Count==2,"native handler also protects a vanilla parent with rare contents");
        });
        run("Server prefix passes ordinary requests into the unchanged native hand-in logic",()=>{
            var profile=Profile(Item(A,mosin.SourceTemplateId));var request=Request(A);
            object?[] args=[null,profile,request,new MongoId(D),null];
            var allowed=(bool)typeof(S.LegendaryQuestHandoverPatch).GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args)!;
            check(allowed&&args[4]==null,"ordinary request does not need protection error services");
            check(profile.Inventory!.Items!.Single().Template==(MongoId)mosin.SourceTemplateId,"ordinary item still owned until native consumption");
        });
    }
}
