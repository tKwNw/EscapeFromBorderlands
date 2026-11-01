from pathlib import Path
here=Path(__file__).resolve().parent
text=(here.parents[2]/'src/EscapeFromBorderlands.Server/RarityMod.cs').read_text()
def block(marker):
    start=text.index(marker);brace=text.index('{',start);depth=0
    for end in range(brace,len(text)):
        depth+=(text[end]=='{')-(text[end]=='}')
        if depth==0:return text[start:end+1]
    raise RuntimeError(marker)
headers='using SPTarkov.Server.Core.Models.Common;using SPTarkov.Server.Core.Models.Eft.Common.Tables;using SPTarkov.Server.Core.Models.Enums;\nnamespace EscapeFromBorderlands.Server;\n'
fields='''internal sealed class StandaloneFixture {
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
'''
methods='\n'.join(block(m) for m in ['private int ApplyCategoryPools(', 'private static bool CopyBossWeaponModPool(', 'private void RigPanelModPool('])
start=text.index('    private static string RigPanelId(');end=text.index('\n',start)
(here/'ProductionStandalone.cs').write_text(headers+fields+methods+'\n'+text[start:end]+'\n}\n')
