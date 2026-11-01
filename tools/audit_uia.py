"""Inspect the supplied UIA binary contract without running its game/UI code."""
import argparse
import hashlib
import json
import re
from pathlib import Path
from ecma import Assembly

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source',required=True,type=Path)
    parser.add_argument('--uia-dll',required=True,type=Path)
    parser.add_argument('--output',required=True,type=Path)
    args=parser.parse_args()
    a=Assembly(args.uia_dll);checks=[]
    def check(name,ok):checks.append(dict(name=name,status='PASS' if ok else 'FAIL'))
    def methods(name):
        tid=a.find('^'+re.escape(name)+'$')[0][0]
        return a.members(tid,6)
    def method(name,member):return next(rid for rid,row in methods(name) if row['Name']==member)
    def il(name,member):return '\n'.join(a.il(method(name,member)))
    row=a.rows(32)[0];version='.'.join(str(row[k]) for k in ['MajorVersion','MinorVersion','BuildNumber','RevisionNumber'])
    check('Supplied UIA assembly is 2.1.6.0',version=='2.1.6.0')
    constants={}
    for row in a.rows(11):
        table,rid=a.coded('HasConstant',row['Parent'])
        if table==4 and row['Type']==14:constants[a.tables[4][rid]['Name']]=a.blob(row['Value']).decode('utf-16-le')
    source=args.source/'src/EscapeFromBorderlands.Client'
    plugin=(source/'Plugin.cs').read_text();inventory=(source/'BackpackInventoryAccess.cs').read_text();adapter=(source/'UseItemsAnywhereCompatibility.cs').read_text()
    check('Optional dependency matches the supplied plugin GUID',constants.get('PluginGuid')=='com.cj.useFromAnywhere' and 'BepInDependency("com.cj.useFromAnywhere", BepInDependency.DependencyFlags.SoftDependency)' in plugin)
    queries='UseItemsAnywhere.Patches.InventorySlotQueries'
    sig='void (EFT.InventoryLogic.InventoryController, System.Collections.Generic.IList`1<!!0>, System.Predicate`1<!!0>)'
    for name in ['CollectGrenades','CollectReloadItems','CollectNativeItems']:
        rid=method(queries,name);row=a.tables[6][rid]
        check('Actual static collector signature: '+name,a.signature(6,rid)==sig and bool(row['Flags']&0x10))
    wheel='UseItemsAnywhere.QuickUseWheel.QuickUseWheelInventory'
    rid=method(wheel,'HasResource')
    check('Actual wheel resource predicate is static bool(Item)',a.signature(6,rid)=='bool (EFT.InventoryLogic.Item)' and bool(a.tables[6][rid]['Flags']&0x10))
    patch='UseItemsAnywhere.Patches.InventoryQueryPatches';enable=il(patch,'Enable');ctor=il(patch,'.cctor');transpiler=il(patch,'Transpiler')
    check('Harmony ordering uses the actual UIA query owner',"'com.cj.useFromAnywhere.inventory-queries'" in enable and '[HarmonyAfter("com.cj.useFromAnywhere.inventory-queries")]' in inventory)
    check('UIA uses default query priority and EFB runs last','set_priority' not in enable and 'stfld HarmonyLib.HarmonyMethod::priority' not in enable and '[HarmonyPriority(Priority.Last)]' in inventory)
    strings=re.findall(r"ldstr '([^']+)'",ctor)
    mappings=[strings[i:i+3] for i in range(0,len(strings),3)]
    expected=[['EFT.PlayerInputTranslator','vmethod_1','CollectGrenades'],['EFT.FirearmHandsInputTranslator','ReloadExternalMagazine','CollectReloadItems'],['BotReload','GetMagazineForReload','CollectNativeItems'],['BotReload','AddAmmoToMagazines','CollectNativeItems']]
    check('All four overlapping targets use the reviewed collectors',all(value in mappings for value in expected))
    check('Actual UIA rewrite validates and replaces one typed inventory call',all(value in transpiler for value in ["'GetReachableItemsOfTypeNonAlloc'",'::MakeGenericMethod',"'Expected one inventory query in {0}, found {1}'",'CodeInstruction::operand']))
    for name,category in [('CollectGrenades',2),('CollectReloadItems',4)]:
        body=il(queries,name)
        check('UIA configured category and co-op fallback retained: '+name,all(value in body for value in ['::get_FeaturesEnabled','Inventory::FastAccessSlots','Configuration::get_ActiveRules',f'ldc.i4.{category}','InventorySlotQueries::Collect']))
    body=il(queries,'CollectNativeItems')
    check('Actual bot collector retains native slots instead of player settings','Inventory::FastAccessSlots' in body and 'get_ActiveRules' not in body)
    callers=[row['Name'] for rid,row in methods(wheel) if 'QuickUseWheelInventory::HasResource' in '\n'.join(a.il(rid))]
    check('Wheel availability paths pass through the patched resource predicate',set(callers)=={'IsItemStillUsable','CreateWheelItem','GetAvailabilityRank'})
    check('EFB resource postfix only narrows usable throwable results','if (__result && __0 is ThrowWeap && !ReusableGrenadeRuntime.Usable(__0)) __result = false;' in adapter)
    report=dict(version='0.30.0',uiaVersion=version,uiaDllSha256=hashlib.sha256(args.uia_dll.read_bytes()).hexdigest(),scope='Supplied binary metadata/IL and source integration contracts; no UIA/game code execution.',passed=sum(v['status']=='PASS' for v in checks),failed=sum(v['status']=='FAIL' for v in checks),resourceCallers=callers,overlappingTargets=expected,checks=checks)
    args.output.write_text(json.dumps(report,indent=2)+'\n');print(json.dumps({k:report[k] for k in ['uiaVersion','passed','failed']}))
    return int(bool(report['failed']))

if __name__=='__main__':raise SystemExit(main())
