using System.Reflection;
using System.Text.Json;
using EscapeFromBorderlands.Client;
using EFT.InventoryLogic;
using EFT.UI;

var cases=new List<object>();int passed=0,failed=0,assertions=0;
var flags=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
void Check(bool condition,string detail){assertions++;if(!condition)throw new Exception(detail);}
void Test(string name,Action action){try{RarityResolver.ResetSession();action();passed++;cases.Add(new{name,status="PASS"});Console.WriteLine("PASS "+name);}catch(Exception e){failed++;cases.Add(new{name,status="FAIL",error=e.GetBaseException().Message});Console.WriteLine("FAIL "+name+": "+e.GetBaseException().Message);}}
void Hook(Type type,string name,params object?[] args)=>type.GetMethod(name,flags)!.Invoke(null,args);
void Description(ItemInfoWindowLabels labels,string value){object?[] args=[labels,value];Hook(typeof(ItemDescriptionLabelPatch),"Prefix",args);labels.SetDescriptionText((string)args[1]!);}
void Inspect(Item item,ItemInfoWindowLabels labels,string caption,string category,string description,string size,bool examined=true){
 var panel=new ItemSpecificationPanel(item,labels){IsExamined=examined};
 // Native Show displays the prefab, calls method_1, then method_0. Its method_1
 // sets type/description/weight and remaining attributes after the EFB prefix.
 Hook(typeof(ItemDescriptionPatch),"Prefix",panel);
 labels.Category=examined?category:"?";Description(labels,examined?description:"");labels.Size=size;
 labels.SetCaptionText(caption);Hook(typeof(InspectItemNameColorPatch),"Postfix",panel);
}
Item ItemFor(string tpl,int n=0)=>new(){Id="inspection-"+tpl+"-"+n,StringTemplateId=tpl};
var catalog=LegendaryCatalog.Load();var bags=catalog.Variants.Where(v=>v.BackpackEffect!=null).ToArray();
var first=bags.First();
Test("Backpack inspection reaches native category, description and size initialization",()=>{
 var labels=new ItemInfoWindowLabels();Inspect(ItemFor(first.TemplateId),labels,first.DisplayName,"GEAR > BACKPACK",first.Description,"5 x 6");
 Check(labels.Category=="GEAR > BACKPACK"&&labels.Size=="5 x 6","prefab placeholders survived");
 Check(labels.Description.Contains("LEGENDARY")&&!labels.Description.Contains("blablabla"),"Legendary description missing");
});
Test("Every backpack identity preserves its native category and storage-size text",()=>{
 Check(bags.Length==1340,"wrong expanded backpack count");var labels=new ItemInfoWindowLabels();
 foreach(var d in bags){Inspect(ItemFor(d.TemplateId),labels,d.DisplayName,"GEAR > BACKPACK",d.Description,"NATIVE SIZE");Check(labels.Category=="GEAR > BACKPACK"&&labels.Size=="NATIVE SIZE",d.TemplateId+" native UI blocked");Check(labels.Caption.Contains("#FF9D28"),"orange name missing");Check(labels.Description.Contains(d.EffectText),"signature missing");}
});
Test("Firearms and melee inspection also complete native initialization",()=>{
 foreach(var d in catalog.Variants.Where(v=>v.BackpackEffect==null)){var labels=new ItemInfoWindowLabels();var category=d.MeleeEffect!=null?"GEAR > MELEE":"WEAPON > NATIVE CLASS";Inspect(ItemFor(d.TemplateId),labels,d.DisplayName,category,d.Description,"NATIVE ATTRIBUTES");Check(labels.Category==category&&labels.Size=="NATIVE ATTRIBUTES","native labels overwritten");Check(labels.Description.Contains(d.EffectText),"signature lost");}
});
Test("Vanilla and third-party items retain their native text on a reused window",()=>{
 var labels=new ItemInfoWindowLabels();Inspect(ItemFor(first.TemplateId),labels,first.DisplayName,"GEAR > BACKPACK",first.Description,"NATIVE SIZE");
 foreach(var tpl in catalog.Variants.Select(v=>v.SourceTemplateId).Distinct().Append("aabbccddeeff001122334455")){Inspect(ItemFor(tpl),labels,"Vanilla caption","NATIVE CATEGORY","Original description","Original size");Check(labels.Caption=="Vanilla caption"&&labels.Description=="Original description"&&labels.Size=="Original size","EFB state leaked to non-EFB item");}
});
Test("Unexamined EFB items do not disclose their rolled description",()=>{
 var labels=new ItemInfoWindowLabels();var item=ItemFor(first.TemplateId);Inspect(item,labels,first.DisplayName,"GEAR > BACKPACK",first.Description,"NATIVE SIZE");Inspect(item,labels,"Unknown item","?","","NATIVE SIZE",false);Check(labels.Description=="","unexamined effects disclosed");
});
Test("Repeated refreshes keep a single rolled block and signature",()=>{
 var labels=new ItemInfoWindowLabels();var item=ItemFor(first.TemplateId);Inspect(item,labels,first.DisplayName,"GEAR > BACKPACK",first.Description,"NATIVE SIZE");var once=labels.Description;Description(labels,once);Check(labels.Description==once,"description block duplicated");
});
Test("Server signature fallbacks become one instance roll",()=>{
 foreach(var prefix in new[]{first.EffectText,"<color=#66D9EF>"+first.EffectText+"</color>"}){var text=ItemDescriptionPresentation.Compose(ItemFor(first.TemplateId),prefix+"\n\nBase description");Check(text.EndsWith("Base description")&&text.Split(first.EffectText).Length==2,"signature repeated or base description lost");}
});
Test("Mystery offers retain four unknown bonuses",()=>{
 var d=bags.First(v=>v.IsMysteryFleaOffer);var labels=new ItemInfoWindowLabels();Inspect(ItemFor(d.TemplateId),labels,d.DisplayName,"GEAR > BACKPACK",d.Description,"NATIVE SIZE");Check(labels.Description.Split("????").Length==5,"mystery offer exposed a roll");
});
Test("A reflected getter failure cannot escape the presentation prefix",()=>{
 var labels=new ItemInfoWindowLabels();var panel=new ItemSpecificationPanel(ItemFor(first.TemplateId),labels){ThrowGetter=true};Hook(typeof(ItemDescriptionPatch),"Prefix",panel);Description(labels,"Native fallback");Check(labels.Description=="Native fallback","stale binding survived failure");
});
Test("Missing item or labels cannot abort native inspection",()=>{
 Hook(typeof(ItemDescriptionPatch),"Prefix",new ItemSpecificationPanel(null,new ItemInfoWindowLabels()));Hook(typeof(ItemDescriptionPatch),"Prefix",new ItemSpecificationPanel(ItemFor(first.TemplateId),null));
});
Console.WriteLine($"Inspection presentation: {passed}/{passed+failed} cases passed; {assertions} assertions; {failed} failures.");
if(args.Length>0)File.WriteAllText(args[0],JsonSerializer.Serialize(new{scope="Production presentation/resolver/catalog with native-shaped UI doubles; no Unity rendering",passed,failed,assertions,cases},new JsonSerializerOptions{WriteIndented=true}));
return failed==0?0:1;
