using Path = System.IO.Path;
using System.Text.Json;
using System.Text.Json.Nodes;
using EscapeFromBorderlands.Server;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils.Json.Converters;

var itemsPath=Path.GetFullPath(args[0]);var destination=Path.GetFullPath(args[1]);Directory.CreateDirectory(destination);
using var db=JsonDocument.Parse(File.ReadAllText(itemsPath));
var catalog=LegendaryCatalog.Load();var all=catalog.Variants.Where(v=>v.BackpackEffect!=null).ToArray();
var options=new JsonSerializerOptions{Converters={new StringToMongoIdConverter()}};
var sources=new Dictionary<string,TemplateItemProperties>();
foreach(var id in all.Select(v=>v.SourceTemplateId).Distinct()){
 var source=JsonSerializer.Deserialize<TemplateItemProperties>(db.RootElement.GetProperty(id).GetProperty("_props").GetRawText(),options)!;
 foreach(var property in typeof(TemplateItemProperties).GetProperties().Where(p=>p.PropertyType==typeof(string)&&p.CanRead&&p.CanWrite&&p.GetIndexParameters().Length==0))if(property.GetValue(source)==null)property.SetValue(source,string.Empty);
 sources[id]=source;
}
var audit=new List<object>();
foreach(var variant in all){
 var source=sources[variant.SourceTemplateId];var before=JsonSerializer.Serialize(source,options);
 var actual=JsonSerializer.SerializeToNode(BackpackStorage.Apply(source,variant),options)!.AsObject();
 if(JsonSerializer.Serialize(source,options)!=before)throw new Exception("Native source mutated: "+variant.SourceTemplateId);
 var originalGrids=db.RootElement.GetProperty(variant.SourceTemplateId).GetProperty("_props").GetProperty("Grids");
 var grids=actual["Grids"]!.AsArray().Select((g,index)=>new{
  index,gridName=g!["_name"]!.GetValue<string>(),width=g["_props"]!["cellsH"]!.GetValue<int>(),height=g["_props"]!["cellsV"]!.GetValue<int>(),
  sourceWidth=index<originalGrids.GetArrayLength()?originalGrids[index].GetProperty("_props").GetProperty("cellsH").GetInt32():0,
  sourceHeight=index<originalGrids.GetArrayLength()?originalGrids[index].GetProperty("_props").GetProperty("cellsV").GetInt32():0,
  addedSignatureGrid=index>=originalGrids.GetArrayLength(),exceeds7=g["_props"]!["cellsH"]!.GetValue<int>()>7
 }).ToArray();
 audit.Add(new{variant.TemplateId,variant.StorageFamilyId,variant.SourceTemplateId,variant.SourceShortName,variant.ShortName,variant.RuntimeKey,
  variant.StorageRollVersion,variant.StorageRows,variant.StorageColumns,variant.SpawnEnabled,variant.IsMysteryFleaOffer,
  sourceGridLayout=db.RootElement.GetProperty(variant.SourceTemplateId).GetProperty("_props").TryGetProperty("GridLayoutName",out var layout)?layout.GetString():null,
  sourceLargestWidth=originalGrids.EnumerateArray().Max(g=>g.GetProperty("_props").GetProperty("cellsH").GetInt32()),
  maxWidth=grids.Max(g=>g.width),overLimitGridCount=grids.Count(g=>g.exceeds7),exceeds7=grids.Any(g=>g.exceeds7),grids});
}
var aliases=catalog.LegacyAliases.Where(v=>sources.ContainsKey(v.SourceTemplateId)||v.WeaponClass=="backpack").Select(v=>new{
 v.TemplateId,v.SourceTemplateId,v.SourceShortName,v.ShortName,
 maxWidth=db.RootElement.GetProperty(v.SourceTemplateId).GetProperty("_props").GetProperty("Grids").EnumerateArray().Max(g=>g.GetProperty("_props").GetProperty("cellsH").GetInt32()),
 effectFree=true
}).ToArray();
var result=new{modVersion="0.29.1",sptVersion="4.1.6",catalogRevision=catalog.CatalogRevision,limitCells=7,rawBackpackDesigns=all.Count(v=>v.StorageRollVersion==0),nativeBackpackBases=sources.Count,auditedLayouts=all.Length,auditedRetiredAliases=aliases.Length,totalAuditedTemplateIds=all.Length+aliases.Length,retiredAliases=aliases,variants=audit};

File.WriteAllText(Path.Combine(destination,"backpack_width_audit.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Audited {all.Length} layouts across {sources.Count} native bases and {all.Count(v=>v.StorageRollVersion==0)} legendary designs using production catalog expansion and BackpackStorage.Apply.");
