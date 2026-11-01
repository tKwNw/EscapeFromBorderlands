using System.Runtime.CompilerServices;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;

// Exact supported APBS argument names/return shapes. Shared datasets are
// deliberately used for multiple roles, as in APBS 2.0/2.2's ExUsec/Default.
internal sealed class ApbsEquipmentFixture
{
    internal Dictionary<EquipmentSlots,Dictionary<MongoId,double>> Shared=new();
    internal bool CloneBeforeReturn;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Dictionary<EquipmentSlots,Dictionary<MongoId,double>> GetEquipmentByBotRole(string botRole,int tierNumber)=>CloneBeforeReturn?Shared.ToDictionary(p=>p.Key,p=>new Dictionary<MongoId,double>(p.Value)):Shared;
}
internal sealed class ApbsLootFixture
{
    internal Dictionary<MongoId,double> Shared=new();
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Dictionary<MongoId,double> GetLootFromCache(string botRole,bool isPmc,string lootType,object? botJsonTemplate=null,int botLevel=1,int tier=1,object? itemPriceMinMax=null)=>new(Shared);
}
