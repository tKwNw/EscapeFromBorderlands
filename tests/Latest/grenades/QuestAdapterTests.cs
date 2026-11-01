using System.Reflection;
using System.Reflection.Emit;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT;
using EFT.HandBook;
using Diz.LanguageExtensions;
using EscapeFromBorderlands.Client;
using HarmonyLib;

internal static class QuestAdapterTests
{
    internal static void Run(Action<string,Action> run,Action<bool,string> check)
    {
        var catalog=LegendaryCatalog.Load();
        var samples=new[]{catalog.Variants.First(v=>v.WeaponClass=="sniperRifle"),catalog.Variants.First(v=>v.MeleeEffect!=null),catalog.Variants.First(v=>v.BackpackEffect!=null),catalog.Variants.First(v=>v.ChestRigEffect!=null),catalog.Variants.First(v=>v.GrenadeChargesPerRaid>0)};
        bool Match(Item item,ConditionItem condition,Handbook handbook,bool native=false)
        {
            object?[] args=[item,condition,handbook,native];
            typeof(LegendaryQuestItemMatchPatch).GetMethod("Postfix",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args);
            return (bool)args[3]!;
        }
        run("Quest item identity adapter supports every EFB item category and preserves native decisions",()=>{
            var handbook=new Handbook();
            foreach(var v in samples)
            {
                var item=new Item{StringTemplateId=v.TemplateId};var condition=new ConditionItem{target=[v.SourceTemplateId]};
                check(Match(item,condition,handbook),"non-consuming base identity for "+v.WeaponClass);
                check(!Match(item,new ConditionItem{target=["other"]},handbook),"wrong base rejected");
                check(Match(item,new ConditionItem{target=["other"]},handbook,true),"native successful decision preserved");
                check(item.StringTemplateId==v.TemplateId,"owned template unchanged");
                check(Match(item,new ConditionFindItem{target=[v.SourceTemplateId]},handbook),"find-item exact base identity");
            }
        });
        run("Quest category matching honors the native FindItem category gate",()=>{
            var v=samples[0];var item=new Item{StringTemplateId=v.TemplateId};var handbook=new Handbook();handbook.Children.Add((v.SourceTemplateId,"category"));
            check(Match(item,new ConditionItem{target=["category"]},handbook),"handover category fallback");
            check(!Match(item,new ConditionFindItem{target=["category"],TargetIsCategory=false},handbook),"exact FindItem cannot gain category matching");
            check(Match(item,new ConditionFindItem{target=["category"],TargetIsCategory=true},handbook),"category FindItem can match source category");
        });
        run("Quest identity adapter preserves specific legendary and unrelated third-party requirements",()=>{
            var v=samples[0];var handbook=new Handbook();var same=catalog.Variants.First(x=>x.SourceTemplateId==v.SourceTemplateId&&x.TemplateId!=v.TemplateId);
            check(!Match(new Item{StringTemplateId=v.TemplateId},new ConditionItem{target=[same.TemplateId]},handbook),"different legendary cannot satisfy specific target");
            check(!Match(new Item{StringTemplateId=v.SourceTemplateId},new ConditionItem{target=[v.TemplateId]},handbook),"ordinary base cannot satisfy legendary target");
            check(!Match(new Item{StringTemplateId="third-party"},new ConditionItem{target=[v.SourceTemplateId]},handbook),"unknown modded item is not aliased");
            check(Match(new Item{StringTemplateId="third-party"},new ConditionItem{target=["third-party"]},handbook,true),"original third-party match preserved");
        });
        run("Equipment identity augmentation supports both native input paths without changing inventory",()=>{
            var a=samples[2];var b=samples[3];var worn=new List<string>{a.TemplateId,b.TemplateId};var expanded=LegendaryQuestItems.WithSources(worn);
            check(expanded.Contains(a.SourceTemplateId)&&expanded.Contains(b.SourceTemplateId),"base requirements see equipped clones");
            check(worn.SequenceEqual([a.TemplateId,b.TemplateId]),"inventory IDs unchanged");
            check(new[]{a.SourceTemplateId,b.SourceTemplateId}.All(expanded.Contains),"all members of required group accepted");
            check(!new[]{a.SourceTemplateId,"missing"}.All(expanded.Contains),"missing AND-group item still fails");
            check(new[]{b.SourceTemplateId}.All(expanded.Contains),"forbidden group also recognizes base identity");
            var method=typeof(LegendaryQuestEquipmentPatch).GetMethod("Transpiler",BindingFlags.NonPublic|BindingFlags.Static)!;
            var toList=typeof(Enumerable).GetMethods().Single(m=>m.Name=="ToList"&&m.GetParameters().Length==1).MakeGenericMethod(typeof(string));
            var getter=typeof(Inventory).GetProperty(nameof(Inventory.EquippedInSlotsTemplateIds))!.GetMethod!;
            var code=((IEnumerable<CodeInstruction>)method.Invoke(null,[new[]{new CodeInstruction(OpCodes.Call,toList),new CodeInstruction(OpCodes.Callvirt,getter)}])!).ToArray();
            check(code.Length==4&&code[1].operand is MethodInfo m&&m.Name=="WithSources"&&code[3].operand is MethodInfo n&&n.Name=="WithSources","both native paths get separate identity views");
        });
        run("Gunsmith rejects legendary weapons and protected contents while preserving native stat failures",()=>{
            bool Fits(Weapon weapon,bool native)
            {
                object?[] args=[weapon,native];typeof(LegendaryQuestAssemblyPatch).GetMethod("Postfix",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args);return (bool)args[1]!;
            }
            var v=samples[0];var legendary=new Weapon{StringTemplateId=v.TemplateId};var ordinary=new Weapon{StringTemplateId=v.SourceTemplateId};
            check(!Fits(legendary,true),"legendary weapon rejected even when native requirements pass");
            check(Fits(ordinary,true),"eligible ordinary weapon accepted");
            check(!Fits(ordinary,false),"native numerical or attachment failure preserved");
            ordinary.Children.Add(new Item{StringTemplateId=samples[4].TemplateId});check(!Fits(ordinary,true),"protected contents prevent consuming the ordinary parent");
        });
        run("Use, sale and quest-marker adapters retain their non-consuming base identity matching",()=>{
            var contains=typeof(Enumerable).GetMethods().Single(m=>m.Name=="Contains"&&m.GetParameters().Length==2).MakeGenericMethod(typeof(string));
            foreach(var type in new[]{typeof(LegendaryQuestUsePatch),typeof(LegendaryQuestSellPatch),typeof(LegendaryQuestBadgePatch),typeof(LegendaryQuestWorldItemPatch)})
            {
                var method=type.GetMethod("Transpiler",BindingFlags.NonPublic|BindingFlags.Static)!;var guard=new CodeInstruction(OpCodes.Ldc_I4_0,null);
                var code=((IEnumerable<CodeInstruction>)method.Invoke(null,[new[]{guard,new CodeInstruction(OpCodes.Call,contains)}])!).ToArray();
                check(code.Length==2&&ReferenceEquals(code[0],guard)&&code[1].operand is MethodInfo replacement&&replacement.DeclaringType==typeof(LegendaryQuestItems),"only native comparison replaced in "+type.Name);
            }
            var v=samples[3];check(LegendaryQuestItems.Contains([v.SourceTemplateId],v.TemplateId),"non-consuming base matching retained");
            check(!LegendaryQuestItems.Contains(["other"],v.TemplateId),"wrong non-consuming target rejected");
        });
        run("Every EFB category is hidden from hand-in and weapon-assembly selection even after a native match",()=>{
            var handbook=new Handbook();
            foreach(var v in samples)
            foreach(var condition in new ConditionItem[]{new ConditionHandoverItem{target=[v.SourceTemplateId]},new ConditionWeaponAssembly{target=[v.SourceTemplateId]}})
            {
                var item=new Item{StringTemplateId=v.TemplateId};
                check(!Match(item,condition,handbook,true),"native positive cannot offer protected copy");
                check(!Match(item,condition,handbook,false),"base equivalence cannot offer protected copy");
                condition.target=[v.TemplateId];check(!Match(item,condition,handbook,true),"explicit legendary target cannot consume it either");
                check(item.StringTemplateId==v.TemplateId,"copy identity unchanged");
            }
        });
        run("Ordinary hand-in eligibility remains native and protected nested contents are excluded",()=>{
            var handbook=new Handbook();var root=new Item{StringTemplateId="ordinary-container"};var condition=new ConditionHandoverItem{target=["ordinary-container"]};
            check(Match(root,condition,handbook,true),"ordinary native match retained");
            check(!Match(root,condition,handbook,false),"native rejection retained");
            var nested=new Item{StringTemplateId="ordinary-inner"};root.Children.Add(nested);nested.Children.Add(new Item{StringTemplateId=samples[2].TemplateId});
            check(!Match(root,condition,handbook,true),"deep legendary contents block parent hand-in");
            check(Match(root,new ConditionFindItem{target=["ordinary-container"]},handbook,true),"non-consuming finding remains eligible");
            check(root.Children.Single()==nested&&nested.Children.Count==1,"contents not removed by eligibility checking");
        });
        run("Final client hand-in guard rejects a whole mixed request before native discard",()=>{
            bool Proceed(Item[] items,out OperationResult<QuestHandoverResult> result)
            {
                object?[] args=[items,default(OperationResult<QuestHandoverResult>)];var allowed=(bool)typeof(LegendaryQuestHandoverGuardPatch).GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args)!;result=(OperationResult<QuestHandoverResult>)args[1]!;return allowed;
            }
            var ordinary=new Item{StringTemplateId="ordinary"};var legendary=new Item{StringTemplateId=samples[0].TemplateId};
            check(!Proceed([ordinary,legendary],out var blocked)&&blocked.Error?.ToString()==LegendaryQuestItems.ProtectedMessage,"mixed request blocked with a readable reason");
            var root=new Item{StringTemplateId="ordinary-container"};root.Children.Add(legendary);
            check(!Proceed([ordinary,root],out _),"ordinary container with legendary also blocked");
            check(Proceed([ordinary],out var allowed)&&allowed.Error==null,"ordinary hand-in proceeds normally");
            check(root.Children.Single()==legendary&&legendary.StringTemplateId==samples[0].TemplateId,"all physical copies and containers retained");
        });
        run("Placement selector rejects protected copies, including contents and explicit matches",()=>{
            bool Place(Item item,bool native)
            {object?[] args=[item,native];typeof(LegendaryQuestPlacePatch).GetMethod("Postfix",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args);return (bool)args[1]!;}
            foreach(var v in samples)check(!Place(new Item{StringTemplateId=v.TemplateId},true),"protected category unavailable for placement");
            var ordinary=new Item{StringTemplateId="ordinary"};check(Place(ordinary,true),"ordinary native placement retained");check(!Place(ordinary,false),"native placement failure retained");
            ordinary.Children.Add(new Item{StringTemplateId=samples[4].TemplateId});check(!Place(ordinary,true),"container contents protected");
        });
        run("Placement completion cancels safely if protected contents are added after selection",()=>{
            var root=new Item{StringTemplateId="ordinary-container"};var state=new InteractionContextHelper.CG_GetAvailableActions5{resultItem=root};
            bool Complete(bool successful)
            {object?[] args=[state,successful];typeof(LegendaryQuestPlaceCommitPatch).GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args);return (bool)args[1]!;}
            check(Complete(true),"ordinary completion unchanged");
            root.Children.Add(new Item{StringTemplateId=samples[0].TemplateId});check(!Complete(true),"late protected content takes the native cancellation path");
            check(!Complete(false),"already canceled action stays canceled");
            check(root.Children.Count==1,"guard never removes the protected contents");
        });
        run("Missing quest comparison boundaries fail rather than changing unrelated calls",()=>{
            foreach(var code in new[]{Array.Empty<CodeInstruction>(),new[]{new CodeInstruction(OpCodes.Nop,null)}})
            {
                var failed=false;try{_=LegendaryQuestItems.ContainsCalls(code).ToArray();}catch(InvalidOperationException){failed=true;}
                check(failed,"missing boundary rejected");
            }
        });
    }
    private sealed class Handbook:IBaseHandbook
    {
        internal readonly HashSet<(string,string)> Children=[];
        public bool IsChildOf(string item,string parent)=>Children.Contains((item.ToString(),parent.ToString()));
    }
}
