using SPTarkov.Server.Core.Models.Common;using SPTarkov.Server.Core.Models.Eft.Common.Tables;using SPTarkov.Server.Core.Models.Enums;
namespace EscapeFromBorderlands.Server;
internal sealed class StandaloneFixture {
private readonly DatabaseFixture databaseService=new();
private BotTableFixture botTable;private TemplateTableFixture templateTable=new();internal StandaloneFixture(){botTable=new(databaseService);}
private sealed class BotTableFixture(DatabaseFixture db){internal Dictionary<string,BotType> Types=>db.Bots;}
private sealed class TemplateTableFixture{internal Dictionary<MongoId,TemplateItem> Items=new();}
private IReadOnlyCollection<SpawnVariant> _spawnVariants=[];
internal Dictionary<string,BotType> Bots=>databaseService.Bots;
internal void Definitions(IEnumerable<(VariantDefinition Variant,EquipmentSlots[] Slots)> definitions)=>_spawnVariants=definitions.Select(d=>new SpawnVariant(d.Variant,d.Slots)).ToArray();
internal int Apply(RarityConfiguration config,string method="standalone",bool nonPmcOnly=false)=>ApplyCategoryPools(config,method,nonPmcOnly);
private sealed record SpawnVariant(VariantDefinition Variant,EquipmentSlots[] EquipmentSlots);
private sealed class DatabaseFixture {
internal readonly Dictionary<string,BotType> Bots=new();
internal BotContainer GetBots()=>new(Bots);
internal Dictionary<MongoId,TemplateItem> GetItems()=>new();
}
private sealed record BotContainer(Dictionary<string,BotType> Types);
private int ApplyCategoryPools(RarityConfiguration config, string method = "standalone", bool nonPmcOnly = false)
    {
        var slots = new[] { EquipmentSlots.FirstPrimaryWeapon, EquipmentSlots.SecondPrimaryWeapon, EquipmentSlots.Holster, EquipmentSlots.Scabbard, EquipmentSlots.Backpack, EquipmentSlots.TacticalVest, EquipmentSlots.ArmorVest, EquipmentSlots.Earpiece };
        var catalog = LegendaryCatalog.Load();
        var allEfb = catalog.Variants.Select(v => new MongoId(v.TemplateId)).Concat(catalog.LegacyAliases.Select(v => new MongoId(v.TemplateId))).ToHashSet();
        var variantWeights = _spawnVariants.ToDictionary(v => v.Variant.TemplateId, v => v.Variant.SpawnWeight);
        var total = 0;
        foreach (var (role, bot) in botTable.Types)
        {
            if (bot is null) continue;
            if (nonPmcOnly && BotSpawnGroups.Group(role) is "pmcUsec" or "pmcBear") continue;
            foreach (var slot in slots)
            {
                if (!bot.BotInventory.Equipment.TryGetValue(slot, out var pool)) continue;
                foreach (var id in allEfb) pool.Remove(id);
                var category = RarityConfiguration.Category(slot.ToString());
                var fraction = config.BotFraction(method, role, category);
                if (fraction <= 0d) continue;
                var sources = _spawnVariants.Where(v => v.EquipmentSlots.Contains(slot))
                    .GroupBy(v => v.Variant.SourceTemplateId)
                    .ToDictionary(g => g.Key, g => g.Select(v => v.Variant.TemplateId).ToArray());
                var originals = pool.ToDictionary(p => p.Key.ToString(), p => p.Value);
                if (category == "guns")
                    foreach (var source in sources.Keys.ToArray())
                        if (!bot.BotInventory.Mods.ContainsKey(new MongoId(source))) sources.Remove(source);
                var weights = CategorySpawnPolicy.Weights(originals, sources, fraction, variantWeights);
                foreach (var (id, weight) in weights)
                {
                    var definition = _spawnVariants.First(v => v.Variant.TemplateId == id).Variant;
                    if (category is "guns" or "chestRigs") CopyBossWeaponModPool(bot.BotInventory.Mods, new MongoId(definition.SourceTemplateId), new MongoId(id));
                    RigPanelModPool(bot.BotInventory.Mods,definition);
                    pool[new MongoId(id)] = weight;
                    total++;
                }
            }
        }
        return total;
    }
private static bool CopyBossWeaponModPool(
        Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>> modPools,
        MongoId sourceId,
        MongoId variantId)
    {
        if (!modPools.TryGetValue(sourceId, out var sourceMods))
        {
            return false;
        }

        modPools[variantId] = sourceMods.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.ToHashSet(),
            StringComparer.Ordinal);
        return true;
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
    private static string RigPanelId(VariantDefinition variant, string slot) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("efb:rig-panel:" + variant.StorageFamilyId + ":" + slot)))[..24].ToLowerInvariant();
}
