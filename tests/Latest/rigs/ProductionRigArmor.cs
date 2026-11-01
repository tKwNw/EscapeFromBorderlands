using System.Text.Json;using SPTarkov.Server.Core.Models.Common;using SPTarkov.Server.Core.Models.Eft.Common.Tables;using SPTarkov.Server.Core.Utils.Json.Converters;
namespace EscapeFromBorderlands.Server;
internal sealed class RigArmorFixture {
internal RigDatabase databaseService=new();internal RigTemplateTable templateTable;internal RigLocales localeTable=new();internal RigCloner cloner=new();internal RigLocale localeService=new();
internal RigArmorFixture(){templateTable=new(databaseService);}
internal void Apply(TemplateItem rig,VariantDefinition variant)=>ApplyRigSoftArmor(rig,variant);
internal void Pools(Dictionary<MongoId,Dictionary<string,HashSet<MongoId>>> pools,VariantDefinition variant)=>RigPanelModPool(pools,variant);
    private static string RigPanelId(VariantDefinition variant, string slot) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("efb:rig-panel:" + variant.StorageFamilyId + ":" + slot)))[..24].ToLowerInvariant();
private void ApplyRigSoftArmor(TemplateItem rig, VariantDefinition variant)
    {
        if (variant.ChestRigEffect?.SoftArmorClass is not > 0) return;
        foreach (var slot in rig.Properties?.Slots ?? [])
        {
            if (slot.Name is not ("Soft_armor_front" or "Soft_armor_back" or "Groin")) continue;
            foreach (var filter in slot.Properties?.Filters ?? [])
            {
                var original = filter.Filter?.FirstOrDefault();
                if (original is null || !templateTable.Items.TryGetValue(original.Value, out var panel)) continue;
                var id = RigPanelId(variant, slot.Name!);
                if (!templateTable.Items.ContainsKey(new MongoId(id)))
                {
                    var copy = cloner.Clone(panel)!;
                    copy.Id = new MongoId(id);
                    var property = typeof(TemplateItemProperties).GetProperty("ArmorClass") ?? throw new InvalidOperationException("Native soft armor class property was not found.");
                    property.SetValue(copy.Properties!, Convert.ChangeType(variant.ChestRigEffect.SoftArmorClass, Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType));
                    templateTable.Items[copy.Id] = copy;
                    foreach(var language in localeTable.Global.Keys)
                    {
                        var locale=localeService.GetLocaleDb(language);
                        foreach(var suffix in new[]{" Name"," ShortName"," Description"})
                            if(locale.TryGetValue(original+suffix,out var value))locale[id+suffix]=value;
                    }
                }
                filter.Filter = [new MongoId(id)];
                var plate = filter.GetType().GetProperty("Plate");
                if (plate?.CanWrite == true) plate.SetValue(filter, plate.PropertyType == typeof(string) ? id : new MongoId(id));
            }
        }
    }
private void RigPanelModPool(Dictionary<MongoId,Dictionary<string,HashSet<MongoId>>> pools,VariantDefinition variant)
    {
        if(variant.ChestRigEffect?.SoftArmorClass is not >0)return;
        var id=new MongoId(variant.TemplateId);
        if(!pools.TryGetValue(id,out var mods))pools[id]=mods=new(StringComparer.Ordinal);
        if(templateTable.Items.TryGetValue(id,out var rig))
            foreach(var slot in rig.Properties?.Slots??[])
                if(slot.Name is "Soft_armor_front" or "Soft_armor_back" or "Groin")
                    mods[slot.Name]=[new MongoId(RigPanelId(variant,slot.Name))];
    }
}
