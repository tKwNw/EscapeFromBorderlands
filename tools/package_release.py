"""Package the validated EFB 0.30.0 native SPT 4.1.6 port."""
import argparse
from datetime import datetime, timezone
import difflib
import hashlib
import json
from pathlib import Path
import re
import zipfile
from ecma import Assembly

VERSION = '0.30.0'
SUITES = {'fixes': 80, 'combat': 43, 'fireball': 37, 'storage': 54,
          'layout': 7, 'navigation': 29, 'visitor': 18, 'rigs': 140,
          'grenades': 199, 'spawns': 52, 'quests': 23, 'earpieces': 85,
          'explosionfx': 19, 'hollywoodfx': 5, 'uia-order': 11}
REPORTS = {'inspection': 10, 'presentation': 16, 'melee': 62, 'server': 52}
AUDITS = {'native-bindings', 'native-port-audit', 'backpack-width', 'performance', 'native-bindings-uia', 'native-port-audit-uia', 'versions'}

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def write_zip(path, entries):
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, source in sorted(entries.items()):
            archive.writestr(name, source.read_bytes())
    with zipfile.ZipFile(path) as archive:
        assert archive.testzip() is None, path.name

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ['source', 'baseline', 'build', 'validation', 'output']:
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    source, baseline, build, validation, output = [getattr(args, name).resolve() for name in ['source', 'baseline', 'build', 'validation', 'output']]
    output.mkdir(parents=True, exist_ok=True)
    assemblies = {}
    for kind in ['client', 'server']:
        path = next(p for p in [build / kind / f'EscapeFromBorderlands.{kind.title()}.dll', build / kind.title() / f'EscapeFromBorderlands.{kind.title()}.dll'] if p.exists())
        row = Assembly(path).rows(32)[0]
        assert '.'.join(str(row[k]) for k in ['MajorVersion', 'MinorVersion', 'BuildNumber', 'RevisionNumber']) == VERSION + '.0'
        log = (validation / ('build-' + kind + '.log')).read_text()
        assert all(value in log for value in ['Build succeeded.', '0 Warning(s)', '0 Error(s)']), kind
        assemblies[kind] = path
    behavior = []
    for name, count in SUITES.items():
        log = (validation / (name + '.log')).read_text()
        match = re.search(r'(\d+)/(\d+) cases passed; (\d+) assertions; 0 failures', log)
        if name in ['grenades', 'spawns', 'quests']:
            other = re.search(r'(\d+) cases, (\d+) assertions, 0 failures', log)
            assert other and int(other[1]) == count, name
            behavior.append({'name': name, 'passed': count, 'failed': 0, 'assertions': int(other[2])})
            continue
        assert match and int(match[1]) == int(match[2]) == count, name
        assert not re.search(r'^FAIL |: error |^Unhandled exception', log, re.M), name
        behavior.append({'name': name, 'passed': count, 'failed': 0, 'assertions': int(match[3])})
    for name, count in REPORTS.items():
        result = json.loads((validation / (name + '.json')).read_text())
        assert result['passed'] == count and result['failed'] == 0, name
        behavior.append({'name': name, 'passed': count, 'failed': 0, **({'assertions': result['assertions']} if 'assertions' in result else {})})
    evidence = {name: json.loads((validation / (name + '.json')).read_text()) for name in ['native-bindings', 'native-port-audit', 'native-semantics', 'reflection', 'port-contracts']}
    for name in ['native-bindings', 'native-semantics', 'reflection']:
        assert evidence[name]['failed'] == 0, name
    assert all(row['status'] == 'PASS' for row in evidence['port-contracts']['checks'])
    assert all(row['exit'] == 0 for row in json.loads((validation / 'suite-runs.json').read_text()))
    assert json.loads((validation / 'number-style.json').read_text())['violations'] == 0
    assert json.loads((validation / 'preservation.json').read_text())['failed'] == 0
    assert json.loads((validation / 'carry-forward.json').read_text())['failed'] == 0
    assert json.loads((validation / 'versions.log').read_text())['errors'] == 0
    runs = json.loads((validation / 'suite-runs.json').read_text())
    assert {row['name'] for row in runs} == set(SUITES) | set(REPORTS) | AUDITS
    assert all(row['exit'] == 0 for row in json.loads((validation / 'static-runs.json').read_text()))
    assert {key: evidence['native-bindings'][key] for key in ['patchClasses', 'nativeTargets', 'parametersChecked']} == {'patchClasses': 281, 'nativeTargets': 354, 'parametersChecked': 648}
    assert evidence['reflection']['passed'] == 199 and evidence['native-semantics']['passed'] == 89
    performance = json.loads((validation / 'performance.log').read_text().splitlines()[-1])
    assert performance['idleFrames'] == 300 and all(performance[key] == 0 for key in ['rectTransformSizeWrites', 'layoutMarks', 'normalizedPositionWrites', 'ancestorComponentLookups'])
    width = json.loads((validation / 'backpack-width/backpack_width_audit.json').read_text())
    assert (width['auditedLayouts'], width['nativeBackpackBases'], width['catalogRevision']) == (1340, 44, 52)
    assert sum(v['exceeds7'] for v in width['variants']) == 48
    assert all(v['StorageRollVersion'] < 3 for v in width['variants'] if v['exceeds7'])
    assert json.loads((validation / 'audio-asset.json').read_text())['failed'] == 0
    for relative, expected_hash in json.loads((validation / 'test-input-sha256.json').read_text()).items():
        assert sha(source / relative) == expected_hash, 'Tested input changed: ' + relative
    actual = evidence['native-port-audit']
    assert actual['selectors'] == 64 and actual['passed'] == 62 and actual['skipped'] == 2 and actual['targets'] == 137 and actual['failed'] == 0
    assert len(actual['transpilers']) == 36 and all(row['status'] == 'PASS' for row in actual['transpilers'])
    for selector in actual['results']:
        if selector['status'] == 'SKIP':
            assert selector['patch'] in ['LegendaryHollywoodFxBlastPatch', 'UseItemsAnywhereGrenadeWheelPatch']
            assert selector['patch'] in [row['patch'] for row in evidence['native-bindings']['skipped']]
            continue
        expected = {(row['target'], tuple(p['Name'] for p in row['parameters']))
                    for row in evidence['native-bindings']['bindings'] if row['patch'] == selector['patch']}
        selected = {(row['owner'] + '.' + row['name'], tuple(row['parameters'])) for row in selector['targets']}
        assert expected == selected, 'Dynamic target-map mismatch: ' + selector['patch']
    uia_bindings=json.loads((validation/'native-bindings-uia.json').read_text())
    uia_audit=json.loads((validation/'native-port-audit-uia.json').read_text())
    uia_contracts=json.loads((validation/'uia-contracts.json').read_text())
    assert uia_bindings['failed']==uia_audit['failed']==uia_contracts['failed']==0
    assert (uia_bindings['patchClasses'],uia_bindings['nativeTargets'],uia_bindings['parametersChecked'])==(281,355,650)
    assert (uia_audit['selectors'],uia_audit['passed'],uia_audit['targets'],uia_audit['skipped'])==(64,63,138,1)
    assert len(uia_audit['uiaComposition'])==4 and all(row['status']=='PASS' for row in uia_audit['uiaComposition'])
    assert uia_contracts['passed']==15 and uia_contracts['uiaVersion']=='2.1.6.0'
    for selector in uia_audit['results']:
        if selector['status']=='SKIP':
            assert selector['patch']=='LegendaryHollywoodFxBlastPatch'
            continue
        expected={(row['target'],tuple(p['Name'] for p in row['parameters'])) for row in uia_bindings['bindings'] if row['patch']==selector['patch']}
        selected={(row['owner']+'.'+row['name'],tuple(row['parameters'])) for row in selector['targets']}
        assert expected==selected,'UIA-present dynamic target mismatch: '+selector['patch']
    status_inventory=json.loads((validation/'status-audit/Status_Effects_Inventory.json').read_text())
    assert status_inventory['summary']['version']==VERSION and status_inventory['summary']['namedDesigns']==1508
    for name,digest in status_inventory['sourceSha256'].items():
        assert sha(source/'src/EscapeFromBorderlands.Client'/name)==digest,'Stale status inventory: '+name
    catalogs = [source / f'src/EscapeFromBorderlands.{kind}/LegendaryDefinitions.json' for kind in ['Client', 'Server']]
    assert catalogs[0].read_bytes() == catalogs[1].read_bytes() == (baseline / 'src/EscapeFromBorderlands.Client/LegendaryDefinitions.json').read_bytes()
    config = source / 'config/config.json'
    assert config.read_bytes() == (baseline / 'config/config.json').read_bytes()
    json.loads(config.read_text())
    catalog = json.loads(catalogs[0].read_text())
    server = json.loads((validation / 'server.json').read_text())
    results = {'version': VERSION, 'targetSptVersion': '4.1.6', 'passed': sum(row['passed'] for row in behavior), 'failed': 0, 'suites': behavior}
    assert results['passed'] == 942
    (validation / 'behavior-results.json').write_text(json.dumps(results, indent=2) + '\n')
    excluded = {'bin', 'obj', '__pycache__', 'output', 'test-results', '.git'}
    files = sorted(p for p in source.rglob('*') if p.is_file() and not excluded.intersection(p.relative_to(source).parts))
    production = [p for p in files if p.relative_to(source).parts[0] in ['src', 'config'] or p.name == 'build.ps1']
    patch = []
    for path in production:
        relative = path.relative_to(source)
        before = baseline / relative
        if before.exists() and before.read_bytes() == path.read_bytes():
            continue
        assert path.suffix.lower() != '.wav', 'Embedded audio must be byte-preserved'
        patch.extend(difflib.unified_diff(before.read_text().splitlines(keepends=True) if before.exists() else [], path.read_text().splitlines(keepends=True), fromfile='legacy-0.29.0/' + relative.as_posix(), tofile='native-0.30.0/' + relative.as_posix()))
    (validation / 'migration.patch').write_text(''.join(patch))
    manifest = {
        'version': VERSION, 'assemblyVersion': VERSION + '.0', 'targetSptVersion': '4.1.6',
        'versionFormat': 'MAJOR.MINOR.PATCH',
        'carryForwardPolicy': 'PORTING.md', 'carryForwardChecks': json.loads((validation/'carry-forward.json').read_text()),
        'legacySourceVersion': '0.29.0', 'legacySptVersion': '4.0.13',
        'previousNativeVersion': '0.28.2', 'nativePortBaseVersion': '0.28.2', 'mergeBaseLegacyVersion': '0.28.0',
        'builtUtc': datetime.now(timezone.utc).isoformat(),
        'catalog': {'revision': catalog['catalogRevision'], 'rawIdentities': len(catalog['variants']),
                    'namedFamilies': len({v.get('storageFamilyId') or v['templateId'] for v in catalog['variants']}),
                    'activeIdentities': server['activeWeapons'], 'retiredAliases': server['retiredAliases'],
                    'firearms': server['activeFirearms'], 'melee': server['activeMelee'],
                    'grenades': server['activeGrenades'], 'earpieces': server['activeEarpieces'], 'backpacks': server['activeBackpacks'], 'chestRigs': server['activeChestRigs'],
                    'privateRigArmorPanels': server['privateRigArmorPanels'], 'presets': server['generatedPresets']},
        'behavior': results, 'nativeBindings': {key: evidence['native-bindings'][key] for key in ['patchClasses', 'nativeTargets', 'parametersChecked', 'failed']},
        'nativeMethodBodyChecks': evidence['native-semantics']['passed'], 'reflectionChecks': evidence['reflection']['passed'],
        'actualDynamicSelectors': actual['passed'], 'actualDynamicTargets': actual['targets'],
        'executedTranspilerTargets': len(actual['transpilers']),
        'optionalNativeSelectorSkips': evidence['native-bindings']['skipped'],
        'uiaCompatibility': {'version': uia_contracts['uiaVersion'], 'dllSha256': uia_contracts['uiaDllSha256'], 'binaryContracts': uia_contracts['passed'], 'presentNativeTargets': uia_bindings['nativeTargets'], 'presentParameterBindings': uia_bindings['parametersChecked'], 'modeledNativeQueryCompositions': uia_audit['uiaComposition']},
        'backpackLayoutsAudited': width['auditedLayouts'], 'retainedWideSavedLayouts': 48,
        'idleLayoutPerformance': performance,
        'exportCatalogApiChecks': len(evidence['port-contracts']['checks']),
        'catalogSha256': sha(catalogs[0]), 'referenceManifestSha256': evidence['port-contracts']['referenceManifestSha256'],
        'assemblies': {path.name: {'sha256': sha(path), 'bytes': path.stat().st_size} for path in assemblies.values()},
        'productionSourceSha256': {path.relative_to(source).as_posix(): sha(path) for path in production},
        'notRun': ['Live EFT/Unity rendering, physics and animations', 'Complete SPT host startup', 'Current APBS integration', 'Fika multiplayer'],
    }
    (validation / 'release-manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    runtime = {'BepInEx/plugins/EscapeFromBorderlands/EscapeFromBorderlands.Client.dll': assemblies['client'],
               'SPT_Runtime/user/mods/EscapeFromBorderlands/EscapeFromBorderlands.Server.dll': assemblies['server'],
               'SPT_Runtime/user/mods/EscapeFromBorderlands/config/config.json': config,
                'SPT_Runtime/user/mods/EscapeFromBorderlands/LICENSE.txt': source / 'LICENSE.txt',
                'SPT_Runtime/user/mods/EscapeFromBorderlands/THIRD_PARTY_NOTICES.md': source / 'THIRD_PARTY_NOTICES.md'}
    full = {'EscapeFromBorderlands/' + path.relative_to(source).as_posix(): path for path in files}
    assert not any(path.suffix.lower() in ['.dll', '.pdb', '.exe'] for path in full.values())
    # Only the requested Source and fresh-install Runtime archives are emitted.
    runtime_zip = output / f'EscapeFromBorderlands_{VERSION}_SPT-4.1.6_Runtime.zip'
    write_zip(runtime_zip, runtime)
    integrity = [{'file': runtime_zip.name, 'sha256': sha(runtime_zip), 'bytes': runtime_zip.stat().st_size, 'crc': 'PASS'}]
    (validation / 'package-integrity.json').write_text(json.dumps(integrity, indent=2) + '\n')
    for path in validation.rglob('*'):
        if path.is_file() and path.suffix in ['.json', '.log', '.patch']:
            full['EscapeFromBorderlands/validation/' + path.relative_to(validation).as_posix()] = path
    source_zip = output / f'EscapeFromBorderlands_{VERSION}_SPT-4.1.6_Source.zip'
    write_zip(source_zip, full)
    integrity.append({'file': source_zip.name, 'sha256': sha(source_zip), 'bytes': source_zip.stat().st_size, 'crc': 'PASS'})
    assert set(path.name for path in output.glob('*.zip')) == {runtime_zip.name, source_zip.name}
    (output / 'release-sha256.json').write_text(json.dumps(integrity, indent=2) + '\n')
    print(json.dumps({'version': VERSION, 'behaviorCases': results['passed'], 'files': integrity}, indent=2))

if __name__ == '__main__':
    main()
