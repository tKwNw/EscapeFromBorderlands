"""Read-only SPT export/catalog contract checks; does not compile or run EFT."""
import argparse
import hashlib
import json
import re
import shutil
from pathlib import Path

from ecma import Assembly


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--references', required=True, type=Path)
    parser.add_argument('--source', required=True, type=Path)
    parser.add_argument('--baseline', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    checks = []

    def check(group, name, ok, detail=None):
        entry = dict(group=group, name=name, status='PASS' if ok else 'FAIL')
        if detail is not None:
            entry['detail'] = detail
        checks.append(entry)

    def sha(path):
        return hashlib.sha256(path.read_bytes()).hexdigest()

    manifest = json.loads((args.references / 'export-manifest.json').read_text())
    verified = all(
        (args.references / f['path']).is_file()
        and (args.references / f['path']).stat().st_size == f['sizeBytes']
        and sha(args.references / f['path']) == f['sha256']
        for f in manifest['files']
    )
    check('export', 'Every exported file matches its size and SHA-256', verified,
          {'files': len(manifest['files'])})

    core = Assembly(args.references / 'Server/SPTarkov.Server.Core.dll')
    common = Assembly(args.references / 'Server/SPTarkov.Common.dll')
    game = Assembly(args.references / 'Client/Assembly-CSharp.dll')
    spt = Assembly(args.references / 'Client/SPT/spt-core.dll')
    bep = Assembly(args.references / 'Client/BepInEx/BepInEx.dll')

    def assembly_version(assembly):
        row = assembly.rows(32)[0]
        return '.'.join(str(row[k]) for k in
                        ('MajorVersion', 'MinorVersion', 'BuildNumber', 'RevisionNumber'))

    check('export', 'Native server and SPT client are 4.1.6.0',
          assembly_version(core) == assembly_version(spt) == '4.1.6.0')

    def methods(assembly, full_name):
        found = assembly.find('^' + re.escape(full_name) + '$')
        if len(found) != 1:
            return {}
        return {(row['Name'], assembly.signature(6, rid)): (rid, row)
                for rid, row in assembly.members(found[0][0], 6)}

    def require(assembly, full_name, method, signature):
        members = methods(assembly, full_name)
        check('serverApi', full_name + '.' + method, (method, signature) in members,
              signature)
        return members.get((method, signature))

    prefix = 'SPTarkov.Server.Core.'
    mongo = prefix + 'Models.Common.MongoId'
    template = prefix + 'Models.Eft.Common.Tables.TemplateItem'
    handbook = prefix + 'Models.Eft.Common.Tables.HandbookBase'
    bot_type = prefix + 'Models.Eft.Common.Tables.BotType'
    preset = prefix + 'Models.Spt.Tables.Preset'
    item = prefix + 'Models.Eft.Common.Tables.Item'
    dictionary = 'System.Collections.Generic.Dictionary`2'
    list_type = 'System.Collections.Generic.List`1'
    airdrop = prefix + 'Services.InRaid.AirdropService'
    loot_groups = f'System.Collections.Generic.IEnumerable`1<{list_type}<{item}>>'
    require(core, airdrop, 'GetAirdropLootConfigByType',
            prefix + 'Models.Spt.Services.AirdropLootRequest (System.Nullable`1<' + prefix + 'Models.Enums.SptAirdropTypeEnum>)')
    require(core, airdrop, 'GetLootThatFitsContainer',
            f'{loot_groups} ({item}, {loot_groups})')

    require(core, prefix + 'DI.IOnLoad', 'OnLoadAsync',
            'System.Threading.Tasks.Task (System.Threading.CancellationToken)')
    require(core, prefix + 'Models.Spt.Mod.IModMetadata', 'get_HasPrepatcher', 'bool ()')
    require(core, prefix + 'Models.Spt.Mod.IModMetadata', 'get_SptVersion',
            'SemanticVersioning.Range ()')
    require(core, prefix + 'Models.Spt.Tables.TemplateTable', 'get_Items',
            f'{dictionary}<{mongo},{template}> ()')
    require(core, prefix + 'Models.Spt.Tables.TemplateTable', 'get_Handbook', handbook + ' ()')
    require(core, prefix + 'Models.Spt.Tables.TemplateTable', 'get_Prices',
            f'{dictionary}<{mongo},double> ()')
    require(core, prefix + 'Models.Spt.Tables.GlobalTable', 'get_ItemPresets',
            f'{dictionary}<{mongo},{preset}> ()')
    require(core, prefix + 'Models.Spt.Tables.BotTable', 'get_Types',
            f'{dictionary}<string,{bot_type}> ()')
    require(core, prefix + 'Helpers.Items.ItemHelper', 'IsOfBaseclass',
            f'bool ({mongo}, {mongo})')
    require(core, prefix + 'Helpers.Items.ItemHelper', 'ReparentItemAndChildren',
            f'{list_type}<{item}> ({item}, {list_type}<{item}>)')
    require(core, prefix + 'Utils.Cloners.ICloner', 'Clone', '!!0 (!!0)')
    clone = require(core, prefix + 'Services.Modding.Custom.CustomItemService',
                    'CreateItemFromClone', prefix + 'Models.Spt.Mod.CreateItemResult ('
                    + prefix + 'Models.Spt.Mod.NewItemFromCloneDetails, System.Reflection.Assembly)')
    require(core, prefix + 'Services.Modding.Custom.CustomItemService', 'AddCustomWeaponToPMCs',
            'void (string, double, string)')
    if clone:
        rid, row = clone
        end = core.tables[6][rid + 1]['ParamList']
        params = [core.tables[8][i] for i in range(row['ParamList'], end)]
        check('serverApi', 'CreateItemFromClone callingAssembly is optional',
              any(p['Sequence'] == 2 and p['Flags'] & 0x10 for p in params))

    for method in ('Info', 'Success', 'Warning', 'Error'):
        found = require(common, 'SPTarkov.Common.Models.Logging.ISptLogger`1', method,
                        'void (string, System.Exception)')
        if found:
            rid, row = found
            end = common.tables[6][rid + 1]['ParamList']
            params = [common.tables[8][i] for i in range(row['ParamList'], end)]
            check('serverApi', method + ' exception argument is optional',
                  any(p['Sequence'] == 2 and p['Flags'] & 0x10 for p in params))

    require(core, prefix + 'Models.Eft.Common.Tables.BotTypeInventory', 'get_Equipment',
            f'{dictionary}<{prefix}Models.Enums.EquipmentSlots,{dictionary}<{mongo},double>> ()')
    require(core, prefix + 'Models.Eft.Common.Tables.BotTypeInventory', 'get_Mods',
            f'{dictionary}<{mongo},{dictionary}<string,System.Collections.Generic.HashSet`1<{mongo}>>> ()')
    require(core, preset, 'get_Items', f'{list_type}<{item}> ()')
    require(core, preset, 'get_Encyclopedia', f'System.Nullable`1<{mongo}> ()')

    required_client_types = ['EFT.EftClientBackendSession', 'EFT.Ballistics.DamageInfo']
    type_names = {game.typename(2, i) for i in range(1, len(game.tables[2]))}
    missing_client = [name for name in required_client_types if name not in type_names]
    client_ready = not missing_client
    check('clientReferences', 'Game assembly contains SPT 4.1 renamed type markers',
          client_ready, {'missing': missing_client})
    spt_game_ref = next(i for i, row in enumerate(spt.rows(35), 1)
                        if row['Name'] == 'Assembly-CSharp')
    spt_game_names = [spt.typename(1, i) for i, row in enumerate(spt.rows(1), 1)
                      if spt.coded('ResolutionScope', row['ResolutionScope']) == (35, spt_game_ref)]
    missing_spt = [name for name in spt_game_names if name not in type_names]
    check('clientReferences', 'SPT core direct game type references resolve',
          not missing_spt, {'missing': missing_spt})
    dependency_ok = ('BepInDependency' in (args.source / 'src/EscapeFromBorderlands.Client/Plugin.cs').read_text()
                     and 'typeof(SPT.Core.SPTCorePlugin)' in
                     (args.source / 'src/EscapeFromBorderlands.Client/Plugin.cs').read_text())
    check('clientMetadata', 'Client source records a hard SPT 4.1.6 core dependency and native assembly reference',
          dependency_ok and ('void (string, string)' in
                             [sig for _, sig in methods(bep, 'BepInEx.BepInDependency')]))

    client_catalog = args.source / 'src/EscapeFromBorderlands.Client/LegendaryDefinitions.json'
    server_catalog = args.source / 'src/EscapeFromBorderlands.Server/LegendaryDefinitions.json'
    catalog = json.loads(client_catalog.read_text())
    variants, aliases = catalog['variants'], catalog['legacyAliases']
    all_entries = variants + aliases
    items = json.loads((args.references / 'Database/items.json').read_text())
    handbook_db = json.loads((args.references / 'Database/handbook.json').read_text())
    globals_db = json.loads((args.references / 'Database/globals.json').read_text())
    handbook_ids = {row['Id'] for row in handbook_db['Items']}
    target_presets = globals_db['ItemPresets']
    roots = sorted({v['sourceTemplateId'] for v in variants})
    all_roots = {v['sourceTemplateId'] for v in all_entries}
    ids = [v['templateId'] for v in all_entries]

    def ancestors(template_id):
        result = set()
        while template_id and template_id not in result:
            result.add(template_id)
            template_id = items.get(template_id, {}).get('_parent')
        return result

    check('catalog', 'Client/server catalogs equal the latest 4.0.13 baseline bytes',
          client_catalog.read_bytes() == server_catalog.read_bytes() ==
          (args.baseline / 'src/EscapeFromBorderlands.Client/LegendaryDefinitions.json').read_bytes())
    check('catalog', 'Revision 52 retains all 1860 raw identities, 292 vanilla roots and 777 aliases',
          (catalog['catalogRevision'], len(variants), len(roots), len(aliases)) == (52, 1860, 292, 777))
    check('catalog', 'All active/alias IDs are unique 24-digit hex IDs',
          len(set(ids)) == len(ids) and all(re.fullmatch('[a-f0-9]{24}', i) for i in ids))
    check('catalog', 'No active or retired receiver ID collides with the target database',
          not set(ids).intersection(items))
    check('catalog', 'All active preset IDs are unique and absent from native presets',
          len({v['presetId'] for v in variants}) == len(variants)
          and not {v['presetId'] for v in variants}.intersection(target_presets))
    check('catalog', 'Every active and alias source root exists in SPT 4.1.6',
          all_roots.issubset(items))
    check('catalog', 'Every source root has a target handbook entry', all_roots.issubset(handbook_ids))
    protected = set(catalog['unlootableMeleeTemplateIds'])
    melee_variants = [v for v in variants if v.get('meleeEffect')]
    retired_melee = [v for v in aliases if v['weaponClass'] == 'melee']
    check('catalog', 'Unsupported boss models are excluded from active melee and recover through four plain aliases',
          len(melee_variants) == 48 and len(retired_melee) == 4 and len(protected) == 2
          and all(v['sourceTemplateId'] not in protected for v in melee_variants)
          and all(v['sourceTemplateId'] in protected for v in retired_melee)
          and all(not v.get('specialId') and not v.get('meleeEffect') for v in retired_melee))
    check('catalog', 'Supported melee loot list retains 21 native roots and excludes zombie and protected models',
          len(catalog['vanillaMeleeTemplateIds']) == 21
          and not protected.intersection(catalog['vanillaMeleeTemplateIds'])
          and '670ad7f1ad195290cd00da7a' not in catalog['vanillaMeleeTemplateIds'])
    red_rebels = [v for v in variants if v['sourceTemplateId'] == '5c0126f40db834002a125382']
    check('catalog', 'All three Red Rebel variants retain their IDs and omit redundant extraction text',
          len(red_rebels) == 3 and {v['templateId'] for v in red_rebels} ==
          {'10c97fde13442d33a6883f47', 'e05e82a117ad64097c147ba9', 'dc36546d8d5bd46592e81fb7'}
          and all(not re.search('extract|counts as a Red Rebel',
                               v['effectText'] + ' ' + v['description'], re.I) for v in red_rebels))
    check('catalog', 'Active roots descend from their native firearm, melee, backpack or chest-rig base class',
          all(('5645bcb74bdc2ded0b8b4578' if v.get('isEarpiece') else '543be6564bdc2df4348b4568' if v.get('grenadeChargesPerRaid', 0) > 0 else '5448e5284bdc2dcb718b4567' if v.get('chestRigEffect') else '5448e53e4bdc2d60728b4567' if v.get('backpackEffect') else '5447e1d04bdc2dff2f8b4567' if v.get('meleeEffect') else '5422acb9af1c889c16000029') in ancestors(v['sourceTemplateId']) for v in variants))
    check('catalog', 'Every active entry remains Legendary/orange with four modifiers',
          all(v['tier'] == 'Legendary' and v['backgroundColor'] == 'orange' and v['affixCount'] == 4
              for v in variants))
    named_families = {v.get('storageFamilyId') or v['templateId']: v for v in variants}
    check('catalog', 'Display names identify one Legendary family while storage identities retain their family name',
          len({v['displayName'] for v in named_families.values()}) == len(named_families)
          and all(v['displayName'] == named_families[v.get('storageFamilyId') or v['templateId']]['displayName'] for v in variants))
    check('catalog', 'Excluded mounted weapons, FN40GL and flare launchers remain absent',
          all(not re.search('FN40GL|NSV|PKTM|AGS-30|flare',
                            v['sourceFullName'] + ' ' + v['sourceShortName'], re.I)
              for v in all_entries))
    check('catalog', 'Shaped Charge stays retired',
          all(v.get('specialId') != 'wave015_shaped_charge' for v in variants))
    check('catalog', 'All target weapon classes/calibers agree with preserved active definitions',
          all(v.get('isEarpiece') or v.get('grenadeChargesPerRaid', 0) > 0 or v.get('meleeEffect') or v.get('backpackEffect') or v.get('chestRigEffect') or (items[v['sourceTemplateId']]['_props'].get('weapClass') == v['weaponClass']
              and items[v['sourceTemplateId']]['_props'].get('ammoCaliber') == v['caliberKey'])
              for v in variants))

    slots = items['55d7217a4bdc2d86028b456d']['_props']['Slots']
    eligible_slots = {}
    for root in sorted(all_roots):
        eligible_slots[root] = [s['_name'] for s in slots
            if s['_name'] in ('FirstPrimaryWeapon', 'SecondPrimaryWeapon', 'Holster', 'Scabbard', 'Backpack', 'TacticalVest', 'ArmorVest', 'Earpiece')
            and any(set(f.get('Filter', [])).intersection(ancestors(root))
                    for f in s['_props']['filters'])]
    check('catalog', 'Every source has an equipment slot or is a validated reusable throwable',
          all(slots or any(v['sourceTemplateId'] == root and v.get('grenadeChargesPerRaid', 0) > 0 for v in variants) for root, slots in eligible_slots.items()))
    preset_roots = {p.get('_encyclopedia') for p in target_presets.values()}
    missing_presets = [r for r in roots if r not in preset_roots]
    source_presets = [p for p in target_presets.values() if p.get('_encyclopedia') in roots]
    check('catalog', 'Every native preset used by the active catalog has valid target item references',
          all(all(part['_tpl'] in items for part in p['_items']) for p in source_presets))
    check('catalog', 'Native default preset roots match their encyclopedia receiver',
          all(next((part['_tpl'] for part in p['_items'] if part['_id'] == p['_parent']), None)
              == p['_encyclopedia'] for p in source_presets))
    check('preservation', 'Default config and stable roll resolver remain unchanged',
          all((args.source / path).read_bytes() == (args.baseline / path).read_bytes()
              for path in ['config/config.json', 'src/EscapeFromBorderlands.Client/RarityResolver.cs']))
    handlers = [p for p in (args.source / 'src/EscapeFromBorderlands.Client').glob('*.cs')
                if p.name != 'Plugin.cs']
    # Record every production source delta for review; semantic equivalence of
    # renamed hooks is checked separately by native bindings and behavior fixtures.
    source_deltas = []
    unchanged = []
    for side in ('Client', 'Server'):
        for path in sorted((args.source / ('src/EscapeFromBorderlands.' + side)).glob('*.cs')):
            original = args.baseline / ('src/EscapeFromBorderlands.' + side) / path.name
            relative = str(path.relative_to(args.source))
            if original.is_file() and path.read_bytes() == original.read_bytes():
                unchanged.append(relative)
            else:
                source_deltas.append({'file': relative, 'baselineSha256': sha(original) if original.is_file() else None, 'portSha256': sha(path)})
    preserved_definitions = [
        'src/EscapeFromBorderlands.Client/BackpackEffectDefinition.cs',
        'src/EscapeFromBorderlands.Client/BackpackBoons.cs',
        'src/EscapeFromBorderlands.Client/MeleeEffectDefinition.cs',
        'src/EscapeFromBorderlands.Server/BackpackEffectDefinition.cs',
        'src/EscapeFromBorderlands.Client/BackpackRules.cs',
        'src/EscapeFromBorderlands.Server/BackpackStorage.cs',
        'src/EscapeFromBorderlands.Server/CategorySpawnPolicy.cs',
        'src/EscapeFromBorderlands.Server/RarityConfiguration.cs',
        'src/EscapeFromBorderlands.Client/ChestRigEffectDefinition.cs',
        'src/EscapeFromBorderlands.Client/ChestRigBoons.cs',
        'src/EscapeFromBorderlands.Server/ChestRigEffectDefinition.cs',
        'src/EscapeFromBorderlands.Server/ChestRigStorage.cs',
    ]
    check('preservation', 'Backpack definitions, rolls, storage, category policy and config migration retain baseline bytes',
          all((args.source / name).read_bytes() == (args.baseline / name).read_bytes() for name in preserved_definitions))
    legacy_lines = []
    for path in handlers:
        for lineno, line in enumerate(path.read_text().splitlines(), 1):
            if re.search(r'\bGClass\d+|\bGinterface\d+', line):
                legacy_lines.append({'file': path.name, 'line': lineno, 'source': line.strip()})

    counts = {group: {'passed': sum(c['status'] == 'PASS' for c in checks if c['group'] == group),
                      'failed': sum(c['status'] == 'FAIL' for c in checks if c['group'] == group)}
              for group in dict.fromkeys(c['group'] for c in checks)}
    report = {
        'version': '0.30.0', 'targetSptVersion': '4.1.6',
        'status': 'NATIVE_DECLARATIONS_AND_CATALOG_PASS' if not any(c['status'] == 'FAIL' for c in checks) else 'FAIL',
        'scope': 'Export integrity, native API declarations, native target database, and baseline preservation. Compilation and behavioral/native binding results are in separate reports.',
        'referenceManifestSha256': sha(args.references / 'export-manifest.json'),
        'catalogSha256': sha(client_catalog), 'counts': counts, 'checks': checks,
        'clientAssemblyReady': client_ready,
        'knownClientLegacyTypeReferences': legacy_lines,
        'unchangedProductionSources': unchanged, 'nativeAdaptedProductionSources': source_deltas,
        'catalog': {'activeLegendaries': len(variants), 'vanillaRoots': len(roots),
                    'legacyAliases': len(aliases), 'allSources': len(all_roots),
                    'sourceRootsWithoutDefaultPreset': missing_presets,
                    'equipmentSlots': eligible_slots},
        'dotnetOnPath': bool(shutil.which('dotnet')), 'build': 'See build logs and release validation manifest', 'nativeGameTests': 'NOT RUN',
        'apbs': 'No target APBS assembly in the export; current bridge compatibility is unverified',
        'collectorExecution': 'Revision 1.2 executed successfully by the user on Windows PowerShell 5.1; patched client references found',
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps({'counts': counts, 'clientAssemblyReady': client_ready,
                      'sourceRootsWithoutDefaultPreset': missing_presets,
                      'nonClientFailures': [c for c in checks
                                           if c['status'] == 'FAIL' and c['group'] != 'clientReferences']}, indent=2))
    if any(c['status'] == 'FAIL' for c in checks):
        raise SystemExit(1)
    if not client_ready:
        raise SystemExit(2)


if __name__ == '__main__':
    main()
