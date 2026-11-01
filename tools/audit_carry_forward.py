"""Verify the separate native repairs/compatibility layers survive an upstream merge."""
import argparse,json,re,hashlib
from pathlib import Path

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    for name in ['source','previous-native','output']:parser.add_argument('--'+name,type=Path,required=True)
    args=parser.parse_args();checks=[]
    def check(name,ok):checks.append(dict(name=name,status='PASS' if ok else 'FAIL'))
    for rel in [
        'src/EscapeFromBorderlands.Client/UseItemsAnywhereCompatibility.cs',
        'src/EscapeFromBorderlands.Client/BackpackInventoryAccess.cs',
        'src/EscapeFromBorderlands.Client/LegendaryHollywoodFx.cs',
        'src/EscapeFromBorderlands.Client/LegendaryExplosionFx.cs',
        'src/EscapeFromBorderlands.Client/BackpackPresentation.cs',
        'src/EscapeFromBorderlands.Client/ItemDescriptionPatch.cs',
        'src/EscapeFromBorderlands.Client/LegendaryQuestItems.cs',
        'src/EscapeFromBorderlands.Server/LegendaryQuestItems.cs',
        'src/EscapeFromBorderlands.Shared/QuestItemEquivalence.cs',
        'src/EscapeFromBorderlands.Server/ApbsCompatibility.cs',
        'src/EscapeFromBorderlands.Server/EarpieceCompatibility.cs',
        'src/EscapeFromBorderlands.Client/GrenadePackingSpeed.cs',
    ]:
        check('Separate native layer byte-preserved: '+rel,(args.source/rel).read_bytes()==(args.previous_native/rel).read_bytes())
    client=args.source/'src/EscapeFromBorderlands.Client'
    plugin=(client/'Plugin.cs').read_text()
    check('Native core dependency and optional UIA/HollywoodFX load dependencies retained',all(value in plugin for value in ['BepInDependency("com.SPT.core", "4.1.6")','BepInDependency("com.cj.useFromAnywhere", BepInDependency.DependencyFlags.SoftDependency)','BepInDependency("com.janky.hollywoodfx", BepInDependency.DependencyFlags.SoftDependency)']))
    medical=(client/'ChestRigMedical.cs').read_text();old=(args.previous_native/'src/EscapeFromBorderlands.Client/ChestRigMedical.cs').read_text()
    def without_presentation(text):
        text=re.sub(r'EffectStatusModel\.Notify\([^;]+;','',text)
        return re.sub(r'[\s{}]+','',text)
    check('Typed medical rules retain previous non-presentation tokens while adding status receipts',without_presentation(medical)==without_presentation(old))
    def block(text,name):
        start=text.index('internal static class '+name);at=text.index('{',start);depth=0
        for end in range(at,len(text)):
            depth+=(text[end]=='{')-(text[end]=='}')
            if depth==0:return text[start:end+1]
        raise ValueError(name)
    grenades=(client/'ReusableGrenadeRuntime.cs').read_text();old=(args.previous_native/'src/EscapeFromBorderlands.Client/ReusableGrenadeRuntime.cs').read_text()
    check('UIA-compatible last-priority grenade quick-select filter preserved',block(grenades,'ReusableGrenadeQuickSelectPatch')==block(old,'ReusableGrenadeQuickSelectPatch') and '[HarmonyPriority(Priority.Last)]\ninternal static class ReusableGrenadeQuickSelectPatch' in grenades)
    check('New backpack status uses the repaired native night-vision helper','case "BP15-1": active = BackpackPresentation.NightVisionOn(p);' in (client/'BackpackEffectStatus.cs').read_text())
    check('New medical status reads the native interruption flag','Native.Get(p.Key, "_interrupted") is not true' in (client/'BackpackNative.cs').read_text())
    report=dict(version='0.30.0',nativeBase='0.28.2',upstream='0.29.0',passed=sum(c['status']=='PASS' for c in checks),failed=sum(c['status']=='FAIL' for c in checks),scope='Byte preservation and targeted source contracts; behavioral/native API tests are separate.',checks=checks)
    args.output.write_text(json.dumps(report,indent=2)+'\n');print(json.dumps({k:report[k] for k in ['passed','failed']}))
    return int(bool(report['failed']))

if __name__=='__main__':raise SystemExit(main())
